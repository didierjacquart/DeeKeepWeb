using System.Text;
using System.Text.RegularExpressions;

namespace DeeKeep.Common.Text;

/// <summary>Découpage en mots des fonctions de rapprochement de <see cref="TagText"/>.</summary>
public enum WordMode
{
    /// <summary>
    /// Version corrigée (défaut) : lettres, marques et chiffres de tout alphabet.
    /// « Мой путь » donne deux mots au lieu de zéro — sans quoi aucun titre
    /// cyrillique, grec ou japonais ne peut jamais être reconnu (spec 01 §3.3).
    /// Identique à <see cref="Python"/> pour un texte en alphabet latin de base ;
    /// « œ », « ø », « æ » y restent des lettres au lieu de couper le mot.
    /// </summary>
    Unicode,

    /// <summary>Comportement exact de l'original : seuls <c>[a-z0-9]</c> forment des mots.</summary>
    Python,
}

/// <summary>
/// Normalisation des titres et artistes pour la recherche en ligne et la
/// sélection des vidéos — portage de <c>deekeep/audio/tag_lookup.py:109-210</c>
/// (et des petites fonctions de requête voisines).
/// </summary>
public static partial class TagText
{
    /// <summary>MIN_SCORE : en dessous, aucune proposition n'est faite.</summary>
    public const double MinScore = 0.55;
    /// <summary>SURE_SCORE : au-dessus, la proposition est cochée d'office.</summary>
    public const double SureScore = 0.80;
    /// <summary>CORE_TITLE_WEIGHT : note maximale d'un titre reconnu à sa seule partie nue.</summary>
    public const double CoreTitleWeight = 0.9;

    // Bruit courant des noms de fichiers glanés en ligne : mentions de clip, et
    // marquage du débit qu'ajoutent certains téléchargeurs (« - 128 », « 320kbps »).
    [GeneratedRegex(@"(?i)\((?:official[^)]*|lyrics?[^)]*|audio|video|clip|hd|hq|full)\)" +
                    @"|\[[^\]]*\]" +
                    @"|\b(?:official (?:video|audio|lyric video)|lyrics?)\b" +
                    @"|[\s\-_]+\d{2,3}\s*k?b?(?:ps)?\s*$", RegexOptions.CultureInvariant)]
    internal static partial Regex NoiseRe();

    [GeneratedRegex(@"^\s*\d{1,3}\s*[.)\-_]\s*|^\s*\d{2}\s+", RegexOptions.CultureInvariant)]
    internal static partial Regex LeadingTrackRe();

    // Mention de version accolée à un titre de catalogue (« (Radio Edit) »).
    [GeneratedRegex(@"\s*[(\[][^)\]]*[)\]]\s*", RegexOptions.CultureInvariant)]
    internal static partial Regex QualifierRe();

    // Même mention sans parenthèses (« ... Full Edit ») en fin de titre.
    [GeneratedRegex(@"(?i)[\s\-–_]+(?:full|radio|extended|club|maxi|original|album|single" +
                    @"|vf|vo)?\s*(?:edit|mix|version|remix|remaster(?:ed)?)\s*\d*$", RegexOptions.CultureInvariant)]
    internal static partial Regex VersionTailRe();

    // Marqueur d'invité dans un nom d'artiste : ce qui suit n'appartient pas à l'artiste principal.
    [GeneratedRegex(@"(?i)\s*[(\[]?\b(?:feat\.?|featuring|ft\.?|avec|with|vs\.?|versus)\b.*$", RegexOptions.CultureInvariant)]
    internal static partial Regex GuestRe();

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex AsciiWordRe();

    // Lettres, marques (signes vocaliques indiens restés après le retrait des
    // diacritiques) et chiffres de tout alphabet.
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnicodeWordRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    [GeneratedRegex(@"([+\-&|!(){}\[\]^""~*?:\\/])")]
    private static partial Regex LuceneRe();

