namespace DeeKeep.Common.Text;

/// <summary>
/// Manipulation de chemins à la manière de <c>ntpath</c> (os.path de Python
/// sous Windows), quel que soit le système hôte.
/// </summary>
/// <remarks>
/// Les chemins de la base existante sont des chemins Windows (« D:\Artiste\Titre.mp3 ») :
/// leurs clés doivent être calculées comme l'application desktop les calculait,
/// même quand le service tourne sous Linux. « / » et « \ » sont tous deux des séparateurs.
/// </remarks>
public static class PyPath
{
    private const char Sep = '\\';

    private static bool IsSep(char c) => c is '\\' or '/';

    /// <summary><c>ntpath.splitroot</c> : (lecteur, racine, reste).</summary>
    public static (string Drive, string Root, string Tail) SplitRoot(string p)
    {
        var normp = p.Replace('/', Sep);
        if (normp.Length >= 1 && normp[0] == Sep)
        {
            if (normp.Length >= 2 && normp[1] == Sep)
            {
                // Partage UNC (\\serveur\partage, \\?\UNC\serveur\partage) ou périphérique (\\.\x, \\?\x).
                var start = normp.Length >= 8 && normp[..8].ToUpperInvariant() == @"\\?\UNC\" ? 8 : 2;
                var index = normp.IndexOf(Sep, start);
                if (index == -1) return (p, "", "");
                var index2 = normp.IndexOf(Sep, index + 1);
                if (index2 == -1) return (p, "", "");
                return (p[..index2], p.Substring(index2, 1), p[(index2 + 1)..]);
            }
            return ("", p[..1], p[1..]);
        }
        if (normp.Length >= 2 && normp[1] == ':')
        {
            if (normp.Length >= 3 && normp[2] == Sep) return (p[..2], p.Substring(2, 1), p[3..]);
            return (p[..2], "", p[2..]);
        }
        return ("", "", p);
    }

    /// <summary><c>ntpath.split</c> : (tête, queue).</summary>
    public static (string Head, string Tail) Split(string? path)
    {
        var (d, r, p) = SplitRoot(path ?? "");
        var i = p.Length;
        while (i > 0 && !IsSep(p[i - 1])) i--;
        return (d + r + p[..i].TrimEnd('\\', '/'), p[i..]);
    }

    /// <summary><c>ntpath.basename</c>.</summary>
    public static string Basename(string? path) => Split(path).Tail;

    /// <summary><c>ntpath.dirname</c>.</summary>
    public static string Dirname(string? path) => Split(path).Head;

    /// <summary>
    /// <c>ntpath.splitext</c> : l'extension commence au dernier point du dernier
    /// composant, points de tête exclus (« .bashrc » n'a pas d'extension).
    /// Attention : « Mr. Brightside » donne (« Mr », « . Brightside »).
    /// </summary>
    public static (string Root, string Ext) SplitExt(string? path)
    {
        var p = path ?? "";
        var sepIndex = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
        var dotIndex = p.LastIndexOf('.');
        if (dotIndex > sepIndex)
        {
            for (var i = sepIndex + 1; i < dotIndex; i++)
                if (p[i] != '.') return (p[..dotIndex], p[dotIndex..]);
        }
        return (p, "");
    }

    /// <summary><c>os.path.splitext(os.path.basename(path))[0]</c> : nom sans extension.</summary>
    public static string Stem(string? path) => SplitExt(Basename(path)).Root;

    /// <summary><c>ntpath.normpath</c> : séparateurs unifiés en « \ », « . » et « .. » résolus, casse conservée.</summary>
    public static string NormPath(string? path)
    {
        var (drive, root, rest) = SplitRoot((path ?? "").Replace('/', Sep));
        var prefix = drive + root;
        var comps = new List<string>(rest.Split(Sep));
        var i = 0;
        while (i < comps.Count)
        {
            if (comps[i].Length == 0 || comps[i] == ".")
                comps.RemoveAt(i);
            else if (comps[i] == "..")
            {
                if (i > 0 && comps[i - 1] != "..")
                {
                    comps.RemoveRange(i - 1, 2);
                    i--;
                }
                else if (i == 0 && root.Length > 0)
                    comps.RemoveAt(i);
                else
                    i++;
            }
            else i++;
        }
        if (prefix.Length == 0 && comps.Count == 0) comps.Add(".");
        return prefix + string.Join(Sep, comps);
    }

    /// <summary><c>ntpath.normcase</c> : minuscules et « \ » (clé de rapprochement insensible à la casse).</summary>
    public static string NormCase(string? path) => (path ?? "").Replace('/', Sep).ToLowerInvariant();
}
