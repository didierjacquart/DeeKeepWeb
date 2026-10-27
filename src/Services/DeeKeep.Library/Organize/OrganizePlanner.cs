using DeeKeep.Common.Text;
using DeeKeep.Contracts.Organize;
using DeeKeep.Library.Rules;

namespace DeeKeep.Library.Organize;

/// <summary>
/// <c>ArtistIndex</c> : ramène les écritures d'un même artiste (« Inna », « Inna Feat.
/// Flo Rida ») à une clé unique, et retient comme nom de dossier l'écriture la plus fréquente.
/// </summary>
public sealed class ArtistIndex
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _folders = new(StringComparer.Ordinal);

    public ArtistIndex(IEnumerable<OrganizeEntry> entries)
    {
        // Counter : ordre de première apparition, comme le dictionnaire Python.
        var raw = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in entries)
        {
            var name = OrganizeText.EntryArtist(entry);
            if (name.Length == 0) continue;
            if (raw.TryGetValue(name, out var n)) raw[name] = n + 1;
            else { raw[name] = 1; order.Add(name); }
        }
        // Seuls les noms présents tels quels autorisent la coupe d'un séparateur ambigu.
        var known = new HashSet<string>(order.Select(LibraryRules.ArtistKey).Where(k => k.Length > 0), StringComparer.Ordinal);

        var spellings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var name in order)
        {
            var count = raw[name];
            var baseName = LibraryRules.MainArtist(name, known);
            var key = LibraryRules.ArtistKey(baseName);
            if (key.Length == 0)
            {
                baseName = name;
                key = LibraryRules.ArtistKey(name);
            }
            _keys[name] = key;
            if (!spellings.TryGetValue(key, out var names)) spellings[key] = names = new Dictionary<string, int>(StringComparer.Ordinal);
            names[baseName] = names.GetValueOrDefault(baseName) + count;
        }

        // max(sorted(names), key=names.get) : à égalité, la première dans l'ordre ordinal.
        foreach (var (key, names) in spellings)
        {
            string? best = null;
            var bestCount = -1;
            foreach (var name in names.Keys.Order(StringComparer.Ordinal))
            {
                if (names[name] > bestCount)
                {
                    best = name;
                    bestCount = names[name];
                }
            }
            _folders[key] = LibraryRules.SafeComponent(best);
        }
    }

    /// <summary>Clé de l'artiste <paramref name="name"/> (vide si le nom est inutilisable).</summary>
    public string Key(string? name)
    {
        var n = PyText.Strip(name ?? "");
        if (n.Length == 0) return "";
        return _keys.TryGetValue(n, out var key) ? key : LibraryRules.ArtistKey(n);
    }

    public string EntryKey(OrganizeEntry entry) => Key(OrganizeText.EntryArtist(entry));

    /// <summary>Nom du dossier de l'artiste désigné par <paramref name="key"/>.</summary>
    public string Folder(string key) => _folders.GetValueOrDefault(key, "");
}

/// <summary>Accès au disque dont le plan a besoin (remplaçable dans les tests).</summary>
public sealed class PlanEnvironment
{
    /// <summary>(débit kb/s, taille octets) d'un fichier ; (0, 0) s'il est illisible.</summary>
    public Func<OrganizeEntry, (int Bitrate, long Size)> Quality { get; init; } = _ => (0, 0);

    /// <summary>
    /// <c>artist_folder_count</c> : fichiers audio déjà rangés sous « racine/dossier », hors
    /// lot analysé (clés de chemin dans le 3e argument).
    /// </summary>
    public Func<string, string, IReadOnlySet<string>, int> ArtistFolderCount { get; init; } = (_, _, _) => 0;

    /// <summary>Format cible des Paramètres (<c>file_format</c>), départage les exemplaires de qualité égale.</summary>
    public string? TargetFormat { get; init; }
}

/// <summary>Ligne du plan (<c>build_plan</c>, spec 03 §5.2).</summary>
public sealed record PlanResult(OrganizeEntry Entry, string Src, string Status, string Dst, IReadOnlyList<string> Missing, bool ToDivers, bool FromDivers);

