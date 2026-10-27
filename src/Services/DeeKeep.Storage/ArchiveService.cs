using System.Diagnostics;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Storage;

namespace DeeKeep.Storage;

/// <summary>Verrous du service : un seul écrivain de l'archive, un seul sens de copie à la fois.</summary>
public sealed class ArchiveLocks
{
    /// <summary>
    /// Écriture d'un objet et de sa ligne d'index d'un seul tenant : un retrait ne peut
    /// pas effacer un objet qu'un archivage vient de retrouver pour un autre titre.
    /// </summary>
    public SemaphoreSlim Write { get; } = new(1, 1);

    /// <summary>Archivage et restauration ne tournent pas ensemble (règle de l'original).</summary>
    public SemaphoreSlim Work { get; } = new(1, 1);
}

/// <summary>
/// Publie <see cref="EventTypes.ArchiveUpdated"/> par paquets : une tâche de 3 000 titres
/// n'émet pas 3 000 événements, mais la colonne NUAGE suit quand même au fil de l'eau.
/// </summary>
public sealed class ArchiveEventBatch(EventLog events, string reason)
{
    private readonly List<string> _paths = [];
    private readonly Stopwatch _since = Stopwatch.StartNew();

    public void Add(string path)
    {
        _paths.Add(path);
        if (_paths.Count >= 50 || _since.Elapsed > TimeSpan.FromSeconds(1)) Flush();
    }

    public void Flush()
    {
        _since.Restart();
        if (_paths.Count == 0) return;
        events.Publish(EventTypes.ArchiveUpdated, new ArchiveUpdatedEvent(_paths.ToList(), reason));
        _paths.Clear();
    }
}

/// <summary>Opérations courtes de l'archive : jauge, plan, test, retrait, écoute.</summary>
public sealed class ArchiveService(ArchiveSettings settings, ArchiveIndex index, PathMap pathMap, ArchiveLocks locks, EventLog events)
{
    /// <summary>
    /// Jauge : occupation de l'archive, place libre de son volume, et l'état de chaque
    /// titre de la bibliothèque — calculé par <c>stat</c> (date + taille), sans relire
    /// un seul fichier audio.
    /// </summary>
    public ArchiveUsage Usage(string? root)
    {
        var config = settings.Get();
        if (!string.IsNullOrWhiteSpace(root)) config = config with { Root = root.Trim(), ArchiveFile = SafeResolve(root), Configured = SafeResolve(root) is not null };

        var rows = index.Live();
        int onDisk = 0, archived = 0, stale = 0, notArchived = 0;
        long bytesOnDisk = 0, toArchive = 0;
        string? firstPresent = null;
        foreach (var track in index.Tracks())
        {
            var row = rows.Find(track.Path);
            var file = Stat(pathMap.ToLocal(track.Path));
            if (file is null)
            {
                // Absent du disque mais archivé : reste un titre archivé (restaurable).
                if (!string.IsNullOrEmpty(row?.Key)) archived++;
                continue;
            }
            onDisk++;
            bytesOnDisk += file.Length;
            firstPresent ??= file.FullName;
            var stamp = Stamp.Archive(Stamp.MtimeNs(file), file.Length);
            switch (ArchiveRules.StateOf(row?.Key, row?.Stamp, stamp))
            {
                case ArchiveState.Fresh:
                    archived++;
                    break;
                case ArchiveState.Stale:
                    stale++;
                    toArchive += file.Length;
                    break;
                default:
                    notArchived++;
                    toArchive += file.Length;
                    break;
            }
        }

        long objects = 0, stored = 0, fileBytes = 0;
        long? free = null, total = null;
        var same = false;
        string? error = null;
        if (config.ArchiveFile is { } archiveFile)
        {
            var store = new ObjectStore(archiveFile, config.Prefix);
            try
            {
                (objects, stored) = store.Usage();
            }
            catch (ArchiveException ex)
            {
                error = ex.Message;
            }
            fileBytes = store.FileBytes();
            (var f, total) = Volumes.Space(archiveFile);
            free = f;
            same = firstPresent is not null && Volumes.SameVolume(archiveFile, firstPresent);
        }
        return new ArchiveUsage(objects, stored, fileBytes, free, total, archived, stale, notArchived, toArchive,
            onDisk, bytesOnDisk, same, config.Configured, config.ArchiveFile, error);
    }

    /// <summary>Ce qu'Archiver et Restaurer feraient des titres retenus.</summary>
    public ArchivePlan Plan(IReadOnlyList<string> dbPaths)
    {
        var rows = index.Live();
        var wanted = dbPaths.Distinct(StringComparer.Ordinal).ToList();
        var onDisk = wanted.Count(p => File.Exists(pathMap.ToLocal(p)));
        var restorable = wanted.Count(p => rows.Find(p) is { } row && IsRestorable(row));
        var elsewhere = rows.All.Count(IsRestorable);
        return new ArchivePlan(wanted.Count, onDisk, restorable, elsewhere, settings.Get().Configured);
    }

