using DeeKeep.Common.Text;

namespace DeeKeep.Catalog.Tags;

/// <summary>
/// Piste candidate rendue par un service (forme commune de <c>search_deezer</c>,
/// <c>search_musicbrainz</c> et <c>acoustid_lookup.parse_results</c>).
/// </summary>
public sealed record TagCandidate(
    string Title,
    string Artist,
    string Album,
    int Seconds,
    string CoverUrl = "",
    string Id = "",
    string AlbumId = "",
    IReadOnlyList<string>? Albums = null,
    double Match = 0);

/// <summary>
/// Fichier à identifier, dans le vocabulaire des tags (entrée d'analyse de <c>read_tags</c>).
/// <see cref="Title"/> vaut le nom du fichier quand <see cref="TitleTagged"/> est faux.
/// </summary>
public sealed record TagEntry(
    string Path,
    string Title,
    bool TitleTagged,
    string Artist = "",
    string Album = "",
    string AlbumArtist = "",
    string Genre = "",
    string Date = "",
    string Track = "",
    string TrackTotal = "",
    string Disc = "")
{
    public string Get(string field) => field switch
    {
        "title" => Title,
        "artist" => Artist,
        "album" => Album,
        "albumartist" => AlbumArtist,
        "genre" => Genre,
        "date" => Date,
        "track" => Track,
        "tracktotal" => TrackTotal,
        "disc" => Disc,
        _ => "",
    };
}

/// <summary>
/// Notation et requêtes de « Compléter les tags » — portage fidèle de
/// <c>deekeep/audio/tag_lookup.py</c> (fonctions pures, sans réseau). Le découpage en
/// mots est celui, corrigé, de <see cref="TagText.Words"/> : identique pour un texte en
/// alphabet latin, il reconnaît en plus les titres cyrilliques, grecs ou japonais.
/// </summary>
public static partial class TagMatcher
{
    /// <summary><c>_QUALIFIER_RE</c> : mention de version entre parenthèses ou crochets.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\s*[(\[][^)\]]*[)\]]\s*", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex QualifierRe();

    public const double MinScore = TagText.MinScore;
    public const double SureScore = TagText.SureScore;
    public const int DurationExact = 3;
    public const int DurationMax = 30;
    public const int DurationAmbiguous = 8;
    public const double DurationOnlyScore = 0.78;
    public const double CoreTitleWeight = TagText.CoreTitleWeight;
    public const int MusicBrainzMaxQueries = 3;
    /// <summary>Note d'une correspondance franche : les requêtes suivantes sont inutiles.</summary>
    public const double FrankScore = 0.95;

    public static readonly IReadOnlyList<string> CoreFields = ["title", "artist", "album"];
    public static readonly IReadOnlyList<string> ExtraFields = ["albumartist", "genre", "date", "track", "tracktotal", "disc"];
    public static readonly IReadOnlyList<string> ExtraTriggerFields = ["albumartist", "genre", "date", "track"];

    private static IReadOnlyList<string> Words(string? text) => TagText.Words(text);

    private static bool SameWords(string? a, string? b) => Words(a).SequenceEqual(Words(b), StringComparer.Ordinal);

    /// <summary><c>missing_fields</c> : le titre manque s'il ne vient pas des tags ; artiste et album s'ils sont vides.</summary>
    public static IReadOnlyList<string> MissingFields(TagEntry entry)
    {
        var absent = new List<string>();
        if (!entry.TitleTagged) absent.Add("title");
        if (PyText.Strip(entry.Artist).Length == 0) absent.Add("artist");
        if (PyText.Strip(entry.Album).Length == 0) absent.Add("album");
        return absent;
    }

    public static IReadOnlyList<string> MissingExtras(TagEntry entry) =>
        ExtraFields.Where(f => PyText.Strip(entry.Get(f)).Length == 0).ToList();

    /// <summary><c>needs_extras</c> : le nombre de pistes et le disque ne déclenchent jamais une recherche à eux seuls.</summary>
    public static bool NeedsExtras(TagEntry entry) => ExtraTriggerFields.Any(f => PyText.Strip(entry.Get(f)).Length == 0);

