using System.Text.RegularExpressions;
using DeeKeep.Common.Text;

namespace DeeKeep.Library.Organize;

/// <summary>
/// Petites règles de texte de l'écran Organiser — portage de
/// <c>organize_page.py:122-197</c> (mots, albums nommés, compilations) et de
/// <c>dedup_dialog.canonical</c>. Les chemins sont comparés à la Windows
/// (<c>ntpath</c> : casse ignorée, « / » et « \ » confondus) quel que soit l'hôte,
/// comme l'application desktop le faisait : une bibliothèque copiée d'un disque
/// NTFS ne doit pas voir apparaître deux dossiers « Madonna » et « madonna ».
/// </summary>
public static partial class OrganizeText
{
    /// <summary>COLLECTION_MIN_ARTISTS : compilation, artistes différents attendus.</summary>
    public const int CollectionMinArtists = 3;
    /// <summary>SOUNDTRACK_MIN_ARTISTS : musique de film, deux interprètes suffisent.</summary>
    public const int SoundtrackMinArtists = 2;
    /// <summary>COLLECTION_NAME_RATIO : part des titres dont l'album nomme le dossier.</summary>
    public const double CollectionNameRatio = 0.6;
    /// <summary>COLLECTION_DOMINANT_RATIO : au-delà, l'album est celui d'un seul artiste.</summary>
    public const double CollectionDominantRatio = 0.5;
    /// <summary>TAGS_LOOKUP_WARN : au-delà, la recherche des tags demande confirmation.</summary>
    public const int TagsLookupWarn = 50;
    /// <summary>SECONDS_PER_LOOKUP : durée observée d'une recherche, pour annoncer un ordre de grandeur.</summary>
    public const double SecondsPerLookup = 1.2;

    [GeneratedRegex(@"(?i)(soundtrack|motion picture|original score|film score|bande[\s-]*originale|musique (?:du|de|des|extraite))")]
    private static partial Regex SoundtrackRe();

    [GeneratedRegex(@"[^\W_]+")]
    private static partial Regex WordRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();

    [GeneratedRegex(@"[\(\[]\s*(copy|copie|\d+)\s*[\)\]]")]
    private static partial Regex CopyMarkRe();

    [GeneratedRegex(@"\b(copy|copie)\b")]
    private static partial Regex CopyWordRe();

    [GeneratedRegex(@"^\s*\d{1,3}\s*[-\._]\s*")]
    private static partial Regex LeadingTrackRe();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAsciiWordRe();

