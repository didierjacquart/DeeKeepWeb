namespace DeeKeep.Common.Data;

/// <summary>
/// Description d'une table synchronisée de la base existante
/// (portage de <c>deekeep/db/db_schema.py</c>). L'ordre des colonnes compte :
/// il entre dans l'empreinte de ligne.
/// </summary>
public sealed record TableSpec(string Name, string Pk, IReadOnlyList<(string Name, string Type)> Columns, bool Ordered = false)
{
    public IEnumerable<string> ContentColumns => Columns.Select(c => c.Name);

    public IReadOnlyList<string> AllColumns { get; } =
        Columns.Select(c => c.Name).Concat(["updated_at", "deleted", "h"]).ToList();

    public string CreateSql()
    {
        var parts = Columns.Select(c => c.Name == Pk ? $"{c.Name} {c.Type} PRIMARY KEY" : $"{c.Name} {c.Type}")
            .Concat(["updated_at REAL NOT NULL DEFAULT 0", "deleted INTEGER NOT NULL DEFAULT 0", "h INTEGER NOT NULL DEFAULT 0"]);
        return $"CREATE TABLE IF NOT EXISTS {Name} (\n    {string.Join(",\n    ", parts)}\n)";
    }
}

public static class Schema
{
    public const int SchemaVersion = 1;

    public static readonly TableSpec AppSettings = new("app_settings", "key", [("key", "TEXT"), ("value", "TEXT")]);

    public static readonly TableSpec Searches = new("searches", "query", [("query", "TEXT"), ("position", "INTEGER")], Ordered: true);

    public static readonly TableSpec Covers = new("covers", "sha1",
    [
        ("sha1", "TEXT"), ("mime", "TEXT"), ("bytes", "INTEGER"), ("width", "INTEGER"), ("height", "INTEGER"), ("data", "BLOB"),
    ]);

    public static readonly TableSpec Tracks = new("tracks", "path",
    [
        ("path", "TEXT"), ("title", "TEXT"), ("artist", "TEXT"), ("album", "TEXT"), ("album_artist", "TEXT"),
        ("genre", "TEXT"), ("year", "TEXT"), ("track_no", "TEXT"), ("duration", "REAL"), ("bitrate", "INTEGER"),
        ("sample_rate", "INTEGER"), ("channels", "INTEGER"), ("codec", "TEXT"), ("ext", "TEXT"), ("file_size", "INTEGER"),
        ("file_mtime", "REAL"), ("cover_sha1", "TEXT"), ("source", "TEXT"), ("in_history", "INTEGER"),
        ("history_pos", "INTEGER"), ("history_cover_sha1", "TEXT"),
    ]);

    public static readonly TableSpec Quality = new("quality", "path",
        [("path", "TEXT"), ("stamp", "TEXT"), ("measures", "TEXT"), ("defects", "TEXT")]);

    public static readonly TableSpec Archive = new("archive", "path",
    [
        ("path", "TEXT"), ("sha1", "TEXT"), ("stamp", "TEXT"), ("key", "TEXT"), ("bytes", "INTEGER"),
        ("bitrate", "INTEGER"), ("source_bytes", "INTEGER"), ("uploaded_at", "REAL"),
    ]);

    public static readonly TableSpec ReferenceDurations = new("reference_durations", "key",
        [("key", "TEXT"), ("durations", "TEXT"), ("fetched_at", "REAL")]);

    /// <summary>Ordre d'écriture : jaquettes avant titres, archive après eux.</summary>
    public static readonly IReadOnlyList<TableSpec> TableOrder =
        [AppSettings, Searches, Covers, Tracks, Quality, Archive, ReferenceDurations];

    public static readonly IReadOnlyDictionary<string, TableSpec> ByName = TableOrder.ToDictionary(t => t.Name);

    public const string SchemaMetaSql = "CREATE TABLE IF NOT EXISTS schema_meta (    key TEXT PRIMARY KEY,    value TEXT)";

    public const string SyncStateSql =
        "CREATE TABLE IF NOT EXISTS sync_state (    table_name TEXT PRIMARY KEY,    synced_count INTEGER NOT NULL DEFAULT -1," +
        "    synced_sum INTEGER NOT NULL DEFAULT -1,    synced_at REAL NOT NULL DEFAULT 0)";

    public const string OutboxSql =
        "CREATE TABLE IF NOT EXISTS sync_outbox (    table_name TEXT NOT NULL,    pk TEXT NOT NULL,    queued_at REAL NOT NULL," +
        "    PRIMARY KEY (table_name, pk))";

    public static readonly IReadOnlyList<string> IndexSql =
    [
        "CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks (artist)",
        "CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks (album)",
        "CREATE INDEX IF NOT EXISTS idx_tracks_cover ON tracks (cover_sha1)",
        "CREATE INDEX IF NOT EXISTS idx_tracks_history ON tracks (in_history)",
        "CREATE INDEX IF NOT EXISTS idx_tracks_updated ON tracks (updated_at)",
        "CREATE INDEX IF NOT EXISTS idx_quality_updated ON quality (updated_at)",
        "CREATE INDEX IF NOT EXISTS idx_mirror_sha1 ON archive (sha1)",
        "CREATE INDEX IF NOT EXISTS idx_mirror_updated ON archive (updated_at)",
    ];

    public static IEnumerable<string> SharedDdl() =>
        new[] { SchemaMetaSql }.Concat(TableOrder.Select(t => t.CreateSql())).Concat(IndexSql);

    public static IEnumerable<string> LocalOnlyDdl() => [SyncStateSql, OutboxSql];

    public static string FingerprintSql(string table) => $"SELECT COUNT(*), COALESCE(SUM(h), 0) FROM {table}";
}
