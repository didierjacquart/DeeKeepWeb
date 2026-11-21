using System.Text;

namespace DeeKeep.Storage.UsbSync;

/// <summary>Fichier vu d'un côté (bibliothèque ou disque USB), chemin relatif avec « / ».</summary>
public sealed record SyncFile(string Relative, long Size, DateTime ModifiedUtc);

/// <summary>Suppression faite dans l'application, à propager à l'autre côté.</summary>
public sealed record SyncTombstone(string Relative, DateTime DeletedUtc);

public enum SyncActionKind
{
    /// <summary>Présent seulement dans la bibliothèque : copie vers le disque USB.</summary>
    CopyToUsb,

    /// <summary>Présent seulement sur le disque USB : copie vers la bibliothèque.</summary>
    CopyToLibrary,

    /// <summary>Plus récent dans la bibliothèque : la copie du disque part à sa corbeille, puis est remplacée.</summary>
    ReplaceOnUsb,

    /// <summary>Plus récent sur le disque : la copie de la bibliothèque part à sa corbeille, puis est remplacée.</summary>
    ReplaceOnLibrary,

    /// <summary>Supprimé depuis l'application : la copie restante du disque part à sa corbeille.</summary>
    TrashOnUsb,

    /// <summary>Supprimé depuis l'application : la copie restante de la bibliothèque part à sa corbeille.</summary>
    TrashOnLibrary,

    /// <summary>La suppression est faite des deux côtés, ou un fichier plus récent l'a remplacée : on l'oublie.</summary>
    ForgetTombstone,
}

public sealed record SyncAction(SyncActionKind Kind, string Relative, long Bytes);

/// <summary>
/// Paire laissée identique par un passage précédent (taille et date de chaque côté telles
/// qu'observées après la copie) : tant qu'aucun côté n'a bougé, les deux sont le même fichier,
/// même si un système de fichiers a arrondi ou réécrit la date de la copie.
/// </summary>
public sealed record SyncedPair(string Relative, long LibrarySize, DateTime LibraryModifiedUtc, long UsbSize, DateTime UsbModifiedUtc)
{
    public bool Matches(SyncFile library, SyncFile usb) =>
        library.Size == LibrarySize && usb.Size == UsbSize &&
        (library.ModifiedUtc - LibraryModifiedUtc).Duration() <= TimeSpan.FromSeconds(2) &&
        (usb.ModifiedUtc - UsbModifiedUtc).Duration() <= TimeSpan.FromSeconds(2);
}

/// <summary>
/// Règles de la synchronisation entre la bibliothèque (NAS) et le disque USB.
/// <list type="bullet">
/// <item>Incrémentale : l'union des deux côtés. Un fichier absent d'un côté y est copié,
/// même s'il a été effacé à la main : seule une suppression faite dans l'application
/// (pierre tombale) se propage.</item>
/// <item>Les deux côtés diffèrent : le plus récent l'emporte ; l'ancien part à la corbeille.</item>
/// <item>Une pierre tombale ne vaut que pour les copies antérieures à la suppression : un
/// fichier écrit après (même nom, retéléchargé) est un nouveau fichier.</item>
/// </list>
/// </summary>
public static class UsbSyncPlanner
{
    /// <summary>Écart toléré entre dates : FAT les arrondit à 2 s.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Clé de rapprochement : FAT et SMB ignorent la casse ; les accents peuvent être
    /// composés ou décomposés selon l'outil qui a écrit le nom.
    /// </summary>
    public static string Key(string relative) =>
        relative.Replace('\\', '/').Trim('/').Normalize(NormalizationForm.FormC).ToLowerInvariant();

    /// <summary>
    /// Même contenu présumé : même taille et même date, à l'arrondi FAT près, ou décalées
    /// d'une heure pile (FAT stocke l'heure locale : un passage à l'heure d'été la décale).
    /// </summary>
    public static bool Same(SyncFile a, SyncFile b)
    {
        if (a.Size != b.Size) return false;
        var delta = (a.ModifiedUtc - b.ModifiedUtc).Duration();
        return delta <= Tolerance || (delta - TimeSpan.FromHours(1)).Duration() <= Tolerance;
    }

    public static IReadOnlyList<SyncAction> Plan(
        IReadOnlyCollection<SyncFile> library, IReadOnlyCollection<SyncFile> usb, IReadOnlyCollection<SyncTombstone> tombstones,
        IReadOnlyCollection<SyncedPair>? synced = null)
    {
        var known = new Dictionary<string, SyncedPair>(StringComparer.Ordinal);
        foreach (var p in synced ?? []) known[Key(p.Relative)] = p;
        var lib = ByKey(library);
        var dev = ByKey(usb);
        var dead = new Dictionary<string, SyncTombstone>(StringComparer.Ordinal);
        foreach (var t in tombstones) dead[Key(t.Relative)] = t;

        var actions = new List<SyncAction>();
        foreach (var key in lib.Keys.Union(dev.Keys).Union(dead.Keys).Order(StringComparer.Ordinal))
        {
            lib.TryGetValue(key, out var l);
            dev.TryGetValue(key, out var u);
            if (dead.TryGetValue(key, out var tomb))
            {
                // Une copie écrite après la suppression est un nouveau fichier : la pierre tombale tombe.
                var newer = (l is not null && l.ModifiedUtc > tomb.DeletedUtc + Tolerance) ||
                            (u is not null && u.ModifiedUtc > tomb.DeletedUtc + Tolerance);
                if (!newer)
                {
                    if (l is not null) actions.Add(new SyncAction(SyncActionKind.TrashOnLibrary, l.Relative, l.Size));
                    if (u is not null) actions.Add(new SyncAction(SyncActionKind.TrashOnUsb, u.Relative, u.Size));
                    // Quand plus rien ne reste, la suppression est faite : rien à retenir.
                    if (l is null && u is null) actions.Add(new SyncAction(SyncActionKind.ForgetTombstone, tomb.Relative, 0));
                    continue;
                }
                actions.Add(new SyncAction(SyncActionKind.ForgetTombstone, tomb.Relative, 0));
            }

            if (l is not null && u is null) actions.Add(new SyncAction(SyncActionKind.CopyToUsb, l.Relative, l.Size));
            else if (l is null && u is not null) actions.Add(new SyncAction(SyncActionKind.CopyToLibrary, u.Relative, u.Size));
            else if (l is not null && u is not null && !Same(l, u) && !(known.TryGetValue(key, out var pair) && pair.Matches(l, u)))
            {
                // Le plus récent l'emporte ; à date égale (taille différente), la bibliothèque fait foi.
                actions.Add(u.ModifiedUtc > l.ModifiedUtc + Tolerance
                    ? new SyncAction(SyncActionKind.ReplaceOnLibrary, u.Relative, u.Size)
                    : new SyncAction(SyncActionKind.ReplaceOnUsb, l.Relative, l.Size));
            }
        }
        return actions;
    }

    private static Dictionary<string, SyncFile> ByKey(IEnumerable<SyncFile> files)
    {
        var map = new Dictionary<string, SyncFile>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            // Deux noms ne différant que par la casse (possible sur un NAS) : le plus récent représente la clé.
            var key = Key(f.Relative);
            if (!map.TryGetValue(key, out var existing) || f.ModifiedUtc > existing.ModifiedUtc) map[key] = f;
        }
        return map;
    }
}
