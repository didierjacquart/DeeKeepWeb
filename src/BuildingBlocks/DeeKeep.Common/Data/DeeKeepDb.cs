using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Data;

public sealed class DatabaseOptions
{
    /// <summary>Base existante (<c>deekeep_cache.db</c>).</summary>
    public string MainPath { get; set; } = "/var/lib/deekeep/deekeep_cache.db";

    /// <summary>Base propre à la version web : tâches, événements, boîtes d'envoi, idempotence, comptes.</summary>
    public string WebPath { get; set; } = "/var/lib/deekeep/deekeep_web.db";

    public int BusyTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// Accès à la base SQLite existante, partagée entre processus (WAL).
/// Les écritures passent par <see cref="UpsertMany"/> qui reproduit
/// <c>LocalDB.upsert_many</c> : ligne complète, horodatage et empreinte <c>h</c>.
/// Turso n'étant plus utilisé, <c>sync_outbox</c> n'est alimentée que sur demande
/// (<c>queue: true</c>).
/// </summary>
public sealed class DeeKeepDb(IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions _options = options.Value;

    public string MainPath => _options.MainPath;

    public SqliteConnection Open(bool readOnly = false) => OpenPath(_options.MainPath, readOnly, _options.BusyTimeoutMs);

    public SqliteConnection OpenWeb() => OpenPath(_options.WebPath, false, _options.BusyTimeoutMs);

    public static SqliteConnection OpenPath(string path, bool readOnly, int busyTimeoutMs)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !readOnly) Directory.CreateDirectory(dir);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = Math.Max(1, busyTimeoutMs / 1000),
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout = {busyTimeoutMs};" +
                          (readOnly ? "" : "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;") +
                          "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    /// <summary>Crée les tables manquantes (sans jamais toucher aux données existantes).</summary>
    public void EnsureSchema()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var sql in Schema.SharedDdl().Concat(Schema.LocalOnlyDdl())) conn.Execute(sql, transaction: tx);
        var version = conn.ExecuteScalar<string>("SELECT value FROM schema_meta WHERE key = 'schema_version'", transaction: tx);
        if (version is null)
            conn.Execute("INSERT INTO schema_meta (key, value) VALUES ('schema_version', @v)", new { v = Schema.SchemaVersion.ToString() }, tx);
        tx.Commit();
    }

    // ------------------------------------------------------------------
    // Écriture des tables synchronisées
    // ------------------------------------------------------------------

    public int Upsert(TableSpec spec, IReadOnlyDictionary<string, object?> row, bool queue = false, double? updatedAt = null) =>
        UpsertMany(spec, [row], queue, updatedAt);

    public int UpsertMany(TableSpec spec, IEnumerable<IReadOnlyDictionary<string, object?>> rows, bool queue = false, double? updatedAt = null)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction(deferred: false);
        var n = UpsertMany(conn, tx, spec, rows, queue, updatedAt);
        tx.Commit();
        return n;
    }

    public static int UpsertMany(SqliteConnection conn, SqliteTransaction tx, TableSpec spec,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows, bool queue = false, double? updatedAt = null)
    {
        var columns = spec.AllColumns;
        var sql = $"INSERT INTO {spec.Name} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))}) " +
                  $"ON CONFLICT({spec.Pk}) DO UPDATE SET {string.Join(", ", columns.Where(c => c != spec.Pk).Select(c => $"{c} = excluded.{c}"))}";
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var parameters = columns.Select((_, i) => cmd.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value))).ToArray();

        using var outbox = conn.CreateCommand();
        outbox.Transaction = tx;
        outbox.CommandText = "INSERT INTO sync_outbox (table_name, pk, queued_at) VALUES ($t, $pk, $q) " +
                             "ON CONFLICT(table_name, pk) DO UPDATE SET queued_at = excluded.queued_at";
        var pt = outbox.Parameters.Add("$t", SqliteType.Text);
        var ppk = outbox.Parameters.Add("$pk", SqliteType.Text);
        var pq = outbox.Parameters.Add("$q", SqliteType.Real);

        var now = Clock.UnixNow();
        var count = 0;
        foreach (var source in rows)
        {
            var record = new Dictionary<string, object?>(source);
            record["deleted"] = record.TryGetValue("deleted", out var d) && d is not null && Convert.ToBoolean(d is long l ? l != 0 : d) ? 1L : 0L;
            var existing = record.TryGetValue("updated_at", out var u) && u is not null && Convert.ToDouble(u) != 0 ? Convert.ToDouble(u) : (double?)null;
            record["updated_at"] = updatedAt ?? existing ?? now;
            record["h"] = RowHash.Compute(spec, record);
            for (var i = 0; i < columns.Count; i++)
                parameters[i].Value = ToDbValue(record.TryGetValue(columns[i], out var v) ? v : null);
            cmd.ExecuteNonQuery();
            if (queue)
            {
                pt.Value = spec.Name;
                ppk.Value = ToDbValue(record[spec.Pk]);
                pq.Value = now;
                outbox.ExecuteNonQuery();
            }
            count++;
        }
        return count;
    }

    /// <summary>Pierre tombale : la ligne reste, vidée de son contenu, <c>deleted = 1</c>.</summary>
    public int MarkDeleted(TableSpec spec, IEnumerable<string> pkValues, bool queue = false) =>
        UpsertMany(spec, pkValues.Select(pk =>
        {
            var record = spec.ContentColumns.ToDictionary(c => c, _ => (object?)null);
            record[spec.Pk] = pk;
            record["deleted"] = 1L;
            return (IReadOnlyDictionary<string, object?>)record;
        }).ToList(), queue);

    public static object ToDbValue(object? value) => value switch
    {
        null => DBNull.Value,
        bool b => b ? 1L : 0L,
        JsonNode n => n.ToJsonString(),
        _ => value,
    };

    // ------------------------------------------------------------------
    // Lecture
    // ------------------------------------------------------------------

    public Dictionary<string, object?>? GetRow(TableSpec spec, string pk)
    {
        using var conn = Open(readOnly: false);
        return conn.QueryRows($"SELECT * FROM {spec.Name} WHERE {spec.Pk} = @pk", new { pk }).FirstOrDefault();
    }

    // ------------------------------------------------------------------
    // Paramètres (app_settings, valeurs JSON)
    // ------------------------------------------------------------------

    public Dictionary<string, JsonNode?> AllSettings()
    {
        using var conn = Open();
        var result = new Dictionary<string, JsonNode?>();
        foreach (var row in conn.QueryRows("SELECT key, value FROM app_settings WHERE deleted = 0"))
            result[(string)row["key"]!] = SettingsJson.Parse(row["value"] as string);
        return result;
    }

    public void SetSettings(IReadOnlyDictionary<string, JsonNode?> values, bool queue = false) =>
        UpsertMany(Schema.AppSettings, values.Select(kv => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["key"] = kv.Key,
            ["value"] = SettingsJson.Dump(kv.Value),
        }).ToList(), queue);
}

/// <summary>JSON compact, clés triées, non-ASCII conservé : identique à <c>db_schema.json_dump</c>.</summary>
public static class SettingsJson
{
    public static JsonNode? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    public static string Dump(JsonNode? node)
    {
        var buffer = new System.Text.StringBuilder();
        Write(node, buffer);
        return buffer.ToString();
    }

    public static string Dump<T>(T value) => Dump(JsonSerializer.SerializeToNode(value));

    private static void Write(JsonNode? node, System.Text.StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append('{');
                var first = true;
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(kv.Key, sb);
                    sb.Append(':');
                    Write(kv.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray arr:
                sb.Append('[');
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(arr[i], sb);
                }
                sb.Append(']');
                break;
            case JsonValue val:
                if (val.TryGetValue<string>(out var s)) WriteString(s, sb);
                else if (val.TryGetValue<bool>(out var b)) sb.Append(b ? "true" : "false");
                else if (val.TryGetValue<double>(out var dbl) && !val.TryGetValue<long>(out _))
                    sb.Append(PythonFloat.Repr(dbl));
                else sb.Append(val.ToJsonString());
                break;
        }
    }

    private static void WriteString(string s, System.Text.StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

public static class Clock
{
    public static double UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
