using DeeKeep.Common.Text;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Library.Tracks;

/// <summary>
/// Recherche multi-tags, cinq propositions et tri de la page Bibliothèque — portage de
/// <c>library_page.py:676-914</c> (spec 02 §2.2 à §2.4). Le client Angular applique les
/// mêmes règles sur ses données en cache ; le serveur les sert à qui l'interroge.
/// </summary>
public static class TrackSearch
{
    /// <summary>MIN_SUGGEST_CHARS.</summary>
    public const int MinSuggestChars = 3;
    /// <summary>MAX_SUGGESTIONS.</summary>
    public const int MaxSuggestions = 5;

    /// <summary>SEARCH_FIELDS : champ et libellé, l'ordre sert de priorité aux propositions.</summary>
    public static readonly IReadOnlyList<(string Field, string Label)> Fields =
    [
        ("artist", "Artiste"), ("album", "Album"), ("title", "Titre"), ("album_artist", "Album de"),
        ("genre", "Genre"), ("year", "Année"), ("format", "Format"),
    ];

    public static string? FieldValue(LibraryEntry e, string field) => field switch
    {
        "artist" => e.Artist,
        "album" => e.Album,
        "title" => e.Title,
        "album_artist" => e.AlbumArtist,
        "genre" => e.Genre,
        "year" => e.Year,
        "format" => e.Format,
        "path" => e.Path,
        _ => null,
    };

    /// <summary><c>_haystack</c> : tous les tags et le nom du fichier en une chaîne comparable.</summary>
    public static string Haystack(LibraryEntry e)
    {
        var parts = Fields.Select(f => FieldValue(e, f.Field) ?? "").Append(PyPath.Basename(e.Path));
        return LibraryText.Canonical(string.Join(" ", parts));
    }

    /// <summary>Mots de la recherche (forme canonique, vides écartés).</summary>
    public static IReadOnlyList<string> Tokens(string? query) =>
        LibraryText.Canonical(PyText.Strip(query ?? "")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary><c>_matches</c> : chaque mot est une SOUS-CHAÎNE de la botte de foin (ordre libre).</summary>
    public static bool Matches(string haystack, IReadOnlyList<string> tokens) => tokens.All(t => haystack.Contains(t, StringComparison.Ordinal));

    public sealed record ValueSlot(string Label, string Value, string Key, int Count, int Priority);

    /// <summary>
    /// <c>_build_value_index</c> : valeurs de tags des titres affichables, une valeur comptée une
    /// fois par titre et rangée sous sa nature la plus prioritaire ; tri (-nombre, priorité, clé).
    /// </summary>
    public static IReadOnlyList<ValueSlot> BuildValueIndex(IEnumerable<LibraryEntry> selectable)
    {
        var counts = new Dictionary<string, ValueSlot>(StringComparer.Ordinal);
        foreach (var entry in selectable)
        {
            var best = new Dictionary<string, (int Priority, string Raw, string Label)>(StringComparer.Ordinal);
            for (var priority = 0; priority < Fields.Count; priority++)
            {
                var (field, label) = Fields[priority];
                var raw = PyText.Strip(FieldValue(entry, field) ?? "");
                var key = raw.Length > 0 ? LibraryText.Canonical(raw) : "";
                if (key.Length == 0) continue;
                if (!best.TryGetValue(key, out var current) || priority < current.Priority) best[key] = (priority, raw, label);
            }
            foreach (var (key, (priority, raw, label)) in best)
            {
                if (!counts.TryGetValue(key, out var slot)) counts[key] = new ValueSlot(label, raw, key, 1, priority);
                else if (priority < slot.Priority) counts[key] = new ValueSlot(label, raw, key, slot.Count + 1, priority);
                else counts[key] = slot with { Count = slot.Count + 1 };
            }
        }
        return counts.Values
            .OrderBy(v => -v.Count).ThenBy(v => v.Priority).ThenBy(v => v.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary><c>_suggestions_for</c> : celles qui commencent par la frappe, puis celles qui la contiennent.</summary>
    public static IReadOnlyList<TagSuggestion> Suggest(IReadOnlyList<ValueSlot> values, string? text, int limit = MaxSuggestions)
    {
        var needle = LibraryText.Canonical(PyText.Strip(text ?? ""));
        if (needle.Length == 0) return [];
        var starts = new List<ValueSlot>();
        var contains = new List<ValueSlot>();
        foreach (var item in values)
        {
            if (item.Key.StartsWith(needle, StringComparison.Ordinal)) starts.Add(item);
            else if (item.Key.Contains(needle, StringComparison.Ordinal)) contains.Add(item);
            if (starts.Count >= limit) break;
        }
        return starts.Concat(contains).Take(limit)
            .Select(v => new TagSuggestion(FieldOfLabel(v.Label), v.Label, v.Value, v.Count)).ToList();
    }

    /// <summary>Libellé exact d'une proposition : « Artiste · Madonna   (61 titres) ».</summary>
    public static string SuggestionText(TagSuggestion s) => $"{s.Label} · {s.Value}   ({s.Count} titre{(s.Count > 1 ? "s" : "")})";

    private static string FieldOfLabel(string label) => Fields.First(f => f.Label == label).Field;

    /// <summary>Colonnes triables (SORT_KEYS) ; « cloud » = état d'archive.</summary>
    public static readonly IReadOnlySet<string> SortKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "title", "artist", "album", "genre", "year", "duration", "format", "cloud", "path",
    };

    /// <summary>
    /// Tri stable de <c>_sorted_rows</c> : durée et état d'archive numériques, le reste en
    /// forme canonique, valeurs vides en fin (ascendant) ; l'année se trie comme une chaîne.
    /// </summary>
    public static IEnumerable<LibraryEntry> Sort(IEnumerable<LibraryEntry> rows, string? sortBy, bool descending)
    {
        var key = sortBy is not null && SortKeys.Contains(sortBy) ? sortBy : "artist";
        if (key is "duration" or "cloud")
        {
            Func<LibraryEntry, double> numeric = key == "duration" ? e => e.Duration ?? 0 : e => e.CloudOrder;
            return descending ? rows.OrderByDescending(numeric) : rows.OrderBy(numeric);
        }
        string Text(LibraryEntry e)
        {
            var canonical = LibraryText.Canonical(FieldValue(e, key) ?? "");
            return canonical.Length > 0 ? canonical : "￿";
        }
        return descending ? rows.OrderByDescending(Text, StringComparer.Ordinal) : rows.OrderBy(Text, StringComparer.Ordinal);
    }
}
