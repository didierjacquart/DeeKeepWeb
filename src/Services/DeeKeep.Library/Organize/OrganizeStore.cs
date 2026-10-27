using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Organize;
using Microsoft.Data.Sqlite;

namespace DeeKeep.Library.Organize;

/// <summary>
/// État serveur de l'écran Organiser (tables <c>library_organize_*</c> de
/// <c>deekeep_web.db</c>) : la dernière analyse et ses fichiers lus, les cases touchées par
/// l'utilisateur, le journal des rangements, les propositions de tags reçues. Le client
/// retrouve tout après un rechargement ou une coupure ; un plan se recalcule sans relire
/// le disque (les entrées sont gardées en mémoire, invalidées à chaque écriture).
/// </summary>
public sealed class OrganizeStore(WebDb webDb)
{
    private static readonly object SchemaLock = new();
    private static bool _schemaReady;
    private readonly ConcurrentDictionary<Guid, List<OrganizeEntry>> _entries = new();

    private sealed record SessionRow(string id, string root, string root_db, string? job_id, string state, long total, long read,
        string? message, double created_at, double updated_at);

    private sealed record EntryRow(long idx, string path, long size, long mtime, long? bitrate, string data);

    private SqliteConnection Open()
    {
        var conn = webDb.Open();
        if (_schemaReady) return conn;
        lock (SchemaLock)
        {
            if (_schemaReady) return conn;
            conn.Execute("""
                CREATE TABLE IF NOT EXISTS library_organize_sessions (
                    id TEXT PRIMARY KEY,
                    root TEXT NOT NULL,
                    root_db TEXT NOT NULL,
                    job_id TEXT,
                    state TEXT NOT NULL,
                    total INTEGER NOT NULL DEFAULT 0,
                    read INTEGER NOT NULL DEFAULT 0,
                    message TEXT,
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS library_organize_entries (
                    session_id TEXT NOT NULL,
                    idx INTEGER NOT NULL,
                    path TEXT NOT NULL,
                    size INTEGER NOT NULL DEFAULT 0,
                    mtime INTEGER NOT NULL DEFAULT 0,
                    bitrate INTEGER,
                    data TEXT NOT NULL,
                    PRIMARY KEY (session_id, idx)
                );
                CREATE TABLE IF NOT EXISTS library_organize_checks (
                    session_id TEXT NOT NULL,
                    idx INTEGER NOT NULL,
                    checked INTEGER NOT NULL,
                    PRIMARY KEY (session_id, idx)
                );
                CREATE TABLE IF NOT EXISTS library_organize_journal (
                    job_id TEXT NOT NULL,
                    idx INTEGER NOT NULL,
                    kind TEXT NOT NULL,
                    source TEXT NOT NULL,
                    destination TEXT NOT NULL,
                    status TEXT NOT NULL,
                    message TEXT,
                    at REAL NOT NULL,
                    PRIMARY KEY (job_id, idx, kind)
                );
                CREATE TABLE IF NOT EXISTS library_organize_lookups (
                    job_id TEXT PRIMARY KEY,
                    session_id TEXT,
                    created_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS library_organize_proposals (
                    job_id TEXT NOT NULL,
                    item_key TEXT NOT NULL,
                    status TEXT NOT NULL,
                    message TEXT,
                    path TEXT,
                    data TEXT,
                    at REAL NOT NULL,
                    PRIMARY KEY (job_id, item_key)
                );
                """);
            _schemaReady = true;
        }
        return conn;
    }

    // ------------------------------------------------------------------ sessions

    public OrganizeSessionDto CreateSession(string root, string rootDb)
    {
        var id = Guid.NewGuid();
        var now = Clock.UnixNow();
        using var conn = Open();
        conn.Execute("INSERT INTO library_organize_sessions (id, root, root_db, state, created_at, updated_at) VALUES (@id, @root, @rootDb, 'running', @now, @now)",
            new { id = id.ToString(), root, rootDb, now });
        return GetSession(id)!;
    }

