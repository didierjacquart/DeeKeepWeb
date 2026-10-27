using System.Text.RegularExpressions;
using DeeKeep.Common.Text;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Library.Presence;

/// <summary>Métadonnées connues d'un fichier (cache <c>tracks</c> ou historique).</summary>
public sealed record KnownTags(string Title, string Artist, string Album);

/// <summary>Verdict d'un titre : état, motif, fichiers en cause (chemins de la base, sans doublon).</summary>
public sealed record PresenceMatch(PresenceState State, string Reason, IReadOnlyList<string> Paths)
{
    public const string AbsentReason = "Absent du dossier de dépôt";
    public const string PendingReason = "Lecture du dossier de dépôt en cours…";

    public static readonly PresenceMatch Absent = new(PresenceState.Absent, AbsentReason, []);
    public static readonly PresenceMatch Pending = new(PresenceState.Pending, PendingReason, []);
}

/// <summary>
/// Ce que le dossier de dépôt contient, prêt à être interrogé — portage de
/// <c>library_index.LibraryIndex</c> (spec 02 §3.1-3.3). Construit une fois, puis complété
/// (<see cref="Add"/>) ou réduit (<see cref="Remove"/>) au fil des extractions et suppressions.
/// Les chemins indexés sont ceux de la base ; le chemin local ne sert qu'à lire les dossiers.
/// </summary>
/// <remarks>Non synchronisé : le service propriétaire le protège par un verrou.</remarks>
public sealed partial class PresenceIndex
{
    private sealed record Entry(string Path, string Title, string Artist, string Album, string TitleKey, string BaseKey,
        string AlbumKey, IReadOnlySet<string> ArtistTokens, string ArtistKey, string MainKey, string StemKey);

    [GeneratedRegex(@"\s+-\s+")]
    private static partial Regex DashRe();

    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byFilename = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byPair = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byMain = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byTitle = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byBase = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byStem = new(StringComparer.Ordinal);
    private Dictionary<string, List<Entry>> _byToken = new(StringComparer.Ordinal);

    public PresenceIndex(string root = "")
    {
        Root = root ?? "";
    }

    /// <summary>Racine locale parcourue (sert à lire artiste et album dans les dossiers).</summary>
    public string Root { get; }

    /// <summary>Nombre de fichiers indexés.</summary>
    public int Count => _paths.Count;

    public static string Key(string path) => LibraryText.MatchKey(path);

    /// <summary>
    /// Indexe un fichier : <paramref name="known"/> s'il porte un titre ou un artiste, sinon
    /// lecture du chemin <paramref name="localPath"/> (dossier d'artiste, d'album, nom « Titre - Artiste »).
    /// </summary>
    public void AddFile(string dbPath, string localPath, KnownTags? known)
    {
        _paths.Add(Key(dbPath));
        var name = PyPath.Basename(dbPath).ToLowerInvariant();
        if (!_byFilename.TryGetValue(name, out var list)) _byFilename[name] = list = [];
        list.Add(dbPath);
        var stem = PyPath.Stem(dbPath);
        if (known is not null && (known.Title.Length > 0 || known.Artist.Length > 0))
            Ingest(dbPath, known.Title, known.Artist, known.Album, stem);
        else
            foreach (var (title, artist, album) in FromPath(localPath, Root))
                Ingest(dbPath, title, artist, album, stem);
    }

    /// <summary><c>add</c> : fichier fraîchement extrait, sans relire le dossier. Déjà indexé, il est remplacé.</summary>
    public void Add(string dbPath, string localPath, string? title, string? artist, string? album)
    {
        if (string.IsNullOrEmpty(dbPath)) return;
        if (HasPath(dbPath)) Remove(dbPath);
        var known = new KnownTags(title ?? "", artist ?? "", album ?? "");
        AddFile(dbPath, localPath, known.Title.Length > 0 || known.Artist.Length > 0 ? known : null);
    }

    /// <summary><c>remove</c> : oublie un fichier supprimé ; vrai s'il était indexé.</summary>
    public bool Remove(string dbPath)
    {
        var key = Key(dbPath);
        if (!_paths.Remove(key)) return false;
        var name = PyPath.Basename(dbPath).ToLowerInvariant();
        if (_byFilename.TryGetValue(name, out var list))
        {
            list.RemoveAll(p => Key(p) == key);
            if (list.Count == 0) _byFilename.Remove(name);
        }
        _entries.RemoveAll(e => Key(e.Path) == key);
        _byPair = new(StringComparer.Ordinal);
        _byMain = new(StringComparer.Ordinal);
        _byTitle = new(StringComparer.Ordinal);
        _byBase = new(StringComparer.Ordinal);
        _byStem = new(StringComparer.Ordinal);
        _byToken = new(StringComparer.Ordinal);
        foreach (var entry in _entries) Index(entry);
        return true;
    }

