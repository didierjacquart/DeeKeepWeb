using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Contracts.Organize;

/// <summary>
/// Options de rangement de l'écran Organiser (clés <c>org_*</c> de app_settings,
/// partagées avec les Paramètres et les extractions).
/// </summary>
public sealed record OrganizeOptions(
    bool Artist = true,
    bool Album = true,
    bool Rename = false,
    string Pattern = "%titre% - %artiste%",
    bool Divers = false,
    int DiversMin = 3,
    bool KeepAlbums = true,
    bool Dedup = true,
    bool RemoveEmpty = true,
    bool ReportUntagged = true,
    bool FiltersOpen = false);

/// <summary>Analyse d'un dossier : la dernière lancée est conservée côté serveur (reprise après rechargement).</summary>
public sealed record OrganizeSessionDto(
    Guid Id,
    string Root,
    string RootDb,
    Guid? JobId,
    // running, done, stopped, failed.
    string State,
    int Total,
    int Read,
    double CreatedAt,
    double UpdatedAt,
    string? Message);

/// <summary>Fichier analysé (tags lus au fil de l'eau) : une ligne de l'arbre.</summary>
public sealed record OrganizeEntryDto(
    int Index,
    string Path,
    string DbPath,
    string Name,
    // Dossier relatif à la racine analysée, « / » comme séparateur, vide pour la racine.
    string Folder,
    string Title,
    bool TitleTagged,
    string Artist,
    string Album,
    string AlbumArtist,
    string Genre,
    string Date,
    string Track,
    string Format,
    // Il manque l'artiste d'album, le genre, l'année ou le numéro de piste.
    bool NeedsExtras);

public sealed record OrganizeEntryPage(Guid SessionId, IReadOnlyList<OrganizeEntryDto> Items, int Total);

/// <summary>État de la page : dernière analyse, dossier par défaut (dossier de dépôt), options enregistrées.</summary>
public sealed record OrganizeStateDto(OrganizeSessionDto? Session, string DefaultRoot, OrganizeOptions Options, string TargetFormat);

public static class OrganizeStatuses
{
    public const string Move = "move";
    public const string InPlace = "inplace";
    public const string Album = "album";
    public const string Duplicate = "duplicate";
    public const string Untagged = "untagged";
}

/// <summary>Ligne du plan : statut et libellés de la colonne « Action » (verbatim de l'original).</summary>
public sealed record OrganizePlanRow(
    int Index,
    string Status,
    string Action,
    string? Tooltip,
    // Destination (move, inplace), exemplaire gardé (duplicate), sinon null.
    string? Destination,
    IReadOnlyList<string> Missing,
    // Case cochée par défaut (tant que l'utilisateur ne l'a pas touchée).
    bool DefaultChecked,
    // Candidat à « Compléter les tags » (non tagué ou tags complémentaires manquants).
    bool NeedsLookup);

/// <summary>Dossier d'album préservé (musique de film, compilation) : texte de sa ligne dossier.</summary>
public sealed record OrganizeAlbumFolder(string Folder, string Label);

public sealed record OrganizePlanStats(int Analyzed, int Move, int InPlace, int Untagged, int Duplicate, int Divers, int FromDivers, int Album, int AlbumDirs);

/// <summary>Aperçu du motif de nommage sur le premier fichier analysé.</summary>
public sealed record OrganizePreview(string Text, bool Warning, IReadOnlyList<string> UnknownTokens);

public sealed record OrganizePlanDto(
    Guid SessionId,
    OrganizeOptions Options,
    IReadOnlyList<OrganizePlanRow> Rows,
    int TotalRows,
    OrganizePlanStats Stats,
    string StatusText,
    string FiltersSummary,
    IReadOnlyList<OrganizeAlbumFolder> AlbumFolders,
    OrganizePreview Preview,
    // « Organiser » est actif : au moins une ligne à déplacer ou à supprimer.
    bool CanApply);

public sealed record OrganizePlanRequest(Guid SessionId, OrganizeOptions? Options = null, int Offset = 0, int Limit = 100_000);

public sealed record OrganizePreviewRequest(Guid? SessionId, string Pattern, bool Rename);

public sealed record OrganizeNameTagItem(string Label, string Token);

