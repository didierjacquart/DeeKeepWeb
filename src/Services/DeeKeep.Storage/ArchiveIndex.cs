using DeeKeep.Common.Data;
using DeeKeep.Common.Text;

namespace DeeKeep.Storage;

/// <summary>Ligne vivante de la table <c>archive</c> (index chemin → objet, dans la base existante).</summary>
public sealed record ArchiveRow(
    string Path,
    string? Sha1,
    string? Stamp,
    string? Key,
    long? Bytes,
    long? Bitrate,
    long? SourceBytes,
    double? UploadedAt)
{
    /// <summary>Ligne complète pour <see cref="DeeKeepDb.UpsertMany"/> (lire-modifier-écrire).</summary>
    public IReadOnlyDictionary<string, object?> ToRecord() => new Dictionary<string, object?>
    {
        ["path"] = Path,
        ["sha1"] = Sha1,
        ["stamp"] = Stamp,
        ["key"] = Key,
        ["bytes"] = Bytes,
        ["bitrate"] = Bitrate,
        ["source_bytes"] = SourceBytes,
        ["uploaded_at"] = UploadedAt,
    };
}

/// <summary>Titre de la table <c>tracks</c>, réduit à ce dont l'archive a besoin.</summary>
public sealed record TrackRef(string Path, long? FileSize, long? Bitrate);

/// <summary>Toutes les lignes vivantes, indexées par chemin exact puis sans tenir compte de la casse.</summary>
public sealed class ArchiveRows
{
    private readonly Dictionary<string, ArchiveRow> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchiveRow> _byMatch = new(StringComparer.Ordinal);

    public ArchiveRows(IEnumerable<ArchiveRow> rows)
    {
        foreach (var row in rows)
        {
            _exact[row.Path] = row;
            _byMatch.TryAdd(LibraryText.MatchKey(row.Path), row);
        }
    }

    public IEnumerable<ArchiveRow> All => _exact.Values;

    public int Count => _exact.Count;

    /// <summary>
    /// Ligne d'un chemin : égalité exacte d'abord (la jointure de l'original), puis
    /// sans la casse (convention de la version web pour les chemins).
    /// </summary>
    public ArchiveRow? Find(string dbPath) =>
        _exact.TryGetValue(dbPath, out var row) ? row : _byMatch.GetValueOrDefault(LibraryText.MatchKey(dbPath));
}

/// <summary>
/// Accès à la table <c>archive</c>, dont le service Storage est le seul
/// propriétaire : écritures par <see cref="DeeKeepDb.UpsertMany"/> (ligne complète,
/// <c>updated_at</c>, empreinte <c>h</c>) et pierres tombales.
/// </summary>
public sealed class ArchiveIndex(DeeKeepDb db)
{
    public ArchiveRows Live()
    {
        using var conn = db.Open(readOnly: true);
        return new ArchiveRows(conn.QueryRows(
                "SELECT path, sha1, stamp, key, bytes, bitrate, source_bytes, uploaded_at FROM archive WHERE deleted = 0")
            .Select(r => new ArchiveRow(r.Str("path")!, r.Str("sha1"), r.Str("stamp"), r.Str("key"), r.Long("bytes"),
                r.Long("bitrate"), r.Long("source_bytes"), r.Dbl("uploaded_at")))
            .ToList());
    }

    public IReadOnlyList<TrackRef> Tracks()
    {
        using var conn = db.Open(readOnly: true);
        return conn.QueryRows("SELECT path, file_size, bitrate FROM tracks WHERE deleted = 0")
            .Select(r => new TrackRef(r.Str("path")!, r.Long("file_size"), r.Long("bitrate")))
            .ToList();
    }

    public void Upsert(ArchiveRow row) => db.UpsertMany(Schema.Archive, [row.ToRecord()]);

    public void MarkDeleted(IReadOnlyCollection<string> paths) => db.MarkDeleted(Schema.Archive, paths);
}
