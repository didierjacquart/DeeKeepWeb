using System.Diagnostics;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Data;
using Microsoft.Extensions.Options;

namespace DeeKeep.Quality.Jobs;

/// <summary>Charge utile de <c>quality.scan</c> : dossier local, mode, relecture forcée.</summary>
public sealed record ScanPayload(string Folder, bool Deep, bool Force);

/// <summary>
/// « Analyser » (<c>QualityScanThread</c>) : parcourt le dossier, mesure chaque fichier dont
/// l'empreinte a changé (un fichier inchangé n'est pas relu), écrit les mesures au fil de l'eau
/// (lots de 8) et publie chaque ligne dès qu'elle est prête. « Arrêter » tue les ffmpeg en
/// cours ; tout ce qui était mesuré est déjà en base. Après un redémarrage, la tâche reprend :
/// les fichiers déjà mesurés sont retrouvés par leur empreinte.
/// </summary>
public sealed class ScanHandler(
    QualityStore store,
    QualityEngine engine,
    QualityMeter meter,
    PathMap paths,
    IOptions<JobWorkerOptions> workerOptions,
    ILogger<ScanHandler> logger) : IJobHandler
{
    /// <summary>QualityScanThread.BATCH_SIZE : lignes écrites et publiées ensemble.</summary>
    private const int BatchSize = 8;

    public async Task<JsonNode?> ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = ctx.GetPayload<ScanPayload>();
        var root = payload.Folder;
        if (!Directory.Exists(root)) throw new PermanentJobException($"Dossier introuvable : {root}");

        // Une nouvelle analyse efface la liste ; une reprise après redémarrage la garde.
        if (store.ListJob != ctx.Id.ToString())
        {
            store.ClearEntries();
            store.ListJob = ctx.Id.ToString();
            engine.Publish(QualityUpdateReasons.Cleared, []);
        }

        ctx.Message("Analyse en cours…");
        var files = await Task.Run(() => AudioExtensions.IterAudioFiles(root, ct).ToList(), ct);
        var total = files.Count;
        ctx.Report(0, total, $"Analyse en cours… {total} fichier(s) à lire", force: true);

        var cache = store.Measures();
        var known = store.Entries().ToDictionary(e => e.MatchKey, StringComparer.Ordinal);
        var thresholds = engine.Thresholds();
        var parallel = Math.Max(1, workerOptions.Value.Concurrency.GetValueOrDefault(QualityJobTypes.Scan, 1));

        var gate = new object();
        var pending = new List<(EntryRow Row, string? Stamp, QualityMeasures Measures, IReadOnlyList<string> Defects, bool Fresh)>();
        int done = 0, flagged = 0, errors = 0, measured = 0;
        var clock = Stopwatch.StartNew();

        void Flush()
        {
            List<(EntryRow Row, string? Stamp, QualityMeasures Measures, IReadOnlyList<string> Defects, bool Fresh)> batch;
            lock (gate)
            {
                if (pending.Count == 0) return;
                batch = [.. pending];
                pending.Clear();
            }
            // Les erreurs ne sont jamais mises en cache : elles seront retentées.
            store.SaveMeasures(batch.Where(b => b.Fresh && !b.Measures.HasError)
                .Select(b => (b.Row.Path, b.Stamp, b.Measures, b.Defects)).ToList());
            store.UpsertEntries(batch.Select(b => b.Row));
            engine.PublishRows(QualityUpdateReasons.Scanned, batch.Select(b => b.Row));
        }

        try
        {
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct }, async (local, token) =>
            {
                var dbPath = paths.ToDb(local);
                var key = LibraryText.MatchKey(dbPath);
                var stamp = Stamp.Quality(local);
                cache.TryGetValue(key, out var cached);
                known.TryGetValue(key, out var previous);

                var tags = await Task.Run(() => QualityEngine.ReadTags(local), token);
                QualityMeasures measures;
                var fresh = false;
                if (!payload.Force && cached is not null && Usable(cached, stamp, payload.Deep))
                {
                    measures = cached.Measures;
                    // Mesure retrouvée sous une autre casse (clé du cache desktop) : réécrite sous le vrai chemin.
                    fresh = cached.Path != dbPath;
                }
                else
                {
                    measures = await meter.MeasureAsync(local, payload.Deep, tags.Duration, token);
                    fresh = true;
                    Interlocked.Increment(ref measured);
                }

                var row = engine.BuildRow(tags, local, dbPath, measures, previous);
                var entry = engine.ToDto(row, new StoredMeasure(dbPath, stamp, measures, DeeKeep.Common.Data.Clock.UnixNow()), thresholds);
                var defects = entry.Defects.Select(d => d.Id).ToList();

                int n;
                lock (gate)
                {
                    pending.Add((row, stamp, measures, defects, fresh));
                    n = ++done;
                    if (measures.HasError) errors++;
                    else if (defects.Count > 0) flagged++;
                }
                ctx.Item(dbPath, measures.HasError ? "error" : "done", measures.HasError ? measures.Error : null, entry);
                bool full;
                lock (gate) full = pending.Count >= BatchSize;
                if (full) Flush();
                ctx.Report(n, total, ProgressText(n, total, flagged, clock.Elapsed), errors);
            });
        }
        finally
        {
            // Arrêt ou échec : ce qui a été mesuré est écrit, rien n'est perdu.
            Flush();
            if (store.CountReferencesPending() > 0) engine.EnsureReferenceJob();
        }

        logger.LogInformation("Analyse de {Root} : {Total} fichier(s), {Measured} lu(s) par ffmpeg, {Flagged} signalé(s)", root, total, measured, flagged);
        var summary = $"Analyse terminée : {done} fichier(s) lus, {flagged} titre(s) à la qualité douteuse" +
                      (errors > 0 ? $" — {errors} illisible(s)" : "") +
                      (payload.Deep ? "" : " — craquements non mesurés (analyse rapide)") + ".";
        ctx.Report(done, total, summary, errors, force: true);
        return new JsonObject { ["files"] = total, ["measured"] = measured, ["flagged"] = flagged, ["errors"] = errors, ["deep"] = payload.Deep };
    }

    /// <summary>
    /// Mesure en cache réutilisable : même empreinte (ou même taille et dates à 2 s près — un
    /// même fichier vu par Windows puis par un montage Linux n'a pas toujours la même précision
    /// de date), et approfondie si l'analyse demandée l'est (une mesure approfondie sert aussi
    /// une analyse rapide, pas l'inverse). Les erreurs ne sont jamais réutilisées.
    /// </summary>
    public static bool Usable(StoredMeasure cached, string? stamp, bool deep)
    {
        if (stamp is null || cached.Measures.HasError) return false;
        if (deep && !cached.Measures.Deep) return false;
        if (cached.Stamp == stamp) return true;
        return SameFile(cached.Stamp, stamp);
    }

    public static bool SameFile(string? a, string? b)
    {
        static (long Mtime, long Size)? Split(string? s)
        {
            var i = s?.IndexOf(':') ?? -1;
            return i > 0 && long.TryParse(s![..i], out var m) && long.TryParse(s[(i + 1)..], out var z) ? (m, z) : null;
        }
        return Split(a) is { } x && Split(b) is { } y && x.Size == y.Size && Math.Abs(x.Mtime - y.Mtime) <= 2_000_000_000L;
    }

    /// <summary>« Analyse en cours… n / N fichiers (k signalés) — environ X min restantes ».</summary>
    public static string ProgressText(int done, int total, int flagged, TimeSpan elapsed)
    {
        var text = $"Analyse en cours… {done} / {total} fichiers ({flagged} signalés)";
        var remaining = RemainingText(done, total, elapsed.TotalSeconds);
        return remaining.Length > 0 ? text + " — " + remaining : text;
    }

    /// <summary><c>_remaining_text</c>.</summary>
    public static string RemainingText(int done, int total, double elapsedSeconds)
    {
        if (done < 5 || total == 0 || elapsedSeconds <= 0) return "";
        var seconds = (total - done) * elapsedSeconds / done;
        if (seconds < 45) return "moins d'une minute restante";
        var minutes = Py.RoundInt(seconds / 60);
        return $"environ {minutes} min restante" + (minutes > 1 ? "s" : "");
    }
}