public sealed record OrganizeNameTagCategory(string Name, IReadOnlyList<OrganizeNameTagItem> Items);

/// <summary>Menu « Insérer une balise » et motifs prédéfinis.</summary>
public sealed record OrganizeNameTags(IReadOnlyList<OrganizeNameTagCategory> Categories, IReadOnlyList<string> Presets, string DefaultPattern);

/// <summary>Cases touchées par l'utilisateur (un recalcul du plan ne les modifie plus).</summary>
public sealed record OrganizeChecks(Guid SessionId, IReadOnlyList<int> Checked, IReadOnlyList<int> Unchecked);

public sealed record OrganizeChecksRequest(Guid SessionId, IReadOnlyList<int> Indices, bool Checked);

public sealed record OrganizeAnalyzeRequest(string Root);

public sealed record OrganizeAnalyzeStart(OrganizeSessionDto Session, JobDto Job);

/// <summary>Charge utile de la tâche <c>organize.analyze</c>.</summary>
public sealed record OrganizeAnalyzePayload(Guid SessionId, string Root);

/// <summary>Application du plan aux fichiers cochés (indices de l'analyse).</summary>
public sealed record OrganizeApplyRequest(Guid SessionId, IReadOnlyList<int> Indices, OrganizeOptions? Options = null);

public static class OrganizeOperationKinds
{
    public const string Move = "move";
    public const string Delete = "delete";
}

/// <summary>Opération figée au lancement (reprise déterministe après redémarrage).</summary>
public sealed record OrganizeOperation(int Index, string Kind, string Source, string Destination);

/// <summary>Charge utile de la tâche <c>organize.apply</c>.</summary>
public sealed record OrganizeApplyPayload(Guid SessionId, string Root, IReadOnlyList<OrganizeOperation> Operations, bool RemoveEmpty);

public sealed record OrganizeFailure(string Path, string Name, string Message);

/// <summary>Bilan de « Organiser » (résultat de la tâche).</summary>
public sealed record OrganizeApplyResult(
    int Moved,
    int ToMove,
    int Deleted,
    int ToDelete,
    IReadOnlyList<string> RemovedDirs,
    IReadOnlyList<OrganizeFailure> Failures,
    string Summary);

/// <summary>Journal d'une opération appliquée (événement job.item et table de journal).</summary>
public sealed record OrganizeJournalEntry(Guid JobId, int Index, string Kind, string Source, string Destination, string Status, string? Message, double At);

/// <summary>Fichier d'un groupe de doublons (écran fichier/fichier).</summary>
public sealed record OrganizeDuplicateFile(int Index, string Path, string DbPath, string Title, string Artist, string Format, int Bitrate, long Size, bool Keep);

public sealed record OrganizeDuplicateGroup(int Number, IReadOnlyList<OrganizeDuplicateFile> Files);

/// <summary>Résultat de la tâche <c>organize.duplicates</c>.</summary>
public sealed record OrganizeDuplicatesResult(Guid SessionId, IReadOnlyList<OrganizeDuplicateGroup> Groups, int FileCount, string TargetFormat);

public sealed record OrganizeDuplicatesRequest(Guid SessionId);

/// <summary>Suppression (corbeille) de fichiers ; ils sont retirés de l'analyse en cours.</summary>
public sealed record OrganizeDeleteRequest(IReadOnlyList<string> Paths, Guid? SessionId = null, string Reason = "dedup");

/// <summary>Lancement de « Compléter les tags » sur les fichiers cochés.</summary>
public sealed record OrganizeLookupRequest(Guid SessionId, IReadOnlyList<int> Indices, OrganizeOptions? Options = null);

/// <summary>Fichier soumis à la recherche : la revue y ajoute une ligne quand il a été examiné.</summary>
public sealed record OrganizeLookupFile(int Index, string Path, string DbPath, string Relative, string Title, bool TitleTagged, string Artist, string Album, bool CoreMissing);

public sealed record OrganizeLookupStart(JobDto? Job, IReadOnlyList<OrganizeLookupFile> Files);

/// <summary>Proposition reçue de la recherche (conservée pour la reprise après une coupure).</summary>
public sealed record OrganizeProposalItem(Guid JobId, string ItemKey, string Status, string? Message, string? Path, TagProposal? Proposal, double At);

