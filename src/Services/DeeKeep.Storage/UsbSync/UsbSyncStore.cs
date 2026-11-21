using System.Text.Json;
using Dapper;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Mémoire de la synchronisation dans <c>deekeep_web.db</c> : suppressions faites dans
/// l'application en attente de propagation (pierres tombales) et bilan du dernier passage.
/// </summary>
public sealed class UsbSyncStore(WebDb webDb)
{
    private bool _ready;

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = webDb.Open();
        if (_ready) return conn;
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS storage_usbsync_tombstones (
                key TEXT PRIMARY KEY,
                relative TEXT NOT NULL,
                deleted_at REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS storage_usbsync_pairs (
                key TEXT PRIMARY KEY,
                relative TEXT NOT NULL,
                lib_size INTEGER NOT NULL,
                lib_mtime REAL NOT NULL,
                usb_size INTEGER NOT NULL,
                usb_mtime REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS storage_usbsync_state (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL);
            """);
        _ready = true;
        return conn;
    }

    public IReadOnlyList<SyncTombstone> Tombstones()
    {
        using var conn = Open();
        return conn.Query<(string Relative, double DeletedAt)>("SELECT relative, deleted_at FROM storage_usbsync_tombstones")
            .Select(r => new SyncTombstone(r.Relative, DateTime.UnixEpoch.AddSeconds(r.DeletedAt)))
            .ToList();
    }

    public int TombstoneCount()
    {
        using var conn = Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM storage_usbsync_tombstones");
    }

    public void AddTombstones(IEnumerable<(string Relative, double DeletedAt)> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var (relative, at) in items)
            conn.Execute("""
                INSERT INTO storage_usbsync_tombstones (key, relative, deleted_at) VALUES (@key, @relative, @at)
                ON CONFLICT(key) DO UPDATE SET relative = excluded.relative, deleted_at = MAX(deleted_at, excluded.deleted_at)
                """, new { key = UsbSyncPlanner.Key(relative), relative, at }, tx);
        tx.Commit();
    }

    public void ForgetTombstones(IEnumerable<string> relatives)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var relative in relatives)
            conn.Execute("DELETE FROM storage_usbsync_tombstones WHERE key = @key", new { key = UsbSyncPlanner.Key(relative) }, tx);
        tx.Commit();
    }

    public IReadOnlyList<SyncedPair> SyncedPairs()
    {
        using var conn = Open();
        return conn.Query<(string Relative, long LibSize, double LibMtime, long UsbSize, double UsbMtime)>(
                "SELECT relative, lib_size, lib_mtime, usb_size, usb_mtime FROM storage_usbsync_pairs")
            .Select(r => new SyncedPair(r.Relative, r.LibSize, DateTime.UnixEpoch.AddSeconds(r.LibMtime), r.UsbSize,
                DateTime.UnixEpoch.AddSeconds(r.UsbMtime)))
            .ToList();
    }

    /// <summary>Retient l'état des deux côtés juste après une opération (l'oublie quand un côté manque).</summary>
    public void SavePair(string relative, SyncFile? library, SyncFile? usb)
    {
        using var conn = Open();
        if (library is null || usb is null)
        {
            conn.Execute("DELETE FROM storage_usbsync_pairs WHERE key = @key", new { key = UsbSyncPlanner.Key(relative) });
            return;
        }
        conn.Execute(
            "INSERT INTO storage_usbsync_pairs (key, relative, lib_size, lib_mtime, usb_size, usb_mtime) " +
            "VALUES (@key, @relative, @ls, @lm, @us, @um) " +
            "ON CONFLICT(key) DO UPDATE SET relative = excluded.relative, lib_size = excluded.lib_size, lib_mtime = excluded.lib_mtime, " +
            "usb_size = excluded.usb_size, usb_mtime = excluded.usb_mtime",
            new
            {
                key = UsbSyncPlanner.Key(relative), relative, ls = library.Size, lm = Unix(library.ModifiedUtc),
                us = usb.Size, um = Unix(usb.ModifiedUtc),
            });
    }

    private static double Unix(DateTime utc) => (DateTime.SpecifyKind(utc, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

    public UsbSyncSummary? LastSummary()
    {
        using var conn = Open();
        var json = conn.ExecuteScalar<string?>("SELECT value FROM storage_usbsync_state WHERE name = 'last'");
        return json is null ? null : JsonSerializer.Deserialize<UsbSyncSummary>(json, JsonDefaults.Options);
    }

    public void SaveSummary(UsbSyncSummary summary)
    {
        using var conn = Open();
        conn.Execute("INSERT INTO storage_usbsync_state (name, value) VALUES ('last', @v) ON CONFLICT(name) DO UPDATE SET value = excluded.value",
            new { v = JsonSerializer.Serialize(summary, JsonDefaults.Options) });
    }
}
