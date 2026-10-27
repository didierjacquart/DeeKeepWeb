using System.Text;
using System.Text.RegularExpressions;

namespace DeeKeep.Common.Text;

/// <summary>
/// Formes comparables des titres, artistes et chemins de l'index de présence
/// et des doublons — portage de <c>deekeep/library/library_index.py:95-212</c>
/// et de <c>db_init.canonical_path</c>/<c>match_key</c>.
/// </summary>
/// <remarks>
/// Les clés reproduisent l'original à l'identique, pertes comprises (« œ », « ø »,
/// alphabets non latins disparaissent de <see cref="Canonical"/>) : les mêmes
/// fichiers doivent se rapprocher que dans l'application desktop. Les textes sont
/// ramenés en NFC avant les regex : <c>_VARIANT_RE</c> contient des mots accentués
/// précomposés (« édition », « stéréo ») qu'un texte décomposé (NFD, fréquent dans
/// les noms de fichiers venus d'un Mac) ne reconnaîtrait pas.
/// </remarks>
public static partial class LibraryText
{
    /// <summary>TITLE_OVERLAP_MIN : part de mots communs ouvrant la comparaison fine.</summary>
    public const double TitleOverlapMin = 0.5;
    /// <summary>TITLE_RATIO_MIN : ressemblance des titres retenue comme proche.</summary>
    public const double TitleRatioMin = 0.85;
    /// <summary>ARTIST_OVERLAP_MIN : part de mots communs tenant deux artistes proches.</summary>
    public const double ArtistOverlapMin = 0.5;

    // Marqueurs explicites d'invité : ce qui suit n'appartient ni au titre ni à l'artiste principal.
    [GeneratedRegex(@"(?i)\s*\b(?:feat\.?|featuring|ft\.?|avec|with|vs\.?|versus)\s+", RegexOptions.CultureInvariant)]
    internal static partial Regex FeatRe();

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    internal static partial Regex BracketsRe();

    // Mots qui ne décrivent qu'une variante d'un même morceau.
    [GeneratedRegex(@"(?i)\b(?:remix(?:es|ed)?|remaster(?:ed|isation|ise|isé|isée)?|live" +
                    @"|version|edit|mix|instrumental|acoustic|acoustique|unplugged|extended" +
                    @"|radio|single|album|original|explicit|clean|cover|karaoke|karaoké|demo" +
                    @"|mono|stereo|stéréo|bonus|deluxe|edition|édition|maxi|club|dub" +
                    @"|bootleg|reprise|inédit|inedit|remake|rework|slowed)\b", RegexOptions.CultureInvariant)]
    internal static partial Regex VariantRe();

    [GeneratedRegex(@"[\(\[]\s*(?:copy|copie|\d+)\s*[\)\]]")]
    private static partial Regex CopyMarkRe();

    [GeneratedRegex(@"^\s*\d{1,3}\s*[-\._]\s*")]
    private static partial Regex LeadingNumberRe();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    [GeneratedRegex(@"\s+-\s+")]
    private static partial Regex DashRe();

