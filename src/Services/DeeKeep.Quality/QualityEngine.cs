using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Data;

namespace DeeKeep.Quality;

/// <summary>
/// Cœur du service : seuils (réglages <c>qual_*</c>), construction des lignes de l'écran
/// (verdict recalculé à la lecture), file des recherches de référence, publication des
/// mises à jour.
/// </summary>
public sealed class QualityEngine(QualityStore store, PathMap paths, JobStore jobs, EventLog events, ILogger<QualityEngine> logger)
{
    // ------------------------------------------------------------------ seuils

    /// <summary>Clés <c>app_settings</c> des seuils, dans l'ordre du volet « Seuils ».</summary>
    public static readonly IReadOnlyList<string> ThresholdKeys =
        ["hiss_db", "crackle_pct", "clipping_pct", "gap_density_pct", "tail_seconds", "head_seconds", "duration_gap", "duration_excess"];

    /// <summary><c>sync_from_settings</c> : seuils lus dans <c>app_settings</c>, défauts de l'original sinon.</summary>
    public QualityThresholds Thresholds() => ThresholdsFrom(store.Settings());

    public static QualityThresholds ThresholdsFrom(IReadOnlyDictionary<string, JsonNode?> settings)
    {
        var d = QualityLimits.Default;
        double Num(string key, double fallback) =>
            settings.TryGetValue("qual_" + key, out var node) && node is JsonValue v && v.TryGetValue<double>(out var x) && double.IsFinite(x) ? x : fallback;
        var deep = !(settings.TryGetValue("qual_deep", out var deepNode) && deepNode is JsonValue dv && dv.TryGetValue<bool>(out var b) && !b);
        return new QualityThresholds(
            Num("hiss_db", d.HissDb), Num("crackle_pct", d.CracklePct), Num("clipping_pct", d.ClippingPct),
            Num("gap_density_pct", d.GapDensityPct), Num("duration_excess", d.DurationExcess), Num("duration_gap", d.DurationGap),
            Num("tail_seconds", d.TailSeconds), Num("head_seconds", d.HeadSeconds), deep);
    }

    public static QualityLimits Limits(QualityThresholds t) => new()
    {
        HissDb = t.HissDb, CracklePct = t.CracklePct, ClippingPct = t.ClippingPct, GapDensityPct = t.GapDensityPct,
        TailSeconds = t.TailSeconds, HeadSeconds = t.HeadSeconds, DurationExcess = t.DurationExcess, DurationGap = t.DurationGap,
    };

    /// <summary>Valeurs <c>app_settings</c> à écrire (tous les réglages à la fois, comme l'original).</summary>
    public static Dictionary<string, JsonNode?> ToSettings(QualityThresholds t) => new()
    {
        ["qual_deep"] = JsonValue.Create(t.Deep),
        ["qual_hiss_db"] = JsonValue.Create(t.HissDb),
        ["qual_crackle_pct"] = JsonValue.Create(t.CracklePct),
        ["qual_clipping_pct"] = JsonValue.Create(t.ClippingPct),
        ["qual_gap_density_pct"] = JsonValue.Create(t.GapDensityPct),
        ["qual_tail_seconds"] = JsonValue.Create(t.TailSeconds),
        ["qual_head_seconds"] = JsonValue.Create(t.HeadSeconds),
        ["qual_duration_gap"] = JsonValue.Create(t.DurationGap),
        ["qual_duration_excess"] = JsonValue.Create(t.DurationExcess),
    };

    /// <summary>Bornes du volet « Seuils » (min, max) : une valeur hors bornes est ramenée dedans.</summary>
    public static QualityThresholds Clamp(QualityThresholds t) => t with
    {
        HissDb = Math.Clamp(t.HissDb, -90.0, -30.0),
        CracklePct = Math.Clamp(t.CracklePct, 0.0005, 1.0),
        ClippingPct = Math.Clamp(t.ClippingPct, 0.005, 5.0),
        GapDensityPct = Math.Clamp(t.GapDensityPct, 5.0, 90.0),
        TailSeconds = Math.Clamp(t.TailSeconds, 1.0, 60.0),
        HeadSeconds = Math.Clamp(t.HeadSeconds, 0.5, 30.0),
        DurationGap = Math.Clamp(t.DurationGap, 2.0, 120.0),
        DurationExcess = Math.Clamp(t.DurationExcess, 1.0, 60.0),
    };

    // ------------------------------------------------------------------ dossier