    /// <summary>Titres archivés (chemins retenus) dont le fichier est présent : ce qu'Archiver traitera.</summary>
    public List<string> OnDisk(IReadOnlyList<string> dbPaths) =>
        dbPaths.Distinct(StringComparer.Ordinal).Where(p => File.Exists(pathMap.ToLocal(p))).ToList();

    /// <summary>Titres retenus qui peuvent redescendre de l'archive (<c>_restorable</c>).</summary>
    public List<string> Restorable(IReadOnlyList<string> dbPaths)
    {
        var rows = index.Live();
        return dbPaths.Distinct(StringComparer.Ordinal).Where(p => rows.Find(p) is { } row && IsRestorable(row)).ToList();
    }

    /// <summary>Une copie existe et le fichier qu'elle rendrait n'est pas sur le disque.</summary>
    public bool IsRestorable(ArchiveRow row)
    {
        if (string.IsNullOrEmpty(row.Key)) return false;
        var (destination, _) = ArchiveRules.RestoreTarget(row.Path, row.Key);
        return !File.Exists(pathMap.ToLocal(destination));
    }

    /// <summary>Tester : l'archive (saisie, même non enregistrée) s'ouvre et accepte une écriture.</summary>
    public async Task<ArchiveTestResult> TestAsync(string? root, CancellationToken ct)
    {
        try
        {
            var store = settings.Open(root);
            // Un disque endormi met quelques secondes à répondre : la vérification
            // tourne à part et a sa propre limite.
            await Task.Run(store.Check, ct).WaitAsync(TimeSpan.FromSeconds(60), ct);
            return new ArchiveTestResult(true, "Archive accessible.");
        }
        catch (ArchiveException ex)
        {
            return new ArchiveTestResult(false, ex.Message);
        }
        catch (TimeoutException)
        {
            return new ArchiveTestResult(false, "Archive inaccessible : le disque ne répond pas.");
        }
    }

    /// <summary>
    /// Retirer de l'archive : pierre tombale des lignes, puis effacement des objets que
    /// plus aucune ligne vivante ne référence — un objet partagé par des titres
    /// identiques (dédoublonnage) reste en place pour les autres.
    /// </summary>
    public async Task<int> RemoveAsync(IReadOnlyList<string> dbPaths, CancellationToken ct)
    {
        await locks.Write.WaitAsync(ct);
        try
        {
            var rows = index.Live();
            var targets = dbPaths.Select(rows.Find).OfType<ArchiveRow>().DistinctBy(r => r.Path).ToList();
            if (targets.Count == 0) return 0;
            var keys = targets.Select(r => r.Key).OfType<string>().Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            // Le magasin est ouvert d'abord : une archive injoignable ne doit pas laisser
            // des lignes retirées pointer vers des objets restés en place sans le dire.
            var store = keys.Count > 0 ? settings.Open() : null;

            // Lignes d'abord : un échec ensuite ne laisse qu'un objet orphelin (de la
            // place perdue), jamais une ligne vers un objet disparu.
            var removedPaths = targets.Select(r => r.Path).ToList();
            index.MarkDeleted(removedPaths);
            var removed = removedPaths.ToHashSet(StringComparer.Ordinal);
            var stillUsed = rows.All.Where(r => !removed.Contains(r.Path) && !string.IsNullOrEmpty(r.Key))
                .Select(r => r.Key!).ToHashSet(StringComparer.Ordinal);
            foreach (var key in keys.Where(k => !stillUsed.Contains(k))) store!.Delete(key);

            events.Publish(EventTypes.ArchiveUpdated, new ArchiveUpdatedEvent(removedPaths, ArchiveUpdateReasons.Removed));
            return removedPaths.Count;
        }
        finally
        {
            locks.Write.Release();
        }
    }

    /// <summary>Flux d'un titre lu dans l'archive (copie telle quelle uniquement), ou null.</summary>
    public (BlobReadStream Stream, string Key, string? Sha1)? OpenTrack(string dbPath)
    {
        var row = index.Live().Find(dbPath);
        if (row?.Key is not { Length: > 0 } key || !ArchiveRules.IsSourceCopy(key)) return null;
        var config = settings.Get();
        if (!config.Configured) return null;
        var stream = ArchiveSettings.Open(config).OpenObject(key);
        return stream is null ? null : (stream, key, row.Sha1);
    }

    private static FileInfo? Stat(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? SafeResolve(string root)
    {
        try
        {
            return ArchiveRules.ResolveArchiveFile(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
