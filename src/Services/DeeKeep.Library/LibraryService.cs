using System.Collections.Concurrent;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;
using DeeKeep.Common.Files;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.History;
using DeeKeep.Library.Presence;
using DeeKeep.Library.Rules;
using DeeKeep.Library.Scan;
using DeeKeep.Library.Settings;
using DeeKeep.Library.Tracks;
using Microsoft.AspNetCore.Http;

namespace DeeKeep.Library;

/// <summary>Décomptes « Divers » des lots d'extraction en cours (un <see cref="Tally"/> par lot).</summary>
public sealed class BatchTallies
{
    private readonly ConcurrentDictionary<string, (Tally Tally, DateTime Used)> _batches = new(StringComparer.Ordinal);

    /// <summary>Décompte du lot ; un lot inconnu ou d'une autre racine repart d'une lecture fraîche du disque.</summary>
    public Tally Get(string? batchId, string root)
    {
        Prune();
        if (string.IsNullOrEmpty(batchId)) return new Tally(root);
        var entry = _batches.AddOrUpdate(batchId, _ => (new Tally(root), DateTime.UtcNow),
            (_, old) => (old.Tally.Root == root ? old.Tally : new Tally(root), DateTime.UtcNow));
        return entry.Tally;
    }

    private void Prune()
    {
        foreach (var (key, value) in _batches)
            if (DateTime.UtcNow - value.Used > TimeSpan.FromHours(6)) _batches.TryRemove(key, out _);
    }
}

/// <summary>Base du service : chemin, taille, nombre de lignes par table.</summary>
public sealed class DatabaseToolStatus(DeeKeepDb db) : IToolStatusProvider
{
    public Task<IReadOnlyList<ToolStatus>> GetAsync(CancellationToken ct)
    {
        try
        {
            var info = LibraryService.ReadDatabaseInfo(db, null, null, false, 0, null);
            var detail = string.Join(", ", info.Tables.Select(t => $"{t.Table} {t.Rows}"));
            return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Base SQLite", true, $"{info.SizeBytes / 1_000_000.0:0.0} Mo", info.Path, detail)]);
        }
        catch (Exception ex)
        {
            return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Base SQLite", false, null, db.MainPath, ex.Message)]);
        }
    }
}

/// <summary>Enregistrement et points d'entrée du service Library.</summary>
public static class LibraryService
{
    public static IServiceCollection AddLibraryService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<LibraryPaths>();
        services.AddSingleton<DiskStateCache>();
        services.AddSingleton<TrackWriter>();
        services.AddSingleton<TrackCatalog>();
        services.AddSingleton<BitrateBackfill>();
        services.AddHostedService(sp => sp.GetRequiredService<BitrateBackfill>());
        services.AddSingleton<HistoryService>();
        services.AddSingleton<PresenceService>();
        services.AddHostedService(sp => sp.GetRequiredService<PresenceService>());
        services.AddSingleton<FileRegistrar>();
        services.AddSingleton<FsService>();
        services.AddSingleton<BatchTallies>();
        services.AddSingleton<IToolStatusProvider, DatabaseToolStatus>();
        services.AddJobHandler<LibraryScanHandler>(LibraryJobTypes.Scan);
        services.AddJobHandler<DesktopImportHandler>(LibraryJobTypes.ImportDesktop);
        services.AddJobHandler<LocalScanHandler>(LibraryJobTypes.LocalScan);
        services.AddJobHandler<WriteTagsHandler>(LibraryJobTypes.WriteTags);

