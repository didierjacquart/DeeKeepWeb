using System.Globalization;
using System.Text.Json;

namespace DeeKeep.Downloader.Extraction;

/// <summary>Réglages d'appel de yt-dlp communs à la recherche et à l'extraction.</summary>
public sealed record YtDlpSettings(
    string? CookiesFile = null,
    string? FfmpegLocation = null,
    string? DenoPath = null,
    IReadOnlyList<string>? PlayerClients = null,
    string? RemoteComponents = "ejs:github",
    string? Proxy = null);

/// <summary>
/// Lignes de commande de yt-dlp — équivalent CLI des options Python de
/// <c>_search</c> et <c>_extract_candidate</c> (<c>download_thread.py:344-490</c>).
/// </summary>
public static class YtDlpCommand
{
    /// <summary>YOUTUBE_PLAYER_CLIENTS : « web_embedded » publie l'audio seul, les deux autres servent de repli.</summary>
    public static readonly IReadOnlyList<string> DefaultPlayerClients = ["web_embedded", "mweb", "tv_simply"];

    /// <summary>Préfixe des lignes de progression (gabarit <c>--progress-template</c>).</summary>
    public const string ProgressPrefix = "DKP";

    /// <summary>Nom du fichier produit dans le dossier de travail : le vrai nom n'est donné qu'à l'installation
    /// (un « % » ou une longueur excessive dans un titre ne peut pas dérouter le gabarit de sortie).</summary>
    public const string WorkStem = "audio";

    /// <summary>Qualité passée à yt-dlp : « 320 », « 128 » si la qualité le dit, « 0 » pour FLAC/WAV.</summary>
    public static string AudioQuality(string format, string? quality)
    {
        if ((quality ?? "").Contains("128", StringComparison.Ordinal)) return "128";
        var f = format.ToLowerInvariant();
        return f.Contains("flac", StringComparison.Ordinal) || f.Contains("wav", StringComparison.Ordinal) ? "0" : "320";
    }

    /// <summary>
    /// Valeur de <c>--audio-quality</c> : yt-dlp lit <c>preferredquality</c> sous 10 comme
    /// une qualité VBR et au-delà comme un débit (« 320 » → « 320K »).
    /// </summary>
    public static string AudioQualityArgument(string ytdlQuality) =>
        double.TryParse(ytdlQuality, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) && q >= 10 ? ytdlQuality + "K" : ytdlQuality;

    /// <summary>Recherche « à plat » : <c>yt-dlp --flat-playlist -J "ytsearch3:&lt;q&gt;"</c>.</summary>
    public static IReadOnlyList<string> SearchArguments(string query, YtDlpSettings settings, int count = YouTubeMatching.SearchCandidates)
    {
        var args = new List<string> { "--flat-playlist", "-J", "--no-warnings", "--no-playlist", "--encoding", "utf-8" };
        AddCommon(args, settings, extraction: false);
        args.Add($"ytsearch{count}:{query}");
        return args;
    }

    /// <summary>Extraction d'une vidéo désignée vers <paramref name="workDir"/>, convertie au format demandé.</summary>
    public static IReadOnlyList<string> ExtractArguments(string videoId, string workDir, string format, string? quality, YtDlpSettings settings)
    {
        var codec = format.ToLowerInvariant();
        var args = new List<string>
        {
            "-f", "bestaudio/best",
            "--no-playlist",
            "--force-overwrites",
            "--encoding", "utf-8",
        };
        if (!string.IsNullOrEmpty(settings.RemoteComponents)) args.AddRange(["--remote-components", settings.RemoteComponents]);
        var clients = settings.PlayerClients is { Count: > 0 } c ? c : DefaultPlayerClients;
        args.AddRange(["--extractor-args", "youtube:player_client=" + string.Join(",", clients)]);
        args.AddRange(["-x", "--audio-format", codec, "--audio-quality", AudioQualityArgument(AudioQuality(codec, quality))]);
        AddCommon(args, settings, extraction: true);
        args.AddRange([
            "--newline",
            "--progress-template",
            $"download:{ProgressPrefix} %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s",
            "-o", Path.Combine(workDir, WorkStem + ".%(ext)s"),
            $"https://www.youtube.com/watch?v={videoId}",
        ]);
        return args;
    }

    private static void AddCommon(List<string> args, YtDlpSettings settings, bool extraction)
    {
        if (!string.IsNullOrEmpty(settings.CookiesFile)) args.AddRange(["--cookies", settings.CookiesFile]);
        if (!string.IsNullOrEmpty(settings.Proxy)) args.AddRange(["--proxy", settings.Proxy]);
        if (!extraction) return;
        if (!string.IsNullOrEmpty(settings.FfmpegLocation)) args.AddRange(["--ffmpeg-location", settings.FfmpegLocation]);
        if (!string.IsNullOrEmpty(settings.DenoPath)) args.AddRange(["--js-runtimes", "deno:" + settings.DenoPath]);
    }

    /// <summary>
    /// Ligne de progression « DKP 1024 4096 NA » → pourcentage d'extraction (0..90),
    /// comme le crochet Python : téléchargé / (total | estimation | 1) × 90.
    /// </summary>
    public static int? ParseProgress(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != ProgressPrefix) return null;
        var downloaded = Number(parts[1]) ?? 0;
        var total = (parts.Length > 2 ? Number(parts[2]) : null) is { } t and > 0 ? t
            : (parts.Length > 3 ? Number(parts[3]) : null) is { } e and > 0 ? e : 1;
        return (int)Math.Clamp(downloaded / total * 90, 0, 90);
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Vrai quand le post-traitement ffmpeg commence (statut « finished » du crochet Python).</summary>
    public static bool IsPostProcessing(string line) =>
        line.StartsWith("[ExtractAudio]", StringComparison.Ordinal) || line.StartsWith("[Merger]", StringComparison.Ordinal);

    /// <summary>
    /// Message d'échec tel que yt-dlp le formule : sa dernière ligne « ERROR: … » (le
    /// texte de l'exception Python), sinon la dernière ligne non vide de la sortie d'erreur.
    /// </summary>
    public static string ErrorMessage(string stderr, int exitCode)
    {
        var lines = stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal));
        return error ?? lines.LastOrDefault() ?? $"yt-dlp a échoué (code {exitCode})";
    }

    /// <summary>Entrées (identifiant, titre) d'une réponse <c>-J</c> de recherche à plat ; liste vide si illisible.</summary>
    public static IReadOnlyList<VideoHit> ParseSearch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return [];
            var hits = new List<VideoHit>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var id = entry.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                var title = entry.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                hits.Add(new VideoHit(id, title));
            }
            return hits;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