    /// <summary>_ARTIST_NOISE : mots qui joignent ou décorent un nom d'artiste sans le distinguer.</summary>
    public static readonly IReadOnlySet<string> ArtistNoise = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "et", "the", "les", "los", "feat", "featuring", "ft", "with",
        "avec", "vs", "versus", "presents", "present", "presente", "dj",
    };

    /// <summary>
    /// <c>canonical</c> : mots en minuscules séparés par une espace, sans accent ni
    /// ponctuation. « &amp; » devient « and », « $ » devient « s » (« Ke$ha » = « Kesha »),
    /// les marqueurs de copie (« (1) », « [copy] ») et le numéro de piste de tête disparaissent.
    /// </summary>
    public static string Canonical(string? text)
    {
        // Ordre exact de l'original : NFKD puis casefold (l'inverse de TagText.Words).
        var t = PyText.CaseFold(PyText.Normalize(text ?? "", NormalizationForm.FormKD));
        t = PyText.RemoveCombining(t);
        t = t.Replace("&", " and ").Replace("$", "s");
        t = CopyMarkRe().Replace(t, " ");
        t = LeadingNumberRe().Replace(t, " ");
        t = NonWordRe().Replace(t, " ");
        return PyText.Strip(SpacesRe().Replace(t, " "));
    }

    /// <summary>
    /// <c>title_key</c> : clé d'un titre, mentions d'invité écartées
    /// (« Titanium (feat. Sia) » = « Titanium »). Effet de bord conservé :
    /// « with » est un marqueur d'invité, « I Wanna Dance With Somebody » → « i wanna dance ».
    /// </summary>
    public static string TitleKey(string? title)
    {
        var cleaned = BracketsRe().Replace(Nfc(title), m => FeatRe().IsMatch(m.Value) ? " " : m.Value);
        var head = BeforeFirst(FeatRe(), cleaned);
        return Canonical(head.Length > 0 ? head : cleaned);
    }

    /// <summary>
    /// <c>base_key</c> : titre débarrassé de sa version (remix, live, edit…),
    /// entre parenthèses ou en suffixe après un tiret. Ne coupe PAS sur un « feat. »
    /// hors parenthèses (asymétrie de l'original avec <see cref="TitleKey"/>).
    /// </summary>
    public static string BaseKey(string? title)
    {
        var text = BracketsRe().Replace(Nfc(title), m => VariantRe().IsMatch(m.Value) || FeatRe().IsMatch(m.Value) ? " " : m.Value);
        var parts = DashRe().Split(text).ToList();
        while (parts.Count > 1 && VariantRe().IsMatch(parts[^1])) parts.RemoveAt(parts.Count - 1);
        return Canonical(string.Join(" - ", parts));
    }

    /// <summary><c>artist_tokens</c> : mots significatifs d'un nom d'artiste (ordre et invités indifférents).</summary>
    public static IReadOnlySet<string> ArtistTokens(string? artist) =>
        new HashSet<string>(Canonical(Nfc(artist)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !ArtistNoise.Contains(w)),
            StringComparer.Ordinal);

    /// <summary><c>artist_key</c> : mots significatifs triés (ordre ordinal, comme <c>sorted</c>).</summary>
    public static string ArtistKey(string? artist) => string.Join(" ", ArtistTokens(artist).Order(StringComparer.Ordinal));

    /// <summary><c>main_artist_key</c> : clé de ce qui précède le premier « feat. » (« Flo Rida Feat. Ke$ha » = « Flo Rida »).</summary>
    public static string MainArtistKey(string? artist) => ArtistKey(BeforeFirst(FeatRe(), Nfc(artist)));

    /// <summary>Clé du nom de fichier d'un chemin (<c>stem_key</c> : canonical du nom sans extension).</summary>
    public static string StemKey(string? path) => Canonical(PyPath.Stem(path));

    /// <summary>
    /// <c>canonical_path</c> : chemin normalisé à la Windows (« \ », « . » et « .. » résolus),
    /// casse conservée — la clé primaire des tables <c>tracks</c>, <c>quality</c>, <c>archive</c>.
    /// Un chemin vide donne « . », comme l'original.
    /// </summary>
    public static string CanonicalPath(string? path) => PyPath.NormPath((path ?? "").Replace('/', '\\'));

    /// <summary><c>match_key</c> : <see cref="CanonicalPath"/> en minuscules (rapprochement insensible à la casse).</summary>
    public static string MatchKey(string? path) => PyPath.NormCase(CanonicalPath(path));

    /// <summary><c>_overlap</c> : part des mots communs, rapportée au plus petit ensemble (0 si l'un est vide).</summary>
    public static double Overlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0.0;
        return (double)left.Count(right.Contains) / Math.Min(left.Count, right.Count);
    }

    /// <summary>
    /// <c>_ratio</c> : <c>difflib.SequenceMatcher(None, a, b).ratio()</c>, algorithme de
    /// Ratcliff/Obershelp porté à l'identique (y compris l'heuristique « autojunk »
    /// qui écarte les caractères trop fréquents quand <paramref name="b"/> dépasse 200 caractères).
    /// </summary>
    public static double Ratio(string? a, string? b)
    {
        var sa = CodePoints(a ?? "");
        var sb = CodePoints(b ?? "");
        var length = sa.Length + sb.Length;
        return length == 0 ? 1.0 : 2.0 * SequenceMatcher.MatchingCharacters(sa, sb) / length;
    }

    private static string Nfc(string? text) => PyText.Normalize(text ?? "", NormalizationForm.FormC);

    // re.split(pattern, text, 1)[0]
    private static string BeforeFirst(Regex regex, string text)
    {
        var m = regex.Match(text);
        return m.Success ? text[..m.Index] : text;
    }

    private static int[] CodePoints(string text) => text.EnumerateRunes().Select(r => r.Value).ToArray();

    /// <summary>Portage de difflib.SequenceMatcher (sans fonction de rebut) réduit à ce qu'exige ratio().</summary>
    private static class SequenceMatcher
    {
        public static int MatchingCharacters(int[] a, int[] b)
        {
            // __chain_b : positions de chaque élément de b, éléments « populaires » purgés.
            var b2j = new Dictionary<int, List<int>>();
            for (var i = 0; i < b.Length; i++)
            {
                if (!b2j.TryGetValue(b[i], out var list)) b2j[b[i]] = list = [];
                list.Add(i);
            }
            if (b.Length >= 200)
            {
                var ntest = b.Length / 100 + 1;
                foreach (var popular in b2j.Where(kv => kv.Value.Count > ntest).Select(kv => kv.Key).ToList())
                    b2j.Remove(popular);
            }

            // get_matching_blocks : seule la somme des tailles compte pour ratio().
            var total = 0;
            var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
            queue.Push((0, a.Length, 0, b.Length));
            while (queue.Count > 0)
            {
                var (alo, ahi, blo, bhi) = queue.Pop();
                var (i, j, k) = FindLongestMatch(a, b, b2j, alo, ahi, blo, bhi);
                if (k == 0) continue;
                total += k;
                if (alo < i && blo < j) queue.Push((alo, i, blo, j));
                if (i + k < ahi && j + k < bhi) queue.Push((i + k, ahi, j + k, bhi));
            }
            return total;
        }

        private static (int I, int J, int K) FindLongestMatch(int[] a, int[] b, Dictionary<int, List<int>> b2j,
            int alo, int ahi, int blo, int bhi)
        {
            int besti = alo, bestj = blo, bestsize = 0;
            var j2len = new Dictionary<int, int>();
            for (var i = alo; i < ahi; i++)
            {
                var newj2len = new Dictionary<int, int>();
                if (b2j.TryGetValue(a[i], out var indices))
                {
                    foreach (var j in indices)
                    {
                        if (j < blo) continue;
                        if (j >= bhi) break;
                        var k = newj2len[j] = j2len.GetValueOrDefault(j - 1) + 1;
                        if (k > bestsize) (besti, bestj, bestsize) = (i - k + 1, j - k + 1, k);
                    }
                }
                j2len = newj2len;
            }
            // Sans fonction de rebut, l'ensemble « bjunk » est vide : seules les deux
            // premières extensions de l'original s'appliquent (elles recollent les
            // éléments populaires exclus de b2j).
            while (besti > alo && bestj > blo && a[besti - 1] == b[bestj - 1])
                (besti, bestj, bestsize) = (besti - 1, bestj - 1, bestsize + 1);
            while (besti + bestsize < ahi && bestj + bestsize < bhi && a[besti + bestsize] == b[bestj + bestsize])
                bestsize++;
            return (besti, bestj, bestsize);
        }
    }
}
