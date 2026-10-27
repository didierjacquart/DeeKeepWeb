using DeeKeep.Common.Files;
using DeeKeep.Common.Text;
using DeeKeep.Library.Settings;

namespace DeeKeep.Library.Files;

/// <summary>
/// Chemins de la bibliothèque : conversion base ↔ serveur, dossier de dépôt, racines
/// que le navigateur de fichiers a le droit de montrer.
/// </summary>
public sealed class LibraryPaths(PathMap map, SettingsStore settings, IConfiguration configuration)
{
    /// <summary>Corbeille : les fichiers « supprimés » y sont déplacés, jamais effacés.</summary>
    public const string TrashFolder = ".deekeep-trash";

    /// <summary>Chemin réel (serveur) d'un chemin stocké en base.</summary>
    public string ToLocal(string dbPath) => map.ToLocal(dbPath);

    /// <summary>Forme stockée en base d'un chemin local, normalisée comme <c>canonical_path</c> (clé de <c>tracks</c>).</summary>
    public string ToDb(string localPath) => LibraryText.CanonicalPath(map.ToDb(localPath));

    /// <summary>Clé de rapprochement insensible à la casse (<c>match_key</c>) d'un chemin de base.</summary>
    public static string Key(string dbPath) => LibraryText.MatchKey(dbPath);

    /// <summary>Dossier de dépôt sur le serveur.</summary>
    public string LocalDownloadDir() => ToLocal(settings.DownloadDir());

    /// <summary>Racines consultables : dossier de dépôt, puis <c>Library:BrowseRoots</c>.</summary>
    public IReadOnlyList<string> Roots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            if (!roots.Any(r => SamePath(r, full))) roots.Add(full);
        }
        Add(LocalDownloadDir());
        foreach (var child in configuration.GetSection("Library:BrowseRoots").GetChildren()) Add(child.Value);
        return roots;
    }

    /// <summary>Vrai si <paramref name="localPath"/> est une racine autorisée ou se trouve dessous.</summary>
    public bool IsAllowed(string localPath)
    {
        string full;
        try { full = Path.GetFullPath(localPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        return Roots().Any(root => IsUnder(full, root));
    }

    public static bool IsUnder(string fullPath, string root)
    {
        var r = Path.TrimEndingDirectorySeparator(root);
        var p = Path.TrimEndingDirectorySeparator(fullPath);
        if (SamePath(p, r)) return true;
        return p.StartsWith(r + Path.DirectorySeparatorChar, PathComparison) ||
               (r.EndsWith(Path.DirectorySeparatorChar) && p.StartsWith(r, PathComparison));
    }

    /// <summary>Comparaison de chemins locaux : sans casse sous Windows, exacte ailleurs.</summary>
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), PathComparison);

    /// <summary>Dossier ignoré par les parcours : corbeilles, dossiers système et cachés (« $RECYCLE.BIN », « .deekeep-trash »).</summary>
    public static bool IsExcludedFolder(string name) =>
        name.StartsWith('$') || name.StartsWith('.') || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
}
