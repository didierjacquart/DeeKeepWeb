using DeeKeep.Common.Data;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.Users;

namespace DeeKeep.Library.Tracks;

/// <summary>
/// Lecture de la bibliothèque : lignes vivantes de <c>tracks</c>, état sur le disque, état
/// d'archive lu dans la table <c>archive</c> (jointure exacte par chemin, comme l'original).
/// </summary>
public sealed class TrackCatalog(DeeKeepDb db, LibraryPaths paths, DiskStateCache disk, BitrateBackfill backfill, UserTracks users)
{
    /// <summary>Toutes les entrées, dans l'ordre de la table (ordre de départ du tri stable).</summary>
    public List<LibraryEntry> Load()
    {
        List<LibraryEntry> entries;
        Dictionary<string, ArchiveRow> archive;
        using (var conn = db.Open(readOnly: true))
        {
            entries = conn.QueryRows("SELECT * FROM tracks WHERE deleted = 0").Select(LibraryEntry.FromRow).ToList();
            archive = LoadArchive(conn);
        }
        var root = LibraryRoot();
        foreach (var e in entries) e.LocalPath = paths.ToLocal(e.Path);
        var states = disk.GetMany(entries.Select(e => e.LocalPath).ToList());
        foreach (var e in entries)
        {
            Apply(e, states[e.LocalPath], root);
            e.Archive = archive.GetValueOrDefault(e.Path);
        }

        var unknownBitrate = entries.Where(e => e.Exists && (e.Bitrate ?? 0) == 0).Select(e => e.Path).ToList();
        if (unknownBitrate.Count > 0) backfill.Enqueue(unknownBitrate);
        return entries;
    }

    public LibraryEntry? Get(string dbPath)
    {
        using var conn = db.Open(readOnly: true);
        var row = conn.QueryRows("SELECT * FROM tracks WHERE path = @p AND deleted = 0", new { p = dbPath }).FirstOrDefault()
                  ?? conn.QueryRows("SELECT * FROM tracks WHERE lower(path) = lower(@p) AND deleted = 0", new { p = dbPath }).FirstOrDefault();
        if (row is null) return null;
        var entry = LibraryEntry.FromRow(row);
        Enrich(entry, LibraryRoot());
        entry.Archive = conn.QueryRows("SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes FROM archive WHERE path = @p AND deleted = 0",
            new { p = entry.Path }).Select(ToArchive).FirstOrDefault();
        return entry;
    }

    public TrackPage Query(TrackQuery query)
    {
        var all = Load();
        var tokens = TrackSearch.Tokens(query.Search);
        var folderKey = string.IsNullOrWhiteSpace(query.Folder) ? null : LibraryPaths.Key(query.Folder).TrimEnd('\\') + "\\";
        // Bibliothèque personnelle : les seuls titres de l'utilisateur.
        var mine = string.IsNullOrEmpty(query.UserId) ? null : users.KeysOf(query.UserId);
        var filtered = all.Where(e =>
            (mine is null || mine.Contains(LibraryPaths.Key(e.Path)))
            && e.Selectable(query.IncludeMissing)
            && (!query.HistoryOnly || e.InHistory)
            && (folderKey is null || LibraryPaths.Key(e.Path).StartsWith(folderKey, StringComparison.Ordinal))
            && (tokens.Count == 0 || TrackSearch.Matches(TrackSearch.Haystack(e), tokens)));
        var sorted = TrackSearch.Sort(filtered, query.SortBy, query.Descending).ToList();
        var limit = query.Limit <= 0 ? sorted.Count : query.Limit;
        var names = query.IncludeUsers ? users.NamesByKey() : null;
        var page = sorted.Skip(Math.Max(0, query.Offset)).Take(limit)
            .Select(e => names is null ? e.ToDto() : e.ToDto() with { Users = names.GetValueOrDefault(LibraryPaths.Key(e.Path)) ?? [] })
            .ToList();
        return new TrackPage(page, sorted.Count, all.Count(e => e.Exists), all.Count(e => !e.Exists));
    }

    public IReadOnlyList<TagSuggestion> Suggest(string? text, int limit, bool includeMissing = false)
    {
        if (PyTextLength(text) < TrackSearch.MinSuggestChars) return [];
        var values = TrackSearch.BuildValueIndex(Load().Where(e => e.Selectable(includeMissing)));
        return TrackSearch.Suggest(values, text, Math.Clamp(limit, 1, 20));
    }

    private static int PyTextLength(string? text) => DeeKeep.Common.Text.PyText.Strip(text ?? "").Length;

    /// <summary>Dossier de sortie choisi (chemin complet), ou null s'il est illisible.</summary>
    private string? LibraryRoot()
    {
        try { return Path.GetFullPath(paths.LocalDownloadDir()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>
    /// « Sur le disque » = le fichier existe <b>dans le dossier de sortie choisi</b> : un fichier
    /// resté ailleurs (ancien dossier, autre lecteur) compte comme absent, comme un titre supprimé.
    /// </summary>
    private void Enrich(LibraryEntry entry, string? root)
    {
        entry.LocalPath = paths.ToLocal(entry.Path);
        Apply(entry, disk.Get(entry.LocalPath), root);
    }

    private static void Apply(LibraryEntry entry, DiskState state, string? root)
    {
        entry.Exists = state.Exists && root is not null && IsInside(entry.LocalPath, root);
        entry.Stamp = state.ArchiveStamp;
        // La taille du disque fait foi, comme dans l'original.
        if (state.Exists) entry.FileSize = state.Size;
    }

    private static bool IsInside(string localPath, string root)
    {
        try { return LibraryPaths.IsUnder(Path.GetFullPath(localPath), root); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static Dictionary<string, ArchiveRow> LoadArchive(Microsoft.Data.Sqlite.SqliteConnection conn) =>
        conn.QueryRows("SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes FROM archive WHERE deleted = 0")
            .Select(ToArchive)
            .GroupBy(a => a.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static ArchiveRow ToArchive(Dictionary<string, object?> r) =>
        new(r.Str("path") ?? "", r.Str("sha1"), r.Str("stamp"), r.Str("key"), r.Long("bytes"), (int?)r.Long("bitrate"), r.Long("source_bytes"));
}
