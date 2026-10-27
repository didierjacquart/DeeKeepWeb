using DeeKeep.Common.Audio;
using DeeKeep.Common.Processes;
using DeeKeep.Common.Text;
using DeeKeep.Downloader.Data;
using DeeKeep.Downloader.Settings;

namespace DeeKeep.Downloader.Extraction;

/// <summary>Emplacement visé : dossier et nom sans extension.</summary>
public sealed record ExtractionTarget(string Directory, string Stem);

/// <summary>Une extraction à mener (piste Deezer, fichier à réencoder ou titre abîmé à remplacer).</summary>
public sealed class ExtractionRequest
{
    public required TrackInfo Track { get; init; }

    /// <summary>Format cible en minuscules (mp3, flac, wav, m4a).</summary>
    public required string Format { get; init; }

    public required string Quality { get; init; }

    /// <summary>Fichier remplacé (réencodage, retéléchargement) : jamais pris pour un homonyme, supprimé s'il diffère du fichier produit.</summary>
    public string? OriginalPath { get; init; }

    /// <summary>Emplacement du fichier produit, demandé une fois les métadonnées résolues (le motif peut réclamer l'année).</summary>
    public required Func<CancellationToken, Task<ExtractionTarget>> ResolveTarget { get; init; }

    /// <summary>Progression (pourcentage, texte exact de l'original).</summary>
    public required Action<int, string> Progress { get; init; }
}

public sealed record ExtractionOutcome(bool Success, string Message, string? Path, VideoCandidate? Video, int Attempts);

/// <summary>yt-dlp introuvable ou impossible à lancer.</summary>
public sealed class YtDlpMissingException() : Exception("yt-dlp est manquant.");

