using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Downloader;

namespace DeeKeep.Downloader.Data;

/// <summary>
/// Tables propres au service dans <c>deekeep_web.db</c> : lignes « En attente » de la
/// page Musique, dossiers de travail en cours (nettoyés au démarrage s'ils survivent
/// à une coupure), et retrait de tâches terminées de la liste.
/// </summary>
public sealed class DownloaderStore(WebDb webDb, ServiceIdentity identity, EventLog events, JobWorker worker, JobStore jobs,
    ILogger<DownloaderStore> logger)
{
    // Les dossiers créés avant le démarrage du processus sont forcément orphelins ;
    // ceux d'après appartiennent à une tâche déjà reprise.
    private static readonly double ProcessStartedAt =
        new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToUnixTimeMilliseconds() / 1000.0;
    private static readonly object SchemaLock = new();
    private static bool _schemaReady;

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = webDb.Open();
        if (_schemaReady) return conn;
        lock (SchemaLock)
        {
            if (!_schemaReady)
            {
                conn.Execute("""
                    CREATE TABLE IF NOT EXISTS downloader_pending (
                        id TEXT PRIMARY KEY,
                        track TEXT NOT NULL,
                        added_at REAL NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS downloader_workdirs (
                        path TEXT PRIMARY KEY,
                        created_at REAL NOT NULL
                    );
                    """);
                _schemaReady = true;
            }
        }
        return conn;
    }

    // ------------------------------------------------------------------ lignes en attente

    public IReadOnlyList<PendingDownload> ListPending()
    {
        using var conn = Open();
        return conn.Query<(string id, string track, double added_at)>("SELECT id, track, added_at FROM downloader_pending ORDER BY added_at, rowid")
            .Select(r => new PendingDownload(Guid.Parse(r.id), JsonSerializer.Deserialize<DeezerTrack>(r.track, JsonDefaults.Options)!, r.added_at))
            .ToList();
    }

    public IReadOnlyList<PendingDownload> AddPending(IReadOnlyList<DeezerTrack> tracks)
    {
        var added = new List<PendingDownload>();
        using (var conn = Open())
        using (var tx = conn.BeginTransaction())
        {
            var now = Clock.UnixNow();
            foreach (var track in tracks)
            {
                var row = new PendingDownload(Guid.NewGuid(), track, now);
                conn.Execute("INSERT INTO downloader_pending (id, track, added_at) VALUES (@id, @track, @now)",
                    new { id = row.Id.ToString(), track = JsonSerializer.Serialize(track, JsonDefaults.Options), now }, tx);
                added.Add(row);
            }
            tx.Commit();
        }
        if (added.Count > 0) PublishChange([], pendingChanged: true);
        return added;
    }

    public int RemovePending(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return 0;
        int n;
        using (var conn = Open())
            n = conn.Execute("DELETE FROM downloader_pending WHERE id IN @ids", new { ids = ids.Select(i => i.ToString()).ToList() });
        if (n > 0) PublishChange([], pendingChanged: true);
        return n;
    }

    // ------------------------------------------------------------------ tâches

    /// <summary>« Purger » : retire de la liste les tâches terminées d'un type (réussies seulement, ou toutes).</summary>
    public int Purge(string type, bool succeededOnly)
    {
        List<Guid> ids;
        using (var conn = Open())
        {
            var statuses = succeededOnly ? new[] { "Succeeded" } : ["Succeeded", "Failed", "Cancelled"];
            ids = conn.Query<string>("DELETE FROM jobs WHERE service = @s AND type = @type AND status IN @statuses RETURNING id",
                new { s = identity.Name, type, statuses }).Select(Guid.Parse).ToList();
        }
        if (ids.Count > 0) PublishChange(ids, pendingChanged: false);
        return ids.Count;
    }

    /// <summary>
    /// « Retirer la sélection » : les tâches désignées quittent la liste. Une tâche encore en
    /// cours est d'abord arrêtée (ce que l'original ne faisait pas : la ligne disparaissait,
    /// l'extraction continuait) ; elle est effacée dès qu'elle a rendu la main.
    /// </summary>
    public int Remove(IReadOnlyCollection<Guid> ids)
    {
        var removed = new List<Guid>();
        var pending = new List<Guid>();
        foreach (var id in ids)
        {
            var job = jobs.Get(id);
            if (job is null || job.Service != identity.Name) continue;
            if (job.Status is JobStatus.Queued or JobStatus.Running) worker.Cancel(id);
            if (DeleteIfFinished(id)) removed.Add(id);
            else pending.Add(id);
        }
        if (removed.Count > 0) PublishChange(removed, pendingChanged: false);
        if (pending.Count > 0) _ = Task.Run(() => DeleteWhenStoppedAsync(pending));
        return removed.Count + pending.Count;
    }

    private bool DeleteIfFinished(Guid id)
    {
        using var conn = Open();
        return conn.Execute("DELETE FROM jobs WHERE id = @id AND service = @s AND status IN ('Succeeded','Failed','Cancelled')",
            new { id = id.ToString(), s = identity.Name }) > 0;
    }

    private async Task DeleteWhenStoppedAsync(List<Guid> ids)
    {
        // L'arrêt tue yt-dlp et ffmpeg sur-le-champ ; quelques secondes suffisent.
        for (var i = 0; i < 60 && ids.Count > 0; i++)
        {
            await Task.Delay(500);
            var done = ids.Where(DeleteIfFinished).ToList();
            if (done.Count == 0) continue;
            ids.RemoveAll(done.Contains);
            PublishChange(done, pendingChanged: false);
        }
        if (ids.Count > 0) logger.LogWarning("{Count} tâche(s) retirée(s) toujours en cours après arrêt", ids.Count);
    }

    private void PublishChange(IReadOnlyList<Guid> removed, bool pendingChanged) =>
        events.Publish(DownloaderEvents.QueueChanged, new DownloadQueueChanged(removed, pendingChanged));

    // ------------------------------------------------------------------ dossiers de travail

    public void AddWorkDir(string path)
    {
        using var conn = Open();
        conn.Execute("INSERT OR REPLACE INTO downloader_workdirs (path, created_at) VALUES (@path, @now)", new { path, now = Clock.UnixNow() });
    }

    public void RemoveWorkDir(string path)
    {
        try
        {
            using var conn = Open();
            conn.Execute("DELETE FROM downloader_workdirs WHERE path = @path", new { path });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Dossier de travail non désinscrit : {Path}", path);
        }
    }

    /// <summary>
    /// Au démarrage : supprime les dossiers de travail <c>.dk_*</c> laissés par une
    /// coupure (l'original les abandonnait sur le disque, dans la bibliothèque).
    /// </summary>
    public int CleanOrphanWorkDirs()
    {
        List<string> paths;
        using (var conn = Open()) paths = conn.Query<string>("SELECT path FROM downloader_workdirs WHERE created_at < @start",
            new { start = ProcessStartedAt }).ToList();
        var cleaned = 0;
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path) && Path.GetFileName(path).StartsWith(".dk_", StringComparison.Ordinal))
                {
                    Directory.Delete(path, recursive: true);
                    cleaned++;
                }
                RemoveWorkDir(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dossier de travail orphelin non supprimé : {Path}", path);
            }
        }
        return cleaned;
    }
}

/// <summary>Nettoyage des dossiers de travail orphelins au démarrage (avant la reprise des tâches).</summary>
public sealed class WorkDirCleanup(DownloaderStore store, ILogger<WorkDirCleanup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var n = store.CleanOrphanWorkDirs();
            if (n > 0) logger.LogInformation("{Count} dossier(s) de travail orphelin(s) supprimé(s)", n);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nettoyage des dossiers de travail impossible");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
