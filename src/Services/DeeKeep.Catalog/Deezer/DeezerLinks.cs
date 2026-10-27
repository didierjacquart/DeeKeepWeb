using System.Text.RegularExpressions;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.Deezer;

/// <summary>Lien Deezer reconnu dans une requête : type d'entité et identifiant tel qu'écrit.</summary>
public sealed record DeezerLink(EntityKind Kind, string Id);

/// <summary>
/// Reconnaissance des liens Deezer dans le champ de recherche — règles 1 à 4 de
/// <c>DeezerSearchThread.run</c>, dans cet ordre exact et par simple sous-chaîne, quel
/// que soit le type de recherche choisi : « track/ », puis « playlist/ », « album/ »,
/// « artist/ ». L'identifiant est ce qui suit le dernier marqueur, jusqu'au premier « ? ».
/// </summary>
public static partial class DeezerLinks
{
    private static readonly (string Marker, EntityKind Kind)[] Markers =
    [
        ("track/", EntityKind.Track),
        ("playlist/", EntityKind.Playlist),
        ("album/", EntityKind.Album),
        ("artist/", EntityKind.Artist),
    ];

    /// <summary>Liens courts de partage (application mobile), à suivre par redirection.</summary>
    [GeneratedRegex(@"^https?://(?:deezer\.page\.link|link\.deezer\.com|dzr\.page\.link)/\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortLinkRe();

    public static DeezerLink? Parse(string? query)
    {
        var q = query ?? "";
        foreach (var (marker, kind) in Markers)
        {
            var index = q.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 0) continue;
            // query.split(marker)[-1].split("?")[0].strip()
            var rest = q[(index + marker.Length)..];
            var question = rest.IndexOf('?');
            if (question >= 0) rest = rest[..question];
            return new DeezerLink(kind, rest.Trim());
        }
        return null;
    }

    public static bool IsShortLink(string? query) => ShortLinkRe().IsMatch((query ?? "").Trim());

    /// <summary>Hôte appartenant à Deezer (destination acceptable d'une redirection de lien court).</summary>
    public static bool IsDeezerHost(string host) =>
        host.Equals("deezer.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".deezer.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Identifiant numérique (le seul que l'API accepte) ; null sinon.</summary>
    public static long? NumericId(string id) =>
        id.Length > 0 && id.All(char.IsAsciiDigit) && long.TryParse(id, out var value) ? value : null;
}