    /// <summary>
    /// _STOPWORDS : mots trop communs pour prouver quoi que ce soit (« La brioche »
    /// et « La Rasbaïa » partagent « la » sans avoir de rapport).
    /// </summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "la", "le", "les", "l", "un", "une", "des", "de", "du", "d", "et", "ou",
        "au", "aux", "en", "the", "an", "of", "to", "in", "on", "and", "or",
    };

    /// <summary>GENERIC_FOLDERS : dossiers dont le nom ne renseigne sur rien.</summary>
    public static readonly IReadOnlySet<string> GenericFolders = new HashSet<string>(StringComparer.Ordinal)
    {
        "divers", "various", "va", "musique", "music", "mp3", "cd", "cd1", "cd2",
        "disc", "disc1", "disc2", "disk", "disk1", "disk2", "itunes", "downloads",
        "telechargements", "compil", "compils", "compilation", "compilations",
        "electro", "dance", "pop", "rock", "pop rock", "pop dance", "ambiance",
        "disco", "funk", "rap", "jazz", "classique", "varietes", "no title",
    };

    /// <summary>
    /// <c>words</c> : mots d'un texte, en minuscules, sans accent ni ponctuation
    /// (casefold, NFKD, retrait des marques de combinaison, puis découpage).
    /// </summary>
    public static IReadOnlyList<string> Words(string? text, WordMode mode = WordMode.Unicode)
    {
        var folded = PyText.RemoveCombining(PyText.Normalize(PyText.CaseFold(text ?? ""), NormalizationForm.FormKD));
        var re = mode == WordMode.Python ? AsciiWordRe() : UnicodeWordRe();
        var result = new List<string>();
        foreach (Match m in re.Matches(folded)) result.Add(m.Value);
        return result;
    }

    /// <summary><c>_significant</c> : mots porteurs de sens ; tous si le texte n'en contient aucun (« The The »).</summary>
    public static IReadOnlyList<string> Significant(IReadOnlyList<string> words)
    {
        var kept = words.Where(w => !StopWords.Contains(w)).ToList();
        return kept.Count > 0 ? kept : words;
    }

    /// <summary>
    /// <c>contained</c> : part des mots significatifs de <paramref name="needle"/>
    /// retrouvés dans <paramref name="haystack"/>. Volontairement asymétrique :
    /// « Losing My Religion » est pleinement contenu dans « Losing My Religion - R.E.M. ».
    /// </summary>
    public static double Contained(string? needle, string? haystack, WordMode mode = WordMode.Unicode)
    {
        var target = Significant(Words(needle, mode));
        if (target.Count == 0) return 0.0;
        var source = new HashSet<string>(Words(haystack, mode), StringComparer.Ordinal);
        return (double)target.Count(source.Contains) / target.Count;
    }

    /// <summary>
    /// <c>clean_stem</c>, fidèle : nom de FICHIER ramené à une requête — sans
    /// dossier ni extension, sans numéro de piste de tête ni mention parasite.
    /// Ne pas l'appliquer à un titre : « Mr. Brightside » y devient « Mr »
    /// (l'original le faisait ; voir <see cref="CleanTitle"/>).
    /// </summary>
    public static string CleanStem(string? name) => CleanCore(PyPath.Stem(name ?? ""));

    /// <summary>
    /// Variante corrigée de <c>clean_stem</c> pour un TITRE : même nettoyage, sans
    /// passer par basename/splitext (« Mr. Brightside », « AC/DC - … » restent entiers).
    /// </summary>
    public static string CleanTitle(string? title) => CleanCore(title ?? "");

    private static string CleanCore(string stem)
    {
        stem = LeadingTrackRe().Replace(stem, "");
        stem = NoiseRe().Replace(stem, " ");
        return PyText.Strip(SpacesRe().Replace(stem, " "), " -_.");
    }

    /// <summary>
    /// <c>core_title</c> : titre ramené au morceau, sans tampon de site ni mention
    /// de version. « White Knuckle Ride Full Edit [Www Keepvid Com] » → « White Knuckle Ride ».
    /// </summary>
    public static string CoreTitle(string? title)
    {
        var original = title ?? "";
        var text = NoiseRe().Replace(original, " ");
        text = QualifierRe().Replace(text, " ");
        text = VersionTailRe().Replace(text, "");
        var result = PyText.Strip(SpacesRe().Replace(text, " "), " -_.");
        return result.Length > 0 ? result : PyText.Strip(original);
    }

    /// <summary>
    /// <c>main_artist</c> : ce qui précède le premier « feat. » — « Mad Ice Feat. Irina » → « Mad Ice ».
    /// </summary>
    public static string MainArtist(string? artist)
    {
        var original = artist ?? "";
        var text = PyText.Strip(GuestRe().Replace(original, ""), " -_,&");
        text = SpacesRe().Replace(text, " ");
        return text.Length > 0 ? text : PyText.Strip(original);
    }

    /// <summary>
    /// <c>_without_artist</c> : nom de fichier privé des mots du nom d'artiste
    /// (MusicBrainz veut le titre seul dans son champ <c>recording</c>).
    /// </summary>
    public static string WithoutArtist(string stem, string? artist, WordMode mode = WordMode.Unicode)
    {
        if (string.IsNullOrEmpty(artist)) return stem;
        var drop = new HashSet<string>(Words(artist, mode), StringComparer.Ordinal);
        var kept = SpacesRe().Split(stem).Where(part => part.Length > 0 && !Words(part, mode).All(drop.Contains));
        return PyText.Strip(string.Join(" ", kept), " -_.");
    }

    /// <summary><c>_lucene</c> : valeur échappée pour une requête MusicBrainz.</summary>
    public static string Lucene(string? value) => LuceneRe().Replace(value ?? "", @"\$1");

    /// <summary><c>_unique</c> : requêtes sans doublon (casse ignorée), vides écartées, ordre conservé.</summary>
    public static IReadOnlyList<string> Unique(IEnumerable<string> queries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var query in queries)
            if (query.Length > 0 && seen.Add(PyText.CaseFold(query))) kept.Add(query);
        return kept;
    }

    /// <summary>Vrai si un nom de dossier ne renseigne sur rien (« Divers », « CD1 », « Pop Rock »).</summary>
    public static bool IsGenericFolder(string? folder, WordMode mode = WordMode.Unicode) =>
        GenericFolders.Contains(string.Join(" ", Words(folder, mode)));

    /// <summary><c>_year</c> : année d'une date Deezer (« 1995-11-07 » → « 1995 »), vide sinon.</summary>
    public static string Year(string? value)
    {
        var text = PyText.Strip(value ?? "");
        return text.Length >= 4 && PyText.IsDigit(text[..4]) ? text[..4] : "";
    }
}
