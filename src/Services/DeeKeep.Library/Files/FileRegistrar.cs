using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.History;
using DeeKeep.Library.Presence;
using DeeKeep.Library.Tracks;

namespace DeeKeep.Library.Files;

/// <summary>
/// Fichiers produits et supprimés : enregistrement d'un fichier écrit par un autre service
/// (extraction, conversion, retéléchargement, restauration) et suppression vers la corbeille.
/// Les deux opérations sont idempotentes (la boîte d'envoi peut les rejouer).
/// </summary>
public sealed class FileRegistrar(
    DeeKeepDb db,
    LibraryPaths paths,
    DiskStateCache disk,
    TrackWriter writer,
    TrackCatalog catalog,
    PresenceService presence,
    EventLog events,
    ILogger<FileRegistrar> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Lit les tags du fichier et met à jour sa ligne (jaquette dédoublonnée dans <c>covers</c>),
    /// en gardant ce que l'historique savait du titre ; ajoute à l'historique si demandé ;
    /// reprend l'historique du fichier remplacé.
    /// </summary>
    public async Task<TrackDto?> RegisterAsync(RegisterFileRequest request, CancellationToken ct)
    {
        var local = request.LocalPath;
        var state = DiskStateCache.Stat(local);
        if (!state.Exists) return null;
        var tags = AudioTags.Read(local);
        var dbPath = paths.ToDb(local);
        var replacedDb = string.IsNullOrEmpty(request.ReplacedLocalPath) ? null : paths.ToDb(request.ReplacedLocalPath);
        if (replacedDb is not null && LibraryPaths.Key(replacedDb) == LibraryPaths.Key(dbPath)) replacedDb = null;

        await Gate.WaitAsync(ct);
        var removed = new List<string>();
        try
        {
            var covers = new List<CoverRow>();
            var embedded = tags.Cover is { } c ? new CoverRow(c.Sha1, c.Data) : null;
            if (embedded is not null) covers.Add(embedded);
            var historyCover = CoverRow.FromBase64(request.HistoryCoverBase64);
            if (historyCover is not null) covers.Add(historyCover);

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var tombstones = new List<string>();
            using (var conn = db.Open(readOnly: true))
            {
                var existing = TrackWriter.Find(conn, dbPath);
                if (existing is not null) dbPath = existing.Str("path") ?? dbPath;
                var replaced = replacedDb is null ? null : TrackWriter.Find(conn, replacedDb, includeDeleted: false);

                var row = TrackWriter.FromFile(dbPath, tags, state, embedded?.Sha1);
                TrackWriter.MergeHistory(row, existing);
                // Le fichier remplacé (réencodage, retéléchargement) lègue sa place dans l'historique.
                if (replaced is not null && (replaced.Long("in_history") ?? 0) != 0 && (existing?.Long("in_history") ?? 0) == 0)
                    TrackWriter.MergeHistory(row, replaced);

                if (request.AddToHistory && (row.Long("in_history") ?? 0) == 0)
                {
                    row["in_history"] = 1L;
                    row["history_pos"] = HistoryService.NextPosition(conn);
                    row["source"] = "both";
                }
                if (historyCover is not null && (row.Long("in_history") ?? 0) != 0) row["history_cover_sha1"] = historyCover.Sha1;
                if (string.IsNullOrEmpty(row.Str("cover_sha1"))) row["cover_sha1"] = row.Str("history_cover_sha1");
                rows.Add(row);

                if (replaced is not null && !File.Exists(paths.ToLocal(replaced.Str("path")!)))
                {
                    // L'ancien fichier a disparu (changement d'extension) : sa ligne n'a plus lieu d'être.
                    tombstones.Add(replaced.Str("path")!);
                }
                else if (replaced is not null && (replaced.Long("in_history") ?? 0) != 0 && (row.Long("in_history") ?? 0) != 0)
                {
                    var dropped = TrackWriter.Content(replaced);
                    dropped["in_history"] = 0L;
                    dropped["history_pos"] = null;
                    dropped["source"] = "library";
                    rows.Add(dropped);
                }
            }
            writer.Write(covers, rows, tombstones);
            removed = tombstones;
        }
        finally
        {
            Gate.Release();
        }

        disk.Invalidate([local, .. removed.Select(paths.ToLocal)]);
        if (removed.Count > 0) presence.Removed(removed);
        presence.Added(dbPath, tags.TitleTagged ? tags.Title : null, tags.Artist, tags.Album);
        events.Publish(EventTypes.TracksChanged, new TracksChangedEvent([dbPath], "register:" + request.Origin));
        if (removed.Count > 0) events.Publish(EventTypes.TracksRemoved, new TracksChangedEvent(removed, "replaced"));
        if (request.AddToHistory) events.Publish(EventTypes.HistoryChanged, new TracksChangedEvent([dbPath], "register"));
        logger.LogInformation("Fichier enregistré ({Origin}) : {Path}", request.Origin, dbPath);
        return catalog.Get(dbPath)?.ToDto();
    }

    /// <summary>
    /// Supprime des fichiers (chemins de la base) : déplacement dans <c>.deekeep-trash</c> à la
    /// racine du dossier de dépôt (ou du dossier du fichier), jamais d'effacement définitif sauf
    /// demande expresse. En base : pierre tombale, sauf pour un titre de l'historique qui reste
    /// consultable (<c>source = history</c>).
    /// </summary>
    public async Task<DeleteFilesResult> DeleteAsync(DeleteFilesRequest request, CancellationToken ct)
    {
        var deleted = new List<string>();
        var failed = new List<string>();
        var errors = new List<string>();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var raw in request.Paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            // Chemins de la base attendus ; un chemin local Linux (« /srv/… ») est aussi accepté.
            var dbPath = raw.StartsWith('/') && File.Exists(raw) ? paths.ToDb(raw) : raw;
            var local = paths.ToLocal(dbPath);
            try
            {
                if (File.Exists(local))
                {
                    if (!paths.IsAllowed(local)) throw new UnauthorizedAccessException("hors des dossiers de la bibliothèque");
                    if (request.ToTrash) MoveToTrash(local, stamp);
                    else File.Delete(local);
                }
                deleted.Add(dbPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed.Add(dbPath);
                errors.Add($"{Path.GetFileName(local)} — {ex.Message}");
            }
        }
        if (deleted.Count > 0) await ForgetAsync(deleted, request.Reason, ct);
        return new DeleteFilesResult(deleted, failed, errors);
    }

    /// <summary>
    /// Effets en base d'une disparition de fichiers (suppression ici ou ailleurs) : pierre
    /// tombale, ou bascule en ligne d'historique ; index, cache, événements.
    /// </summary>
    public async Task ForgetAsync(IReadOnlyList<string> dbPaths, string reason, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        var tombstones = new List<string>();
        var updated = new List<IReadOnlyDictionary<string, object?>>();
        try
        {
            using (var conn = db.Open(readOnly: true))
            {
                foreach (var path in dbPaths)
                {
                    if (File.Exists(paths.ToLocal(path))) continue;
                    var row = TrackWriter.Find(conn, path, includeDeleted: false);
                    if (row is null) continue;
                    if ((row.Long("in_history") ?? 0) != 0)
                    {
                        // L'historique doit rester consultable après un ménage sur le disque.
                        var content = TrackWriter.Content(row);
                        content["source"] = "history";
                        updated.Add(content);
                    }
                    else tombstones.Add(row.Str("path")!);
                }
            }
            writer.Write([], updated, tombstones);
        }
        finally
        {
            Gate.Release();
        }
        disk.Invalidate(dbPaths.Select(paths.ToLocal));
        presence.Removed(dbPaths);
        events.Publish(EventTypes.FilesDeleted, new FilesDeletedEvent(dbPaths, reason));
        if (tombstones.Count > 0) events.Publish(EventTypes.TracksRemoved, new TracksChangedEvent(tombstones, reason));
        if (updated.Count > 0) events.Publish(EventTypes.TracksChanged, new TracksChangedEvent(updated.Select(r => (string)r["path"]!).ToList(), reason));
    }

    private void MoveToTrash(string local, string stamp)
    {
        var root = paths.LocalDownloadDir();
        string trashRoot, relative;
        var full = Path.GetFullPath(local);
        if (Directory.Exists(root) && LibraryPaths.IsUnder(full, Path.GetFullPath(root)))
        {
            trashRoot = Path.Join(root, LibraryPaths.TrashFolder, stamp);
            relative = Path.GetRelativePath(root, full);
        }
        else
        {
            trashRoot = Path.Join(Path.GetDirectoryName(full), LibraryPaths.TrashFolder, stamp);
            relative = Path.GetFileName(full);
        }
        var target = Path.Join(trashRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) target = Path.Join(Path.GetDirectoryName(target), $"{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}{Path.GetExtension(target)}");
        File.Move(full, target);
        logger.LogInformation("Mis à la corbeille : {Path} → {Target}", local, target);
    }
}
