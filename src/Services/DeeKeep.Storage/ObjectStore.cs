using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dapper;
using DeeKeep.Common.Data;
using Microsoft.Data.Sqlite;

namespace DeeKeep.Storage;

/// <summary>Échec d'un échange avec l'archive (<c>BlobError</c>) ; le message est montré tel quel.</summary>
public class ArchiveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Aucune archive n'est configurée (<c>NotConfigured</c>).</summary>
public sealed class ArchiveNotConfiguredException() :
    ArchiveException("Aucune archive locale n'est configurée : indiquez le fichier où ranger les titres.");

/// <summary>Le contenu lu ne correspond pas à l'empreinte attendue.</summary>
public sealed class ArchiveDigestException(string message) : ArchiveException(message);

/// <summary>
/// Magasin d'objets dans un fichier SQLite, compatible à l'octet avec
/// <c>sqlite_store.py</c> (tables <c>objects</c> et <c>archive_meta</c>, mêmes
/// colonnes, mêmes PRAGMA) : l'application desktop et la version web lisent la même
/// archive. Contrairement à l'original, aucun contenu ne transite en entier par la
/// mémoire : écriture par <c>zeroblob</c> puis <see cref="SqliteBlob"/> par tranches de
/// 1 Mio, lecture de même (restauration, écoute avec requêtes Range).
/// </summary>
public sealed class ObjectStore
{
    private const string SchemaVersion = "1";

    private static readonly string[] Schema =
    [
        "CREATE TABLE IF NOT EXISTS objects (    key    TEXT PRIMARY KEY,    bytes  INTEGER NOT NULL,    stored_at REAL NOT NULL,    data   BLOB NOT NULL)",
        "CREATE TABLE IF NOT EXISTS archive_meta (    key TEXT PRIMARY KEY,    value TEXT)",
    ];

    // Fichiers dont le schéma a déjà été vérifié par ce processus.
    private static readonly ConcurrentDictionary<string, bool> Initialized = new(StringComparer.Ordinal);

    public ObjectStore(string path, string prefix)
    {
        Path = path;
        Prefix = prefix;
    }

    /// <summary>Chemin absolu du fichier d'archive.</summary>
    public string Path { get; }

    /// <summary>Préfixe des clés, normalisé.</summary>
    public string Prefix { get; }

    public bool Exists => File.Exists(Path);

    // ------------------------------------------------------------------
    // Connexions
    // ------------------------------------------------------------------

    /// <summary>
    /// Connexion en écriture : crée dossier, fichier et schéma au besoin (<c>_db</c>).
    /// WAL pour que l'écoute lise pendant qu'un archivage écrit ; <c>synchronous=NORMAL</c>
    /// comme l'original (une coupure coûte au pire la dernière transaction).
    /// </summary>
    public SqliteConnection OpenWrite()
    {
        SqliteConnection conn;
        var existed = Exists;
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            conn = Connect(SqliteOpenMode.ReadWriteCreate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException)
        {
            throw new ArchiveException($"Archive inaccessible : {ex.Message}", ex);
        }
        try
        {
            conn.Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
            if (!existed || !Initialized.ContainsKey(Path))
            {
                foreach (var sql in Schema) conn.Execute(sql);
                conn.Execute("INSERT OR IGNORE INTO archive_meta(key, value) VALUES('schema_version', @v)", new { v = SchemaVersion });
                conn.Execute("INSERT OR IGNORE INTO archive_meta(key, value) VALUES('created_at', @v)", new { v = PythonFloat.Repr(Clock.UnixNow()) });
                Initialized[Path] = true;
            }
        }
        catch (SqliteException ex)
        {
            conn.Dispose();
            throw new ArchiveException($"Archive illisible : {ex.Message}", ex);
        }
        return conn;
    }

    /// <summary>Connexion de lecture, ou null si le fichier n'existe pas (une lecture ne crée rien).</summary>
    public SqliteConnection? OpenRead()
    {
        if (!Exists) return null;
        try
        {
            return Connect(SqliteOpenMode.ReadWrite);
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Archive inaccessible : {ex.Message}", ex);
        }
    }

