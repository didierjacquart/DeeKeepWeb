using DeeKeep.Common.Text;

namespace DeeKeep.Library.Organize;

/// <summary>Exemplaire candidat dans un groupe de doublons.</summary>
public sealed record CopyCandidate(string Path, int Bitrate, long Size, string? Format, string? Artist);

/// <summary>
/// Choix du meilleur exemplaire d'un groupe de doublons : UNE règle pour les trois écrans
/// qui en avaient chacun une (spec 03 §6.5-1) — « Ne garder qu'un exemplaire » du plan
/// (<c>best_copy</c> : débit &gt; taille &gt; rangement), l'écran fichier/fichier
/// (<c>_keep_score</c> : format cible &gt; dossier de l'artiste &gt; profondeur &gt; débit) et
/// l'écran piste/fichiers (<c>file_score</c> : débit &gt; taille &gt; profondeur).
/// </summary>
/// <remarks>
/// <para>Une suppression ne se rattrape pas : la qualité passe d'abord, puis le rangement.
/// Rang (le plus petit est gardé, le premier à égalité totale) :</para>
/// <list type="number">
/// <item>meilleur débit ;</item>
/// <item>taille à 3 % près de la plus grande du groupe (<c>DUPLICATE_SIZE_RATIO</c>) : au-delà,
/// l'exemplaire est tronqué ou appauvri ;</item>
/// <item>format cible des Paramètres (<c>file_format</c>) ;</item>
/// <item>rangé dans le dossier de son artiste (un composant du dossier parent porte son nom) ;</item>
/// <item>hors « Divers » ;</item>
/// <item>le moins enfoui (nombre de séparateurs) ;</item>
/// <item>le plus gros (pochette, tags plus complets) ;</item>
/// <item>chemin le plus court, puis ordre alphabétique (casefold) : le choix ne dépend pas de
/// l'ordre de lecture.</item>
/// </list>
/// <para>Écarts voulus : <c>best_copy</c> plaçait la taille exacte avant le rangement, ce qui
/// rendait le seuil de 3 % sans effet (§3.5) ; il est désormais effectif. L'écran fichier/fichier
/// faisait passer le format cible et le dossier de l'artiste avant le débit : un 128 kbps au bon
/// format pouvait évincer un 320 kbps. Identique au client (organize-rules.ts, <c>bestCopyIndex</c>).</para>
/// </remarks>
public static class BestCopy
{
    /// <summary>DUPLICATE_SIZE_RATIO : en deçà de cet écart, deux exemplaires sont de qualité équivalente.</summary>
    public const double SizeRatio = 0.97;

    /// <summary>Indice de l'exemplaire à garder.</summary>
    public static int Choose(IReadOnlyList<CopyCandidate> group, string? targetFormat = null, string? diversRoot = null)
    {
        if (group.Count == 0) return -1;
        var largest = group.Max(c => c.Size);
        var target = (targetFormat ?? "").TrimStart('.').ToUpperInvariant();
        var best = 0;
        var bestRank = Rank(group[0], largest, target, diversRoot);
        for (var i = 1; i < group.Count; i++)
        {
            var rank = Rank(group[i], largest, target, diversRoot);
            if (rank.CompareTo(bestRank) < 0)
            {
                best = i;
                bestRank = rank;
            }
        }
        return best;
    }

    private static CopyRank Rank(CopyCandidate c, long largest, string target, string? diversRoot)
    {
        var path = c.Path ?? "";
        var format = (string.IsNullOrEmpty(c.Format) ? PyPath.SplitExt(path).Ext : c.Format).TrimStart('.').ToUpperInvariant();
        var inDivers = diversRoot is not null
            ? OrganizeText.IsInside(path, diversRoot)
            : PyPath.Dirname(path).Split('\\', '/').Any(p => p.Equals(OrganizePlanner.DiversFolder, StringComparison.OrdinalIgnoreCase));
        return new CopyRank(
            -c.Bitrate,
            c.Size >= SizeRatio * largest ? 0 : 1,
            target.Length > 0 && format == target ? 0 : 1,
            InArtistFolder(path, c.Artist) ? 0 : 1,
            inDivers ? 1 : 0,
            OrganizeText.Depth(path),
            -c.Size,
            path.Length,
            PyText.CaseFold(path));
    }

