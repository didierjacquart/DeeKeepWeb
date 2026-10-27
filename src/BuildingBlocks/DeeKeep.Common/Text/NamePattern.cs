using System.Text.RegularExpressions;

namespace DeeKeep.Common.Text;

/// <summary>Une balise du menu « Insérer une balise ».</summary>
public sealed record NameTagItem(string Label, string Token);

/// <summary>Une catégorie du menu « Insérer une balise ».</summary>
public sealed record NameTagCategory(string Name, IReadOnlyList<NameTagItem> Items);

/// <summary>
/// Motifs de nommage des fichiers audio (« %piste_00% - %titre% » → « 03 - Africa »),
/// à la manière de Mp3tag — portage complet de <c>deekeep/library/name_pattern.py</c>.
/// </summary>
/// <remarks>
/// Les valeurs sont lues par clé dans le vocabulaire des tags (<c>title</c>, <c>artist</c>,
/// <c>albumartist</c>, <c>track</c>, <c>tracktotal</c>, <c>disc</c>, <c>disctotal</c>, <c>date</c>,
/// <c>genre</c>, <c>composer</c>, <c>publisher</c>, <c>comment</c>, <c>compilation</c>, <c>path</c>,
/// <c>format</c>, <c>bitrate</c>, <c>title_tagged</c>) via une fonction de lecture : un
/// dictionnaire, ou <c>AudioTags.Get</c>. Faute de types Python, une valeur booléenne ou
/// numérique est lue comme texte : « », « 0 » et « false » valent faux.
/// Le résultat de <see cref="FormatName(string?, Func{string, string?})"/> n'est pas un nom
/// de fichier sûr : l'appelant le passe par <see cref="FileNames.SafeComponent"/>.
/// </remarks>
public static partial class NamePattern
{
    /// <summary>Convention historique de DeeKeep, partagée avec le téléchargement.</summary>
    public const string DefaultPattern = "%titre% - %artiste%";

    [GeneratedRegex(@"%[a-z0-9_]+%")]
    private static partial Regex TokenRe();

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex YearRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    [GeneratedRegex(@"\(\s*\)|\[\s*\]|\{\s*\}")]
    private static partial Regex EmptyBracketsRe();

    [GeneratedRegex(@"\s*([-–_,])\s*\1*\s*(?=[-–_,])")]
    private static partial Regex RepeatedSeparatorRe();

    // Balise -> valeur calculée. Les balises numériques sont nommées d'après ce
    // qu'elles produisent : « %piste_00% » rend « 07 », « %total_pistes% » le nombre
    // de pistes de l'album (« %piste2% » laissait croire à la piste numéro 2).
    private static readonly (string Token, Func<Func<string, string?>, string> Value)[] TokenTable =
    [
        ("%titre%", Tag("title")),
        ("%artiste%", Tag("artist")),
        ("%artiste_album%", Tag("albumartist")),
        ("%artiste_album_ou_artiste%", e => Or(Tag("albumartist")(e), Tag("artist")(e))),
        ("%album%", Tag("album")),
        ("%piste%", Tag("track")),
        ("%piste_00%", e => Pad(e("track"), 2)),
        ("%total_pistes%", Tag("tracktotal")),
        ("%disque%", Tag("disc")),
        ("%disque_00%", e => Pad(e("disc"), 2)),
        ("%total_disques%", Tag("disctotal")),
        ("%date%", Tag("date")),
        ("%annee%", Year),
        ("%decennie%", Decade),
        ("%genre%", Tag("genre")),
        ("%compositeur%", Tag("composer")),
        ("%editeur%", Tag("publisher")),
        ("%commentaire%", Tag("comment")),
        ("%compilation%", Tag("compilation")),
        ("%nom_fichier%", e => PyPath.Stem(e("path") ?? "")),
        ("%dossier_parent%", e => PyPath.Basename(PyPath.Dirname(e("path") ?? ""))),
        ("%format%", Tag("format")),
        // str(bitrate) seulement si non nul : un débit inconnu (0) ne s'écrit pas.
        ("%debit%", e => IsTruthy(e("bitrate")) ? e("bitrate")! : ""),
    ];

