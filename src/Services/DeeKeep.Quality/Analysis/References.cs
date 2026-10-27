using System.Text.RegularExpressions;

namespace DeeKeep.Quality.Analysis;

/// <summary>
/// Durées de référence côté écran — portage de <c>reference_duration._canonical</c> (clé de
/// la table <c>reference_durations</c>), de <c>closest</c> et de la règle d'expiration des
/// recherches négatives.
/// </summary>
public static partial class References
{
    /// <summary>NEGATIVE_TTL : une recherche sans résultat est retentée après 30 jours.</summary>
    public const double NegativeTtlSeconds = 30 * 24 * 3600;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnumRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    /// <summary>
    /// <c>_canonical</c> : minuscules, tout ce qui n'est pas [a-z0-9] devient une espace (les
    /// lettres accentuées disparaissent : « Teló » → « tel »), espaces réduites.
    /// </summary>
    public static string Canonical(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        t = NonAlnumRe().Replace(t, " ");
        return SpacesRe().Replace(t, " ").Trim();
    }

    /// <summary>Clé de cache : <c>canonical(artiste) + "|" + canonical(titre)</c>.</summary>
    public static string Key(string? artist, string? title) => Canonical(artist?.Trim()) + "|" + Canonical(title?.Trim());

    /// <summary>
    /// <c>closest(durations, measured)</c> : la durée connue la plus proche de celle du fichier
    /// (la première en cas d'égalité), pour ne pas accuser un titre de ne pas durer autant qu'un remix.
    /// </summary>
    public static double? Closest(IReadOnlyList<double>? durations, double measured)
    {
        if (durations is null || durations.Count == 0 || measured == 0 || double.IsNaN(measured)) return null;
        var best = durations[0];
        foreach (var d in durations)
        {
            if (Math.Abs(d - measured) < Math.Abs(best - measured)) best = d;
        }
        return best;
    }

    /// <summary>Une entrée de cache est-elle encore valable (une liste vide expire après 30 jours) ?</summary>
    public static bool IsFresh(IReadOnlyList<double> durations, double? fetchedAt, double now) =>
        durations.Count > 0 || (fetchedAt is { } at && now - at <= NegativeTtlSeconds);
}