    /// <summary>
    /// <c>_in_artist_folder</c> : un dossier parent porte EXACTEMENT le nom de l'artiste (casse et
    /// ponctuation ignorées) — « Shakira - Addicted To You 2012 » n'est pas le dossier de Shakira.
    /// </summary>
    public static bool InArtistFolder(string path, string? artist)
    {
        var target = OrganizeText.AsciiNorm(artist);
        if (target.Length == 0) return false;
        return PyPath.Dirname(path.Replace('\\', '/')).Split('\\', '/').Any(part => OrganizeText.AsciiNorm(part) == target);
    }

    private readonly record struct CopyRank(int Bitrate, int SizeClass, int Format, int ArtistFolder, int Divers, int Depth, long Size, int Length, string Folded)
        : IComparable<CopyRank>
    {
        public int CompareTo(CopyRank o)
        {
            int c;
            if ((c = Bitrate.CompareTo(o.Bitrate)) != 0) return c;
            if ((c = SizeClass.CompareTo(o.SizeClass)) != 0) return c;
            if ((c = Format.CompareTo(o.Format)) != 0) return c;
            if ((c = ArtistFolder.CompareTo(o.ArtistFolder)) != 0) return c;
            if ((c = Divers.CompareTo(o.Divers)) != 0) return c;
            if ((c = Depth.CompareTo(o.Depth)) != 0) return c;
            if ((c = Size.CompareTo(o.Size)) != 0) return c;
            if ((c = Length.CompareTo(o.Length)) != 0) return c;
            return string.CompareOrdinal(Folded, o.Folded);
        }
    }
}

/// <summary>
/// Rapprochement approximatif des fichiers (<c>dedup_dialog.duplicate_groups</c>) : trois clés
/// par fichier — nom de fichier, couple artiste/titre, artiste principal/titre — et union-find
/// (transitivité : A~B par le nom, B~C par les tags réunit les trois).
/// </summary>
public static class DuplicateFinder
{
    /// <summary><c>_keys</c> : clés de rapprochement d'un fichier.</summary>
    public static IReadOnlyCollection<string> Keys(OrganizeEntry entry, Func<OrganizeEntry, string>? artistKey)
    {
        // Ensemble Python : l'ordre d'itération n'influe pas sur les classes obtenues.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fromName = OrganizeText.DedupCanonical(PyPath.Stem(entry.Path));
        if (fromName.Length > 0) keys.Add("f:" + fromName);
        var title = OrganizeText.DedupCanonical(entry.Title);
        var artist = OrganizeText.DedupCanonical(entry.Artist);
        if (title.Length > 0 && artist.Length > 0) keys.Add("m:" + artist + "|" + title);
        if (title.Length > 0 && artistKey is not null)
        {
            var main = artistKey(entry);
            if (main.Length > 0) keys.Add("a:" + main + "|" + title);
        }
        return keys;
    }

    /// <summary>Groupes d'au moins deux fichiers, dans l'ordre de leur représentant.</summary>
    public static IReadOnlyList<IReadOnlyList<OrganizeEntry>> Groups(IReadOnlyList<OrganizeEntry> entries, Func<OrganizeEntry, string>? artistKey)
    {
        var parent = Enumerable.Range(0, entries.Count).ToArray();

        int Find(int x)
        {
            var root = x;
            while (parent[root] != root) root = parent[root];
            while (parent[x] != root) (parent[x], x) = (root, parent[x]);
            return root;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        var firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            foreach (var key in Keys(entries[i], artistKey))
            {
                if (firstSeen.TryGetValue(key, out var first)) Union(first, i);
                else firstSeen[key] = i;
            }
        }

        // groups.setdefault(find(i), []) : ordre de première apparition du représentant.
        var groups = new Dictionary<int, List<OrganizeEntry>>();
        var order = new List<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                groups[root] = list = [];
                order.Add(root);
            }
            list.Add(entries[i]);
        }
        return order.Select(r => groups[r]).Where(g => g.Count >= 2).Select(g => (IReadOnlyList<OrganizeEntry>)g).ToList();
    }
}