    public bool HasPath(string dbPath) => _paths.Contains(Key(dbPath));

    /// <summary><c>paths_named</c> : fichiers indexés portant ce nom (casse ignorée).</summary>
    public IReadOnlyList<string> PathsNamed(string filename) =>
        _byFilename.TryGetValue(PyPath.Basename(filename).ToLowerInvariant(), out var list) ? list.ToList() : [];

    // ------------------------------------------------------------------ lecture du chemin

    /// <summary>
    /// <c>_from_path</c> : lectures (titre, artiste, album) déduites du chemin — une seule
    /// d'ordinaire, deux quand le dossier ne dit pas de quel côté du tiret est l'artiste.
    /// </summary>
    public static IReadOnlyList<(string Title, string Artist, string Album)> FromPath(string localPath, string root)
    {
        var stem = PyPath.Stem(localPath);
        string folderArtist = "", folderAlbum = "";
        var parts = RelativeParts(localPath, root);
        if (parts.Length >= 2)
        {
            // « Divers » est le fourre-tout des artistes peu représentés : il ne nomme personne.
            if (LibraryText.Canonical(parts[0]) != "divers") folderArtist = parts[0];
            if (parts.Length >= 3) folderAlbum = parts[1];
        }
        var (primary, alternate) = SplitStem(stem, folderArtist);
        var readings = new List<(string, string, string)> { (primary.Title, primary.Artist, folderAlbum) };
        if (alternate is { } alt && alt != primary) readings.Add((alt.Title, alt.Artist, folderAlbum));
        return readings;
    }

    /// <summary><c>_split_stem</c> : titre et artiste d'un nom « Titre - Artiste », le dossier tranche s'il peut.</summary>
    public static ((string Title, string Artist) Primary, (string Title, string Artist)? Alternate) SplitStem(string stem, string folderArtist)
    {
        var parts = DashRe().Split(stem);
        if (parts.Length < 2) return ((stem, folderArtist), null);
        var left = PyText.Strip(string.Join(" - ", parts[..^1]));
        var right = PyText.Strip(parts[^1]);
        if (folderArtist.Length > 0)
        {
            var folder = LibraryText.Canonical(folderArtist);
            if (LibraryText.Canonical(right) == folder) return ((left, right), null);
            if (LibraryText.Canonical(left) == folder) return ((right, left), null);
        }
        return ((left, right), (right, left));
    }

