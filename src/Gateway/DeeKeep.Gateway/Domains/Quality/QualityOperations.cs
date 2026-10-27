using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Quality;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.Subscriptions;

namespace DeeKeep.Gateway.Domains.Quality;

/// <summary>Seuils du volet « Seuils » et case « Détecter les craquements » (valeurs bornées par le service).</summary>
public sealed record QualityThresholdsInput(
    double HissDb, double CracklePct, double ClippingPct, double GapDensityPct, double DurationExcess, double DurationGap,
    double TailSeconds, double HeadSeconds, bool Deep)
{
    public QualityThresholds ToContract() => new(HissDb, CracklePct, ClippingPct, GapDensityPct, DurationExcess, DurationGap, TailSeconds, HeadSeconds, Deep);
}

/// <summary>Ligne de l'écran Qualité : verdict recalculé avec les seuils courants, textes des cellules et infobulles.</summary>
public sealed record QualityRow(
    long Uid, string Path, string? Title, string? Artist, string? Album, string? Format, int? Bitrate, double? Duration,
    double? ReferenceDuration, string ReferenceState, int Score, IReadOnlyList<DefectInfo> Defects,
    JsonNode? Measures, JsonNode Cells, JsonNode Tooltips, JsonNode? Tags, string? Error, bool OnDisk, double UpdatedAt,
    string? Status, string? StatusDetail, Guid? RedownloadJobId)
{
    public static QualityRow From(QualityEntry e) => new(
        e.Uid, e.Path, e.Title, e.Artist, e.Album, e.Format ?? e.Ext?.ToUpperInvariant(), e.Bitrate, e.Duration, e.ReferenceDuration,
        e.ReferenceState, e.Score, e.Defects, e.Measures, Dict(e.Cells)!, Dict(e.Tooltips)!, Dict(e.Tags), e.Error, e.OnDisk, e.UpdatedAt,
        e.Status, e.StatusDetail, e.RedownloadJobId);

    private static JsonObject? Dict(IReadOnlyDictionary<string, string>? d)
    {
        if (d is null) return null;
        var o = new JsonObject();
        foreach (var (k, v) in d) o[k] = v;
        return o;
    }
}

public sealed record QualityRowsPage(
    IReadOnlyList<QualityRow> Items, int Total, int Flagged, int Errors, int ReferencesPending, string? Folder, QualityThresholds? Thresholds);

public sealed record QualityRedownloadOutcome(IReadOnlyList<JobDto> Jobs, int Requested, int Skipped, IReadOnlyList<string> SkippedPaths);

/// <summary>Mise à jour poussée par le service : lignes ajoutées ou modifiées, ou motif d'un rechargement complet.</summary>
public sealed record QualityUpdate(string Reason, IReadOnlyList<QualityRow> Entries, IReadOnlyList<string>? Removed);

[QueryType]
public static partial class QualityQueries
{
    /// <summary>
    /// Lignes de l'écran Qualité. <c>defects</c> : défauts cochés (absent = tous) ; <c>includeClean</c> : « Titres sans défaut ».
    /// Un fichier illisible est toujours rendu. Le verdict est recalculé à chaque lecture avec les seuils courants.
    /// </summary>
    public static async Task<QualityRowsPage> GetQualityEntries([Service] QualityClient client, IReadOnlyList<string>? defects, bool? includeClean,
        int? offset, int? limit, string? folder, CancellationToken ct)
    {
        var page = await client.QueryAsync(new QualityQuery(folder, defects, includeClean ?? false, offset ?? 0, limit ?? 5000), ct);
        return new QualityRowsPage(page.Items.Select(QualityRow.From).ToList(), page.Total, page.Flagged, page.Errors, page.ReferencesPending,
            page.Folder, page.Thresholds);
    }

    /// <summary>Seuils de détection courants (clés <c>qual_*</c> de app_settings).</summary>
    public static Task<QualityThresholds> GetQualityThresholds([Service] QualityClient client, CancellationToken ct) => client.GetThresholdsAsync(ct);
}

[MutationType]
public static partial class QualityMutations
{
    /// <summary>Modifie les seuils : aucun fichier n'est relu, le verdict est recalculé.</summary>
    public static Task<QualityThresholds> UpdateQualityThresholds([Service] QualityClient client, QualityThresholdsInput thresholds, CancellationToken ct) =>
        client.SetThresholdsAsync(thresholds.ToContract(), ct);

    /// <summary>« Analyser » : tâche <c>quality.scan</c> (approfondie par défaut selon la case « Détecter les craquements »).</summary>
    public static Task<JobDto> StartQualityScan([Service] QualityClient client, string? folder, bool? deep, bool? force, CancellationToken ct) =>
        client.StartScanAsync(new QualityScanRequest(folder, deep, force ?? false), ct);

    /// <summary>Réanalyse de fichiers précis (chemins en base).</summary>
    public static Task<JobDto> Reanalyze([Service] QualityClient client, IReadOnlyList<string> paths, CancellationToken ct) =>
        client.ReanalyzeAsync(paths, ct);

    /// <summary>
    /// « Retélécharger la sélection » : les titres illisibles ou sans artiste/titre sont écartés ; le fichier remplace
    /// l'ancien, puis est réanalysé automatiquement.
    /// </summary>
    public static async Task<QualityRedownloadOutcome> RedownloadQuality([Service] QualityClient client, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var r = await client.RedownloadAsync(paths, ct);
        return new QualityRedownloadOutcome(r.Jobs, r.Requested, r.Skipped, r.SkippedPaths);
    }

    /// <summary>« Modifier le dossier » : vide la liste et arrête l'analyse en cours si le dossier change.</summary>
    public static Task<string> SetQualityFolder([Service] QualityClient client, string folder, CancellationToken ct) => client.SetFolderAsync(folder, ct);

    /// <summary>« Vider la liste » (refusé pendant une analyse ou un retéléchargement) : les mesures restent en base.</summary>
    public static Task<int> ClearQuality([Service] QualityClient client, CancellationToken ct) => client.ClearAsync(ct);

    /// <summary>Importe le <c>quality_cache.json</c> de l'application desktop.</summary>
    public static Task<JobDto> ImportQualityCache([Service] QualityClient client, CancellationToken ct) => client.ImportDesktopCacheAsync(ct);
}

[SubscriptionType]
public static partial class QualitySubscriptions
{
    public static async IAsyncEnumerable<ServiceEvent> SubscribeToQualityUpdated([Service] ITopicEventReceiver receiver, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<ServiceEvent>(Topics.For(EventTypes.QualityUpdated), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct)) yield return evt;
    }

    /// <summary>Lignes ajoutées ou modifiées (analyse, références, réanalyse, remplacement) ou rechargement complet (<c>cleared</c>, <c>thresholds</c>, <c>folder</c>).</summary>
    [Subscribe(With = nameof(SubscribeToQualityUpdated))]
    public static QualityUpdate QualityUpdated([EventMessage] ServiceEvent evt)
    {
        var data = evt.Data?.Deserialize<QualityUpdatedEvent>(JsonDefaults.Options) ?? new QualityUpdatedEvent("unknown", []);
        return new QualityUpdate(data.Reason, data.Entries.Select(QualityRow.From).ToList(), data.Removed);
    }
}