    private static readonly Dictionary<string, Func<Func<string, string?>, string>> TokenMap =
        TokenTable.ToDictionary(t => t.Token, t => t.Value, StringComparer.Ordinal);

    /// <summary>Les 23 balises reconnues, dans l'ordre de l'original.</summary>
    public static readonly IReadOnlyList<string> Tokens = TokenTable.Select(t => t.Token).ToArray();

    /// <summary>
    /// Anciens noms de balises, toujours interprétés au formatage et réécrits par
    /// <see cref="Modernize"/> (dans cet ordre).
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Aliases =
    [
        new("%piste2%", "%piste_00%"),
        new("%disque2%", "%disque_00%"),
        new("%nb_pistes%", "%total_pistes%"),
        new("%nb_disques%", "%total_disques%"),
    ];

    private static readonly Dictionary<string, string> AliasMap = Aliases.ToDictionary(StringComparer.Ordinal);

    /// <summary>Menu « Insérer une balise » : libellés verbatim (chaque balise numérique montre la valeur qu'elle produit).</summary>
    public static readonly IReadOnlyList<NameTagCategory> TagCategories =
    [
        new("Tags de base",
        [
            new("Titre", "%titre%"),
            new("Artiste", "%artiste%"),
            new("Artiste de l'album", "%artiste_album%"),
            new("Artiste de l'album ou artiste", "%artiste_album_ou_artiste%"),
            new("Album", "%album%"),
            new("Genre", "%genre%"),
        ]),
        new("Numéros de piste et de disque",
        [
            new("Numéro de la piste   →  7", "%piste%"),
            new("Numéro de la piste sur 2 chiffres   →  07", "%piste_00%"),
            new("Nombre total de pistes de l'album   →  12", "%total_pistes%"),
            new("Numéro du disque   →  1", "%disque%"),
            new("Numéro du disque sur 2 chiffres   →  01", "%disque_00%"),
            new("Nombre total de disques   →  2", "%total_disques%"),
        ]),
        new("Date de sortie",
        [
            new("Date complète   →  1982-04-08", "%date%"),
            new("Année seule   →  1982", "%annee%"),
            new("Décennie   →  1980", "%decennie%"),
        ]),
        new("Tags supplémentaires",
        [
            new("Compositeur", "%compositeur%"),
            new("Éditeur / label", "%editeur%"),
            new("Commentaire", "%commentaire%"),
            new("Partie d'une compilation", "%compilation%"),
        ]),
        new("Fichier",
        [
            new("Nom de fichier actuel", "%nom_fichier%"),
            new("Dossier parent", "%dossier_parent%"),
            new("Format   →  MP3", "%format%"),
            new("Débit   →  320", "%debit%"),
        ]),
    ];

    /// <summary>Motifs proposés dans la liste déroulante de l'écran Organiser.</summary>
    public static readonly IReadOnlyList<string> Presets =
    [
        "%titre% - %artiste%",
        "%artiste% - %titre%",
        "%artiste% - %album% - %titre%",
        "%album% - %titre%",
        "%artiste% - %titre% (%annee%)",
        "%artiste% - %album% (%annee%) - %titre%",
        "%artiste_album_ou_artiste% - %titre%",
        "%titre% (%artiste%)",
        "%titre%",
        "%nom_fichier%",
    ];

    // Balises dont la valeur, faute de tag, serait reprise du nom de fichier actuel :
    // (balise, clé attestant du tag, nom du tag pour l'utilisateur).
    private static readonly (string Token, string Flag, string Label)[] FilenameBacked =
    [
        ("%titre%", "title_tagged", "titre"),
    ];

    /// <summary>
    /// <c>modernize</c> : réécrit les anciens noms de balises d'un motif enregistré
    /// (cosmétique : les alias restent interprétés).
    /// </summary>
    public static string Modernize(string? pattern)
    {
        var result = pattern ?? "";
        foreach (var (old, current) in Aliases) result = result.Replace(old, current, StringComparison.Ordinal);
        return result;
    }

