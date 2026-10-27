using System.Text.RegularExpressions;
using DeeKeep.Common.Text;

namespace DeeKeep.Downloader.Extraction;

/// <summary>Vidéo YouTube rendue par une recherche « à plat » (identifiant, titre).</summary>
public sealed record VideoHit(string Id, string Title);

/// <summary>Vidéo retenue pour une piste, avec sa note (correspondance du titre, + 1 si l'artiste est annoncé).</summary>
public sealed record VideoCandidate(string Id, string Title, double Score);

/// <summary>
/// Choix de la vidéo YouTube d'une piste — portage de <c>search_queries</c> et
/// <c>_youtube_candidates</c> (<c>download_thread.py:81-101, 371-414</c>).
/// </summary>
/// <remarks>
/// Deux défauts de l'original sont corrigés (spec 01 §3.3, §6.5) : la deuxième requête
/// nettoie le titre sans passer par <c>basename/splitext</c> (« Mr. Brightside » n'y
/// devient plus « Mr », ni « AC/DC » « DC »), et les mots sont découpés dans tout
/// alphabet (un titre cyrillique ou japonais peut enfin être reconnu). Le mode
/// <see cref="WordMode.Python"/> et <see cref="TagText.CleanStem"/> restent disponibles
/// pour vérifier l'équivalence avec l'original.
/// </remarks>
public static partial class YouTubeMatching
{
    /// <summary>MIN_TITLE_MATCH : part des mots du titre qu'une vidéo doit reprendre.</summary>
    public const double MinTitleMatch = 0.7;

    /// <summary>SEARCH_CANDIDATES : vidéos examinées par requête.</summary>
    public const int SearchCandidates = 3;

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    /// <summary>
    /// Requêtes à essayer, de la plus fidèle à la plus large : celle qu'a toujours
    /// employée l'application (« (Official Audio) »), puis l'artiste principal avec le
    /// titre débarrassé du bruit, puis avec le seul morceau (sans mention de version).
    /// </summary>
    public static IReadOnlyList<string> SearchQueries(string? artist, string? title, bool faithful = false)
    {
        var queries = new List<string>();
        var performer = TagText.MainArtist(artist);
        var cleaned = faithful ? TagText.CleanStem(title) : TagText.CleanTitle(title);
        foreach (var candidate in new[]
                 {
                     $"{artist} - {title} (Official Audio)",
                     $"{performer} - {cleaned}",
                     $"{performer} - {TagText.CoreTitle(title)}",
                 })
        {
            var query = SpacesRe().Replace(candidate, " ").Trim(' ', '-');
            if (query.Length > 0 && !queries.Contains(query, StringComparer.Ordinal)) queries.Add(query);
        }
        return queries;
    }

    /// <summary>
    /// Filtre et classe les vidéos rendues par les requêtes successives : une vidéo
    /// doit reprendre au moins 70 % des mots significatifs du morceau ; celle qui
    /// annonce aussi l'artiste passe devant. On s'arrête à la première requête qui
    /// produit un candidat (les plus larges ne servent qu'en dernier recours) ; le tri
    /// est stable, l'ordre de pertinence de YouTube départage les égalités.
    /// </summary>
    public static async Task<IReadOnlyList<VideoCandidate>> SelectAsync(
        string? artist, string? title, Func<string, Task<IReadOnlyList<VideoHit>>> search,
        Action? onQuery = null, WordMode mode = WordMode.Unicode, bool faithful = false)
    {
        var wanted = TagText.CoreTitle(title);
        var performer = TagText.MainArtist(artist);
        var kept = new List<VideoCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in SearchQueries(artist, title, faithful))
        {
            onQuery?.Invoke();
            foreach (var hit in await search(query))
            {
                if (!seen.Add(hit.Id)) continue;
                var score = Score(wanted, performer, hit.Title, mode);
                if (score is { } s) kept.Add(new VideoCandidate(hit.Id, hit.Title, s));
            }
            if (kept.Count > 0) break;
        }
        // OrderByDescending est stable : à note égale, l'ordre de YouTube est conservé.
        return kept.OrderByDescending(c => c.Score).ToList();
    }

    /// <summary>Note d'une vidéo, ou null si son titre ne reprend pas assez le morceau.</summary>
    public static double? Score(string wanted, string performer, string videoTitle, WordMode mode = WordMode.Unicode)
    {
        var score = TagText.Contained(wanted, videoTitle, mode);
        if (score < MinTitleMatch) return null;
        if (TagText.Contained(performer, videoTitle, mode) >= MinTitleMatch) score += 1;
        return score;
    }

    /// <summary>Message d'échec définitif quand aucune vidéo ne convient.</summary>
    public static string NoVideoMessage(string? artist, string? title) =>
        $"Aucune vidéo ne correspond à « {TagText.MainArtist(artist)} - {TagText.CoreTitle(title)} » sur YouTube : " +
        "les tags du fichier ne mènent à rien de reconnaissable.";
}
