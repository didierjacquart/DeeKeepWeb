namespace DeeKeep.Common.Audio;

/// <summary>
/// Empreintes « date + taille » d'un fichier, qui disent si une mesure ou une copie
/// d'archive est encore valable. Deux formats coexistent dans la base existante :
/// <list type="bullet">
/// <item>table <c>quality</c> (<c>quality_scan.MeasureCache._stamp</c>) : <c>"{st_mtime_ns}:{st_size}"</c>,
/// ex. « 1784567348000000000:3878924 » ;</item>
/// <item>table <c>archive</c> (<c>library_mirror.stamp</c>) : <c>"{int(st_mtime)}:{st_size}"</c>,
/// secondes tronquées, ex. « 1784576096:3642033 ».</item>
/// </list>
/// </summary>
/// <remarks>
/// Précision : .NET ne lit la date de modification qu'au dixième de microseconde
/// (100 ns). Sous Windows c'est exactement la précision de NTFS, donc de Python : les
/// empreintes sont identiques. Sous Linux (ext4 : nanoseconde), les deux derniers
/// chiffres de <c>st_mtime_ns</c> sont perdus — une empreinte « quality » calculée ici
/// ne coïncidera pas avec celle que Python aurait calculée sous Linux pour un fichier
/// dont la date porte des nanosecondes. Sans conséquence : les empreintes sont toujours
/// comparées à d'autres calculées par ce même code, et un fichier copié depuis Windows
/// en conservant sa date (rsync -t, robocopy) garde une date multiple de 100 ns.
/// L'empreinte « archive », à la seconde, n'est pas concernée.
/// </remarks>
public static class Stamp
{
    private const long NanosPerSecond = 1_000_000_000;

    /// <summary>Date de modification en nanosecondes depuis l'époque Unix (<c>st_mtime_ns</c>).</summary>
    public static long MtimeNs(FileInfo file) =>
        (file.LastWriteTimeUtc.Ticks - DateTime.UnixEpoch.Ticks) * 100;

    /// <summary>Empreinte de la table <c>quality</c>, ou null si le fichier est illisible (comme l'original).</summary>
    public static string? Quality(string path)
    {
        var file = Stat(path);
        return file is null ? null : Quality(MtimeNs(file), file.Length);
    }

    /// <summary>Empreinte de la table <c>quality</c> : <c>"{mtime_ns}:{taille}"</c>.</summary>
    public static string Quality(long mtimeNs, long size) => $"{mtimeNs}:{size}";

    /// <summary>Empreinte de la table <c>archive</c>, ou "" si le fichier est absent (comme l'original).</summary>
    public static string Archive(string path)
    {
        var file = Stat(path);
        return file is null ? "" : Archive(MtimeNs(file), file.Length);
    }

    /// <summary>
    /// Empreinte de la table <c>archive</c> : <c>"{int(st_mtime)}:{taille}"</c>. Python calcule
    /// <c>st_mtime = sec + nsec × 1e-9</c> en double puis le tronque : le même calcul est
    /// refait ici, arrondi flottant compris (une date à 0,99999999 s près de la seconde
    /// suivante y bascule, comme dans l'original).
    /// </summary>
    public static string Archive(long mtimeNs, long size)
    {
        var sec = Math.DivRem(mtimeNs, NanosPerSecond, out var nsec);
        if (nsec < 0) { sec -= 1; nsec += NanosPerSecond; }
        var mtime = sec + nsec * 1e-9;
        return $"{(long)mtime}:{size}";
    }

    private static FileInfo? Stat(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