public sealed record PlanOutcome(IReadOnlyList<PlanResult> Results, OrganizePlanStats Stats, IReadOnlyList<string> ProtectedDirs);

/// <summary>
/// Calcul du plan de rangement — portage de <c>organize_page.build_plan</c> et de ses
/// auxiliaires (albums préservés, doublons certains, décompte « Divers »).
/// </summary>
public static class OrganizePlanner
{
    public const string DiversFolder = LibraryRules.DiversFolder;

    /// <summary>Options de plan normalisées (motif vide → défaut, seuil ≥ 1).</summary>
    public static OrganizeOptions Normalize(OrganizeOptions o) => o with
    {
        Pattern = string.IsNullOrEmpty(o.Pattern) ? NamePattern.DefaultPattern : o.Pattern,
        DiversMin = Math.Max(1, o.DiversMin == 0 ? 3 : o.DiversMin),
    };

    /// <summary>
    /// <c>collection_folders</c> : dossiers formant un album à part entière (musique de film,
    /// compilation), renvoyés dans l'ordre de rencontre (chemins tels quels).
    /// </summary>
    public static IReadOnlyList<string> CollectionFolders(IReadOnlyList<OrganizeEntry> entries, string root, ArtistIndex index)
    {
        var byDir = new Dictionary<string, List<OrganizeEntry>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in entries)
        {
            var dir = PyPath.Dirname(entry.Path);
            if (!byDir.TryGetValue(dir, out var list))
            {
                byDir[dir] = list = [];
                order.Add(dir);
            }
            list.Add(entry);
        }