        // Fichiers produits ou supprimés par les autres services (en plus de /api/files/register, via la boîte d'envoi).
        foreach (var producer in new[] { ServiceNames.Downloader, ServiceNames.Storage, ServiceNames.Quality })
        {
            services.AddEventSubscription(producer, async (sp, evt, ct) =>
            {
                var paths = sp.GetRequiredService<LibraryPaths>();
                if (evt.Type == EventTypes.FileProduced && evt.Data?.Deserialize<FileProducedEvent>(JsonDefaults.Options) is { } produced)
                {
                    var local = File.Exists(produced.Path) ? produced.Path : paths.ToLocal(produced.Path);
                    await sp.GetRequiredService<FileRegistrar>().RegisterAsync(new RegisterFileRequest(local, "event", false,
                        produced.ReplacedPath is null ? null : (File.Exists(produced.ReplacedPath) ? produced.ReplacedPath : paths.ToLocal(produced.ReplacedPath))), ct);
                }
                else if (evt.Type == EventTypes.FilesDeleted && evt.Data?.Deserialize<FilesDeletedEvent>(JsonDefaults.Options) is { } deleted)
                    await sp.GetRequiredService<FileRegistrar>().ForgetAsync(deleted.Paths, deleted.Reason, ct);
            }, durable: true, EventTypes.FileProduced, EventTypes.FilesDeleted);
        }
        return services;
    }

    public static WebApplication MapLibraryService(this WebApplication app)
    {
        app.Services.GetRequiredService<DeeKeepDb>().EnsureSchema();

        var api = app.MapGroup("/api");

        // Paramètres.
        api.MapGet("/settings", (SettingsStore s) => SettingsStore.ToObject(s.GetAll()));
        api.MapPut("/settings", (SettingsStore s, Dictionary<string, JsonNode?> values) => SettingsStore.ToObject(s.Set(values).All));

        // Titres.
        api.MapPost("/tracks/query", (TrackCatalog c, TrackQuery q) => c.Query(q));
        api.MapGet("/tracks/one", (TrackCatalog c, string path) => c.Get(path)?.ToDto() is { } t ? Results.Ok(t) : Results.NotFound());
        api.MapGet("/tracks/suggest", (TrackCatalog c, string q, int? limit, bool? includeMissing) => c.Suggest(q, limit ?? 5, includeMissing ?? false));

        // Présence.
        api.MapPost("/presence/lookup", (PresenceService p, List<PresenceQuery> items) => p.Lookup(items));
        api.MapPost("/presence/rebuild", (PresenceService p) => { p.RequestRebuild(); return Results.Accepted(); });

        // Règles de rangement.
        api.MapPost("/rules/reserve", (BatchTallies batches, LibraryPaths paths, ReserveBatchRequest r) =>
        {
            var tally = batches.Get(r.BatchId, paths.LocalDownloadDir());
            foreach (var item in r.Items) tally.Reserve(item.Artist);
            return Results.NoContent();
        });
        api.MapPost("/rules/destination", (BatchTallies batches, LibraryPaths paths, SettingsStore settings, DestinationRequest r) =>
            Destination(batches, paths, settings, r));
        api.MapGet("/paths/local", (LibraryPaths paths, string path) => paths.ToLocal(path));

        // Fichiers.
        api.MapPost("/files/register", async (FileRegistrar f, RegisterFileRequest r, CancellationToken ct) => Results.Ok(await f.RegisterAsync(r, ct)));
        api.MapPost("/files/delete", async (FileRegistrar f, DeleteFilesRequest r, CancellationToken ct) => await f.DeleteAsync(r, ct));
        api.MapGet("/files/info", (FsService fs, string path) => fs.Info(path) is { } i ? Results.Ok(i) : Results.NotFound());
        api.MapGet("/fs/list", (FsService fs, string? path, bool? audioOnly) =>
        {
            try { return Results.Ok(fs.List(path, audioOnly ?? false)); }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        });

        // Historique.
        api.MapGet("/history", (HistoryService h) => h.List());
        api.MapPost("/history/purge-missing", (HistoryService h) => h.PurgeMissing());
        api.MapPost("/history/clear", (HistoryService h) => h.Clear());

        // Tâches.
        api.MapPost("/library/scan", (JobStore jobs) => Enqueue(jobs, LibraryJobTypes.Scan, "Relecture de la bibliothèque", null));
        api.MapPost("/library/import", (JobStore jobs, DesktopImportRequest? r) =>
            Enqueue(jobs, LibraryJobTypes.ImportDesktop, "Import de l'application desktop", r ?? new DesktopImportRequest()));
        api.MapPost("/files/scan", (JobStore jobs, LocalScanRequest r) => Enqueue(jobs, LibraryJobTypes.LocalScan, "Lecture de fichiers", r));
        api.MapPost("/tags/write", (JobStore jobs, WriteTagsRequest r) => Enqueue(jobs, LibraryJobTypes.WriteTags, "Écriture de tags", r));

        api.MapGet("/database", (DeeKeepDb db, PresenceService p, LibraryPaths paths, SettingsStore s, DesktopImportHandler import) =>
            ReadDatabaseInfo(db, s.DownloadDir(), paths.LocalDownloadDir(), p.Ready, p.Files, import.DefaultDataDir()));

        MapMedia(app);
        return app;
    }

    private static JobDto Enqueue(JobStore jobs, string type, string title, object? payload) =>
        jobs.Enqueue(new EnqueueJobRequest(type, title, payload is null ? null : JsonSerializer.SerializeToNode(payload, JsonDefaults.Options), GroupKey: "library"));

    private static DestinationResponse Destination(BatchTallies batches, LibraryPaths paths, SettingsStore settings, DestinationRequest r)
    {
        var root = string.IsNullOrWhiteSpace(r.Root) ? paths.LocalDownloadDir() : r.Root;
        var tags = new Dictionary<string, string?>
        {
            ["title"] = r.Title, ["artist"] = r.Artist, ["album"] = r.Album, ["albumartist"] = r.AlbumArtist, ["date"] = r.Year, ["genre"] = r.Genre,
            ["track"] = r.TrackNumber?.ToString(), ["tracktotal"] = r.TrackTotal?.ToString(), ["disc"] = r.DiscNumber?.ToString(),
            ["disctotal"] = r.DiscTotal?.ToString(), ["format"] = r.Extension.TrimStart('.').ToUpperInvariant(),
            ["bitrate"] = r.Bitrate?.ToString(),
        };
        var extension = "." + r.Extension.TrimStart('.').ToLowerInvariant();
        string local;
        if (!string.IsNullOrEmpty(r.InPlaceFolder))
        {
            // Remplacement sur place : dossier imposé, nom selon la convention historique.
            local = Path.Join(r.InPlaceFolder, LibraryRules.TargetStem(tags, null) + extension);
        }
        else
        {
            var rules = LibraryRules.FromSettings(settings.GetAll());
            local = LibraryRules.Destination(root, tags, rules, extension, r.ReplacedLocalPath, batches.Get(r.BatchId, root));
        }
        return new DestinationResponse(local, paths.ToDb(local), Path.GetDirectoryName(local) ?? root);
    }

    public static DatabaseInfo ReadDatabaseInfo(DeeKeepDb db, string? downloadDir, string? localDir, bool ready, int files, string? desktopDir)
    {
        using var conn = db.Open(readOnly: true);
        var tables = Schema.TableOrder.Select(t =>
        {
            var row = conn.QueryRows($"SELECT COUNT(*) AS n, COALESCE(SUM(deleted), 0) AS d FROM {t.Name}").First();
            return new TableCount(t.Name, (row.Long("n") ?? 0) - (row.Long("d") ?? 0), row.Long("d") ?? 0);
        }).ToList();
        var history = (int)(conn.QueryRows("SELECT COUNT(*) AS n FROM tracks WHERE in_history = 1 AND deleted = 0").First().Long("n") ?? 0);
        var coverBytes = conn.QueryRows("SELECT COALESCE(SUM(bytes), 0) AS s FROM covers WHERE deleted = 0").First().Long("s") ?? 0;
        long size = 0;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(db.MainPath + suffix)) size += new FileInfo(db.MainPath + suffix).Length;
        return new DatabaseInfo(db.MainPath, size, tables, history, coverBytes, downloadDir, localDir, ready, files, desktopDir);
    }

    // ------------------------------------------------------------------ flux binaires

    private static void MapMedia(WebApplication app)
    {
        // Flux audio avec requêtes Range. Le chemin est celui de la base ; il doit désigner un
        // fichier audio d'une racine autorisée.
        app.MapGet("/media/track", (LibraryPaths paths, HttpContext http, string path) =>
        {
            var local = paths.ToLocal(path);
            if (!AudioExtensions.IsAudioFile(local) || !paths.IsAllowed(local) || !File.Exists(local)) return Results.NotFound();
            var info = new FileInfo(local);
            http.Response.Headers.CacheControl = "private, max-age=3600";
            var etag = new EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
            return Results.Stream(new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan),
                AudioExtensions.ContentType(local), null, info.LastWriteTimeUtc, etag, enableRangeProcessing: true);
        });

        // Jaquettes : contenu adressé par SHA-1, donc cache HTTP long et ETag = sha1.
        app.MapGet("/covers/{sha1}", (DeeKeepDb db, HttpContext http, string sha1) =>
        {
            sha1 = sha1.ToLowerInvariant();
            if (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)) return Results.NotFound();
            using var conn = db.Open(readOnly: true);
            var row = conn.QueryRows("SELECT mime, data FROM covers WHERE sha1 = @s AND deleted = 0", new { s = sha1 }).FirstOrDefault();
            if (row is null || row["data"] is not byte[] data) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(data, row.Str("mime") is { Length: > 0 } m ? m : "application/octet-stream", entityTag: new EntityTagHeaderValue($"\"{sha1}\""));
        });
    }
}