    /// <summary>_VARIOUS_NAMES : artistes d'album ne désignant personne.</summary>
    public static readonly IReadOnlySet<string> VariousNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "various", "various artists", "va", "artistes varies", "artistes variés",
        "divers", "compilation", "compilations", "multi interpretes",
        "multi interprètes", "unknown artist", "artiste inconnu",
    };

    /// <summary>
    /// <c>words</c> (version Organiser) : mots en minuscules (casefold), ponctuation et
    /// « _ » retirés, lettres accentuées CONSERVÉES (≠ <see cref="TagText.Words"/>).
    /// </summary>
    public static string Words(string? value) =>
        string.Join(" ", WordRe().Matches(PyText.CaseFold(value ?? "")).Select(m => m.Value));

    /// <summary><c>name_matches</c> : dossier et album se désignent l'un l'autre (mots entiers, dans les deux sens).</summary>
    public static bool NameMatches(string? folder, string? album)
    {
        var left = Words(folder);
        var right = Words(album);
        if (left.Length == 0 || right.Length == 0) return false;
        return $" {right} ".Contains($" {left} ", StringComparison.Ordinal) || $" {left} ".Contains($" {right} ", StringComparison.Ordinal);
    }

    /// <summary><c>is_soundtrack_album</c>.</summary>
    public static bool IsSoundtrackAlbum(string? album) => SoundtrackRe().IsMatch(album ?? "");

    /// <summary><c>is_various</c> : ce nom d'artiste ne désigne personne (« Artistes Variés »).</summary>
    public static bool IsVarious(string? name) => VariousNames.Contains(Words(name));

    /// <summary><c>_is_compilation_flag</c> : TCMP vaut oui.</summary>
    public static bool IsCompilationFlag(string? value) =>
        PyText.Strip(value ?? "").ToLowerInvariant() is "1" or "true" or "yes" or "oui";

    /// <summary><c>entry_artist</c> : artiste, à défaut artiste d'album s'il nomme quelqu'un.</summary>
    public static string EntryArtist(OrganizeEntry entry)
    {
        var artist = PyText.Strip(entry.Artist);
        if (artist.Length > 0) return artist;
        var albumArtist = PyText.Strip(entry.AlbumArtist);
        return IsVarious(albumArtist) ? "" : albumArtist;
    }

    /// <summary><c>equivalent_name</c> : deux noms qui ne diffèrent que par des blancs multiples (casse comprise).</summary>
    public static bool EquivalentName(string current, string target) =>
        SpacesRe().Replace(current, " ") == SpacesRe().Replace(target, " ");

    /// <summary>
    /// <c>dedup_dialog.canonical</c> : clé du rapprochement approximatif. Lettres non ASCII
    /// supprimées (« Été » → « t ») comme dans l'original : les mêmes fichiers se rapprochent.
    /// </summary>
    public static string DedupCanonical(string? text)
    {
        var s = (text ?? "").ToLowerInvariant();
        s = CopyMarkRe().Replace(s, " ");
        s = CopyWordRe().Replace(s, " ");
        s = LeadingTrackRe().Replace(s, " ");
        s = NonAsciiWordRe().Replace(s, " ");
        return PyText.Strip(SpacesRe().Replace(s, " "));
    }

    /// <summary><c>DeduplicationDialog._norm</c> : minuscules ASCII, ponctuation en espaces.</summary>
    public static string AsciiNorm(string? text) =>
        PyText.Strip(SpacesRe().Replace(NonAsciiWordRe().Replace((text ?? "").ToLowerInvariant(), " "), " "));

    // ------------------------------------------------------------------ chemins

    /// <summary>Clé de chemin : <c>normcase(normpath(p))</c> à la Windows.</summary>
    public static string PathKey(string? path) => PyPath.NormCase(PyPath.NormPath(path ?? ""));

    /// <summary><c>same_path</c>.</summary>
    public static bool SamePath(string a, string b) => PathKey(a) == PathKey(b);

    /// <summary><c>is_inside</c> : <paramref name="path"/> est sous <paramref name="folder"/>, à n'importe quel niveau.</summary>
    public static bool IsInside(string path, string folder) =>
        PathKey(path).StartsWith(PathKey(folder).TrimEnd('\\') + "\\", StringComparison.Ordinal);

    /// <summary>Nombre de séparateurs d'un chemin (profondeur), « / » et « \ » confondus.</summary>
    public static int Depth(string path) => path.Count(c => c is '\\' or '/');

    /// <summary>Joint deux composants avec le séparateur que la racine emploie déjà.</summary>
    public static string Join(string root, string child)
    {
        if (root.Length == 0) return child;
        var sep = root.Contains('/') && !root.Contains('\\') ? '/'
            : root.Contains('\\') && !root.Contains('/') ? '\\'
            : Path.DirectorySeparatorChar;
        return root.TrimEnd('\\', '/') + sep + child;
    }

    /// <summary>Chemin affiché (<c>_rel</c>) : relatif à la racine quand c'est possible, absolu sinon.</summary>
    public static string Relative(string path, string root)
    {
        // Les chemins de l'analyse commencent littéralement par la racine (parcours depuis
        // elle) : la clé décide, le chemin d'origine est découpé tel quel.
        var r = root.TrimEnd('\\', '/');
        if (path.Length > r.Length && PathKey(path).StartsWith(PathKey(r) + "\\", StringComparison.Ordinal)) return path[(r.Length + 1)..];
        return SamePath(path, r) ? "." : path;
    }

    /// <summary>Dossier relatif d'un fichier (« / » comme séparateur, vide à la racine) ; hors racine : rattaché à la racine.</summary>
    public static string RelativeFolder(string path, string root)
    {
        var dir = PyPath.Dirname(path);
        if (SamePath(dir, root) || !IsInside(dir, root)) return "";
        return Relative(dir, root).Replace('\\', '/');
    }
}