    /// <summary>Dossier de l'écran (chemin local) : celui choisi, sinon le dossier de dépôt (<c>download_dir</c>).</summary>
    public string CurrentFolder()
    {
        var folder = store.Folder;
        if (!string.IsNullOrWhiteSpace(folder)) return folder;
        var settings = store.Settings();
        var download = settings.TryGetValue("download_dir", out var n) && n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return string.IsNullOrWhiteSpace(download) ? "" : paths.ToLocal(download);
    }

    // ------------------------------------------------------------------ lignes

    /// <summary>
    /// Ligne lue sur le disque (<c>build_entry</c>) : tags complets, débit, format ; la référence
    /// est reprise du cache de durées si le titre y est déjà, sinon laissée « à chercher ».
    /// </summary>
    public EntryRow ReadEntry(string localPath, string dbPath, QualityMeasures measures, EntryRow? previous) =>
        BuildRow(ReadTags(localPath), localPath, dbPath, measures, previous);

    /// <summary>Tags et caractéristiques (débit, durée), sans la jaquette.</summary>
    public static AudioTags ReadTags(string localPath) => AudioTags.Read(localPath, withProperties: true, withCover: false);

    public EntryRow BuildRow(AudioTags tags, string localPath, string dbPath, QualityMeasures measures, EntryRow? previous)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "title", "artist", "album", "albumartist", "track", "tracktotal", "disc", "disctotal", "date", "genre", "composer", "publisher", "comment", "compilation" })
        {
            var value = tags.Get(key);
            if (!string.IsNullOrEmpty(value)) map[key] = value;
        }
        var row = new EntryRow
        {
            Path = dbPath,
            MatchKey = LibraryText.MatchKey(dbPath),
            Title = tags.Title,
            Artist = tags.Artist,
            Album = tags.Album,
            Bitrate = tags.Bitrate,
            Format = PyPath.SplitExt(localPath).Ext.ToLowerInvariant().TrimStart('.').ToUpperInvariant(),
            Tags = JsonSerializer.Serialize(map),
            Status = previous?.Status,
            StatusDetail = previous?.StatusDetail,
            RedownloadJob = previous?.RedownloadJob,
        };
        ResolveReference(row, measures, previous);
        return row;
    }

    /// <summary>
    /// État de référence d'une ligne. Les durées connues sont gardées tant que l'artiste et le
    /// titre ne changent pas — la plus proche est recalculée à chaque lecture, si bien qu'un
    /// fichier remplacé par une version de durée différente est jugé sur la bonne référence.
    /// </summary>
    public void ResolveReference(EntryRow row, QualityMeasures measures, EntryRow? previous)
    {
        var key = References.Key(row.Artist, row.Title);
        if (measures.HasError || measures.Duration == 0 || PyText.Strip(row.Artist).Length == 0 || PyText.Strip(row.Title).Length == 0)
        {
            // Rien à chercher : ni durée ni blancs ne seront mis en cause (« sans référence »).
            row.RefState = QualityReferenceStates.None;
            row.RefKey = key;
            row.RefDurations = null;
            return;
        }
        if (previous is not null && previous.RefKey == key && previous.RefState is QualityReferenceStates.Value or QualityReferenceStates.None)
        {
            row.RefState = previous.RefState;
            row.RefKey = key;
            row.RefDurations = previous.RefDurations;
            return;
        }
        row.RefKey = key;
        row.RefState = QualityReferenceStates.Pending;
        row.RefDurations = null;
        var known = store.ReferenceDurations([key]);
        if (known.TryGetValue(key, out var hit) && References.IsFresh(hit.Durations, hit.FetchedAt, DeeKeep.Common.Data.Clock.UnixNow()))
        {
            row.RefState = hit.Durations.Count > 0 ? QualityReferenceStates.Value : QualityReferenceStates.None;
            row.RefDurations = JsonSerializer.Serialize(hit.Durations);
        }
    }

    /// <summary>
    /// DTO d'une ligne, verdict calculé avec les seuils courants. En analyse rapide demandée
    /// (case « Détecter les craquements » décochée), les craquements ne comptent pas dans la note.
    /// </summary>
    public QualityEntry ToDto(EntryRow row, StoredMeasure? stored, QualityThresholds thresholds)
    {
        var measures = stored?.Measures ?? QualityMeasures.Empty(thresholds.Deep, "Mesure absente : relancez l'analyse");
        var limits = Limits(thresholds);
        var reference = row.RefState == QualityReferenceStates.Value ? References.Closest(row.Durations, measures.Duration) : null;
        var refState = row.RefState == QualityReferenceStates.Value && reference is null ? QualityReferenceStates.None : row.RefState;
        var verdict = QualityVerdict.Evaluate(measures, limits, reference, includeCrackle: thresholds.Deep);
        var (cells, tips) = QualityPresenter.Present(measures, verdict, limits, refState, reference, row.Format, row.Bitrate, row.Status, row.StatusDetail);
        var defects = verdict.Defects.Select(d => new DefectInfo(d, Defects.Labels[d], verdict.Severities[d], QualityVerdict.Describe(measures, d))).ToList();
        return new QualityEntry(
            row.Path, row.Title, row.Artist, row.Album, row.Format.ToLowerInvariant(), row.Bitrate,
            measures.HasError ? null : measures.Duration, reference, refState == QualityReferenceStates.Value,
            measures.HasError ? 0 : verdict.Score, defects, measures.ToJson(), cells, tips,
            measures.HasError ? measures.Error : null, File.Exists(paths.ToLocal(row.Path)), stored?.UpdatedAt ?? row.UpdatedAt,
            row.Format, row.Uid, refState, row.Status, row.StatusDetail,
            Guid.TryParse(row.RedownloadJob, out var job) ? job : null, row.TagMap);
    }

    public IReadOnlyList<QualityEntry> ToDtos(IEnumerable<EntryRow> rows)
    {
        var measures = store.Measures();
        var thresholds = Thresholds();
        return rows.Select(r => ToDto(r, measures.GetValueOrDefault(r.MatchKey), thresholds)).ToList();
    }

    /// <summary>Requête de l'écran : filtres de défauts (un illisible est toujours rendu), « Titres sans défaut ».</summary>
    public QualityPage Query(QualityQuery query)
    {
        var thresholds = Thresholds();
        var measures = store.Measures();
        var rows = query.Paths is { Count: > 0 } wanted ? store.Entries(wanted) : store.Entries();
        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            var prefix = LibraryText.MatchKey(paths.ToDb(query.Folder)).TrimEnd('\\') + "\\";
            rows = rows.Where(r => r.MatchKey.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
        var all = rows.Select(r => ToDto(r, measures.GetValueOrDefault(r.MatchKey), thresholds)).ToList();
        var visible = query.Defects is null ? null : new HashSet<string>(query.Defects, StringComparer.Ordinal);
        var kept = all.Where(e =>
            e.Error is not null
            || (visible is null ? e.Defects.Count > 0 : e.Defects.Any(d => visible.Contains(d.Id)))
            || (query.IncludeClean && e.Defects.Count == 0)).ToList();
        var items = kept.Skip(Math.Max(0, query.Offset)).Take(Math.Clamp(query.Limit, 1, 20000)).ToList();
        return new QualityPage(items, all.Count, all.Count(e => e.Defects.Count > 0), all.Count(e => e.Error is not null),
            all.Count(e => e.ReferenceState == QualityReferenceStates.Pending || e.ReferenceState == QualityReferenceStates.Network),
            CurrentFolder(), thresholds);
    }

    // ------------------------------------------------------------------ publication

    public void Publish(string reason, IReadOnlyList<QualityEntry> entries, IReadOnlyList<string>? removed = null) =>
        events.Publish(EventTypes.QualityUpdated, new QualityUpdatedEvent(reason, entries, removed));

    public void PublishRows(string reason, IEnumerable<EntryRow> rows) => Publish(reason, ToDtos(rows));

    // ------------------------------------------------------------------ tâches

    /// <summary>Recherche des références en attente : une seule tâche à la fois, qui vide la file puis s'arrête.</summary>
    public JobDto? EnsureReferenceJob()
    {
        var active = jobs.List(activeOnly: true, type: QualityJobTypes.References);
        if (active.Count > 0) return active[0];
        if (store.CountReferencesPending() == 0) return null;
        logger.LogDebug("Recherche des durées de référence en file");
        return jobs.Enqueue(new EnqueueJobRequest(QualityJobTypes.References, "Durées de référence (Deezer, MusicBrainz)",
            new JsonObject(), Priority: -1, MaxAttempts: 20));
    }

    /// <summary>Format d'un nombre pour un message (point décimal).</summary>
    public static string Inv(double value) => value.ToString(CultureInfo.InvariantCulture);
}