    /// <summary><c>_clues</c> : (nom de fichier nettoyé, artiste, album, dossier parent utile).</summary>
    public static (string Stem, string Artist, string Album, string Parent) Clues(TagEntry entry)
    {
        var parent = PyPath.Basename(PyPath.Dirname(entry.Path));
        if (TagText.IsGenericFolder(parent)) parent = "";
        return (TagText.CleanStem(entry.Path), PyText.Strip(entry.Artist), PyText.Strip(entry.Album), parent);
    }

    /// <summary><c>deezer_queries</c> : de la plus étayée à la plus spéculative (guillemets non échappés, comme l'original).</summary>
    public static IReadOnlyList<string> DeezerQueries(TagEntry entry)
    {
        var (stem, artist, album, parent) = Clues(entry);
        var queries = new List<string>();
        if (artist.Length > 0 && album.Length > 0) queries.Add($"artist:\"{artist}\" album:\"{album}\"");
        if (artist.Length > 0 && stem.Length > 0 && !SameWords(stem, artist)) queries.Add($"{artist} {stem}");
        else if (artist.Length > 0) queries.Add($"artist:\"{artist}\"");
        if (stem.Length > 0) queries.Add(stem);
        if (parent.Length > 0)
        {
            if (stem.Length > 0 && !SameWords(stem, parent)) queries.Add($"{parent} {stem}");
            else if (artist.Length > 0) queries.Add($"{parent} {artist}");
        }
        return TagText.Unique(queries);
    }

    /// <summary><c>musicbrainz_queries</c> : champs nommés d'abord, texte libre ensuite ; trois au plus.</summary>
    public static IReadOnlyList<string> MusicBrainzQueries(TagEntry entry)
    {
        var (stem, artist, album, parent) = Clues(entry);
        var L = TagText.Lucene;
        var queries = new List<string>();
        if (artist.Length > 0 && album.Length > 0) queries.Add($"artist:\"{L(artist)}\" AND release:\"{L(album)}\"");
        if (artist.Length > 0)
        {
            var title = TagText.WithoutArtist(stem, artist);
            queries.Add(title.Length > 0 ? $"recording:\"{L(title)}\" AND artist:\"{L(artist)}\"" : $"artist:\"{L(artist)}\"");
        }
        else if (stem.Length > 0)
        {
            // « Titre - Artiste » ou l'inverse : les deux lectures sont tentées.
            var cut = stem.IndexOf(" - ", StringComparison.Ordinal);
            var left = PyText.Strip(cut >= 0 ? stem[..cut] : stem);
            var right = PyText.Strip(cut >= 0 ? stem[(cut + 3)..] : "");
            if (right.Length > 0)
            {
                queries.Add($"recording:\"{L(left)}\" AND artist:\"{L(right)}\"");
                queries.Add($"recording:\"{L(right)}\" AND artist:\"{L(left)}\"");
            }
            else queries.Add($"recording:\"{L(stem)}\"");
        }
        if (stem.Length > 0) queries.Add(L(stem));
        if (parent.Length > 0 && stem.Length > 0 && !SameWords(stem, parent)) queries.Add($"recording:\"{L(stem)}\" AND artist:\"{L(parent)}\"");
        return TagText.Unique(queries).Take(MusicBrainzMaxQueries).ToList();
    }

    /// <summary><c>_duration_agreement</c> : 1 pour identiques, 0 à 30 s, -1 au-delà ; null si l'une est inconnue.</summary>
    public static double? DurationAgreement(int local, int remote)
    {
        if (local == 0 || remote == 0) return null;
        var delta = Math.Abs(local - remote);
        if (delta > DurationMax) return -1.0;
        if (delta <= DurationExact) return 1.0;
        return 1.0 - (double)(delta - DurationExact) / (DurationMax - DurationExact);
    }

    /// <summary><c>title_source</c> : nom du fichier, dossier, et titre s'il vient des tags (jamais l'album).</summary>
    public static string TitleSource(TagEntry entry)
    {
        var parts = new List<string> { TagText.CleanStem(entry.Path), PyPath.Basename(PyPath.Dirname(entry.Path)) };
        if (entry.TitleTagged) parts.Add(entry.Title);
        return string.Join(" ", parts);
    }

