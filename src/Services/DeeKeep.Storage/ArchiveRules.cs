using System.Globalization;
using System.Security.Cryptography;

namespace DeeKeep.Storage;

/// <summary>États d'un titre vis-à-vis de l'archive (<c>library_mirror.ABSENT/FRESH/STALE</c>).</summary>
public enum ArchiveState
{
    Absent,
    Fresh,
    Stale,
}

/// <summary>
/// Règles de l'archive portées de <c>library_mirror.py</c> et <c>sqlite_store.py</c> :
/// clés d'objet, préfixe, état d'un titre, destination de restauration, empreinte SHA-1,
/// taille lisible des messages.
/// </summary>
public static class ArchiveRules
{
    /// <summary>Préfixe par défaut des clés (<c>DEFAULT_PREFIX</c>).</summary>
    public const string DefaultPrefix = "deekeep/";

    /// <summary>Nom du fichier créé quand l'emplacement désigne un dossier (<c>DEFAULT_FILENAME</c>).</summary>
    public const string DefaultFileName = "deekeep-archive.db";

    /// <summary>Rayon des copies telles quelles (<c>SOURCE_SHELF</c>).</summary>
    public const string SourceShelf = "audio/source/";

    /// <summary>Tranche de lecture et d'écriture (<c>CHUNK</c> : 1 Mio).</summary>
    public const int Chunk = 1 << 20;

    /// <summary>
    /// Plus gros fichier accepté : SQLite refuse une valeur de plus de
    /// <c>SQLITE_MAX_LENGTH</c> (1 000 000 000 octets par défaut, enregistrement compris).
    /// Le refus est dit proprement au lieu d'une erreur SQLite obscure.
    /// </summary>
    public const long MaxObjectBytes = 999_000_000;

    /// <summary><c>normalize_prefix</c> : sans « / » de tête, avec « / » de queue, "" si vide.</summary>
    public static string NormalizePrefix(string? prefix)
    {
        var value = (prefix ?? "").Trim().Trim('/');
        return value.Length > 0 ? value + "/" : "";
    }

    /// <summary><c>object_key</c> : <c>{prefix}audio/source/{sha1[:2]}/{sha1}{ext.lower()}</c>.</summary>
    public static string ObjectKey(string prefix, string sha1, string? extension) =>
        $"{prefix}{SourceShelf}{sha1[..Math.Min(2, sha1.Length)]}/{sha1}{(extension ?? "").ToLowerInvariant()}";

    /// <summary><c>is_source_copy</c> : l'objet est-il le fichier d'origine (et non une réduction Opus héritée) ?</summary>
    public static bool IsSourceCopy(string? key) => (key ?? "").Contains(SourceShelf, StringComparison.Ordinal);

    /// <summary>
    /// <c>restore_target</c> : où écrire l'objet, et s'il s'agit de l'original. Une clé
    /// héritée du miroir Opus désignerait une réduction : destination en « .mp3 ».
    /// </summary>
    public static (string Destination, bool Identical) RestoreTarget(string path, string? key)
    {
        if (IsSourceCopy(key)) return (path, true);
        var ext = Path.GetExtension(path);
        return (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? path : path[..^ext.Length] + ".mp3", false);
    }

    /// <summary><c>state_of</c> : ABSENT sans clé ; STALE si les deux empreintes existent et diffèrent ; FRESH sinon.</summary>
    public static ArchiveState StateOf(string? key, string? rowStamp, string? currentStamp)
    {
        if (string.IsNullOrEmpty(key)) return ArchiveState.Absent;
        if (!string.IsNullOrEmpty(currentStamp) && !string.IsNullOrEmpty(rowStamp) && rowStamp != currentStamp) return ArchiveState.Stale;
        return ArchiveState.Fresh;
    }

    /// <summary>
    /// <c>resolve</c> du fichier d'archive : un dossier existant, ou un chemin sans
    /// extension, reçoit <c>deekeep-archive.db</c> ; le chemin est rendu absolu. Null si vide.
    /// </summary>
    public static string? ResolveArchiveFile(string? root)
    {
        var path = (root ?? "").Trim();
        if (path.Length == 0) return null;
        if (Directory.Exists(path) || Path.GetExtension(path).Length == 0) path = Path.Combine(path, DefaultFileName);
        return Path.GetFullPath(path);
    }

    /// <summary>Motif <c>LIKE … ESCAPE '\'</c> d'un préfixe (un « _ » ne vaut pas « n'importe quel caractère »).</summary>
    public static string LikePattern(string prefix) =>
        prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary><c>human_bytes</c> de la page Bibliothèque : « 1,2 Go », « 340,0 Mo », « 12 o ».</summary>
    public static string HumanBytes(long count)
    {
        double value = count;
        foreach (var (unit, threshold) in new[] { ("Go", 1e9), ("Mo", 1e6), ("Ko", 1e3) })
        {
            if (value >= threshold)
                return (value / threshold).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " " + unit;
        }
        return $"{(long)value} o";
    }

    /// <summary>SHA-1 hexadécimal minuscule d'un fichier, lu par tranches de 1 Mio (<c>file_sha1</c>).</summary>
    public static async Task<string> FileSha1Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[Chunk];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) sha.AppendData(buffer, 0, read);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// <c>probe_bitrate</c> : débit en kb/s lu dans l'en-tête, sinon déduit de la taille
    /// et de la durée, 0 si indéchiffrable. Arrondi bancaire comme <c>round</c> de Python.
    /// </summary>
    public static int ProbeBitrate(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var props = file.Properties;
            if (props is null) return 0;
            if (props.AudioBitrate > 0) return props.AudioBitrate;
            var length = props.Duration.TotalSeconds;
            if (length > 0) return (int)Math.Round(new FileInfo(path).Length * 8 / length / 1000.0, MidpointRounding.ToEven);
        }
        catch (Exception)
        {
            // Fichier tronqué, en-tête introuvable : le débit reste inconnu, ce qui
            // n'empêche en rien de l'archiver tel quel.
        }
        return 0;
    }
}
