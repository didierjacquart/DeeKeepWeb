using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.Presence;
using DeeKeep.Library.Tracks;

namespace DeeKeep.Library.History;

/// <summary>
/// Historique des téléchargements, tenu dans <c>tracks</c> (<c>in_history</c>, <c>history_pos</c>,
/// <c>history_cover_sha1</c>, <c>source</c> — spec 05 §2.5.1) : liste, purge des introuvables,
/// vidage, réparation des chemins par nom de fichier (spec 01 §3.10).
/// </summary>
public sealed class HistoryService(DeeKeepDb db, LibraryPaths paths, DiskStateCache disk, TrackWriter writer, EventLog events, ILogger<HistoryService> logger)
{
    private readonly object _lock = new();

    /// <summary>Entrées dans l'ordre de l'historique (rang 0 = le plus ancien téléchargement).</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        using var conn = db.Open(readOnly: true);
        var rows = conn.QueryRows("""
            SELECT path, title, artist, album, COALESCE(history_cover_sha1, cover_sha1) AS sha1, history_pos, file_mtime
            FROM tracks WHERE in_history = 1 AND deleted = 0 ORDER BY history_pos
            """).ToList();
        return rows.Select(r =>
        {
            var path = r.Str("path") ?? "";
            var state = disk.Get(paths.ToLocal(path));
            return new HistoryEntry(path, r.Str("title"), r.Str("artist"), r.Str("album"), r.Str("sha1"), (int)(r.Long("history_pos") ?? 0),
                state.Exists, state.Exists ? state.MtimeSeconds : r.Dbl("file_mtime"));
        }).ToList();
    }

    /// <summary>Rang du prochain téléchargement (ajouté en fin, comme <c>downloaded_history.append</c>).</summary>
    public static long NextPosition(Microsoft.Data.Sqlite.SqliteConnection conn) =>
        (conn.QueryRows("SELECT MAX(history_pos) AS m FROM tracks WHERE in_history = 1 AND deleted = 0").First().Long("m") ?? -1) + 1;

    /// <summary>« Purger les fichiers introuvables » : retire les entrées dont le fichier n'existe plus.</summary>
    public HistoryChangeResult PurgeMissing()
    {
        int removed;
        lock (_lock)
        {
            var rows = HistoryRows();
            var missing = rows.Where(r => !disk.Get(paths.ToLocal(r.Str("path") ?? "")).Exists || string.IsNullOrEmpty(r.Str("path"))).ToList();
            removed = Drop(missing, "purge");
        }
        return new HistoryChangeResult(removed, removed > 0
            ? $"{removed} entrée(s) purgée(s) : fichier(s) introuvable(s) sur le disque."
            : "Rien à purger : tous les fichiers de l'historique existent sur le disque.");
    }

    /// <summary>« Vider l'historique » : retire toutes les entrées (les fichiers du disque sont conservés).</summary>
    public HistoryChangeResult Clear()
    {
        int removed;
        lock (_lock) removed = Drop(HistoryRows(), "clear");
        return new HistoryChangeResult(removed, removed > 0 ? $"{removed} entrée(s) retirée(s) de l'historique." : "L'historique est déjà vide.");
    }

    private List<Dictionary<string, object?>> HistoryRows()
    {
        using var conn = db.Open(readOnly: true);
        return conn.QueryRows("SELECT * FROM tracks WHERE in_history = 1 AND deleted = 0").ToList();
    }

    /// <summary>
    /// Sortie de l'historique (<c>_mirror_history</c>) : on lève le drapeau, et on ne pose une
    /// pierre tombale que si le titre n'était connu que par l'historique (fichier absent).
    /// <c>history_cover_sha1</c> est conservé.
    /// </summary>
    private int Drop(IReadOnlyCollection<Dictionary<string, object?>> rows, string reason)
    {
        if (rows.Count == 0) return 0;
        var updated = new List<IReadOnlyDictionary<string, object?>>();
        var tombstones = new List<string>();
        foreach (var row in rows)
        {
            var path = row.Str("path") ?? "";
            if (row.Str("source") == "history" && !disk.Get(paths.ToLocal(path)).Exists)
            {
                tombstones.Add(path);
                continue;
            }
            var content = TrackWriter.Content(row);
            content["in_history"] = 0L;
            content["history_pos"] = null;
            content["source"] = "library";
            updated.Add(content);
        }
        writer.Write([], updated, tombstones);
        logger.LogInformation("Historique ({Reason}) : {Updated} titre(s) sorti(s), {Removed} pierre(s) tombale(s)", reason, updated.Count, tombstones.Count);
        Publish(updated.Select(r => (string)r["path"]!).ToList(), tombstones, reason);
        return rows.Count;
    }

    /// <summary>
    /// Réparation des chemins (<c>_repair_history_paths</c>) : une entrée dont le fichier a
    /// disparu et que l'index ne connaît pas est rattachée au fichier indexé de même nom,
    /// s'il est unique et pas déjà réclamé. Rend le nombre d'entrées réparées.
    /// </summary>
    public int Repair(Func<string, bool> indexed, Func<string, IReadOnlyList<string>> pathsNamed)
    {
        lock (_lock)
        {
            var rows = HistoryRows().OrderBy(r => r.Long("history_pos") ?? 0).ToList();
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var stale = new List<Dictionary<string, object?>>();
            foreach (var row in rows)
            {
                var path = row.Str("path") ?? "";
                if (path.Length == 0) continue;
                if (indexed(path) || disk.Get(paths.ToLocal(path)).Exists) claimed.Add(LibraryPaths.Key(path));
                else stale.Add(row);
            }
            if (stale.Count == 0) return 0;

            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            var moves = new List<(Dictionary<string, object?> Row, string Target)>();
            foreach (var row in stale)
            {
                var path = row.Str("path")!;
                var key = LibraryPaths.Key(path);
                if (!resolved.TryGetValue(key, out var found))
                {
                    var free = pathsNamed(PyPath.Basename(path)).Where(c => !claimed.Contains(LibraryPaths.Key(c))).ToList();
                    if (free.Count != 1) continue;
                    found = free[0];
                    resolved[key] = found;
                    claimed.Add(LibraryPaths.Key(found));
                }
                moves.Add((row, found));
            }
            if (moves.Count == 0) return 0;

            var updated = new List<IReadOnlyDictionary<string, object?>>();
            var tombstones = new List<string>();
            using (var conn = db.Open(readOnly: true))
            {
                foreach (var (row, target) in moves)
                {
                    var current = TrackWriter.Find(conn, target);
                    var content = current is not null && (current.Long("deleted") ?? 0) == 0 ? TrackWriter.Content(current) : TrackWriter.Content(row);
                    content["path"] = current?.Str("path") ?? target;
                    foreach (var field in new[] { "title", "artist", "album", "album_artist", "genre", "year", "track_no" })
                        if (string.IsNullOrEmpty(content.Str(field)) && !string.IsNullOrEmpty(row.Str(field))) content[field] = row[field];
                    content["in_history"] = 1L;
                    content["history_pos"] = row.Long("history_pos");
                    content["history_cover_sha1"] = row.Str("history_cover_sha1") ?? content.Str("history_cover_sha1");
                    content["source"] = "both";
                    if (string.IsNullOrEmpty(content.Str("cover_sha1"))) content["cover_sha1"] = content.Str("history_cover_sha1");
                    updated.Add(content);
                    // L'ancienne ligne n'était connue que par l'historique : pierre tombale ; sinon elle redevient « library ».
                    var old = row.Str("path")!;
                    if (row.Str("source") == "history") tombstones.Add(old);
                    else
                    {
                        var dropped = TrackWriter.Content(row);
                        dropped["in_history"] = 0L;
                        dropped["history_pos"] = null;
                        dropped["source"] = "library";
                        updated.Add(dropped);
                    }
                }
            }
            writer.Write([], updated, tombstones);
            logger.LogInformation("[historique] {Count} chemin(s) rattrapé(s) après déplacement de la bibliothèque.", moves.Count);
            Publish(updated.Select(r => (string)r["path"]!).ToList(), tombstones, "history-repair");
            return moves.Count;
        }
    }

    private void Publish(IReadOnlyList<string> changed, IReadOnlyList<string> removed, string reason)
    {
        if (changed.Count > 0) events.Publish(EventTypes.TracksChanged, new TracksChangedEvent(changed, reason));
        if (removed.Count > 0) events.Publish(EventTypes.TracksRemoved, new TracksChangedEvent(removed, reason));
        events.Publish(EventTypes.HistoryChanged, new TracksChangedEvent(changed.Concat(removed).ToList(), reason));
    }
}
