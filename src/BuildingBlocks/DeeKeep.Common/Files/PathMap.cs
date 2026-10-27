using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Files;

public sealed class PathMapping
{
    /// <summary>Préfixe tel qu'il figure dans la base (ex. <c>D:\</c>).</summary>
    public string From { get; set; } = "";

    /// <summary>Emplacement réel sur le serveur (ex. <c>/srv/musique/</c>).</summary>
    public string To { get; set; } = "";
}

public sealed class PathMapOptions
{
    public List<PathMapping> Mappings { get; set; } = [];
}

/// <summary>
/// Correspondance entre les chemins stockés dans la base (écrits par
/// l'application Windows : <c>D:\Artiste\Titre.mp3</c>) et les chemins du
/// serveur Linux. La base n'est jamais réécrite : un chemin stocké reste
/// identique, la synchronisation Turso et l'application desktop restent
/// compatibles ; un fichier créé sous un préfixe connu est enregistré sous sa
/// forme d'origine.
/// </summary>
public sealed class PathMap(IOptions<PathMapOptions> options)
{
    private readonly List<PathMapping> _mappings = options.Value.Mappings
        .Where(m => !string.IsNullOrWhiteSpace(m.From) && !string.IsNullOrWhiteSpace(m.To))
        .OrderByDescending(m => m.From.Length)
        .ToList();

    /// <summary>Chemin de la base → chemin local du serveur.</summary>
    public string ToLocal(string dbPath)
    {
        foreach (var m in _mappings)
        {
            if (StartsWithPrefix(dbPath, m.From))
            {
                var rest = dbPath[m.From.Length..].Replace('\\', '/').TrimStart('/');
                return Path.Combine(m.To, rest);
            }
        }
        return OperatingSystem.IsWindows() ? dbPath : dbPath.Replace('\\', '/');
    }

    /// <summary>Chemin local du serveur → forme stockée en base.</summary>
    public string ToDb(string localPath)
    {
        var full = Path.GetFullPath(localPath);
        foreach (var m in _mappings.OrderByDescending(m => m.To.Length))
        {
            var to = Path.GetFullPath(m.To);
            if (full.StartsWith(to.TrimEnd('/', '\\'), StringComparison.Ordinal))
            {
                var rest = full[to.TrimEnd('/', '\\').Length..].TrimStart('/', '\\');
                var windows = m.From.Contains('\\') || (m.From.Length >= 2 && m.From[1] == ':');
                var sep = windows ? '\\' : '/';
                var prefix = m.From.TrimEnd('/', '\\');
                return prefix + sep + rest.Replace('/', sep).Replace('\\', sep);
            }
        }
        return full;
    }

    private static bool StartsWithPrefix(string path, string prefix)
    {
        var p = path.Replace('/', '\\');
        var f = prefix.Replace('/', '\\');
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