    public OrganizeSessionDto? GetSession(Guid id)
    {
        using var conn = Open();
        var row = conn.QueryFirstOrDefault<SessionRow>("SELECT * FROM library_organize_sessions WHERE id = @id", new { id = id.ToString() });
        return row is null ? null : Map(row);
    }

    /// <summary>Dernière analyse lancée (la seule que l'écran montre).</summary>
    public OrganizeSessionDto? LatestSession()
    {
        using var conn = Open();
        var row = conn.QueryFirstOrDefault<SessionRow>("SELECT * FROM library_organize_sessions ORDER BY created_at DESC LIMIT 1");
        return row is null ? null : Map(row);
    }

    public void UpdateSession(Guid id, string? state = null, int? total = null, int? read = null, string? message = null, Guid? jobId = null)
    {
        using var conn = Open();
        conn.Execute("""
            UPDATE library_organize_sessions SET state = COALESCE(@state, state), total = COALESCE(@total, total), read = COALESCE(@read, read),
                message = COALESCE(@message, message), job_id = COALESCE(@job, job_id), updated_at = @now WHERE id = @id
            """, new { id = id.ToString(), state, total, read, message, job = jobId?.ToString(), now = Clock.UnixNow() });
    }

    /// <summary>Oublie les analyses antérieures à <paramref name="keep"/> (leurs fichiers ont servi de cache).</summary>
    public void DeleteOtherSessions(Guid keep)
    {
        using var conn = Open();
        var ids = conn.Query<string>("SELECT id FROM library_organize_sessions WHERE id <> @keep", new { keep = keep.ToString() }).ToList();
        if (ids.Count == 0) return;
        using var tx = conn.BeginTransaction();
        conn.Execute("DELETE FROM library_organize_entries WHERE session_id IN @ids", new { ids }, tx);
        conn.Execute("DELETE FROM library_organize_checks WHERE session_id IN @ids", new { ids }, tx);
        conn.Execute("DELETE FROM library_organize_sessions WHERE id IN @ids", new { ids }, tx);
        tx.Commit();
        foreach (var id in ids) _entries.TryRemove(Guid.Parse(id), out _);
    }

    private static OrganizeSessionDto Map(SessionRow r) => new(
        Guid.Parse(r.id), r.root, r.root_db, r.job_id is null ? null : Guid.Parse(r.job_id), r.state, (int)r.total, (int)r.read,
        r.created_at, r.updated_at, r.message);

    // ------------------------------------------------------------------ entrées

    /// <summary>Fichiers lus d'une analyse, dans l'ordre du parcours (copie partagée en lecture seule).</summary>
    public IReadOnlyList<OrganizeEntry> Entries(Guid sessionId)
    {
        if (_entries.TryGetValue(sessionId, out var cached)) return cached;
        using var conn = Open();
        var list = conn.Query<EntryRow>("SELECT idx, path, size, mtime, bitrate, data FROM library_organize_entries WHERE session_id = @id ORDER BY idx",
                new { id = sessionId.ToString() })
            .Select(ToEntry).ToList();
        _entries[sessionId] = list;
        return list;
    }

    /// <summary>Cache de lecture : fichiers déjà lus par n'importe quelle analyse encore en base (clé de chemin).</summary>
    public Dictionary<string, OrganizeEntry> ReadCache()
    {
        using var conn = Open();
        var cache = new Dictionary<string, OrganizeEntry>(StringComparer.Ordinal);
        foreach (var row in conn.Query<EntryRow>("SELECT idx, path, size, mtime, bitrate, data FROM library_organize_entries"))
            cache[OrganizeText.PathKey(row.path)] = ToEntry(row);
        return cache;
    }