/// <summary>
/// Extraction d'une piste depuis YouTube — portage de <c>TrackDownloaderThread</c>
/// (<c>download_thread.py</c>) : choix de la vidéo, tentatives, yt-dlp en processus
/// fils (tué à l'annulation), conversion ffmpeg, tags et jaquette marquée, installation
/// atomique dans le dossier visé.
/// </summary>
public sealed class TrackExtractor(
    ProcessRunner runner,
    DownloaderSettings settings,
    DownloaderStore store,
    ExtractionGate gate,
    ILogger<TrackExtractor> logger)
{
    public async Task<ExtractionOutcome> RunAsync(ExtractionRequest request, CancellationToken ct)
    {
        var maxRetries = Math.Max(1, settings.Options.MaxRetries);
        var cookies = settings.CookiesFile();
        var yt = settings.YtDlp(cookies);
        var state = new AttemptState();

        request.Progress(0, "En file d'attente");
        using var slot = await gate.EnterAsync(ct);

        var last = "";
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            state.Attempt = attempt;
            void Report(int pct, string text) => request.Progress(pct, Failures.WithAttempt(text, attempt, maxRetries));

            var (ok, message, path, video) = await AttemptAsync(request, yt, state, Report, ct);
            if (ok)
            {
                request.Progress(100, "Terminé ✓");
                return new ExtractionOutcome(true, "Sauvegardé", path, video, attempt);
            }
            last = message;
            if (state.Permanent is { } permanent) return new ExtractionOutcome(false, permanent, null, null, attempt);
            if (attempt < maxRetries)
            {
                request.Progress(5, Failures.RetryText(attempt, maxRetries));
                await Task.Delay(TimeSpan.FromSeconds(Failures.RetryDelaySeconds(attempt, maxRetries)), ct);
            }
        }
        return new ExtractionOutcome(false, Failures.GaveUp(maxRetries, last), null, null, maxRetries);
    }

    private sealed class AttemptState
    {
        public int Attempt;
        public ExtractionTarget? Target;
        public IReadOnlyList<VideoCandidate>? Candidates;
        public string? Permanent;
    }

    private async Task<(bool Ok, string Message, string? Path, VideoCandidate? Video)> AttemptAsync(
        ExtractionRequest request, YtDlpSettings yt, AttemptState state, Action<int, string> report, CancellationToken ct)
    {
        var track = request.Track;
        report(5, "Initialisation...");
        state.Target ??= await request.ResolveTarget(ct);
        var target = state.Target;
        var format = request.Format.ToLowerInvariant();
        Directory.CreateDirectory(target.Directory);
        var finalPath = Path.Combine(target.Directory, $"{target.Stem}.{format}");

        try
        {
            // Liste gardée d'une tentative à l'autre : la recherche a déjà été payée.
            state.Candidates ??= await YouTubeMatching.SelectAsync(track.Artist, track.Title,
                q => SearchAsync(q, yt, ct), () => report(4, "Recherche YouTube..."));
        }
        catch (YtDlpMissingException ex)
        {
            state.Permanent = ex.Message;
            return (false, ex.Message, null, null);
        }

        var candidates = state.Candidates;
        if (candidates.Count == 0)
        {
            state.Permanent = YouTubeMatching.NoVideoMessage(track.Artist, track.Title);
            return (false, state.Permanent, null, null);
        }

        var last = "";
        for (var i = 0; i < candidates.Count; i++)
        {
            var video = candidates[i];
            if (i > 0) report(5, $"Autre version ({i + 1}/{candidates.Count})...");
            var (ok, message) = await ExtractCandidateAsync(request, video, target, finalPath, yt, report, ct);
            if (ok)
            {
                RemoveReplacedOriginal(request.OriginalPath, finalPath);
                return (true, "", finalPath, video);
            }
            last = message;
            if (Failures.IsServerBlocked(message))
            {
                // Refus de la machine, pas de la vidéo : ni autre version, ni nouvel essai.
                state.Permanent = Failures.ServerBlockedMessage(message, yt.CookiesFile is not null);
                return (false, state.Permanent, null, null);
            }
            // Échec passager : la vidéo reste la bonne, c'est la tentative qui est à refaire.
            if (!Failures.IsPermanent(message)) return (false, message, null, null);
        }
        state.Permanent = Failures.AllRefused(candidates.Count, last);
        return (false, state.Permanent, null, null);
    }

    /// <summary>(identifiant, titre) des premières vidéos d'une recherche à plat ; vide si YouTube n'a rien rendu.</summary>
    private async Task<IReadOnlyList<VideoHit>> SearchAsync(string query, YtDlpSettings yt, CancellationToken ct)
    {
        var result = await runner.RunAsync(runner.Tools.YtDlp, YtDlpCommand.SearchArguments(query, yt), ct,
            timeout: settings.Options.SearchTimeout, environment: settings.YtDlpEnvironment());
        if (result.ExitCode == -1 && result.StdOut.Length == 0) throw new YtDlpMissingException();
        if (!result.Success)
        {
            logger.LogInformation("Recherche YouTube « {Query} » sans réponse : {Error}", query, YtDlpCommand.ErrorMessage(result.StdErr, result.ExitCode));
            return [];
        }
        return YtDlpCommand.ParseSearch(result.StdOut);
    }

    private async Task<(bool Ok, string Message)> ExtractCandidateAsync(ExtractionRequest request, VideoCandidate video, ExtractionTarget target,
        string finalPath, YtDlpSettings yt, Action<int, string> report, CancellationToken ct)
    {
        var format = request.Format.ToLowerInvariant();
        // Dossier de travail unique dans le dossier visé : même volume (déplacement
        // atomique), pas de collision entre extractions simultanées. Inscrit en base
        // pour être supprimé au démarrage si une coupure l'abandonne.
        var workDir = Path.Combine(target.Directory, ".dk_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(workDir);
        store.AddWorkDir(workDir);
        try
        {
            var lastPct = -1;
            var result = await runner.RunAsync(runner.Tools.YtDlp,
                YtDlpCommand.ExtractArguments(video.Id, workDir, format, request.Quality, yt), ct,
                onStdOutLine: line =>
                {
                    if (YtDlpCommand.ParseProgress(line) is { } pct)
                    {
                        pct = Math.Max(5, pct);
                        if (pct == lastPct) return;
                        lastPct = pct;
                        report(pct, "Extraction...");
                    }
                    else if (YtDlpCommand.IsPostProcessing(line)) report(95, "Finalisation...");
                },
                timeout: settings.Options.ExtractTimeout, environment: settings.YtDlpEnvironment());
            if (result.ExitCode == -1 && result.StdOut.Length == 0) return (false, "yt-dlp est manquant.");
            if (!result.Success) return (false, "Erreur: " + YtDlpCommand.ErrorMessage(result.StdErr, result.ExitCode));

            var produced = Path.Combine(workDir, $"{YtDlpCommand.WorkStem}.{format}");
            if (!File.Exists(produced))
            {
                // Repli : le plus gros fichier produit, quelle que soit son extension exacte.
                produced = new DirectoryInfo(workDir).EnumerateFiles()
                    .Where(f => !f.Name.EndsWith(".part", StringComparison.Ordinal) && !f.Name.EndsWith(".ytdl", StringComparison.Ordinal))
                    .OrderByDescending(f => f.Length).FirstOrDefault()?.FullName ?? "";
                if (produced.Length == 0) return (false, "Fichier extrait introuvable.");
            }

            ApplyMetadata(produced, request);
            RemoveHomonyms(request, target, format);
            File.Move(produced, finalPath, overwrite: true);
            return (true, "");
        }
        catch (IOException ex)
        {
            return (false, "Erreur: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (false, "Erreur: " + ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dossier de travail non supprimé : {Dir}", workDir);
            }
            store.RemoveWorkDir(workDir);
        }
    }

    /// <summary>
    /// <c>apply_metadata</c> : tous les tags connus et la jaquette marquée de l'icône
    /// (la jaquette d'origine reste celle de l'historique). Un échec n'empêche pas le succès.
    /// </summary>
    private void ApplyMetadata(string path, ExtractionRequest request)
    {
        var cover = request.Track.Cover is { Length: > 0 } c ? CoverImage.StampAppIcon(c) : null;
        var result = AudioTagWriter.Write(path, request.Track.Tags(request.Format), cover, TagWriteMode.Replace, safe: false);
        if (!result.Success) logger.LogWarning("[Tags] Erreur lors de l'application : {Message}", result.Message);
    }

    /// <summary>
    /// Supprime les doublons du dossier visé — le nom visé, « Titre - Artiste » et « Artiste - Titre »
    /// (nommages des extractions précédentes) — sans jamais toucher au fichier remplacé.
    /// </summary>
    private void RemoveHomonyms(ExtractionRequest request, ExtractionTarget target, string format)
    {
        var track = request.Track;
        foreach (var name in new[]
                 {
                     target.Stem,
                     FileNames.SanitizeFilename($"{track.Title} - {track.Artist}"),
                     FileNames.SanitizeFilename($"{track.Artist} - {track.Title}"),
                 }.Distinct(StringComparer.Ordinal))
        {
            var path = Path.Combine(target.Directory, $"{name}.{format}");
            if (request.OriginalPath is not null && SamePath(path, request.OriginalPath)) continue;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogInformation("Homonyme non supprimé : {Path} ({Error})", path, ex.Message);
            }
        }
    }

    /// <summary>Réencodage réussi : le fichier d'origine disparaît s'il diffère du fichier produit (extension ou nom changés).</summary>
    private void RemoveReplacedOriginal(string? original, string produced)
    {
        if (original is null || SamePath(original, produced) || !File.Exists(original)) return;
        try
        {
            File.Delete(original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Fichier remplacé non supprimé : {Path} ({Error})", original, ex.Message);
        }
    }

    /// <summary>Même fichier : sans tenir compte de la casse sous Windows, comme l'original (<c>abspath</c>).</summary>
    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

/// <summary>
/// Plafond commun des extractions simultanées (Musique, Réencoder, Qualité confondus) :
/// chaque type de tâche a sa concurrence, mais yt-dlp et ffmpeg se partagent la machine.
/// </summary>
public sealed class ExtractionGate(Microsoft.Extensions.Options.IOptions<DownloaderOptions> options)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxParallel));

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        return new Releaser(_slots);
    }

    private sealed class Releaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