    /// <summary><c>evaluate</c> : (note, raison) d'une piste candidate ; note 0 = écartée.</summary>
    public static (double Score, string Reason) Evaluate(TagCandidate candidate, TagEntry entry, int duration, string source)
    {
        var agreement = DurationAgreement(duration, candidate.Seconds);
        var delta = duration != 0 && candidate.Seconds != 0 ? Math.Abs(duration - candidate.Seconds) : 0;

        var titleScore = TagText.Contained(candidate.Title, source);
        var core = PyText.Strip(QualifierRe().Replace(candidate.Title, " "));
        if (core.Length > 0 && core != candidate.Title)
            titleScore = Math.Max(titleScore, CoreTitleWeight * TagText.Contained(core, source));
        var artist = PyText.Strip(entry.Artist);
        var album = PyText.Strip(entry.Album);
        var artistScore = Math.Max(artist.Length > 0 ? TagText.Contained(candidate.Artist, artist) : 0.0, TagText.Contained(candidate.Artist, source));
        var albumScore = album.Length > 0 ? TagText.Contained(candidate.Album, album) : 0.0;

        var conclusive = titleScore >= CoreTitleWeight && artistScore >= 0.99;
        if (agreement is < 0 && !conclusive) return (0.0, "");

        var durationScore = agreement switch
        {
            null => 0.5,
            < 0 => 0.05 * DurationMax / Math.Max(delta, DurationMax),
            { } a => a,
        };
        var score = 0.45 * titleScore + 0.30 * artistScore + 0.25 * durationScore;

        if (titleScore < 0.5 && albumScore >= 0.8 && agreement is >= 1.0)
            return (Math.Max(score, DurationOnlyScore), "durée identique dans l'album déjà connu");
        if (titleScore < 0.5) return (0.0, "");
        if (artistScore < 0.5)
        {
            if (artist.Length > 0) return (0.0, "");
            var exact = SameWords(candidate.Title, TagText.CleanStem(entry.Path));
            if (!exact || agreement is null || agreement < 1.0) return (0.0, "");
            return (DurationOnlyScore, "titre exact et durée identique — artiste non vérifiable dans le nom du fichier");
        }

        string reason;
        if (agreement is < 0) reason = $"titre et artiste reconnus, mais durée très différente ({delta} s d'écart)";
        else if (agreement is >= 1.0) reason = "titre reconnu, durée identique";
        else if (conclusive) reason = "titre et artiste reconnus dans le nom du fichier";
        else if (agreement is null) reason = "titre reconnu, durée inconnue";
        else reason = $"titre reconnu, durée proche ({delta} s d'écart)";
        return (score, reason);
    }

    /// <summary><c>_rivals</c> : autres pistes (autre titre ou autre interprète) dont la durée approche autant.</summary>
    public static IReadOnlyList<TagCandidate> Rivals(TagCandidate best, IReadOnlyList<TagCandidate> seen, int duration) =>
        seen.Where(c => !ReferenceEquals(c, best) && c.Seconds != 0 && Math.Abs(c.Seconds - duration) <= DurationAmbiguous &&
                        (!SameWords(c.Title, best.Title) || !SameWords(c.Artist, best.Artist)))
            .ToList();

    /// <summary>Requête nommée (champs <c>artist:</c>, <c>release:</c>, <c>recording:</c>) : 25 résultats, sinon 8.</summary>
    public static int LimitFor(string query) =>
        query.Contains("artist:", StringComparison.Ordinal) || query.Contains("release:", StringComparison.Ordinal) ||
        query.Contains("recording:", StringComparison.Ordinal) ? 25 : 8;

