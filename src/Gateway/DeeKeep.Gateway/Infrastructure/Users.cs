using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>Rôles des comptes : un administrateur voit tout et gère les comptes.</summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string User = "user";

    public static bool IsKnown(string? role) => role is Admin or User;
}

/// <summary>Pages de l'application ouvrables compte par compte (noms des routes du client).</summary>
public static class PageKeys
{
    public const string Musique = "musique";
    public const string Bibliotheque = "bibliotheque";
    public const string Reencoder = "reencoder";
    public const string Organiser = "organiser";
    public const string Qualite = "qualite";
    public const string Diagnostic = "diagnostic";
    public const string Historique = "historique";
    public const string Parametres = "parametres";

    public static readonly IReadOnlyList<string> All =
        [Musique, Bibliotheque, Reencoder, Organiser, Qualite, Diagnostic, Historique, Parametres];

    /// <summary>Libellés des pages, pour les messages de refus.</summary>
    public static string Label(string page) => page switch
    {
        Musique => "Musique",
        Bibliotheque => "Bibliothèque",
        Reencoder => "Réencoder",
        Organiser => "Organiser",
        Qualite => "Qualité",
        Diagnostic => "Diagnostic",
        Historique => "Historique",
        Parametres => "Paramètres",
        _ => page,
    };

    /// <summary>Pages connues, sans doublon, dans l'ordre de la navigation ; une clé inconnue est refusée.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? pages)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pages ?? [])
        {
            var key = p.Trim().ToLowerInvariant();
            if (!All.Contains(key)) throw new ArgumentException($"Page inconnue : « {p} ».");
            set.Add(key);
        }
        return All.Where(set.Contains).ToList();
    }
}

/// <summary>
/// Compte tel que le voient l'API et les règles d'accès (jamais le haché du mot de passe).
/// <see cref="Name"/> est l'adresse e-mail du compte (les comptes antérieurs gardent leur nom).
/// <see cref="Pending"/> : inscrit lui-même, en attente de validation par un administrateur
/// (il est alors aussi désactivé).
/// </summary>
public sealed record AppUser(
    string Id,
    string Name,
    string Role,
    IReadOnlyList<string> Pages,
    bool CanDownload,
    bool CanDelete,
    bool Disabled,
    double CreatedAt,
    bool Pending = false)
{
    public bool IsAdmin => Role == Roles.Admin;

    public bool HasPage(string page) => IsAdmin || Pages.Contains(page);

    /// <summary>Utilisateur implicite quand l'authentification est désactivée : tous les droits.</summary>
    public static readonly AppUser Anonymous =
        new("", "", Roles.Admin, PageKeys.All, CanDownload: true, CanDelete: true, Disabled: false, CreatedAt: 0);
}

/// <summary>Adresses e-mail des comptes : forme canonique (minuscules, sans espaces) et contrôle de forme.</summary>
public static partial class Emails
{
    public const int MaxLength = 254;