    private SqliteConnection Connect(SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 30,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        // timeout=30 de l'original : un disque endormi met quelques secondes à répondre.
        conn.Execute("PRAGMA busy_timeout = 30000;");
        return conn;
    }

    // ------------------------------------------------------------------
    // Opérations (contrat de sqlite_store)
    // ------------------------------------------------------------------

    /// <summary><c>head</c> : taille d'un objet, ou null s'il est absent.</summary>
    public long? Head(string key)
    {
        using var conn = OpenRead();
        if (conn is null) return null;
        try
        {
            return conn.ExecuteScalar<long?>("SELECT bytes FROM objects WHERE key = @key", new { key });
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Lecture de {key} impossible : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// <c>put</c> en flux : l'objet entre entièrement ou pas du tout (transaction).
    /// L'empreinte du contenu effectivement copié est recalculée pendant la copie et
    /// comparée à <paramref name="expectedSha1"/> : un fichier modifié entre son
    /// hachage et sa copie n'entre pas sous une clé qui ne lui correspond plus.
    /// </summary>
    public async Task<long> PutAsync(string key, Stream source, long length, string? expectedSha1, CancellationToken ct)
    {
        if (length > ArchiveRules.MaxObjectBytes)
            throw new ArchiveException("Fichier trop volumineux pour l'archive (une copie est limitée à un milliard d'octets).");
        using var conn = OpenWrite();
        try
        {
            // Une copie de plusieurs mégaoctets sans déborder du cache de pages : sinon
            // les pages du zeroblob partent dans le WAL avant d'être réécrites.
            conn.Execute("PRAGMA cache_size = -8192;");
            using var tx = conn.BeginTransaction(deferred: false);
            conn.Execute("INSERT OR REPLACE INTO objects(key, bytes, stored_at, data) VALUES(@key, @length, @now, zeroblob(@length))",
                new { key, length, now = Clock.UnixNow() }, tx);
            var rowid = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
            using (var blob = new SqliteBlob(conn, "objects", "data", rowid))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                var buffer = new byte[ArchiveRules.Chunk];
                long written = 0;
                while (written < length)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - written)), ct);
                    if (read == 0) break;
                    blob.Write(buffer, 0, read);
                    sha.AppendData(buffer, 0, read);
                    written += read;
                }
                if (written != length || await source.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
                    throw new ArchiveDigestException("Le fichier a changé pendant la copie : il sera repris au prochain archivage.");
                if (expectedSha1 is not null && !Convert.ToHexStringLower(sha.GetHashAndReset()).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new ArchiveDigestException("Le fichier a changé pendant la copie : il sera repris au prochain archivage.");
            }
            ct.ThrowIfCancellationRequested();
            tx.Commit();
            return length;
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Écriture de {key} impossible : {ex.Message}", ex);
        }
    }

    /// <summary>Flux de lecture d'un objet (positionnable : sert les requêtes Range), ou null s'il est absent.</summary>
    public BlobReadStream? OpenObject(string key)
    {
        var conn = OpenRead();
        if (conn is null) return null;
        try
        {
            var rowid = conn.ExecuteScalar<long?>("SELECT rowid FROM objects WHERE key = @key", new { key });
            if (rowid is null)
            {
                conn.Dispose();
                return null;
            }
            return new BlobReadStream(conn, new SqliteBlob(conn, "objects", "data", rowid.Value, readOnly: true));
        }
        catch (SqliteException ex)
        {
            conn.Dispose();
            throw new ArchiveException($"Lecture de {key} impossible : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// <c>download</c> : écrit l'objet dans un temporaire « .dk_* » du dossier de
    /// destination, le relit pour en vérifier l'empreinte, puis le met en place d'un
    /// coup, sans jamais écraser un fichier apparu entre-temps. Une copie interrompue
    /// ou invalide ne laisse rien derrière elle.
    /// </summary>
    public async Task<long> DownloadAsync(string key, string destination, string? expectedSha1, CancellationToken ct)
    {
        await using var source = OpenObject(key) ?? throw new ArchiveException($"Lecture de {key} refusée : objet absent de l'archive.");
        var folder = System.IO.Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(folder)) folder = ".";
        var temporary = System.IO.Path.Combine(folder, ".dk_" + Guid.NewGuid().ToString("N")[..12]);
        long written = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, ArchiveRules.Chunk, FileOptions.Asynchronous))
            {
                var buffer = new byte[ArchiveRules.Chunk];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                }
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }

            var expected = (expectedSha1 ?? "").ToLowerInvariant();
            if (expected.Length > 0)
            {
                // La copie doit se prouver : relue depuis le disque, elle doit avoir
                // l'empreinte de l'original — sinon elle est écartée plutôt que gardée.
                string actual;
                try
                {
                    actual = await ArchiveRules.FileSha1Async(temporary, ct);
                }
                catch (IOException ex)
                {
                    throw new ArchiveDigestException($"Relecture impossible : {ex.Message}");
                }
                if (actual != expected)
                    throw new ArchiveDigestException("L'empreinte du fichier rendu ne correspond pas à celle attendue : la copie a été écartée.");
            }

            // Pas d'écrasement : si le fichier est apparu pendant la copie, il reste tel quel.
            File.Move(temporary, destination, overwrite: false);
            return written;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary><c>delete</c> : un objet déjà absent n'est pas une erreur.</summary>
    public void Delete(string key)
    {
        using var conn = OpenRead();
        if (conn is null) return;
        try
        {
            conn.Execute("DELETE FROM objects WHERE key = @key", new { key });
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Effacement de {key} impossible : {ex.Message}", ex);
        }
    }

    /// <summary><c>iter_objects</c> : (clé, taille) sous le préfixe de DeeKeep, sans lire les contenus.</summary>
    public IReadOnlyList<(string Key, long Bytes)> ListObjects(string? prefix = null)
    {
        using var conn = OpenRead();
        if (conn is null) return [];
        var wanted = prefix ?? Prefix;
        try
        {
            var rows = wanted.Length > 0
                ? conn.Query<(string, long)>("SELECT key, bytes FROM objects WHERE key LIKE @motif ESCAPE '\\' ORDER BY key", new { motif = ArchiveRules.LikePattern(wanted) })
                : conn.Query<(string, long)>("SELECT key, bytes FROM objects ORDER BY key");
            return rows.ToList();
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Archive illisible : {ex.Message}", ex);
        }
    }

    /// <summary><c>usage</c> : (nombre d'objets, octets occupés) sous le préfixe.</summary>
    public (long Count, long Bytes) Usage()
    {
        using var conn = OpenRead();
        if (conn is null) return (0, 0);
        try
        {
            var row = Prefix.Length > 0
                ? conn.QueryFirst<(long, long?)>("SELECT COUNT(*), SUM(bytes) FROM objects WHERE key LIKE @motif ESCAPE '\\'", new { motif = ArchiveRules.LikePattern(Prefix) })
                : conn.QueryFirst<(long, long?)>("SELECT COUNT(*), SUM(bytes) FROM objects");
            return (row.Item1, row.Item2 ?? 0);
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Archive illisible : {ex.Message}", ex);
        }
    }

    /// <summary><c>check</c> : l'archive s'ouvre et accepte une écriture.</summary>
    public void Check()
    {
        using var conn = OpenWrite();
        try
        {
            conn.Execute("INSERT OR REPLACE INTO archive_meta(key, value) VALUES('checked_at', @v)", new { v = PythonFloat.Repr(Clock.UnixNow()) });
        }
        catch (SqliteException ex)
        {
            throw new ArchiveException($"Archive inutilisable : {ex.Message}", ex);
        }
    }

    /// <summary><c>file_bytes</c> : poids du fichier sur le disque, WAL compris.</summary>
    public long FileBytes()
    {
        long total = 0;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                var info = new FileInfo(Path + suffix);
                if (info.Exists) total += info.Length;
            }
            catch (IOException)
            {
            }
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Flux d'un BLOB qui ferme aussi sa connexion.</summary>
public sealed class BlobReadStream(SqliteConnection connection, SqliteBlob blob) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => blob.Length;

    public override long Position
    {
        get => blob.Position;
        set => blob.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => blob.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => blob.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(blob.Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => blob.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            blob.Dispose();
            connection.Dispose();
        }
        base.Dispose(disposing);
    }
}
