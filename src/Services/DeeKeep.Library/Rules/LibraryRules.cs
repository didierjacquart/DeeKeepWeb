using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DeeKeep.Common.Text;

namespace DeeKeep.Library.Rules;

/// <summary>
/// Réglages de rangement enregistrés par l'écran « Organiser » (<c>rules_from_settings</c>) :
/// ils décident aussi de la destination des fichiers extraits.
/// </summary>
public sealed record OrganizeRules(
    bool Artist = true,
    bool Album = true,
    bool Rename = false,
    string Pattern = NamePattern.DefaultPattern,
    bool Divers = false,
    int DiversMin = 3);

/// <summary>
/// Règles de rangement de la bibliothèque, partagées par l'extraction (destination
/// d'un fichier produit) et par « Organiser » (plan de rangement) — portage de
/// <c>deekeep/library/library_rules.py</c>. Les chemins manipulés ici sont des
/// chemins LOCAUX du serveur (voir <c>PathMap</c>).
/// </summary>
public static partial class LibraryRules
{
    /// <summary>Nom du dossier fourre-tout des artistes peu représentés.</summary>
    public const string DiversFolder = "Divers";

    // Marqueurs explicites d'invité : ce qui suit n'appartient pas à l'artiste principal.
    [GeneratedRegex(@"(?i)\s+(?:feat\.?|featuring|ft\.?|vs\.?|versus|with|avec|présente|presente|presents?)\s+")]
    private static partial Regex FeatRe();

    // Séparateurs ambigus : coupés seulement si ce qui précède est un artiste connu.
    [GeneratedRegex(@"(?i)\s*(?:&|\+|,|/|\bx\b|\band\b|\bet\b)\s*")]
    private static partial Regex JoinRe();

    [GeneratedRegex(@"\s+-\s+")]
    private static partial Regex StemSplitRe();

    /// <summary><c>safe_component</c> : nom de dossier ou de fichier utilisable partout.</summary>
    public static string SafeComponent(string? name) => FileNames.SafeComponent(name);

    /// <summary><c>artist_key</c> : clé de regroupement d'un artiste (son nom de dossier, casse ignorée).</summary>
    public static string ArtistKey(string? name) => PyText.CaseFold(SafeComponent(name ?? ""));

    /// <summary>
    /// <c>main_artist</c> : artiste principal d'un nom de tag. Les marqueurs d'invité sont
    /// toujours coupés ; les séparateurs ambigus (« &amp; », « , ») seulement si ce qui
    /// précède est déjà connu seul dans <paramref name="known"/> (« Calvin Harris &amp; Sam Smith »
    /// rejoint « Calvin Harris », « Earth, Wind &amp; Fire » reste entier).
    /// </summary>
    public static string MainArtist(string? name, IReadOnlySet<string> known)
    {
        var text = name ?? "";
        var first = FeatRe().Split(text, 2)[0];
        var stripped = PyText.Strip(first);
        var baseName = stripped.Length > 0 ? stripped : PyText.Strip(text);
        var baseKey = ArtistKey(baseName);
        foreach (Match match in JoinRe().Matches(baseName))
        {
            var prefix = PyText.Strip(baseName[..match.Index]);
            var key = ArtistKey(prefix);
            if (key.Length > 0 && known.Contains(key) && key != baseKey) return prefix;
        }
        return baseName;
    }

    /// <summary><c>stem_artist</c> : artiste lu dans un nom « Titre - Artiste » (vide si le nom ne suit pas la convention).</summary>
    public static string StemArtist(string? filename)
    {
        var stem = PyPath.Stem(filename ?? "");
        var parts = StemSplitRe().Split(stem);
        return parts.Length > 1 ? PyText.Strip(parts[^1]) : "";
    }

    /// <summary><c>rules_from_settings</c> : les clés <c>org_*</c> sont la seule source.</summary>
    public static OrganizeRules FromSettings(IReadOnlyDictionary<string, JsonNode?>? settings)
    {
        settings ??= new Dictionary<string, JsonNode?>();
        var pattern = settings.TryGetValue("org_pattern", out var p) && p is JsonValue pv && pv.TryGetValue<string>(out var ps) && ps.Length > 0
            ? ps
            : NamePattern.DefaultPattern;
        return new OrganizeRules(
            Artist: Truthy(settings, "org_artist", true),
            Album: Truthy(settings, "org_album", true),
            Rename: Truthy(settings, "org_rename", false),
            Pattern: pattern,
            Divers: Truthy(settings, "org_divers", false),
            DiversMin: Math.Max(1, IntOr(settings, "org_divers_min", 3)));
    }