    public void SaveEntries(Guid sessionId, IReadOnlyList<OrganizeEntry> entries)
    {
        using (var conn = Open())
        {
            using var tx = conn.BeginTransaction();
            foreach (var e in entries)
            {
                conn.Execute("""
                    INSERT INTO library_organize_entries (session_id, idx, path, size, mtime, bitrate, data) VALUES (@sid, @idx, @path, @size, @mtime, @bitrate, @data)
                    ON CONFLICT(session_id, idx) DO UPDATE SET path = excluded.path, size = excluded.size, mtime = excluded.mtime,
                        bitrate = excluded.bitrate, data = excluded.data
                    """, new
                {
                    sid = sessionId.ToString(), idx = e.Index, path = e.Path, size = e.Size, mtime = e.MtimeTicks, bitrate = e.Bitrate,
                    data = JsonSerializer.Serialize(e, JsonDefaults.Options),
                }, tx);
            }
            tx.Commit();
        }
        _entries.TryRemove(sessionId, out _);
    }

    /// <summary>Retire les entrées au-delà de <paramref name="count"/> (parcours plus court qu'une reprise précédente).</summary>
    public void TrimEntries(Guid sessionId, int count)
    {
        using (var conn = Open())
            conn.Execute("DELETE FROM library_organize_entries WHERE session_id = @id AND idx >= @count", new { id = sessionId.ToString(), count });
        _entries.TryRemove(sessionId, out _);
    }

    /// <summary>Retire de l'analyse des fichiers supprimés (ils ne pèsent plus sur le plan ni sur « Divers »).</summary>
    public int RemoveEntries(Guid sessionId, IReadOnlyCollection<int> indices)
    {
        if (indices.Count == 0) return 0;
        int n;
        using (var conn = Open())
        {
            n = conn.Execute("DELETE FROM library_organize_entries WHERE session_id = @id AND idx IN @indices", new { id = sessionId.ToString(), indices });
            conn.Execute("DELETE FROM library_organize_checks WHERE session_id = @id AND idx IN @indices", new { id = sessionId.ToString(), indices });
        }
        _entries.TryRemove(sessionId, out _);
        return n;
    }

