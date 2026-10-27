using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Files;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Data;
using DeeKeep.Quality.Jobs;

namespace DeeKeep.Quality;

/// <summary>Enregistrement et points d'entrée du service Quality (propriétaire de la table <c>quality</c>).</summary>
public static class QualityService
{
    public static IServiceCollection AddQualityService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AudioProbe>();
        services.AddSingleton<QualityMeter>();
        services.AddSingleton<QualityStore>();
        services.AddSingleton<QualityEngine>();
        services.AddJobHandler<ScanHandler>(QualityJobTypes.Scan);
        services.AddJobHandler<ReanalyzeHandler>(QualityJobTypes.Reanalyze);
        services.AddJobHandler<ReferencesHandler>(QualityJobTypes.References);
        services.AddJobHandler<ImportHandler>(QualityJobTypes.ImportCache);
        // Fichier remplacé par un retéléchargement : réanalyse automatique (idempotent : « au moins une fois »).
        services.AddEventSubscription(ServiceNames.Downloader, OnFileReplacedAsync, durable: true, EventTypes.FileReplaced);
        return services;
    }

    /// <summary>Chemin en base : tel quel s'il en a déjà la forme (« D:\… »), sinon converti depuis le chemin local.</summary>
    internal static string DbPathOf(PathMap map, string path) =>
        path.Length > 2 && path[1] == ':' && path.Contains('\\') ? path : map.ToDb(path);

    private static Task OnFileReplacedAsync(IServiceProvider sp, ServiceEvent evt, CancellationToken ct)
    {
        var data = evt.Data?.Deserialize<FileProducedEvent>(JsonDefaults.Options);
        if (data is null) return Task.CompletedTask;
        var store = sp.GetRequiredService<QualityStore>();
        var map = sp.GetRequiredService<PathMap>();
        var engine = sp.GetRequiredService<QualityEngine>();
        var jobs = sp.GetRequiredService<JobStore>();

        var newPath = DbPathOf(map, data.Path);
        var oldPath = data.ReplacedPath is { Length: > 0 } r ? DbPathOf(map, r) : newPath;
        var row = (data.JobId is { } id ? store.EntryByJob(id) : null) ?? store.Entries([oldPath, newPath]).FirstOrDefault();
        if (row is null) return Task.CompletedTask; // un fichier qui n'est pas dans la liste de l'écran

        var previous = row.Path;
        if (!string.Equals(previous, newPath, StringComparison.Ordinal))
        {
            // Rangé ailleurs : la ligne suit le fichier, la mesure de l'ancien chemin est retirée (pierre tombale).
            store.MoveEntry(previous, newPath);
            store.DeleteMeasures([previous]);
            row.Path = newPath;
            row.MatchKey = LibraryText.MatchKey(newPath);
        }
        row.Status = QualityStatuses.Reanalyzing;
        row.RedownloadJob = null;
        store.UpsertEntries([row]);
        engine.PublishRows(QualityUpdateReasons.Replaced, [row]);
        jobs.Enqueue(new EnqueueJobRequest(QualityJobTypes.Reanalyze, "Réanalyse après remplacement",
            JsonSerializer.SerializeToNode(new ReanalyzePayload([newPath]), JsonDefaults.Options), Priority: 1));
        return Task.CompletedTask;
    }

    public static WebApplication MapQualityService(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/thresholds", (QualityEngine engine) => engine.Thresholds());

        api.MapPut("/thresholds", async (QualityEngine engine, LibraryClient library, QualityThresholds body, CancellationToken ct) =>
        {
            var t = QualityEngine.Clamp(body);
            // Écriture par le service propriétaire d'app_settings ; aucun fichier n'est relu.
            await library.SetSettingsAsync(QualityEngine.ToSettings(t), ct);
            engine.Publish(QualityUpdateReasons.Thresholds, []);
            return t;
        });

        api.MapPost("/entries/query", (QualityEngine engine, QualityQuery query) => engine.Query(query));

        api.MapPost("/entries/clear", (QualityStore store, QualityEngine engine, JobStore jobs) =>
        {
            if (Busy(jobs)) return Results.Conflict("Attendez la fin de l'analyse et des retéléchargements en cours avant de vider la liste.");
            var n = store.ClearEntries();
            store.ListJob = null;
            engine.Publish(QualityUpdateReasons.Cleared, []);
            return Results.Ok(n);
        });

        api.MapPut("/folder", (QualityStore store, QualityEngine engine, JobStore jobs, JobWorker worker, string path) =>
        {
            if (!Directory.Exists(path)) return Results.BadRequest($"Le dossier suivant n'existe pas :\n{path}");
            var current = engine.CurrentFolder();
            if (current.Length == 0 || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(current), StringComparison.Ordinal))
            {
                // Corrige l'original : changer de dossier arrête l'analyse en cours au lieu de la laisser alimenter une liste vidée.
                foreach (var job in jobs.List(true, type: QualityJobTypes.Scan)) worker.Cancel(job.Id);
                store.Folder = path;
                store.ClearEntries();
                store.ListJob = null;
                engine.Publish(QualityUpdateReasons.Folder, []);
            }
            return Results.Ok(path);
        });

        api.MapPost("/scan", async (QualityStore store, QualityEngine engine, JobStore jobs, AudioProbe probe, QualityScanRequest request, CancellationToken ct) =>
        {
            var active = jobs.List(true, type: QualityJobTypes.Scan);
            if (active.Count > 0) return Results.Ok(active[0]);
            var folder = string.IsNullOrWhiteSpace(request.Folder) ? engine.CurrentFolder() : request.Folder;
            if (!Directory.Exists(folder)) return Results.BadRequest($"Le dossier suivant n'existe pas :\n{folder}");
            if (!await probe.IsFfmpegAvailableAsync(ct))
                return Results.BadRequest("L'analyse du signal repose sur FFmpeg, qui est introuvable.\nConsultez la page Diagnostic pour connaître son état.");
            store.Folder = folder;
            var deep = request.Deep ?? engine.Thresholds().Deep;
            return Results.Ok(jobs.Enqueue(new EnqueueJobRequest(QualityJobTypes.Scan, "Analyse de la qualité sonore",
                JsonSerializer.SerializeToNode(new ScanPayload(folder, deep, request.Force), JsonDefaults.Options), Priority: -1, MaxAttempts: 5)));
        });

        api.MapPost("/reanalyze", (JobStore jobs, IReadOnlyList<string> paths) =>
            jobs.Enqueue(new EnqueueJobRequest(QualityJobTypes.Reanalyze, "Réanalyse",
                JsonSerializer.SerializeToNode(new ReanalyzePayload(paths), JsonDefaults.Options), Priority: 1)));

        api.MapPost("/import", (JobStore jobs) =>
            jobs.Enqueue(new EnqueueJobRequest(QualityJobTypes.ImportCache, "Import du cache de qualité (application desktop)", new JsonObject(), MaxAttempts: 1)));

        api.MapPost("/redownload", async (QualityStore store, QualityEngine engine, DownloaderClient downloader, IReadOnlyList<string> paths, CancellationToken ct) =>
        {
            var rows = store.Entries(paths);
            var measures = store.Measures();
            // Illisibles écartés ; sans artiste ni titre, la recherche n'aurait rien à chercher.
            var selected = rows.Where(r => measures.TryGetValue(r.MatchKey, out var m) && !m.Measures.HasError).ToList();
            var targets = selected.Where(r => PyText.Strip(r.Artist).Length > 0 && PyText.Strip(r.Title).Length > 0).ToList();
            var skipped = paths.Except(targets.Select(r => r.Path)).ToList();
            if (targets.Count == 0) return new QualityRedownloadResult([], paths.Count, paths.Count, skipped);
            var settings = store.Settings();
            string? Str(string k) => settings.TryGetValue(k, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            var options = Str("file_format") is { } f ? new ExportOptions(f, Str("quality") ?? "320 kbps (HQ)") : null;
            var items = targets.Select(r => new RedownloadItem(r.Path, r.Artist, r.Title, r.Album, measures[r.MatchKey].Measures.Duration)).ToList();
            var jobs = await downloader.EnqueueRedownloadsAsync(new EnqueueRedownloadsRequest(items, options, "quality"), ct);
            for (var i = 0; i < targets.Count && i < jobs.Count; i++)
            {
                targets[i].RedownloadJob = jobs[i].Id.ToString();
                targets[i].Status = QualityStatuses.Redownload;
            }
            store.UpsertEntries(targets);
            engine.PublishRows(QualityUpdateReasons.Redownload, targets);
            return new QualityRedownloadResult(jobs, paths.Count, paths.Count - targets.Count, skipped);
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try { app.Services.GetRequiredService<QualityEngine>().EnsureReferenceJob(); }
            catch (Exception ex) { app.Logger.LogDebug(ex, "Reprise des références différée"); }
        });
        return app;
    }

    private static bool Busy(JobStore jobs) =>
        jobs.List(true, type: QualityJobTypes.Scan).Count > 0 || jobs.List(true, type: QualityJobTypes.Reanalyze).Count > 0;
}
