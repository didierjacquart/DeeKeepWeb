using System.Text.Json;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.History;

namespace DeeKeep.Library.Presence;

/// <summary>
/// Index de présence du dossier de dépôt (pastilles ✓ ≈ ○ ⏳ des pages Musique et de la
/// recherche) : construit en tâche de fond au démarrage et après chaque changement de
/// fond (dossier de dépôt, collecte, rangement, import), complété ou réduit au fil des
/// fichiers produits et supprimés, puis rediffusé (<c>presence.changed</c>).
/// </summary>
public sealed class PresenceService(
    DeeKeepDb db,
    LibraryPaths paths,
    EventLog events,
    IServiceProvider services,
    ILogger<PresenceService> logger) : BackgroundService
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _rebuild = new(0);
    private PresenceIndex? _index;
    private long _version;
    private string? _lastRoot;

    public bool Ready
    {
        get { lock (_lock) return _index is not null; }
    }

    public int Files
    {
        get { lock (_lock) return _index?.Count ?? 0; }
    }

    /// <summary>Demande une reconstruction (regroupée : un parcours en cours n'est pas doublé).</summary>
    public void RequestRebuild(bool invalidate = false)
    {
        if (invalidate)
        {
            lock (_lock) _index = null;
            Announce("invalidated");
        }
        if (_rebuild.CurrentCount == 0) _rebuild.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => WatchOwnEventsAsync(stoppingToken), stoppingToken);
        RequestRebuild();
        while (!stoppingToken.IsCancellationRequested)
        {
            await _rebuild.WaitQuietlyAsync(Timeout.InfiniteTimeSpan, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;
            // Regroupe les demandes rapprochées (rangement de plusieurs centaines de fichiers).
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            while (_rebuild.CurrentCount > 0) await _rebuild.WaitAsync(stoppingToken);
            try
            {
                await Task.Run(() => Build(stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Échec : index vide (tout « absent »), sans message, comme l'original.
                logger.LogWarning(ex, "Construction de l'index de présence impossible");
                lock (_lock) _index = new PresenceIndex(_lastRoot ?? "");
                Announce("failed");
            }
        }
    }

    private void Build(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var root = paths.LocalDownloadDir();
        var index = new PresenceIndex(root);
        if (Directory.Exists(root))
        {
            var known = KnownTagsFromCache();
            foreach (var local in AudioExtensions.IterAudioFiles(root, ct))
            {
                var dbPath = paths.ToDb(local);
                index.AddFile(dbPath, local, known.GetValueOrDefault(PresenceIndex.Key(dbPath)));
            }
        }
        lock (_lock)
        {
            _index = index;
            _lastRoot = root;
        }
        logger.LogInformation("Index de présence : {Count} fichier(s) sous {Root} en {Ms} ms", index.Count, root, (int)(DateTime.UtcNow - started).TotalMilliseconds);
        Announce("built");

        // À chaque index prêt : réparation des chemins de l'historique.
        try
        {
            using var scope = services.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<HistoryService>();
            history.Repair(p => { lock (_lock) return _index?.HasPath(p) ?? false; }, n => { lock (_lock) return _index?.PathsNamed(n) ?? []; });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Réparation des chemins de l'historique impossible");
        }
    }

    /// <summary>Métadonnées relevées par la collecte (<c>_tags_from_cache</c>) ; base illisible → aucune.</summary>
    private Dictionary<string, KnownTags> KnownTagsFromCache()
    {
        var tags = new Dictionary<string, KnownTags>(StringComparer.Ordinal);
        try
        {
            using var conn = db.Open(readOnly: true);
            // Les lignes de l'historique passent après les autres : elles font foi (métadonnées du téléchargement).
            foreach (var row in conn.QueryRows("SELECT path, title, artist, album FROM tracks WHERE deleted = 0 ORDER BY in_history"))
            {
                var path = row.Str("path") ?? "";
                var title = row.Str("title") ?? "";
                var artist = row.Str("artist") ?? "";
                if (path.Length > 0 && (title.Length > 0 || artist.Length > 0))
                    tags[PresenceIndex.Key(path)] = new KnownTags(title, artist, row.Str("album") ?? "");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cache des titres illisible pour l'index de présence");
        }
        return tags;
    }

    // ------------------------------------------------------------------ mises à jour incrémentales

    /// <summary>Un fichier vient d'être écrit (extraction, conversion, restauration) : ajouté sans relire le dossier.</summary>
    public void Added(string dbPath, string? title, string? artist, string? album)
    {
        lock (_lock)
        {
            if (_index is null) return;
            _index.Add(dbPath, paths.ToLocal(dbPath), title, artist, album);
        }
        Announce("added");
    }

    /// <summary>Fichiers supprimés du disque : retirés de l'index.</summary>
    public void Removed(IEnumerable<string> dbPaths)
    {
        var changed = false;
        lock (_lock)
        {
            if (_index is null) return;
            foreach (var path in dbPaths) changed |= _index.Remove(path);
        }
        if (changed) Announce("removed");
    }

    // ------------------------------------------------------------------ interrogation

    public IReadOnlyList<PresenceVerdict> Lookup(IReadOnlyList<PresenceQuery> items)
    {
        var matches = new List<(PresenceQuery Query, PresenceMatch Match)>();
        lock (_lock)
        {
            foreach (var item in items)
                matches.Add((item, _index is null ? PresenceMatch.Pending : _index.Lookup(item.Title, item.Artist, item.Album)));
        }
        var details = FileDetails(matches.SelectMany(m => m.Match.Paths).Distinct(StringComparer.Ordinal).ToList());
        return matches.Select(m => new PresenceVerdict(m.Query.Key, m.Match.State, m.Match.Reason,
            m.Match.Paths.Select(p => details.TryGetValue(p, out var d)
                ? d with { Reason = m.Match.Reason }
                : new PresenceMatchFile(p, m.Match.Reason, null, null, DeeKeep.Common.Text.PyPath.SplitExt(p).Ext.TrimStart('.').ToLowerInvariant())).ToList())).ToList();
    }

    private Dictionary<string, PresenceMatchFile> FileDetails(IReadOnlyList<string> dbPaths)
    {
        var result = new Dictionary<string, PresenceMatchFile>(StringComparer.Ordinal);
        if (dbPaths.Count == 0) return result;
        try
        {
            using var conn = db.Open(readOnly: true);
            foreach (var chunk in dbPaths.Chunk(400))
            {
                var p = new Dapper.DynamicParameters();
                for (var i = 0; i < chunk.Length; i++) p.Add("p" + i, chunk[i]);
                foreach (var row in conn.QueryRows($"SELECT path, bitrate, file_size, ext FROM tracks WHERE deleted = 0 AND path IN ({string.Join(",", chunk.Select((_, i) => "@p" + i))})", p))
                {
                    var path = row.Str("path")!;
                    result[path] = new PresenceMatchFile(path, "", (int?)row.Long("bitrate"), row.Long("file_size"), row.Str("ext"));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Détails des fichiers en cause indisponibles");
        }
        return result;
    }

    private void Announce(string reason)
    {
        bool ready;
        int files;
        lock (_lock)
        {
            ready = _index is not null;
            files = _index?.Count ?? 0;
        }
        events.Publish(EventTypes.PresenceChanged, new PresenceChangedEvent(ready, files, Interlocked.Increment(ref _version), reason));
    }

    /// <summary>Événements du service lui-même qui changent le fond de la bibliothèque.</summary>
    private async Task WatchOwnEventsAsync(CancellationToken ct)
    {
        var reader = events.Subscribe(ct);
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                switch (evt.Type)
                {
                    case EventTypes.SettingsChanged when evt.Data?["changed"] is { } changed && changed.ToJsonString().Contains("\"download_dir\""):
                        RequestRebuild(invalidate: true);
                        break;
                    case LibraryEvents.LibraryChanged:
                        RequestRebuild();
                        break;
                    case EventTypes.TracksChanged:
                        var reason = evt.Data?.Deserialize<TracksChangedEvent>(JsonDefaults.Options)?.Reason;
                        if (reason is "scan" or "import" or "organize") RequestRebuild();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service.
        }
    }
}