    /// <summary>Mémorise un débit lu à la demande (doublons).</summary>
    public void SaveBitrates(Guid sessionId, IEnumerable<OrganizeEntry> entries)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var e in entries)
            conn.Execute("UPDATE library_organize_entries SET bitrate = @bitrate, size = @size WHERE session_id = @id AND idx = @idx",
                new { id = sessionId.ToString(), idx = e.Index, bitrate = e.Bitrate, size = e.Size }, tx);
        tx.Commit();
    }

    private static OrganizeEntry ToEntry(EntryRow row)
    {
        var e = JsonSerializer.Deserialize<OrganizeEntry>(row.data, JsonDefaults.Options) ?? new OrganizeEntry();
        e.Index = (int)row.idx;
        e.Path = row.path;
        e.Size = row.size;
        e.MtimeTicks = row.mtime;
        e.Bitrate = row.bitrate is { } b ? (int)b : null;
        return e;
    }

    // ------------------------------------------------------------------ cases

    public OrganizeChecks Checks(Guid sessionId)
    {
        using var conn = Open();
        var rows = conn.Query<(long idx, long chk)>("SELECT idx, checked FROM library_organize_checks WHERE session_id = @id ORDER BY idx",
            new { id = sessionId.ToString() }).ToList();
        return new OrganizeChecks(sessionId, rows.Where(r => r.chk != 0).Select(r => (int)r.idx).ToList(),
            rows.Where(r => r.chk == 0).Select(r => (int)r.idx).ToList());
    }

    public OrganizeChecks SetChecks(Guid sessionId, IReadOnlyList<int> indices, bool isChecked)
    {
        using (var conn = Open())
        {
            using var tx = conn.BeginTransaction();
            foreach (var idx in indices.Distinct())
                conn.Execute("INSERT INTO library_organize_checks (session_id, idx, checked) VALUES (@id, @idx, @c) " +
                             "ON CONFLICT(session_id, idx) DO UPDATE SET checked = excluded.checked",
                    new { id = sessionId.ToString(), idx, c = isChecked ? 1 : 0 }, tx);
            tx.Commit();
        }
        return Checks(sessionId);
    }

    public OrganizeChecks ResetChecks(Guid sessionId)
    {
        using (var conn = Open())
            conn.Execute("DELETE FROM library_organize_checks WHERE session_id = @id", new { id = sessionId.ToString() });
        return Checks(sessionId);
    }

    // ------------------------------------------------------------------ journal

    public Dictionary<(int, string), OrganizeJournalEntry> Journal(Guid jobId) =>
        JournalList(jobId).ToDictionary(j => (j.Index, j.Kind));

    public IReadOnlyList<OrganizeJournalEntry> JournalList(Guid jobId)
    {
        using var conn = Open();
        return conn.Query<(long idx, string kind, string source, string destination, string status, string? message, double at)>(
                "SELECT idx, kind, source, destination, status, message, at FROM library_organize_journal WHERE job_id = @id ORDER BY at, idx",
                new { id = jobId.ToString() })
            .Select(r => new OrganizeJournalEntry(jobId, (int)r.idx, r.kind, r.source, r.destination, r.status, r.message, r.at))
            .ToList();
    }

    public void Log(OrganizeJournalEntry entry)
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO library_organize_journal (job_id, idx, kind, source, destination, status, message, at) VALUES (@job, @idx, @kind, @src, @dst, @status, @message, @at)
            ON CONFLICT(job_id, idx, kind) DO UPDATE SET status = excluded.status, message = excluded.message, at = excluded.at
            """, new
        {
            job = entry.JobId.ToString(), idx = entry.Index, kind = entry.Kind, src = entry.Source, dst = entry.Destination, status = entry.Status,
            message = entry.Message, at = entry.At,
        });
    }

    // ------------------------------------------------------------------ recherche des tags

    public void AddLookup(Guid jobId, Guid? sessionId)
    {
        using var conn = Open();
        conn.Execute("INSERT OR IGNORE INTO library_organize_lookups (job_id, session_id, created_at) VALUES (@job, @session, @now)",
            new { job = jobId.ToString(), session = sessionId?.ToString(), now = Clock.UnixNow() });
        // Les recherches de plus d'une semaine n'intéressent plus personne.
        var old = Clock.UnixNow() - 7 * 86400;
        conn.Execute("DELETE FROM library_organize_proposals WHERE job_id IN (SELECT job_id FROM library_organize_lookups WHERE created_at < @old)", new { old });
        conn.Execute("DELETE FROM library_organize_lookups WHERE created_at < @old", new { old });
    }

    public bool IsLookup(Guid jobId)
    {
        using var conn = Open();
        return conn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_organize_lookups WHERE job_id = @job", new { job = jobId.ToString() }) > 0;
    }

    public void SaveProposal(JobItemEvent item, TagProposal? proposal)
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO library_organize_proposals (job_id, item_key, status, message, path, data, at) VALUES (@job, @key, @status, @message, @path, @data, @at)
            ON CONFLICT(job_id, item_key) DO UPDATE SET status = excluded.status, message = excluded.message, path = excluded.path, data = excluded.data, at = excluded.at
            """, new
        {
            job = item.JobId.ToString(), key = item.ItemKey, status = item.Status, message = item.Message, path = proposal?.Path ?? item.ItemKey,
            data = proposal is null ? null : JsonSerializer.Serialize(proposal, JsonDefaults.Options), at = Clock.UnixNow(),
        });
    }

    public IReadOnlyList<OrganizeProposalItem> Proposals(Guid jobId)
    {
        using var conn = Open();
        return conn.Query<(string item_key, string status, string? message, string? path, string? data, double at)>(
                "SELECT item_key, status, message, path, data, at FROM library_organize_proposals WHERE job_id = @job ORDER BY at",
                new { job = jobId.ToString() })
            .Select(r => new OrganizeProposalItem(jobId, r.item_key, r.status, r.message, r.path,
                r.data is null ? null : JsonSerializer.Deserialize<TagProposal>(r.data, JsonDefaults.Options), r.at))
            .ToList();
    }
}
