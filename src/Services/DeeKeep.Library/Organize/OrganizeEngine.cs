using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;
using DeeKeep.Contracts.Organize;
using DeeKeep.Library.Files;
using DeeKeep.Library.Rules;
using DeeKeep.Library.Settings;

namespace DeeKeep.Library.Organize;

/// <summary>
/// Services de l'écran Organiser : options (clés <c>org_*</c>), calcul du plan sur les
/// entrées gardées en mémoire, mise en forme des libellés de l'original, aperçu du motif.
/// </summary>
public sealed class OrganizeEngine(OrganizeStore store, SettingsStore settings, LibraryPaths paths, ILogger<OrganizeEngine> logger)
{
    // Fichiers audio sous chaque dossier d'artiste, par analyse : la règle « Divers » les
    // consulte à chaque recalcul du plan, le disque n'est parcouru qu'une fois.
    private readonly ConcurrentDictionary<(Guid, string), IReadOnlyList<string>> _folderFiles = new();

    // ------------------------------------------------------------------ options

    public OrganizeOptions LoadOptions()
    {
        var all = settings.GetAll();
        var rules = LibraryRules.FromSettings(all);
        return new OrganizeOptions(
            Artist: rules.Artist,
            Album: rules.Album,
            Rename: rules.Rename,
            Pattern: NamePattern.Modernize(rules.Pattern),
            Divers: rules.Divers,
            DiversMin: rules.DiversMin,
            KeepAlbums: Bool(all, "org_keep_albums", true),
            Dedup: Bool(all, "org_dedup", true),
            RemoveEmpty: Bool(all, "org_remove_empty", true),
            ReportUntagged: Bool(all, "org_report_untagged", true),
            FiltersOpen: Bool(all, "org_filters_open", false));
    }

    public OrganizeOptions SaveOptions(OrganizeOptions options)
    {
        settings.Set(new Dictionary<string, JsonNode?>
        {
            ["org_artist"] = options.Artist,
            ["org_album"] = options.Album,
            ["org_rename"] = options.Rename,
            ["org_pattern"] = string.IsNullOrEmpty(options.Pattern) ? NamePattern.DefaultPattern : options.Pattern,
            ["org_divers"] = options.Divers,
            ["org_divers_min"] = Math.Clamp(options.DiversMin, 2, 10),
            ["org_keep_albums"] = options.KeepAlbums,
            ["org_dedup"] = options.Dedup,
            ["org_remove_empty"] = options.RemoveEmpty,
            ["org_report_untagged"] = options.ReportUntagged,
            ["org_filters_open"] = options.FiltersOpen,
        });
        return LoadOptions();
    }

    public string TargetFormat() => settings.GetString("file_format", "MP3");

    public string DefaultRoot()
    {
        try { return paths.LocalDownloadDir(); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return ""; }
    }

    private static bool Bool(IReadOnlyDictionary<string, JsonNode?> all, string key, bool fallback) =>
        all.TryGetValue(key, out var node) && node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    // ------------------------------------------------------------------ plan

    public PlanEnvironment Environment(Guid sessionId) => new()
    {
        Quality = e => Quality(sessionId, e),
        ArtistFolderCount = (root, folder, analyzed) => ArtistFolderCount(sessionId, root, folder, analyzed),
        TargetFormat = TargetFormat(),
    };

    public PlanOutcome Plan(OrganizeSessionDto session, OrganizeOptions options) =>
        OrganizePlanner.BuildPlan(store.Entries(session.Id), session.Root, options, Environment(session.Id));

