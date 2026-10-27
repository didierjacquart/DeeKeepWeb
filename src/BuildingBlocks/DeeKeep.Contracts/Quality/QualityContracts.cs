using System.Text.Json.Nodes;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Quality;

/// <summary>Seuils de l'écran Qualité (clés <c>qual_*</c> de app_settings).</summary>
public sealed record QualityThresholds(
    double HissDb,
    double CracklePct,
    double ClippingPct,
    double GapDensityPct,
    double DurationExcess,
    double DurationGap,
    double TailSeconds,
    double HeadSeconds,
    bool Deep);

public sealed record DefectInfo(string Id, string Label, double Severity, string? Detail);

/// <summary>Ligne de l'écran Qualité : mesures brutes, verdict recalculé avec les seuils courants, tags.</summary>
public sealed record QualityEntry(
    string Path,
    string? Title,
    string? Artist,
    string? Album,
    string? Ext,
    int? Bitrate,
    double? Duration,
    double? ReferenceDuration,
    bool ReferenceKnown,
    int Score,
    IReadOnlyList<DefectInfo> Defects,
    JsonNode? Measures,
    IReadOnlyDictionary<string, string> Cells,
    IReadOnlyDictionary<string, string> Tooltips,
    string? Error,
    bool OnDisk,
    double UpdatedAt,
    string? Format = null,
    long Uid = 0,
    string ReferenceState = QualityReferenceStates.Pending,
    string? Status = null,
    string? StatusDetail = null,
    Guid? RedownloadJobId = null,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>État de la durée de référence d'une ligne (« ? », valeur, rien, réseau indisponible).</summary>
public static class QualityReferenceStates
{
    /// <summary>Pas encore cherchée (« référence… »).</summary>
    public const string Pending = "pending";
    /// <summary>Durée connue : <see cref="QualityEntry.ReferenceDuration"/> est la plus proche de celle du fichier.</summary>
    public const string Value = "value";
    /// <summary>Ni Deezer ni MusicBrainz ne connaissent ce titre (ou ses tags ne permettent pas de le chercher).</summary>
    public const string None = "none";
    /// <summary>Recherche impossible faute de réseau : elle sera retentée.</summary>
    public const string Network = "network";
}

/// <summary>Statut de remplacement d'une ligne (colonne STATUT), stocké par le service.</summary>
public static class QualityStatuses
{
    /// <summary>Retéléchargement confié au service d'extraction (suivi par <see cref="QualityEntry.RedownloadJobId"/>).</summary>
    public const string Redownload = "redownload";
    public const string Reanalyzing = "reanalyzing";
    /// <summary>Remplacé puis réanalysé : « Remplacé ✓ défaut corrigé / persistant » selon le verdict courant.</summary>
    public const string Replaced = "replaced";
    /// <summary>Remplacé mais la réanalyse a échoué.</summary>
    public const string ReplacedUnreadable = "replaced_error";
}

public sealed record QualityScanRequest(string? Folder, bool? Deep = null, bool Force = false);

/// <summary>
/// Lignes de l'écran : <see cref="Defects"/> = défauts cochés (null : tous), <see cref="IncludeClean"/> = « Titres
/// sans défaut ». Un fichier illisible est toujours rendu. <see cref="Paths"/> restreint à quelques chemins.
/// </summary>
public sealed record QualityQuery(string? Folder = null, IReadOnlyList<string>? Defects = null, bool IncludeClean = false, int Offset = 0, int Limit = 5000,
    IReadOnlyList<string>? Paths = null);

public sealed record QualityPage(
    IReadOnlyList<QualityEntry> Items,
    int Total,
    int Flagged,
    int Errors = 0,
    int ReferencesPending = 0,
    string? Folder = null,
    QualityThresholds? Thresholds = null);

/// <summary>Retéléchargement de la sélection : tâches d'extraction créées, titres écartés faute de tags.</summary>
public sealed record QualityRedownloadResult(IReadOnlyList<JobDto> Jobs, int Requested, int Skipped, IReadOnlyList<string> SkippedPaths);

/// <summary>
/// Charge utile de l'événement <see cref="EventTypes.QualityUpdated"/> : lignes ajoutées ou
/// modifiées (verdict calculé avec les seuils courants), ou motif d'un rechargement complet
/// (« cleared », « thresholds », « folder »).
/// </summary>
public sealed record QualityUpdatedEvent(string Reason, IReadOnlyList<QualityEntry> Entries, IReadOnlyList<string>? Removed = null);

public static class QualityUpdateReasons
{
    public const string Scanned = "scanned";
    public const string References = "references";
    public const string Reanalyzed = "reanalyzed";
    public const string Redownload = "redownload";
    public const string Replaced = "replaced";
    public const string Cleared = "cleared";
    public const string Thresholds = "thresholds";
    public const string Folder = "folder";
    public const string Imported = "imported";
}

public static class QualityJobTypes
{
    public const string Scan = "quality.scan";
    public const string Reanalyze = "quality.reanalyze";
    public const string References = "quality.references";
    public const string ImportCache = "quality.import";
}

[ServiceClient(ServiceNames.Quality)]
public sealed class QualityClient(HttpClient http) : ServiceClient(http, ServiceNames.Quality)
{
    public Task<JobDto> StartScanAsync(QualityScanRequest request, CancellationToken ct = default) => PostAsync<JobDto>("/api/scan", request, ct);

    public Task<QualityPage> QueryAsync(QualityQuery query, CancellationToken ct = default) => PostAsync<QualityPage>("/api/entries/query", query, ct);

    public Task<QualityThresholds> GetThresholdsAsync(CancellationToken ct = default) => GetAsync<QualityThresholds>("/api/thresholds", ct);

    public Task<QualityThresholds> SetThresholdsAsync(QualityThresholds thresholds, CancellationToken ct = default) =>
        PutAsync<QualityThresholds>("/api/thresholds", thresholds, ct);

    public Task<JobDto> ReanalyzeAsync(IReadOnlyList<string> dbPaths, CancellationToken ct = default) => PostAsync<JobDto>("/api/reanalyze", dbPaths, ct);

    /// <summary>« Vider la liste » : retire les lignes de l'écran (les mesures restent en base). Rend le nombre de lignes retirées.</summary>
    public Task<int> ClearAsync(CancellationToken ct = default) => PostAsync<int>("/api/entries/clear", null, ct);

    /// <summary>« Modifier le dossier » : vide la liste et arrête l'analyse en cours si le dossier change. Rend le dossier retenu.</summary>
    public Task<string> SetFolderAsync(string folder, CancellationToken ct = default) =>
        PutAsync<string>($"/api/folder?path={Uri.EscapeDataString(folder)}", null, ct);

    /// <summary>« Retélécharger la sélection » : confie les titres au service d'extraction ; la réanalyse suit l'événement file.replaced.</summary>
    public Task<QualityRedownloadResult> RedownloadAsync(IReadOnlyList<string> dbPaths, CancellationToken ct = default) =>
        PostAsync<QualityRedownloadResult>("/api/redownload", dbPaths, ct);

    /// <summary>Importe le cache de mesures de l'application desktop (<c>quality_cache.json</c>).</summary>
    public Task<JobDto> ImportDesktopCacheAsync(CancellationToken ct = default) => PostAsync<JobDto>("/api/import", null, ct);
}