    private static string[] RelativeParts(string localPath, string root)
    {
        if (string.IsNullOrEmpty(root)) return [];
        try
        {
            var relative = Path.GetRelativePath(root, localPath);
            // Autre lecteur : GetRelativePath rend le chemin absolu, relpath lèverait ValueError.
            if (Path.IsPathRooted(relative)) return [];
            return relative.Split(['\\', '/']);
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ tables

    private void Ingest(string path, string title, string artist, string album, string stem)
    {
        var entry = new Entry(path, title, artist, album,
            LibraryText.TitleKey(title), LibraryText.BaseKey(title), LibraryText.Canonical(album),
            LibraryText.ArtistTokens(artist), LibraryText.ArtistKey(artist), LibraryText.MainArtistKey(artist), LibraryText.Canonical(stem));
        if (entry.TitleKey.Length == 0) return;
        _entries.Add(entry);
        Index(entry);
    }

    private void Index(Entry entry)
    {
        Append(_byPair, entry.ArtistKey + "|" + entry.TitleKey, entry);
        Append(_byMain, entry.MainKey + "|" + entry.TitleKey, entry);
        Append(_byTitle, entry.TitleKey, entry);
        if (entry.BaseKey.Length > 0) Append(_byBase, entry.BaseKey, entry);
        if (entry.StemKey.Length > 0) Append(_byStem, entry.StemKey, entry);
        foreach (var token in entry.TitleKey.Split(' ').Distinct(StringComparer.Ordinal)) Append(_byToken, token, entry);
    }

    private static void Append(Dictionary<string, List<Entry>> table, string key, Entry entry)
    {
        if (!table.TryGetValue(key, out var list)) table[key] = list = [];
        list.Add(entry);
    }

    // ------------------------------------------------------------------ interrogation

    /// <summary><c>lookup</c> : rapprochements du plus sûr au plus lâche ; le premier qui aboutit tranche.</summary>
    public PresenceMatch Lookup(string? title, string? artist, string? album = "")
    {
        var tkey = LibraryText.TitleKey(title);
        if (tkey.Length == 0 || _entries.Count == 0) return PresenceMatch.Absent;
        var akey = LibraryText.ArtistKey(artist);
        var queryTokens = LibraryText.ArtistTokens(artist);
        var mkey = LibraryText.MainArtistKey(artist);
        var alkey = LibraryText.Canonical(album);

        // 1. Même titre, même artiste.
        if (_byPair.TryGetValue(akey + "|" + tkey, out var found))
            return Match(PresenceState.Present, "Déjà présent (même titre, même artiste)", found);

        // 2. Le fichier porte exactement le nom qu'une extraction lui donnerait.
        foreach (var stem in new[] { LibraryText.Canonical($"{title} - {artist}"), LibraryText.Canonical($"{artist} - {title}") })
            if (stem.Length > 0 && _byStem.TryGetValue(stem, out found))
                return Match(PresenceState.Present, "Déjà présent (fichier du même nom)", found);

        // 3. Même titre, même artiste principal : l'invité seul diffère.
        if (mkey.Length > 0 && _byMain.TryGetValue(mkey + "|" + tkey, out found))
            return Match(PresenceState.Possible, "Doublon probable : même titre, même artiste principal (l'invité diffère)", found);

        // 4. Même titre : artiste proche, ou même album.
        var sameTitle = _byTitle.GetValueOrDefault(tkey) ?? [];
        var close = sameTitle.Where(e => LibraryText.Overlap(queryTokens, e.ArtistTokens) >= LibraryText.ArtistOverlapMin).ToList();
        if (close.Count > 0) return Match(PresenceState.Possible, "Doublon possible : même titre, artiste similaire", close);
        if (alkey.Length > 0)
        {
            var sameAlbum = sameTitle.Where(e => e.AlbumKey == alkey).ToList();
            if (sameAlbum.Count > 0) return Match(PresenceState.Possible, "Doublon possible : même titre, même album", sameAlbum);
        }

        // 5. Même morceau, autre version (remix, live, edit…).
        var bkey = LibraryText.BaseKey(title);
        if (bkey.Length > 0)
        {
            var versions = (_byBase.GetValueOrDefault(bkey) ?? [])
                .Where(e => (mkey.Length > 0 && e.MainKey == mkey) || LibraryText.Overlap(queryTokens, e.ArtistTokens) >= LibraryText.ArtistOverlapMin)
                .ToList();
            if (versions.Count > 0) return Match(PresenceState.Possible, "Doublon possible : autre version du même titre", versions);
        }

        // 6. Recherche approchante : titre voisin et artiste compatible.
        var approximate = Approximate(tkey, queryTokens, mkey);
        if (approximate.Count > 0) return Match(PresenceState.Possible, "Doublon possible : titre proche, artiste compatible", approximate);

        return PresenceMatch.Absent;
    }

    private List<Entry> Approximate(string tkey, IReadOnlySet<string> queryTokens, string mkey)
    {
        var words = new HashSet<string>(tkey.Split(' '), StringComparer.Ordinal);
        var seen = new HashSet<Entry>(ReferenceEqualityComparer.Instance);
        var candidates = new List<Entry>();
        // L'ordre d'itération d'un set Python n'est pas celui d'insertion : seul l'ensemble
        // des chemins retenus compte, pas leur ordre.
        foreach (var word in words)
        {
            foreach (var entry in _byToken.GetValueOrDefault(word) ?? [])
            {
                if (!seen.Add(entry)) continue;
                var entryWords = new HashSet<string>(entry.TitleKey.Split(' '), StringComparer.Ordinal);
                if (LibraryText.Overlap(words, entryWords) >= LibraryText.TitleOverlapMin) candidates.Add(entry);
            }
        }
        return candidates.Where(e =>
                ((mkey.Length > 0 && e.MainKey == mkey) || LibraryText.Overlap(queryTokens, e.ArtistTokens) >= LibraryText.ArtistOverlapMin)
                && LibraryText.Ratio(tkey, e.TitleKey) >= LibraryText.TitleRatioMin)
            .ToList();
    }

    private static PresenceMatch Match(PresenceState state, string reason, IEnumerable<Entry> entries)
    {
        var paths = new List<string>();
        foreach (var entry in entries)
            if (entry.Path.Length > 0 && !paths.Contains(entry.Path)) paths.Add(entry.Path);
        return new PresenceMatch(state, reason, paths);
    }
}