    /// <summary><c>unknown_tokens</c> : balises du motif qui ne correspondent à rien (pour prévenir l'utilisateur).</summary>
    public static IReadOnlyList<string> UnknownTokens(string? pattern) =>
        TokenRe().Matches(pattern ?? "").Select(m => m.Value)
            .Where(t => !TokenMap.ContainsKey(t) && !AliasMap.ContainsKey(t)).ToList();

    /// <summary>
    /// <c>missing_required_tags</c> : tags que le motif réclame et que seul le nom de
    /// fichier actuel comblerait — « Joan Baez.mp3 » deviendrait « Joan Baez - Joan Baez.mp3 »
    /// à chaque rangement. Mieux vaut alors ne pas renommer et le signaler.
    /// </summary>
    public static IReadOnlyList<string> MissingRequiredTags(string? pattern, Func<string, string?> entry)
    {
        var tokens = TokenRe().Matches(pattern ?? "").Select(m => AliasMap.GetValueOrDefault(m.Value, m.Value)).ToHashSet(StringComparer.Ordinal);
        return FilenameBacked.Where(f => tokens.Contains(f.Token) && !IsTruthy(entry(f.Flag))).Select(f => f.Label).ToList();
    }

    /// <inheritdoc cref="MissingRequiredTags(string?, Func{string, string?})"/>
    public static IReadOnlyList<string> MissingRequiredTags(string? pattern, IReadOnlyDictionary<string, string?> entry) =>
        MissingRequiredTags(pattern, Reader(entry));

    /// <summary>
    /// <c>format_name</c> : nom (sans extension) produit par le motif. Une balise inconnue
    /// est recopiée telle quelle ; les séparateurs laissés orphelins par une balise vide
    /// sont recollés (<see cref="Tidy"/>). Chaîne vide si rien d'exploitable.
    /// </summary>
    public static string FormatName(string? pattern, Func<string, string?> entry) =>
        Tidy(TokenRe().Replace(pattern ?? "", m =>
            TokenMap.TryGetValue(AliasMap.GetValueOrDefault(m.Value, m.Value), out var value) ? value(entry) : m.Value));

    /// <inheritdoc cref="FormatName(string?, Func{string, string?})"/>
    public static string FormatName(string? pattern, IReadOnlyDictionary<string, string?> entry) => FormatName(pattern, Reader(entry));

    /// <summary>
    /// <c>_tidy</c> : supprime les parenthèses vides, réduit les séparateurs consécutifs
    /// (« Toto -  - Africa » → « Toto - Africa ») puis retire séparateurs et points
    /// de début et de fin (« R.E.M. » → « R.E.M »).
    /// </summary>
    public static string Tidy(string name)
    {
        name = SpacesRe().Replace(name, " ");
        name = EmptyBracketsRe().Replace(name, "");
        string? previous = null;
        while (previous != name)
        {
            previous = name;
            name = RepeatedSeparatorRe().Replace(name, " ");
        }
        name = SpacesRe().Replace(name, " ");
        return PyText.Strip(name, " -–_,.");
    }

    // ------------------------------------------------------------------ valeurs

    private static Func<string, string?> Reader(IReadOnlyDictionary<string, string?> entry) =>
        key => entry.TryGetValue(key, out var value) ? value : null;

    private static Func<Func<string, string?>, string> Tag(string key) => e => PyText.Strip(e(key) ?? "");

    private static string Or(string first, string second) => first.Length > 0 ? first : second;

    /// <summary>« 3 » → « 03 ». Laisse la valeur telle quelle si ce n'est pas un nombre.</summary>
    private static string Pad(string? value, int width)
    {
        var text = PyText.Strip(value ?? "");
        if (text.Length == 0) return "";
        return PyText.IsDigit(text) ? text.PadLeft(width, '0') : text;
    }

    /// <summary>Premier groupe de 4 chiffres de la date.</summary>
    private static string Year(Func<string, string?> e)
    {
        var m = YearRe().Match(e("date") ?? "");
        return m.Success ? m.Value : "";
    }

    /// <summary>Décennie sur 4 chiffres (1994 → 1990).</summary>
    private static string Decade(Func<string, string?> e)
    {
        var year = Year(e);
        return year.Length > 0 ? year[..3] + "0" : "";
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrEmpty(value) && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