    /// <summary><c>_best_from_text</c> : meilleure piste d'un service interrogé par requêtes.</summary>
    public static async Task<TagMatch?> BestFromTextAsync(TagEntry entry, int duration, string source, IReadOnlyList<string> queries,
        Func<string, int, CancellationToken, Task<IReadOnlyList<TagCandidate>>> search, CancellationToken ct)
    {
        TagCandidate? best = null;
        double bestScore = 0;
        string bestReason = "", bestQuery = "";
        var seen = new List<TagCandidate>();
        foreach (var query in queries)
        {
            foreach (var candidate in await search(query, LimitFor(query), ct))
            {
                seen.Add(candidate);
                var (score, reason) = Evaluate(candidate, entry, duration, source);
                if (score > bestScore) (best, bestScore, bestReason, bestQuery) = (candidate, score, reason, query);
            }
            if (bestScore >= FrankScore) break;
        }
        if (best is null) return null;
        // Identification par la seule durée : elle ne vaut que sans rivale aussi proche.
        if (bestReason.StartsWith("durée", StringComparison.Ordinal) && Rivals(best, seen, duration).Count > 0) return null;
        return new TagMatch(best, bestScore, bestReason, bestQuery);
    }

    /// <summary><c>_best_from_fingerprint</c> : premier candidat AcoustID titré dont la durée ne dément pas l'empreinte.</summary>
    public static TagMatch? BestFromFingerprint(IReadOnlyList<TagCandidate> candidates, int duration)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Title.Length == 0) continue;
            var agreement = DurationAgreement(duration, candidate.Seconds);
            if (agreement is < 0) continue;
            var match = candidate.Match;
            var reason = $"empreinte acoustique (concordance {PercentText(match)})";
            if (agreement is >= 1.0) reason += ", durée identique";
            var score = agreement is null or >= 1.0 ? match : 0.75 * match;
            return new TagMatch(candidate, score, reason, "empreinte");
        }
        return null;
    }

    /// <summary><c>f"{match:.0%}"</c>.</summary>
    public static string PercentText(double ratio) =>
        Math.Round(ratio * 100, MidpointRounding.ToEven).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Fin de <c>_propose</c> pour un service retenu : parution ressemblant à l'album connu,
    /// champs vides comblés, tags complémentaires (Deezer seul), tags contredits.
    /// Null si le service ne comble rien.
    /// </summary>
    public static TagFound? Assemble(TagEntry entry, TagMatch match, string service, IReadOnlyList<string> absent,
        IReadOnlyList<string> extras, IReadOnlyDictionary<string, string>? details, int duration, string note)
    {
        var best = match.Candidate;
        var album = PyText.Strip(entry.Album);
        if (album.Length > 0)
        {
            foreach (var title in best.Albums ?? [])
            {
                if (TagText.Contained(title, album) >= 0.8 || TagText.Contained(album, title) >= 0.8)
                {
                    best = best with { Album = title };
                    break;
                }
            }
        }

        var values = new Dictionary<string, string>();
        foreach (var field in absent)
        {
            var value = Field(best, field);
            if (value.Length > 0) values[field] = value;
        }
        if (absent.Count > 0 && values.Count == 0) return null;
        if (details is not null)
            foreach (var (field, value) in details)
                if (extras.Contains(field)) values[field] = value;
        if (values.Count == 0) return null;

        var replaces = new Dictionary<string, string>();
        foreach (var field in CoreFields)
        {
            var current = PyText.Strip(entry.Get(field));
            var found = Field(best, field);
            if (!values.ContainsKey(field) && current.Length > 0 && found.Length > 0 && !SameWords(current, found)) replaces[field] = found;
        }

        return new TagFound(best, values, replaces, Math.Round(match.Score, 2, MidpointRounding.ToEven), match.Score >= SureScore,
            match.Reason + note, match.Query, duration, service);
    }

    private static string Field(TagCandidate c, string field) => field switch
    {
        "title" => c.Title,
        "artist" => c.Artist,
        "album" => c.Album,
        _ => "",
    };
}

/// <summary>Meilleure piste d'un service : (piste, note, raison, requête).</summary>
public sealed record TagMatch(TagCandidate Candidate, double Score, string Reason, string Query);

/// <summary>Proposition de <c>find_tags</c> (§5.4) : champs comblés (<c>values</c>) et contredits (<c>replaces</c>).</summary>
public sealed record TagFound(
    TagCandidate Best,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Replaces,
    double Score,
    bool Sure,
    string Reason,
    string Query,
    int Duration,
    string Source);