/// <summary>Écriture des tags acceptés dans la revue (tâche <c>organize.tags</c>).</summary>
public sealed record OrganizeWriteTagsRequest(Guid? SessionId, IReadOnlyList<WriteTagsItem> Items);

public sealed record OrganizeWriteTagsResult(int Written, int Accepted, IReadOnlyList<OrganizeFailure> Failures, string Summary);

public static class OrganizeJobTypes
{
    public const string Analyze = LibraryJobTypes.OrganizeAnalyze;
    public const string Apply = LibraryJobTypes.OrganizeApply;
    public const string Duplicates = "organize.duplicates";
    public const string WriteTags = "organize.tags";
}

[ServiceClient(ServiceNames.Library)]
public sealed class OrganizeClient(HttpClient http) : ServiceClient(http, ServiceNames.Library)
{
    public Task<OrganizeStateDto> GetStateAsync(CancellationToken ct = default) => GetAsync<OrganizeStateDto>("/api/organize/state", ct);

    public Task<OrganizeEntryPage> GetEntriesAsync(Guid sessionId, int offset = 0, int limit = 100_000, CancellationToken ct = default) =>
        GetAsync<OrganizeEntryPage>($"/api/organize/entries?session={sessionId}&offset={offset}&limit={limit}", ct);

    public Task<OrganizePlanDto> GetPlanAsync(OrganizePlanRequest request, CancellationToken ct = default) =>
        PostAsync<OrganizePlanDto>("/api/organize/plan", request, ct);

    public Task<OrganizePreview> GetPreviewAsync(OrganizePreviewRequest request, CancellationToken ct = default) =>
        PostAsync<OrganizePreview>("/api/organize/preview", request, ct);

    public Task<OrganizeNameTags> GetNameTagsAsync(CancellationToken ct = default) => GetAsync<OrganizeNameTags>("/api/organize/name-tags", ct);

    public Task<OrganizeOptions> SaveOptionsAsync(OrganizeOptions options, CancellationToken ct = default) =>
        PutAsync<OrganizeOptions>("/api/organize/options", options, ct);

    public Task<OrganizeChecks> GetChecksAsync(Guid sessionId, CancellationToken ct = default) =>
        GetAsync<OrganizeChecks>($"/api/organize/checks?session={sessionId}", ct);

    public Task<OrganizeChecks> SetChecksAsync(OrganizeChecksRequest request, CancellationToken ct = default) =>
        PostAsync<OrganizeChecks>("/api/organize/checks", request, ct);

    public Task<OrganizeChecks> ResetChecksAsync(Guid sessionId, CancellationToken ct = default) =>
        PostAsync<OrganizeChecks>($"/api/organize/checks/reset?session={sessionId}", null, ct);

    public Task<OrganizeAnalyzeStart> AnalyzeAsync(OrganizeAnalyzeRequest request, CancellationToken ct = default) =>
        PostAsync<OrganizeAnalyzeStart>("/api/organize/analyze", request, ct);

    public Task<JobDto> ApplyAsync(OrganizeApplyRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/organize/apply", request, ct);

    public Task<IReadOnlyList<OrganizeJournalEntry>> GetJournalAsync(Guid jobId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<OrganizeJournalEntry>>($"/api/organize/journal?job={jobId}", ct);

    public Task<JobDto> FindDuplicatesAsync(OrganizeDuplicatesRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/organize/duplicates", request, ct);

    public Task<DeleteFilesResult> DeleteFilesAsync(OrganizeDeleteRequest request, CancellationToken ct = default) =>
        PostAsync<DeleteFilesResult>("/api/organize/delete", request, ct);

    public Task<OrganizeLookupStart> StartLookupAsync(OrganizeLookupRequest request, CancellationToken ct = default) =>
        PostAsync<OrganizeLookupStart>("/api/organize/lookup", request, ct);

    public Task<IReadOnlyList<OrganizeProposalItem>> GetProposalsAsync(Guid jobId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<OrganizeProposalItem>>($"/api/organize/lookup/proposals?job={jobId}", ct);

    public Task<JobDto> WriteTagsAsync(OrganizeWriteTagsRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/organize/tags", request, ct);
}