    /// <summary><c>download_pattern</c> : sans renommage demandé, un fichier extrait suit la convention « Titre - Artiste ».</summary>
    public static string DownloadPattern(OrganizeRules? rules)
    {
        if (rules is null || !rules.Rename) return NamePattern.DefaultPattern;
        return string.IsNullOrEmpty(rules.Pattern) ? NamePattern.DefaultPattern : rules.Pattern;
    }

    /// <summary>
    /// <c>target_dir</c> : dossier visé sous <paramref name="root"/>. Sans artiste connu, le titre
    /// reste à la racine ; sous le seuil « Divers », il va dans « Divers » (plat).
    /// </summary>
    public static string TargetDir(string root, IReadOnlyDictionary<string, string?> tags, OrganizeRules? rules, string? exclude = null, Tally? tally = null)
    {
        if (rules is null || !rules.Artist) return root;
        var name = PyText.Strip(tags.GetValueOrDefault("artist") ?? "");
        if (name.Length == 0) return root;

        tally ??= new Tally(root);
        var artist = tally.Folder(name);
        if (artist.Length == 0) return root;

        if (rules.Divers && tally.Count(name, exclude) < rules.DiversMin)
            return Path.Join(root, DiversFolder);

        var path = Path.Join(root, artist);
        var album = SafeComponent(PyText.Strip(tags.GetValueOrDefault("album") ?? ""));
        if (rules.Album && album.Length > 0) path = Path.Join(path, album);
        return path;
    }

    /// <summary>
    /// <c>target_stem</c> : nom de fichier visé (sans extension). Un motif qui ne rend rien
    /// retombe sur « Titre - Artiste », le titre, l'artiste, puis « piste ».
    /// </summary>
    public static string TargetStem(IReadOnlyDictionary<string, string?> tags, OrganizeRules? rules)
    {
        var stem = SafeComponent(NamePattern.FormatName(DownloadPattern(rules), tags));
        if (stem.Length > 0) return stem;
        var title = PyText.Strip(tags.GetValueOrDefault("title") ?? "");
        var artist = PyText.Strip(tags.GetValueOrDefault("artist") ?? "");
        foreach (var fallback in new[] { title.Length > 0 && artist.Length > 0 ? $"{title} - {artist}" : "", title, artist })
        {
            stem = SafeComponent(fallback);
            if (stem.Length > 0) return stem;
        }
        return "piste";
    }

    /// <summary><c>destination</c> : chemin complet visé ; <paramref name="extension"/> comprend son point.</summary>
    public static string Destination(string root, IReadOnlyDictionary<string, string?> tags, OrganizeRules? rules, string extension,
        string? exclude = null, Tally? tally = null) =>
        Path.Join(TargetDir(root, tags, rules, exclude, tally), TargetStem(tags, rules) + extension);

    private static bool Truthy(IReadOnlyDictionary<string, JsonNode?> settings, string key, bool fallback)
    {
        if (!settings.TryGetValue(key, out var node)) return fallback;
        return node switch
        {
            null => false,
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            JsonValue v when v.TryGetValue<double>(out var d) => d != 0,
            JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
            JsonArray a => a.Count > 0,
            JsonObject o => o.Count > 0,
            _ => fallback,
        };
    }

    // int(settings.get(key, fallback) or fallback)
    private static int IntOr(IReadOnlyDictionary<string, JsonNode?> settings, string key, int fallback)
    {
        if (!settings.TryGetValue(key, out var node) || node is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return d == 0 ? fallback : (int)Math.Truncate(d);
        if (v.TryGetValue<string>(out var s) && int.TryParse(s.Trim(), out var i)) return i == 0 ? fallback : i;
        if (v.TryGetValue<bool>(out var b)) return b ? 1 : fallback;
        return fallback;
    }
}
