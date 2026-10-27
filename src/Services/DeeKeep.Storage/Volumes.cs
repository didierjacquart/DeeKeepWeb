namespace DeeKeep.Storage;

/// <summary>
/// Volumes et place libre (<c>free_bytes</c>, <c>same_device_as</c>). L'original ne
/// comparait que la lettre de lecteur, toujours vide sous Linux : ici le volume est
/// le point de montage le plus long qui contient le chemin (la lettre sous Windows).
/// </summary>
public static class Volumes
{
    /// <summary>Premier dossier existant en remontant depuis <paramref name="path"/>, ou null.</summary>
    public static string? ExistingFolder(string path)
    {
        try
        {
            var probe = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (parent is null || parent == probe) return null;
                probe = parent;
            }
            return string.IsNullOrEmpty(probe) ? null : probe;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }

    /// <summary>Volume qui porte un chemin (point de montage ou racine de lecteur), ou null.</summary>
    public static DriveInfo? DriveOf(string path)
    {
        var folder = ExistingFolder(path);
        if (folder is null) return null;
        DriveInfo? best = null;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName;
                if (!IsUnder(folder, root)) continue;
                if (best is null || root.Length > best.RootDirectory.FullName.Length) best = drive;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return best;
    }

    /// <summary>(libre, total) du volume qui porte <paramref name="path"/> ; (0, null) s'il est injoignable.</summary>
    public static (long Free, long? Total) Space(string path)
    {
        var drive = DriveOf(path);
        if (drive is null) return (0, null);
        try
        {
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, null);
        }
    }

    /// <summary>Vrai si les deux chemins vivent sur le même volume.</summary>
    public static bool SameVolume(string a, string b)
    {
        var da = DriveOf(a);
        var db = DriveOf(b);
        return da is not null && db is not null &&
               string.Equals(da.RootDirectory.FullName, db.RootDirectory.FullName,
                   OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsUnder(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root, comparison)) return false;
        return path.Length == root.Length || root.EndsWith(Path.DirectorySeparatorChar) || path[root.Length] == Path.DirectorySeparatorChar;
    }
}