        var diversRoot = OrganizeText.Join(root, DiversFolder);
        var protectedDirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dirpath in order)
        {
            var items = byDir[dirpath];
            if (OrganizeText.SamePath(dirpath, root) || OrganizeText.SamePath(dirpath, diversRoot)) continue;
            if (OrganizeText.IsInside(dirpath, diversRoot)) continue;

            var folder = PyPath.Basename(dirpath);
            var named = items.Where(i => OrganizeText.NameMatches(folder, i.Album)).ToList();
            if (named.Count == 0) continue;

            var artists = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var key = index.EntryKey(item);
                if (key.Length > 0) artists[key] = artists.GetValueOrDefault(key) + 1;
            }
            var coherent = named.Count >= OrganizeText.CollectionNameRatio * items.Count;
            var multi = artists.Count >= OrganizeText.CollectionMinArtists;

            void Protect()
            {
                if (seen.Add(OrganizeText.PathKey(dirpath))) protectedDirs.Add(dirpath);
            }

            if (named.Any(i => OrganizeText.IsSoundtrackAlbum(i.Album)))
            {
                // Une musique de film n'a pas besoin de trois artistes, mais il en faut plus d'un.
                if (artists.Count >= OrganizeText.SoundtrackMinArtists && (coherent || multi)) Protect();
                continue;
            }
            if (!coherent || !multi) continue;

            var total = artists.Values.Sum();
            var dominant = artists.Values.Max() / (double)total;
            var flagged = items.Count(i => OrganizeText.IsCompilationFlag(i.Compilation) || OrganizeText.IsVarious(i.AlbumArtist));
            if (dominant < OrganizeText.CollectionDominantRatio || flagged > items.Count / 2.0) Protect();
        }
        return protectedDirs;
    }

    private sealed class Target
    {
        public string ArtistId = "";
        public string Artist = "";
        public string Album = "";
        public string Name = "";
        public bool NoArtist;
        public IReadOnlyList<string> NoTags = [];
        public bool NoPattern;
        public bool Blocked;
    }

    /// <summary><c>build_plan</c> : une ligne par entrée, dans l'ordre de l'analyse.</summary>
    public static PlanOutcome BuildPlan(IReadOnlyList<OrganizeEntry> entries, string root, OrganizeOptions options, PlanEnvironment env)
    {
        var o = Normalize(options);
        var useDivers = o.Divers && o.Artist;
        var index = new ArtistIndex(entries);

        // Albums à part entière : ni rangés, ni comptés, ni dédoublonnés (un titre peut
        // figurer dans une bande originale et chez son artiste).
        var protectedDirs = o.KeepAlbums ? CollectionFolders(entries, root, index) : [];
        var protectedKeys = protectedDirs.Select(OrganizeText.PathKey).ToHashSet(StringComparer.Ordinal);
        var diversRoot = OrganizeText.Join(root, DiversFolder);

        // Première passe : artiste et nom visés, indépendants du décompte (les doublons
        // sont reconnus avant de compter : trois exemplaires ne valent pas trois titres).
        var targets = new Target?[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var src = entry.Path;
            if (protectedKeys.Count > 0 && protectedKeys.Contains(OrganizeText.PathKey(PyPath.Dirname(src)))) continue;

            var artistId = index.EntryKey(entry);
            var t = new Target
            {
                ArtistId = artistId,
                Artist = index.Folder(artistId),
                Album = PyText.Strip(entry.Album),
                Name = PyPath.Basename(src),
            };
            t.NoArtist = o.Artist && t.Artist.Length == 0;
            if (o.Rename && !t.NoArtist)
            {
                // Un motif dont un tag manque, que le nom de fichier comblerait, rallongerait ce
                // nom à chaque passe : le fichier est signalé, jamais renommé.
                t.NoTags = NamePattern.MissingRequiredTags(o.Pattern, entry.Get);
                if (t.NoTags.Count == 0)
                {
                    var stem = LibraryRules.SafeComponent(NamePattern.FormatName(o.Pattern, entry.Get));
                    if (stem.Length > 0) t.Name = stem + PyPath.SplitExt(src).Ext;
                    else t.NoPattern = true;
                }
            }
            t.Blocked = t.NoArtist || t.NoTags.Count > 0 || t.NoPattern || (o.Album && t.Album.Length == 0);
            targets[i] = t;
        }

        var duplicates = o.Dedup ? DuplicateCopies(entries, targets, o.Album, diversRoot, env) : new Dictionary<string, string>();

        var analyzed = entries.Select(e => OrganizeText.PathKey(e.Path)).ToHashSet(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (useDivers)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (targets[i] is null || duplicates.ContainsKey(OrganizeText.PathKey(entries[i].Path))) continue;
                var key = index.EntryKey(entries[i]);
                if (key.Length > 0) counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            // Le dossier existant de l'artiste compte aussi : deux titres égarés dans « Divers »
            // rejoignent un dossier d'artiste même si l'analyse ne porte que sur « Divers ».
            foreach (var key in counts.Keys.ToList())
                if (counts[key] < o.DiversMin)
                    counts[key] += env.ArtistFolderCount(root, index.Folder(key), analyzed);
        }

        var results = new List<PlanResult>(entries.Count);
        int move = 0, inplace = 0, untagged = 0, duplicate = 0, divers = 0, fromDivers = 0, album = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var src = entry.Path;
            var t = targets[i];
            if (t is null)
            {
                album++;
                results.Add(new PlanResult(entry, src, OrganizeStatuses.Album, "", [], false, false));
                continue;
            }
            if (duplicates.TryGetValue(OrganizeText.PathKey(src), out var kept))
            {
                duplicate++;
                results.Add(new PlanResult(entry, src, OrganizeStatuses.Duplicate, kept, [], false, false));
                continue;
            }

            // Sous le seuil, « Divers » : ni dossier d'artiste ni d'album, l'album n'est pas requis.
            var toDivers = useDivers && t.ArtistId.Length > 0 && counts.GetValueOrDefault(t.ArtistId) < o.DiversMin;

            var missing = new List<string>();
            if (t.NoArtist) missing.Add("artiste");
            missing.AddRange(t.NoTags);
            if (o.Album && t.Album.Length == 0 && !toDivers) missing.Add("album");
            if (missing.Count == 0 && t.NoPattern) missing.Add("motif de nommage sans valeur");
            if (missing.Count > 0)
            {
                untagged++;
                results.Add(new PlanResult(entry, src, OrganizeStatuses.Untagged, "", missing, false, false));
                continue;
            }

            var targetDir = root;
            if (toDivers) targetDir = OrganizeText.Join(targetDir, DiversFolder);
            else
            {
                if (o.Artist) targetDir = OrganizeText.Join(targetDir, t.Artist);
                if (o.Album) targetDir = OrganizeText.Join(targetDir, LibraryRules.SafeComponent(t.Album));
            }
            var dst = OrganizeText.Join(targetDir, t.Name);
            if (OrganizeText.SamePath(PyPath.Dirname(src), targetDir) && OrganizeText.EquivalentName(PyPath.Basename(src), t.Name))
            {
                inplace++;
                results.Add(new PlanResult(entry, src, OrganizeStatuses.InPlace, dst, [], false, false));
                continue;
            }

            move++;
            var leavesDivers = false;
            if (toDivers) divers++;
            else if (OrganizeText.IsInside(src, diversRoot))
            {
                // Le titre quitte « Divers » : son artiste a atteint le seuil.
                fromDivers++;
                leavesDivers = true;
            }
            results.Add(new PlanResult(entry, src, OrganizeStatuses.Move, dst, [], toDivers, leavesDivers));
        }

        var stats = new OrganizePlanStats(entries.Count, move, inplace, untagged, duplicate, divers, fromDivers, album, protectedDirs.Count);
        return new PlanOutcome(results, stats, protectedDirs);
    }

    /// <summary>
    /// <c>duplicate_copies</c> : exemplaires en trop d'un même titre — même artiste principal,
    /// même album (si l'option Album est active), même nom visé — {clé de chemin → chemin gardé}.
    /// </summary>
    private static Dictionary<string, string> DuplicateCopies(IReadOnlyList<OrganizeEntry> entries, Target?[] targets, bool orgAlbum,
        string diversRoot, PlanEnvironment env)
    {
        var groups = new Dictionary<(string, string, string), List<OrganizeEntry>>();
        var order = new List<(string, string, string)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var t = targets[i];
            // Un fichier que le plan ne sait pas ranger ne sert jamais de référence.
            if (t is null || t.Blocked) continue;
            var key = (t.ArtistId, orgAlbum ? PyText.CaseFold(LibraryRules.SafeComponent(t.Album)) : "", PyText.CaseFold(t.Name));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                order.Add(key);
            }
            list.Add(entries[i]);
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in order)
        {
            var group = groups[key];
            if (group.Count < 2) continue;
            var keptIndex = BestCopy.Choose(group.Select(e => Candidate(e, env)).ToList(), env.TargetFormat, diversRoot);
            var kept = group[keptIndex];
            foreach (var entry in group)
                if (!ReferenceEquals(entry, kept)) extra[OrganizeText.PathKey(entry.Path)] = kept.Path;
        }
        return extra;
    }

    public static CopyCandidate Candidate(OrganizeEntry entry, PlanEnvironment env)
    {
        var (bitrate, size) = env.Quality(entry);
        return new CopyCandidate(entry.Path, bitrate, size, entry.Format, OrganizeText.EntryArtist(entry));
    }

    /// <summary>Fichiers à soumettre à « Compléter les tags » : non tagués dans le plan, ou tags complémentaires manquants.</summary>
    public static bool NeedsLookup(PlanResult result) => result.Status == OrganizeStatuses.Untagged || result.Entry.NeedsExtras;

    /// <summary><c>_default_checked</c> : à faire coché ; déjà en place coché seulement s'il lui manque des tags complémentaires.</summary>
    public static bool DefaultChecked(PlanResult result) => result.Status switch
    {
        OrganizeStatuses.Move or OrganizeStatuses.Duplicate or OrganizeStatuses.Untagged => true,
        _ => result.Entry.NeedsExtras,
    };
}
