using Dapper;
using DeeKeep.Common.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Web;

/// <summary>
/// Base propre à la version web (<c>deekeep_web.db</c>) : tâches, journal
/// d'événements, boîte d'envoi inter-services, curseurs, idempotence, comptes.
/// Séparée de la base existante pour ne rien ajouter à ce que synchronise Turso.
/// </summary>
public sealed class WebDb(IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions _options = options.Value;
    private static readonly object InitLock = new();
    private static bool _initialized;

    public SqliteConnection Open()
    {
        var conn = DeeKeepDb.OpenPath(_options.WebPath, false, _options.BusyTimeoutMs);
        if (!_initialized)
        {
            lock (InitLock)
            {
                if (!_initialized)
                {
                    EnsureSchema(conn);
                    _initialized = true;
                }
            }
        }
        return conn;
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS jobs (
                id TEXT PRIMARY KEY,
                service TEXT NOT NULL,
                type TEXT NOT NULL,
                title TEXT,
                status TEXT NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                progress REAL NOT NULL DEFAULT 0,
                done INTEGER NOT NULL DEFAULT 0,
                failed INTEGER NOT NULL DEFAULT 0,
                total INTEGER NOT NULL DEFAULT 0,
                message TEXT,
                payload TEXT,
                result TEXT,
                error TEXT,
                attempts INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 3,
                group_key TEXT,
                not_before REAL NOT NULL DEFAULT 0,
                created_at REAL NOT NULL,
                updated_at REAL NOT NULL,
                started_at REAL,
                finished_at REAL
            );
            CREATE INDEX IF NOT EXISTS idx_jobs_queue ON jobs (service, status, priority DESC, created_at);
            CREATE INDEX IF NOT EXISTS idx_jobs_updated ON jobs (updated_at);
            CREATE INDEX IF NOT EXISTS idx_jobs_group ON jobs (group_key);

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                service TEXT NOT NULL,
                type TEXT NOT NULL,
                key TEXT,
                data TEXT,
                at REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_events_service ON events (service, id);

            CREATE TABLE IF NOT EXISTS event_cursors (
                consumer TEXT NOT NULL,
                producer TEXT NOT NULL,
                last_id INTEGER NOT NULL,
                PRIMARY KEY (consumer, producer)
            );

            CREATE TABLE IF NOT EXISTS outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                service TEXT NOT NULL,
                target TEXT NOT NULL,
                method TEXT NOT NULL,
                path TEXT NOT NULL,
                body TEXT,
                attempts INTEGER NOT NULL DEFAULT 0,
                next_at REAL NOT NULL,
                created_at REAL NOT NULL,
                last_error TEXT,
                dead INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_outbox_due ON outbox (service, dead, next_at);

            CREATE TABLE IF NOT EXISTS idempotency (
                key TEXT PRIMARY KEY,
                response TEXT,
                created_at REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS users (
                name TEXT PRIMARY KEY,
                password_hash TEXT NOT NULL,
                created_at REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kv (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at REAL NOT NULL
            );
            """);
    }

    public string? GetValue(string key)
    {
        using var conn = Open();
        return conn.ExecuteScalar<string?>("SELECT value FROM kv WHERE key = @key", new { key });
    }

    public void SetValue(string key, string? value)
    {
        using var conn = Open();
        conn.Execute("INSERT INTO kv (key, value, updated_at) VALUES (@key, @value, @now) " +
                     "ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at",
            new { key, value, now = Clock.UnixNow() });
    }
}
