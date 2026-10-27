using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Text;
using Microsoft.Data.Sqlite;

namespace DeeKeep.Library.Tracks;

/// <summary>Jaquette prête à être écrite dans <c>covers</c> (octets bruts, sans retouche).</summary>
public sealed record CoverRow(string Sha1, byte[] Data)
{
    public static CoverRow? From(byte[]? data) => data is { Length: > 0 } ? new CoverRow(CoverImage.Sha1(data), data) : null;

    public static CoverRow? FromBase64(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try { return From(Convert.FromBase64String(text)); }
        catch (FormatException) { return null; }
    }

    /// <summary>Ligne de <c>covers</c> : type et dimensions lus comme <c>db_init.image_info</c>.</summary>
    public IReadOnlyDictionary<string, object?> ToRow()
    {
        var info = CoverImage.DescribeLikePython(Data);
        return new Dictionary<string, object?>
        {
            ["sha1"] = Sha1, ["mime"] = info.Mime, ["bytes"] = (long)Data.Length, ["width"] = info.Width, ["height"] = info.Height, ["data"] = Data,
        };
    }
}

/// <summary>
/// Écritures de la table <c>tracks</c> (et des jaquettes qu'elle désigne), toujours en ligne
/// complète par <see cref="DeeKeepDb.UpsertMany(TableSpec, IEnumerable{IReadOnlyDictionary{string, object?}}, bool, double?)"/> :
/// lire, modifier, réécrire — jamais d'UPDATE partiel.
/// </summary>
public sealed class TrackWriter(DeeKeepDb db)
{
    /// <summary>
    /// Ligne de <c>tracks</c> d'un fichier lu sur le disque (<c>scan_library</c>) :
    /// <c>source = library</c>, hors historique. Les champs d'historique sont ensuite repris
    /// de la ligne existante par <see cref="MergeHistory"/>.
    /// </summary>
    public static Dictionary<string, object?> FromFile(string dbPath, AudioTags tags, DiskState disk, string? coverSha1) => new()
    {
        ["path"] = dbPath,
        ["title"] = tags.Title,
        ["artist"] = tags.Artist,
        ["album"] = tags.Album,
        ["album_artist"] = tags.AlbumArtist,
        ["genre"] = tags.Genre,
        ["year"] = tags.Year,
        ["track_no"] = tags.TrackNo,
        ["duration"] = tags.Duration,
        ["bitrate"] = tags.TrackBitrate is > 0 ? (long?)tags.TrackBitrate : null,
        ["sample_rate"] = (long?)tags.SampleRate,
        ["channels"] = (long?)tags.Channels,
        ["codec"] = tags.Codec,
        ["ext"] = PyPath.SplitExt(dbPath).Ext.ToLowerInvariant().TrimStart('.'),
        ["file_size"] = disk.Size,
        ["file_mtime"] = PythonFloat.Round(disk.MtimeSeconds, 3),
        ["cover_sha1"] = coverSha1,
        ["source"] = "library",
        ["in_history"] = 0L,
        ["history_pos"] = null,
        ["history_cover_sha1"] = null,
    };

    /// <summary>
    /// Reprend sur une ligne relue du disque ce que l'historique savait du titre
    /// (<c>merge_history</c>) : rang, jaquette de l'historique (héritée si le fichier n'en a
    /// pas), tags de base qui ne comblent que les vides.
    /// </summary>
    public static void MergeHistory(Dictionary<string, object?> row, IReadOnlyDictionary<string, object?>? existing)
    {
        if (existing is null || (existing.Long("deleted") ?? 0) != 0) return;
        row["history_cover_sha1"] = existing.Str("history_cover_sha1");
        if ((existing.Long("in_history") ?? 0) == 0) return;
        row["in_history"] = 1L;
        row["history_pos"] = existing.Long("history_pos");
        row["source"] = "both";
        if (string.IsNullOrEmpty(row.Str("cover_sha1"))) row["cover_sha1"] = existing.Str("history_cover_sha1");
        foreach (var field in new[] { "title", "artist", "album", "album_artist", "genre", "year", "track_no" })
            if (string.IsNullOrEmpty(row.Str(field)) && !string.IsNullOrEmpty(existing.Str(field))) row[field] = existing[field];
    }

    /// <summary>Colonnes de contenu seulement : <c>updated_at</c> et <c>h</c> sont recalculés à l'écriture.</summary>
    public static Dictionary<string, object?> Content(IReadOnlyDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>();
        foreach (var column in Schema.Tracks.ContentColumns) result[column] = row.TryGetValue(column, out var v) ? v : null;
        result["deleted"] = 0L;
        return result;
    }

    public static Dictionary<string, object?>? Find(SqliteConnection conn, string dbPath, bool includeDeleted = true)
    {
        var filter = includeDeleted ? "" : " AND deleted = 0";
        return conn.QueryRows($"SELECT * FROM tracks WHERE path = @p{filter}", new { p = dbPath }).FirstOrDefault()
               ?? conn.QueryRows($"SELECT * FROM tracks WHERE lower(path) = lower(@p){filter} ORDER BY deleted", new { p = dbPath }).FirstOrDefault();
    }

    /// <summary>Jaquettes absentes de <c>covers</c> (dédoublonnage par SHA-1), puis titres : dans cet ordre.</summary>
    public void Write(IReadOnlyCollection<CoverRow> covers, IReadOnlyCollection<IReadOnlyDictionary<string, object?>> tracks, IReadOnlyCollection<string>? tombstones = null)
    {
        using var conn = db.Open();
        WriteCovers(conn, covers);
        foreach (var chunk in tracks.Chunk(500))
        {
            using var tx = conn.BeginTransaction(deferred: false);
            DeeKeepDb.UpsertMany(conn, tx, Schema.Tracks, chunk.Select(r => (IReadOnlyDictionary<string, object?>)Content(r)));
            tx.Commit();
        }
        if (tombstones is { Count: > 0 })
        {
            foreach (var chunk in tombstones.Chunk(500))
            {
                using var tx = conn.BeginTransaction(deferred: false);
                DeeKeepDb.UpsertMany(conn, tx, Schema.Tracks, chunk.Select(Tombstone));
                tx.Commit();
            }
        }
    }

    public static IReadOnlyDictionary<string, object?> Tombstone(string path)
    {
        var record = Schema.Tracks.ContentColumns.ToDictionary(c => c, _ => (object?)null);
        record["path"] = path;
        record["deleted"] = 1L;
        return record;
    }

    public static void WriteCovers(SqliteConnection conn, IEnumerable<CoverRow> covers)
    {
        var distinct = covers.GroupBy(c => c.Sha1).Select(g => g.First()).ToList();
        if (distinct.Count == 0) return;
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in distinct.Chunk(400))
            known.UnionWith(conn.QueryRows($"SELECT sha1 FROM covers WHERE deleted = 0 AND sha1 IN ({string.Join(",", chunk.Select((_, i) => "@s" + i))})",
                ToParams(chunk.Select(c => c.Sha1))).Select(r => (string)r["sha1"]!));
        var fresh = distinct.Where(c => !known.Contains(c.Sha1)).ToList();
        foreach (var chunk in fresh.Chunk(100))
        {
            using var tx = conn.BeginTransaction(deferred: false);
            DeeKeepDb.UpsertMany(conn, tx, Schema.Covers, chunk.Select(c => c.ToRow()));
            tx.Commit();
        }
    }

    private static Dapper.DynamicParameters ToParams(IEnumerable<string> values)
    {
        var p = new Dapper.DynamicParameters();
        var i = 0;
        foreach (var v in values) p.Add("s" + i++, v);
        return p;
    }
}