    /// <summary>Plan mis en forme pour l'écran (libellés verbatim de la colonne « Action »).</summary>
    public OrganizePlanDto PlanDto(OrganizeSessionDto session, OrganizeOptions? requested, int offset, int limit)
    {
        var options = OrganizePlanner.Normalize(requested ?? LoadOptions());
        var outcome = Plan(session, options);
        var root = session.Root;
        var bySrc = outcome.Results.ToDictionary(r => OrganizeText.PathKey(r.Src), StringComparer.Ordinal);

        // Emplacement final de l'exemplaire gardé : sa destination s'il en a une (_final_path).
        string FinalPath(string path) =>
            bySrc.TryGetValue(OrganizeText.PathKey(path), out var r) && (r.Dst.Length > 0 || r.Src.Length > 0) ? (r.Dst.Length > 0 ? r.Dst : r.Src) : path;

        var rows = new List<OrganizePlanRow>();
        foreach (var r in outcome.Results.Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)))
        {
            string action;
            string? tooltip = null;
            string? destination = null;
            switch (r.Status)
            {
                case OrganizeStatuses.Move:
                    // Un simple renommage montre le nouveau nom seul : le chemin complet ne dirait pas ce qui change.
                    action = OrganizeText.SamePath(PyPath.Dirname(r.Src), PyPath.Dirname(r.Dst))
                        ? $"Renommer  →  {PyPath.Basename(r.Dst)}"
                        : OrganizeText.Relative(r.Dst, root);
                    tooltip = r.Dst;
                    destination = r.Dst;
                    break;
                case OrganizeStatuses.Duplicate:
                    var kept = FinalPath(r.Dst);
                    action = $"Doublon à supprimer  ·  exemplaire gardé : {OrganizeText.Relative(kept, root)}";
                    tooltip = $"Ce fichier sera supprimé du disque.\nExemplaire conservé :\n{kept}";
                    destination = kept;
                    break;
                case OrganizeStatuses.InPlace:
                    action = "Déjà en place";
                    destination = r.Dst;
                    break;
                case OrganizeStatuses.Album:
                    action = "Album conservé";
                    break;
                default:
                    action = options.ReportUntagged ? $"Tag manquant : {string.Join(", ", r.Missing)}" : "";
                    tooltip = options.ReportUntagged ? "Complétez les tags de ce fichier pour pouvoir le ranger." : null;
                    break;
            }
            rows.Add(new OrganizePlanRow(r.Entry.Index, r.Status, action, tooltip, destination, r.Missing,
                OrganizePlanner.DefaultChecked(r), OrganizePlanner.NeedsLookup(r)));
        }

        var albumFolders = outcome.ProtectedDirs
            .Select(d => new OrganizeAlbumFolder(OrganizeText.RelativeFolder(OrganizeText.Join(d, "x"), root), "Musique de film / compilation — laissée intacte"))
            .ToList();

        var s = outcome.Stats;
        return new OrganizePlanDto(session.Id, options, rows, outcome.Results.Count, s, StatusText(s), FiltersSummary(options), albumFolders,
            Preview(session.Id, options.Pattern, options.Rename), s.Move + s.Duplicate > 0);
    }

    /// <summary>Ligne d'état après calcul du plan (fragments joints par « · »).</summary>
    public static string StatusText(OrganizePlanStats s)
    {
        var parts = new List<string> { $"{s.Analyzed} fichiers analysés", $"{s.Move} à ranger", $"{s.InPlace} déjà en place" };
        if (s.Duplicate > 0) parts.Add($"{s.Duplicate} doublon(s) à supprimer");
        if (s.Divers > 0) parts.Add($"{s.Divers} vers « {OrganizePlanner.DiversFolder} »");
        if (s.FromDivers > 0) parts.Add($"{s.FromDivers} sortent de « {OrganizePlanner.DiversFolder} »");
        if (s.Album > 0) parts.Add($"{s.Album} dans {s.AlbumDirs} album(s) préservé(s)");
        if (s.Untagged > 0) parts.Add($"{s.Untagged} sans tags exploitables");
        return string.Join(" · ", parts);
    }

    /// <summary><c>_filters_summary</c> : énumération des filtres actifs, pour le volet replié.</summary>
    public static string FiltersSummary(OrganizeOptions o)
    {
        var parts = new List<string>();
        if (o.Artist) parts.Add("dossier par artiste");
        if (o.Album) parts.Add("dossier par album");
        if (o.Rename) parts.Add($"renommage « {o.Pattern} »");
        if (o.Divers && o.Artist) parts.Add($"« {OrganizePlanner.DiversFolder} » sous {o.DiversMin} titres");
        if (o.KeepAlbums) parts.Add("musiques de film et compilations préservées");
        if (o.Dedup) parts.Add("un seul exemplaire des doublons");
        if (o.RemoveEmpty) parts.Add("dossiers vides supprimés");
        if (o.ReportUntagged) parts.Add("fichiers sans tags signalés");
        return parts.Count > 0 ? string.Join(" · ", parts) : "aucun";
    }

    /// <summary><c>_refresh_preview</c> : aperçu du motif sur le premier fichier analysé.</summary>
    public OrganizePreview Preview(Guid? sessionId, string? pattern, bool rename)
    {
        if (!rename)
            return new OrganizePreview("Renommage désactivé : les fichiers gardent leur nom actuel. Cochez « Renommer les fichiers » pour appliquer ce modèle.", false, []);
        var p = string.IsNullOrEmpty(pattern) ? NamePattern.DefaultPattern : pattern;
        var unknown = NamePattern.UnknownTokens(p);
        if (unknown.Count > 0)
            return new OrganizePreview("Balise inconnue : " + string.Join(", ", unknown) + " — elle sera recopiée telle quelle.", true, unknown);
        var entry = sessionId is { } id ? store.Entries(id).FirstOrDefault() : null;
        if (entry is null)
            return new OrganizePreview("Aperçu disponible après l'analyse. Exemple de balises : %titre%, %artiste%, %album%, %piste_00%, %annee%.", false, []);
        var current = PyPath.Basename(entry.Path);
        var absent = NamePattern.MissingRequiredTags(p, entry.Get);
        if (absent.Count > 0)
            return new OrganizePreview($"Aperçu : {current}  →  inchangé ({string.Join(", ", absent)} absent des tags : fichier signalé)", false, []);
        var name = LibraryRules.SafeComponent(NamePattern.FormatName(p, entry.Get));
        var ext = PyPath.SplitExt(entry.Path).Ext;
        return new OrganizePreview($"Aperçu : {current}  →  {(name.Length > 0 ? name + ext : "(nom vide : fichier signalé)")}", false, []);
    }

    public static OrganizeNameTags NameTags() => new(
        NamePattern.TagCategories.Select(c => new OrganizeNameTagCategory(c.Name, c.Items.Select(i => new OrganizeNameTagItem(i.Label, i.Token)).ToList())).ToList(),
        NamePattern.Presets,
        NamePattern.DefaultPattern);

    // ------------------------------------------------------------------ disque

    /// <summary>
    /// <c>copy_quality</c> : (débit, taille), lus à la demande puis mémorisés dans l'analyse —
    /// le débit n'est pas lu pendant l'analyse (inutile au rangement, coûteux en MP3).
    /// </summary>
    public (int Bitrate, long Size) Quality(Guid sessionId, OrganizeEntry entry)
    {
        if (entry.Bitrate is null)
        {
            try
            {
                var info = new FileInfo(entry.Path);
                if (!info.Exists) return (0, 0);
                entry.Size = info.Length;
                entry.Bitrate = AudioTags.Read(entry.Path, withProperties: true, withCover: false).Bitrate;
                store.SaveBitrates(sessionId, [entry]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (0, 0);
            }
        }
        return (entry.Bitrate ?? 0, entry.Size);
    }

    /// <summary>
    /// <c>artist_folder_count</c>. Le dossier de l'artiste est cherché sans tenir compte de la
    /// casse (sous Linux, « madonna » existant doit compter pour « Madonna »).
    /// </summary>
    public int ArtistFolderCount(Guid sessionId, string root, string folder, IReadOnlySet<string> analyzed)
    {
        if (folder.Length == 0) return 0;
        var files = _folderFiles.GetOrAdd((sessionId, PyText.CaseFold(folder)), _ =>
        {
            var dir = FindChildDirectory(root, folder);
            if (dir is null) return [];
            try { return AudioExtensions.IterAudioFiles(dir).Select(OrganizeText.PathKey).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Dossier d'artiste illisible : {Dir}", dir);
                return [];
            }
        });
        return files.Count(f => !analyzed.Contains(f));
    }

    /// <summary>Oublie les décomptes de dossiers d'une analyse (nouvelle analyse, rangement).</summary>
    public void ForgetSession(Guid sessionId)
    {
        foreach (var key in _folderFiles.Keys.Where(k => k.Item1 == sessionId).ToList()) _folderFiles.TryRemove(key, out _);
    }

    /// <summary>Sous-dossier <paramref name="name"/> de <paramref name="parent"/>, casse ignorée (l'écriture exacte d'abord).</summary>
    public static string? FindChildDirectory(string parent, string name)
    {
        var exact = Path.Join(parent, name);
        if (Directory.Exists(exact)) return exact;
        if (!Directory.Exists(parent)) return null;
        try
        {
            var folded = PyText.CaseFold(name);
            return Directory.EnumerateDirectories(parent).FirstOrDefault(d => PyText.CaseFold(Path.GetFileName(d)) == folded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
