using System.Security.Cryptography;
using System.Text;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>Issue de la vérification d'un code.</summary>
public enum CodeCheck
{
    Ok,
    Wrong,
    Expired,
    TooManyAttempts,
    Missing,
}

/// <summary>Code refusé : envoi trop rapproché ou trop d'envois dans l'heure.</summary>
public sealed class CodeRateLimitException(string message, int retryAfterSeconds) : Exception(message)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Codes à usage unique envoyés par e-mail (inscription, mot de passe oublié), table
/// <c>auth_codes</c> de <c>deekeep_web.db</c> : un seul code vivant par adresse (en redemander un
/// invalide le précédent), valable <see cref="Lifetime"/>, <see cref="MaxAttempts"/> essais.
/// Seul un HMAC du code est gardé. Envois limités : <see cref="ResendDelay"/> entre deux,
/// <see cref="MaxPerHour"/> par heure et par adresse.
/// </summary>
public sealed class AuthCodeStore(IOptions<DatabaseOptions> options, InternalKey internalKey)
{
    public const int Digits = 6;
    public const int MaxAttempts = 5;
    public const int MaxPerHour = 6;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(30);

    // Sans clé interne (développement), une clé propre au processus : les codes ne survivent pas à un redémarrage.
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);
    private readonly Lock _schemaLock = new();
    private bool _schemaReady;

    private byte[] Key => string.IsNullOrEmpty(internalKey.Value) ? ProcessKey : Encoding.UTF8.GetBytes(internalKey.Value);

    private SqliteConnection Open()
    {
        var o = options.Value;
        var conn = DeeKeepDb.OpenPath(o.WebPath, false, o.BusyTimeoutMs);
        if (!_schemaReady)
        {
            lock (_schemaLock)
            {
                conn.Execute("""
                    CREATE TABLE IF NOT EXISTS auth_codes (
                        email TEXT PRIMARY KEY,
                        code_hash TEXT NOT NULL,
                        expires_at REAL NOT NULL,
                        attempts INTEGER NOT NULL DEFAULT 0,
                        sent_at REAL NOT NULL,
                        window_start REAL NOT NULL,
                        window_count INTEGER NOT NULL)
                    """);
                _schemaReady = true;
            }
        }
        return conn;
    }

    private string Hash(string email, string code)
    {
        var mac = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{email}\n{code}"));
        return Convert.ToHexString(mac);
    }

    /// <summary>Nouveau code pour l'adresse (canonique), qui remplace le précédent.</summary>
    public string Issue(string email) => Issue(email, Clock.UnixNow());

    public string Issue(string email, double now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var previous = conn.QueryFirstOrDefault<Row>(
            "SELECT code_hash AS Hash, expires_at AS ExpiresAt, attempts AS Attempts, sent_at AS SentAt, " +
            "window_start AS WindowStart, window_count AS WindowCount FROM auth_codes WHERE email = @email", new { email }, tx);
        var windowStart = now;
        long windowCount = 1;
        if (previous is { } p && p.SentAt > 0)
        {
            var wait = p.SentAt + ResendDelay.TotalSeconds - now;
            if (wait > 0) throw new CodeRateLimitException($"Patientez {Math.Ceiling(wait):0} s avant de redemander un code.", (int)Math.Ceiling(wait));
            if (now - p.WindowStart < 3600)
            {
                if (p.WindowCount >= MaxPerHour)
                {
                    var retry = (int)Math.Ceiling(p.WindowStart + 3600 - now);
                    throw new CodeRateLimitException($"Trop de codes demandés : réessayez dans {Math.Ceiling(retry / 60.0):0} min.", retry);
                }
                windowStart = p.WindowStart;
                windowCount = p.WindowCount + 1;
            }
        }
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Digits)).ToString(new string('0', Digits));
        conn.Execute("""
            INSERT OR REPLACE INTO auth_codes (email, code_hash, expires_at, attempts, sent_at, window_start, window_count)
            VALUES (@email, @hash, @expires, 0, @now, @windowStart, @windowCount)
            """, new { email, hash = Hash(email, code), expires = now + Lifetime.TotalSeconds, now, windowStart, windowCount }, tx);
        tx.Commit();
        return code;
    }

    /// <summary>Code qui n'a pas pu partir : il ne doit pas rester valable (le délai de renvoi est levé).</summary>
    public void Cancel(string email)
    {
        using var conn = Open();
        conn.Execute("UPDATE auth_codes SET code_hash = '', sent_at = 0 WHERE email = @email", new { email });
    }

    /// <summary>
    /// Vérifie un code. Une erreur compte un essai ; au dernier, le code est détruit. <paramref name="consume"/> :
    /// un code juste est détruit (étape finale), sinon il reste valable (simple contrôle avant le mot de passe).
    /// </summary>
    public CodeCheck Check(string email, string? code, bool consume) => Check(email, code, consume, Clock.UnixNow());

    public CodeCheck Check(string email, string? code, bool consume, double now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var r = conn.QueryFirstOrDefault<Row>(
            "SELECT code_hash AS Hash, expires_at AS ExpiresAt, attempts AS Attempts FROM auth_codes WHERE email = @email", new { email }, tx);
        if (r is null || r.Hash.Length == 0) return CodeCheck.Missing;
        if (now >= r.ExpiresAt) return CodeCheck.Expired;
        if (r.Attempts >= MaxAttempts) return CodeCheck.TooManyAttempts;

        var given = (code ?? "").Trim().Replace(" ", "");
        var expected = Encoding.ASCII.GetBytes(r.Hash);
        var actual = Encoding.ASCII.GetBytes(Hash(email, given));
        if (given.Length == Digits && CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            // Un code consommé ne sert plus ; le compteur d'envois reste (limite horaire).
            if (consume) conn.Execute("UPDATE auth_codes SET code_hash = '' WHERE email = @email", new { email }, tx);
            tx.Commit();
            return CodeCheck.Ok;
        }
        var attempts = r.Attempts + 1;
        conn.Execute("UPDATE auth_codes SET attempts = @attempts WHERE email = @email", new { email, attempts }, tx);
        tx.Commit();
        return attempts >= MaxAttempts ? CodeCheck.TooManyAttempts : CodeCheck.Wrong;
    }

    private sealed class Row
    {
        public string Hash { get; set; } = "";
        public double ExpiresAt { get; set; }
        public long Attempts { get; set; }
        public double SentAt { get; set; }
        public double WindowStart { get; set; }
        public long WindowCount { get; set; }
    }

    public static string Message(CodeCheck result) => result switch
    {
        CodeCheck.Ok => "",
        CodeCheck.Wrong => "Code incorrect.",
        CodeCheck.Expired => "Ce code a expiré : demandez-en un nouveau.",
        CodeCheck.TooManyAttempts => "Trop d'essais : demandez un nouveau code.",
        _ => "Aucun code en cours pour cette adresse : demandez-en un.",
    };
}
