using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Web;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using Microsoft.Data.Sqlite;

namespace DeeKeep.Library.Users;

/// <summary>
/// Bibliothèques personnelles : quel utilisateur possède quel titre. Un fichier n'existe qu'une
/// fois sur le disque ; deux utilisateurs qui le téléchargent partagent le même fichier, et le
/// supprimer ne fait que rompre le lien tant que quelqu'un d'autre l'utilise.
/// Table <c>library_user_tracks</c> de <c>deekeep_web.db</c> ; les comptes (<c>users</c>, tenus
/// par la passerelle) n'y sont que lus. Clé : chemin en base, sans tenir compte de la casse.
/// </summary>
public sealed class UserTracks(WebDb webDb, ILogger<UserTracks> logger)
{
    private bool _ready;

    private SqliteConnection Open()
    {
        var conn = webDb.Open();
        if (_ready) return conn;
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS library_user_tracks (
                user_id TEXT NOT NULL,
                key TEXT NOT NULL,
                path TEXT NOT NULL,
                origin TEXT,
                added_at REAL NOT NULL,
                PRIMARY KEY (user_id, key));
            CREATE INDEX IF NOT EXISTS ix_library_user_tracks_key ON library_user_tracks (key);
            CREATE TABLE IF NOT EXISTS library_user_expected (
                user_id TEXT NOT NULL,
                deezer_id INTEGER NOT NULL,
                created_at REAL NOT NULL,
                PRIMARY KEY (user_id, deezer_id));
            """);
        _ready = true;
        return conn;
    }

    private static string Key(string dbPath) => LibraryPaths.Key(dbPath);

    /// <summary>Le module des comptes est-il en place (sinon : application sans comptes, tout est visible) ?</summary>
    private static bool HasUsers(SqliteConnection conn) =>
        conn.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('users') WHERE name IN ('id', 'role')") == 2;

    /// <summary>Premier administrateur actif : il hérite des titres sans propriétaire.</summary>
    public string? FirstAdminId()
    {
        using var conn = Open();
        if (!HasUsers(conn)) return null;
        return conn.ExecuteScalar<string?>(
            "SELECT id FROM users WHERE role = 'admin' AND disabled = 0 AND id IS NOT NULL ORDER BY created_at, name LIMIT 1");
    }

    /// <summary>Clés des titres d'un utilisateur.</summary>
    public HashSet<string> KeysOf(string userId)
    {
        using var conn = Open();
        return conn.Query<string>("SELECT key FROM library_user_tracks WHERE user_id = @userId", new { userId }).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Noms des utilisateurs de chaque titre (vue de l'administrateur).</summary>
    public Dictionary<string, List<string>> NamesByKey()
    {
        using var conn = Open();
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!HasUsers(conn)) return result;
        foreach (var (key, name) in conn.Query<(string Key, string Name)>(
                     "SELECT t.key, u.name FROM library_user_tracks t JOIN users u ON u.id = t.user_id ORDER BY u.name"))
        {
            if (!result.TryGetValue(key, out var list)) result[key] = list = [];
            list.Add(name);
        }
        return result;
    }

    public IReadOnlyList<TrackUser> UsersOf(string dbPath)
    {
        using var conn = Open();
        if (!HasUsers(conn)) return [];
        return conn.Query<TrackUser>("""
            SELECT u.id AS Id, u.name AS Name, t.added_at AS AddedAt, t.origin AS Origin
            FROM library_user_tracks t JOIN users u ON u.id = t.user_id
            WHERE t.key = @key ORDER BY u.name
            """, new { key = Key(dbPath) }).ToList();
    }

    public IReadOnlyList<UserTrackCount> Counts()
    {
        using var conn = Open();
        if (!HasUsers(conn)) return [];
        return conn.Query<UserTrackCount>("""
            SELECT u.id AS Id, u.name AS Name, (SELECT COUNT(*) FROM library_user_tracks t WHERE t.user_id = u.id) AS Tracks
            FROM users u WHERE u.id IS NOT NULL ORDER BY u.name
            """).ToList();
    }

    /// <summary>Nombre d'utilisateurs d'un titre.</summary>
    public int CountUsers(string dbPath)
    {
        using var conn = Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM library_user_tracks WHERE key = @key", new { key = Key(dbPath) });
    }

    public bool IsLinked(string userId, string dbPath)
    {
        using var conn = Open();
        return conn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_user_tracks WHERE user_id = @userId AND key = @key",
            new { userId, key = Key(dbPath) }) > 0;
    }

    /// <summary>Associe des titres à un utilisateur ; rend le nombre de liens nouveaux.</summary>
    public int Link(string userId, IEnumerable<string> dbPaths, string origin)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var added = 0;
        foreach (var path in dbPaths.Distinct(StringComparer.Ordinal))
            added += conn.Execute(
                "INSERT OR IGNORE INTO library_user_tracks (user_id, key, path, origin, added_at) VALUES (@userId, @key, @path, @origin, @now)",
                new { userId, key = Key(path), path, origin, now = Clock.UnixNow() }, tx);
        tx.Commit();
        return added;
    }

    /// <summary>Rompt des liens ; rend le nombre de liens rompus.</summary>
    public int Unlink(string userId, IEnumerable<string> dbPaths)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var removed = 0;
        foreach (var path in dbPaths.Distinct(StringComparer.Ordinal))
            removed += conn.Execute("DELETE FROM library_user_tracks WHERE user_id = @userId AND key = @key", new { userId, key = Key(path) }, tx);
        tx.Commit();
        return removed;
    }

    /// <summary>La piste est déjà en cours d'extraction pour quelqu'un d'autre : l'utilisateur la recevra à l'arrivée du fichier.</summary>
    public void Expect(string userId, IEnumerable<long> deezerIds)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var id in deezerIds.Distinct())
            conn.Execute("INSERT OR IGNORE INTO library_user_expected (user_id, deezer_id, created_at) VALUES (@userId, @id, @now)",
                new { userId, id, now = Clock.UnixNow() }, tx);
        tx.Commit();
    }

    /// <summary>Le fichier d'une piste Deezer vient d'être enregistré : ceux qui l'attendaient le reçoivent.</summary>
    public int LinkExpected(long deezerId, string dbPath)
    {
        using var conn = Open();
        var waiting = conn.Query<string>("SELECT user_id FROM library_user_expected WHERE deezer_id = @deezerId", new { deezerId }).ToList();
        if (waiting.Count == 0) return 0;
        conn.Execute("DELETE FROM library_user_expected WHERE deezer_id = @deezerId", new { deezerId });
        conn.Dispose();
        return waiting.Sum(u => Link(u, [dbPath], "download"));
    }

    /// <summary>Le fichier a changé de chemin (rangement, conversion, retéléchargement) : ses utilisateurs le suivent.</summary>
    public void Move(string fromDbPath, string toDbPath)
    {
        var from = Key(fromDbPath);
        var to = Key(toDbPath);
        if (from == to && fromDbPath == toDbPath) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        conn.Execute("""
            INSERT OR IGNORE INTO library_user_tracks (user_id, key, path, origin, added_at)
            SELECT user_id, @to, @toPath, origin, added_at FROM library_user_tracks WHERE key = @from
            """, new { from, to, toPath = toDbPath }, tx);
        if (from != to) conn.Execute("DELETE FROM library_user_tracks WHERE key = @from", new { from }, tx);
        else conn.Execute("UPDATE library_user_tracks SET path = @toPath WHERE key = @to", new { to, toPath = toDbPath }, tx);
        tx.Commit();
    }

    /// <summary>Utilisateurs d'un chemin qui disparaît (ids), pour les reporter ailleurs.</summary>
    public IReadOnlyList<string> UserIdsOf(string dbPath)
    {
        using var conn = Open();
        return conn.Query<string>("SELECT user_id FROM library_user_tracks WHERE key = @key", new { key = Key(dbPath) }).ToList();
    }

    /// <summary>Oublie tous les liens de ces titres (fichier parti à la corbeille).</summary>
    public void Forget(IEnumerable<string> dbPaths)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var path in dbPaths.Distinct(StringComparer.Ordinal))
            conn.Execute("DELETE FROM library_user_tracks WHERE key = @key", new { key = Key(path) }, tx);
        tx.Commit();
    }

    /// <summary>
    /// Titres sans propriétaire (déjà présents avant les bibliothèques personnelles, ajoutés à la
    /// main dans le dossier ou arrivés par le disque USB) : rattachés au premier administrateur.
    /// </summary>
    public int AdoptOrphans(IEnumerable<string> liveDbPaths)
    {
        var admin = FirstAdminId();
        if (admin is null) return 0;
        HashSet<string> owned;
        using (var conn = Open())
        {
            owned = conn.Query<string>("SELECT DISTINCT key FROM library_user_tracks").ToHashSet(StringComparer.Ordinal);
        }
        var orphans = liveDbPaths.Where(p => !owned.Contains(Key(p))).ToList();
        if (orphans.Count == 0) return 0;
        var added = Link(admin, orphans, "adopted");
        logger.LogInformation("Bibliothèques personnelles : {Count} titre(s) sans propriétaire rattachés à l'administrateur", added);
        return added;
    }
}

/// <summary>Rattache régulièrement les titres sans propriétaire (premier passage au démarrage).</summary>
public sealed class OrphanAdopter(UserTracks users, DeeKeep.Library.Tracks.TrackCatalog catalog, ILogger<OrphanAdopter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { users.AdoptOrphans(catalog.Load().Select(e => e.Path)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Rattachement des titres sans propriétaire"); }
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt du service
        }
    }
}