    [System.Text.RegularExpressions.GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial System.Text.RegularExpressions.Regex Shape();

    /// <summary>Adresse canonique, ou <see cref="ArgumentException"/> si elle n'a pas la forme d'un e-mail.</summary>
    public static string Normalize(string? email)
    {
        var value = (email ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) throw new ArgumentException("L'adresse e-mail est obligatoire.");
        if (value.Length > MaxLength || !Shape().IsMatch(value)) throw new ArgumentException("Adresse e-mail invalide.");
        return value;
    }

    public static bool LooksLikeEmail(string? value) => value is not null && Shape().IsMatch(value);
}

/// <summary>Issue d'une inscription ou d'un mot de passe oublié validé par code.</summary>
public sealed record RegisterResult(AppUser User, bool Created);

/// <summary>Droits d'un compte, à la création ou à la modification.</summary>
public sealed record UserRights(string Role, IReadOnlyList<string>? Pages, bool CanDownload, bool CanDelete, bool Disabled);

/// <summary>
/// Comptes de la table <c>users</c> de <c>deekeep_web.db</c>. Le schéma d'origine
/// (nom, haché, date) est complété au premier accès par les colonnes de rôle et de
/// droits ; les comptes déjà présents deviennent administrateurs avec tous les droits.
/// Les lectures passent par un cache de quelques secondes (chaque requête
/// authentifiée relit son compte) ; toute écriture faite ici le vide aussitôt.
/// La base est ouverte directement (pas par <c>WebDb</c>) : la table <c>users</c> est
/// créée et migrée ici, quel que soit l'ordre d'ouverture des autres tables.
/// </summary>
public sealed class UserStore(IOptions<DatabaseOptions> options)
{
    public const int MinPasswordLength = 8;
    /// <summary>Pages d'un compte inscrit par lui-même, une fois validé (l'administrateur les ajuste).</summary>
    public static readonly IReadOnlyList<string> SelfServicePages = [PageKeys.Musique, PageKeys.Bibliotheque];
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, (AppUser? User, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _schemaLock = new();
    private bool _schemaReady;

    private SqliteConnection Open()
    {
        var o = options.Value;
        var conn = DeeKeepDb.OpenPath(o.WebPath, false, o.BusyTimeoutMs);
        if (!_schemaReady)
        {
            lock (_schemaLock)
            {
                if (!_schemaReady)
                {
                    EnsureSchema(conn);
                    _schemaReady = true;
                }
            }
        }
        return conn;
    }

    /// <summary>Migration idempotente : ajoute les colonnes manquantes, sans toucher aux comptes existants.</summary>
    private static void EnsureSchema(SqliteConnection conn)
    {
        conn.Execute("CREATE TABLE IF NOT EXISTS users (name TEXT PRIMARY KEY, password_hash TEXT NOT NULL, created_at REAL NOT NULL)");
        var columns = conn.Query<string>("SELECT name FROM pragma_table_info('users')").ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var tx = conn.BeginTransaction();
        if (!columns.Contains("id"))
        {
            conn.Execute("ALTER TABLE users ADD COLUMN id TEXT", transaction: tx);
        }
        if (!columns.Contains("role"))
            conn.Execute($"ALTER TABLE users ADD COLUMN role TEXT NOT NULL DEFAULT '{Roles.Admin}'", transaction: tx);
        if (!columns.Contains("pages"))
        {
            conn.Execute("ALTER TABLE users ADD COLUMN pages TEXT NOT NULL DEFAULT '[]'", transaction: tx);
            conn.Execute("UPDATE users SET pages = @all", new { all = JsonSerializer.Serialize(PageKeys.All) }, tx);
        }
        if (!columns.Contains("can_download"))
            conn.Execute("ALTER TABLE users ADD COLUMN can_download INTEGER NOT NULL DEFAULT 1", transaction: tx);
        if (!columns.Contains("can_delete"))
            conn.Execute("ALTER TABLE users ADD COLUMN can_delete INTEGER NOT NULL DEFAULT 1", transaction: tx);
        if (!columns.Contains("disabled"))
            conn.Execute("ALTER TABLE users ADD COLUMN disabled INTEGER NOT NULL DEFAULT 0", transaction: tx);
        if (!columns.Contains("pending"))
            conn.Execute("ALTER TABLE users ADD COLUMN pending INTEGER NOT NULL DEFAULT 0", transaction: tx);
        // Identifiant stable (adresses de l'API d'administration, cookie) : un compte
        // supprimé puis recréé sous le même nom n'hérite pas des sessions de l'ancien.
        foreach (var name in conn.Query<string>("SELECT name FROM users WHERE id IS NULL", transaction: tx))
            conn.Execute("UPDATE users SET id = @id WHERE name = @name", new { id = NewId(), name }, tx);
        conn.Execute("CREATE UNIQUE INDEX IF NOT EXISTS ux_users_id ON users (id)", transaction: tx);
        tx.Commit();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private const string SelectColumns =
        "id AS Id, name AS Name, role AS Role, pages AS Pages, can_download AS CanDownload, can_delete AS CanDelete, " +
        "disabled AS Disabled, created_at AS CreatedAt, pending AS Pending";

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Role { get; set; }
        public string? Pages { get; set; }
        public long CanDownload { get; set; }
        public long CanDelete { get; set; }
        public long Disabled { get; set; }
        public double CreatedAt { get; set; }
        public long Pending { get; set; }

        public AppUser ToUser() => new(Id, Name, Roles.IsKnown(Role) ? Role! : Roles.User,
            ReadPages(Pages), CanDownload != 0, CanDelete != 0, Disabled != 0, CreatedAt, Pending != 0);

        private static IReadOnlyList<string> ReadPages(string? json)
        {
            try
            {
                var pages = JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [];
                return PageKeys.All.Where(pages.Contains).ToList();
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    public bool Any()
    {
        using var conn = Open();
        return conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0;
    }

    /// <summary>Adresses des administrateurs actifs (avis des nouvelles inscriptions).</summary>
    public IReadOnlyList<string> AdminEmails() =>
        List().Where(u => u.IsAdmin && !u.Disabled && Emails.LooksLikeEmail(u.Name)).Select(u => u.Name).ToList();

    public IReadOnlyList<AppUser> List()
    {
        using var conn = Open();
        return conn.Query<Row>($"SELECT {SelectColumns} FROM users ORDER BY name COLLATE NOCASE").Select(r => r.ToUser()).ToList();
    }

    /// <summary>Compte par nom (sans tenir compte de la casse), lu au plus quelques secondes plus tôt.</summary>
    public AppUser? FindCached(string name)
    {
        var cached = _cache.TryGetValue(name, out var hit);
        if (cached && DateTime.UtcNow - hit.At < CacheLifetime) return hit.User;
        AppUser? user;
        try
        {
            user = FindByName(name);
        }
        catch (SqliteException) when (cached)
        {
            // Base momentanément verrouillée ou injoignable : la dernière lecture vaut mieux qu'une panne générale.
            return hit.User;
        }
        _cache[name] = (user, DateTime.UtcNow);
        return user;
    }

    public AppUser? FindByName(string name)
    {
        using var conn = Open();
        return conn.QueryFirstOrDefault<Row>($"SELECT {SelectColumns} FROM users WHERE name = @name COLLATE NOCASE", new { name })?.ToUser();
    }

    public AppUser? FindById(string id)
    {
        using var conn = Open();
        return conn.QueryFirstOrDefault<Row>($"SELECT {SelectColumns} FROM users WHERE id = @id", new { id })?.ToUser();
    }

    /// <summary>Vérifie le mot de passe ; rend le compte (même désactivé : l'appelant en décide) ou null.</summary>
    public AppUser? Verify(string name, string password)
    {
        using var conn = Open();
        var hash = conn.ExecuteScalar<string?>("SELECT password_hash FROM users WHERE name = @name COLLATE NOCASE", new { name });
        if (hash is null || string.IsNullOrEmpty(password) || !Auth.VerifyPassword(password, hash)) return null;
        return FindByName(name);
    }

    /// <summary>Premier compte (page d'accueil) : administrateur, seulement tant que la table est vide.</summary>
    public AppUser? CreateFirst(string email, string password)
    {
        var name = Emails.Normalize(email);
        CheckPassword(password);
        using var conn = Open();
        var inserted = conn.Execute(
            "INSERT INTO users (id, name, password_hash, created_at, role, pages, can_download, can_delete, disabled) " +
            "SELECT @id, @name, @hash, @now, @role, @pages, 1, 1, 0 WHERE NOT EXISTS (SELECT 1 FROM users)",
            new { id = NewId(), name, hash = Auth.HashPassword(password), now = Clock.UnixNow(), role = Roles.Admin, pages = JsonSerializer.Serialize(PageKeys.All) });
        Invalidate();
        return inserted == 0 ? null : FindByName(name);
    }

    public AppUser Create(string email, string password, UserRights rights)
    {
        var name = Emails.Normalize(email);
        CheckPassword(password);
        CheckRole(rights.Role);
        var pages = PageKeys.Normalize(rights.Pages);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users WHERE name = @name COLLATE NOCASE", new { name }, tx) > 0)
            throw new InvalidOperationException("Ce nom d'utilisateur existe déjà.");
        var id = NewId();
        conn.Execute(
            "INSERT INTO users (id, name, password_hash, created_at, role, pages, can_download, can_delete, disabled) " +
            "VALUES (@id, @name, @hash, @now, @role, @pages, @download, @delete, @disabled)",
            new
            {
                id, name, hash = Auth.HashPassword(password), now = Clock.UnixNow(), role = rights.Role,
                pages = JsonSerializer.Serialize(pages), download = rights.CanDownload, delete = rights.CanDelete, disabled = rights.Disabled,
            }, tx);
        tx.Commit();
        Invalidate();
        return FindById(id)!;
    }

    /// <summary>
    /// Adresse dont la boîte a été prouvée par un code : nouveau mot de passe d'un compte existant
    /// (mot de passe oublié), ou création d'un compte. Un compte créé ainsi attend la validation d'un
    /// administrateur (désactivé, « en attente »), sauf le tout premier, administrateur d'office.
    /// </summary>
    public RegisterResult Register(string email, string password)
    {
        var name = Emails.Normalize(email);
        CheckPassword(password);
        var hash = Auth.HashPassword(password);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var id = conn.ExecuteScalar<string?>("SELECT id FROM users WHERE name = @name COLLATE NOCASE", new { name }, tx);
        var created = id is null;
        if (id is not null)
        {
            conn.Execute("UPDATE users SET password_hash = @hash WHERE id = @id", new { id, hash }, tx);
        }
        else
        {
            id = NewId();
            var first = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users", transaction: tx) == 0;
            conn.Execute(
                "INSERT INTO users (id, name, password_hash, created_at, role, pages, can_download, can_delete, disabled, pending) " +
                "VALUES (@id, @name, @hash, @now, @role, @pages, 1, @delete, @off, @off)",
                new
                {
                    id, name, hash, now = Clock.UnixNow(),
                    role = first ? Roles.Admin : Roles.User,
                    pages = JsonSerializer.Serialize(first ? PageKeys.All : SelfServicePages),
                    delete = first,
                    off = !first,
                }, tx);
        }
        tx.Commit();
        Invalidate();
        return new RegisterResult(FindById(id)!, created);
    }

    /// <summary>Change le rôle et les droits ; refuse de laisser l'application sans administrateur actif.</summary>
    public AppUser Update(string id, UserRights rights)
    {
        CheckRole(rights.Role);
        var pages = PageKeys.Normalize(rights.Pages);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var current = Get(conn, tx, id);
        var staysActiveAdmin = rights.Role == Roles.Admin && !rights.Disabled;
        if (IsActiveAdmin(current) && !staysActiveAdmin && OtherActiveAdmins(conn, tx, id) == 0)
            throw new InvalidOperationException("Il faut garder au moins un administrateur actif.");
        conn.Execute(
            // Activer un compte en attente le valide.
            "UPDATE users SET role = @role, pages = @pages, can_download = @download, can_delete = @delete, disabled = @disabled, " +
            "pending = CASE WHEN @disabled THEN pending ELSE 0 END WHERE id = @id",
            new { id, role = rights.Role, pages = JsonSerializer.Serialize(pages), download = rights.CanDownload, delete = rights.CanDelete, disabled = rights.Disabled }, tx);
        tx.Commit();
        Invalidate();
        return FindById(id)!;
    }

    public void SetPassword(string id, string password)
    {
        CheckPassword(password);
        using var conn = Open();
        if (conn.Execute("UPDATE users SET password_hash = @hash WHERE id = @id", new { id, hash = Auth.HashPassword(password) }) == 0)
            throw new KeyNotFoundException("Compte introuvable.");
        Invalidate();
    }

    /// <summary>Supprime un compte ; ni le sien, ni le dernier administrateur actif.</summary>
    public void Delete(string id, string? currentUserId)
    {
        if (id == currentUserId) throw new InvalidOperationException("Impossible de supprimer son propre compte.");
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var current = Get(conn, tx, id);
        if (IsActiveAdmin(current) && OtherActiveAdmins(conn, tx, id) == 0)
            throw new InvalidOperationException("Il faut garder au moins un administrateur actif.");
        conn.Execute("DELETE FROM users WHERE id = @id", new { id }, tx);
        tx.Commit();
        Invalidate();
    }

    public void Invalidate() => _cache.Clear();

    private static AppUser Get(SqliteConnection conn, SqliteTransaction tx, string id) =>
        conn.QueryFirstOrDefault<Row>($"SELECT {SelectColumns} FROM users WHERE id = @id", new { id }, tx)?.ToUser()
        ?? throw new KeyNotFoundException("Compte introuvable.");

    private static bool IsActiveAdmin(AppUser user) => user.IsAdmin && !user.Disabled;

    private static long OtherActiveAdmins(SqliteConnection conn, SqliteTransaction tx, string id) =>
        conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users WHERE role = @role AND disabled = 0 AND id <> @id", new { role = Roles.Admin, id }, tx);

    public static void CheckPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            throw new ArgumentException($"Le mot de passe doit compter au moins {MinPasswordLength} caractères.");
    }

    private static void CheckRole(string? role)
    {
        if (!Roles.IsKnown(role)) throw new ArgumentException($"Rôle inconnu : « {role} ».");
    }
}
