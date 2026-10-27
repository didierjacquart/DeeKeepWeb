using System.Text.Json;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Organize;

namespace DeeKeep.Gateway.Domains.Organize;

[QueryType]
public static partial class OrganizeQueries
{
    /// <summary>Dernière analyse (conservée côté serveur), dossier par défaut, options enregistrées, format cible.</summary>
    public static Task<OrganizeStateDto> GetOrganizeState([Service] OrganizeClient client, CancellationToken ct) => client.GetStateAsync(ct);

    /// <summary>Fichiers lus de l'analyse (l'arbre), dans l'ordre du parcours.</summary>
    public static Task<OrganizeEntryPage> GetOrganizeEntries([Service] OrganizeClient client, Guid sessionId, int? offset, int? limit, CancellationToken ct) =>
        client.GetEntriesAsync(sessionId, offset ?? 0, limit ?? 100_000, ct);

    /// <summary>
    /// Plan de rangement recalculé avec les options données (sans relire le disque ni toucher aux cases).
    /// Sans options : celles enregistrées dans les Paramètres.
    /// </summary>
    public static Task<OrganizePlanDto> GetOrganizePlan([Service] OrganizeClient client, Guid sessionId, OrganizeOptionsInput? options, int? offset, int? limit,
        CancellationToken ct) =>
        client.GetPlanAsync(new OrganizePlanRequest(sessionId, options?.ToOptions(), offset ?? 0, limit ?? 100_000), ct);

    /// <summary>Aperçu du motif de nommage sur le premier fichier analysé.</summary>
    public static Task<OrganizePreview> GetOrganizePreview([Service] OrganizeClient client, Guid? sessionId, string pattern, bool rename, CancellationToken ct) =>
        client.GetPreviewAsync(new OrganizePreviewRequest(sessionId, pattern, rename), ct);

    /// <summary>Menu « Insérer une balise » et motifs prédéfinis.</summary>
    public static Task<OrganizeNameTags> GetOrganizeNameTags([Service] OrganizeClient client, CancellationToken ct) => client.GetNameTagsAsync(ct);

    /// <summary>Cases posées à la main par l'utilisateur (un recalcul du plan ne les modifie plus).</summary>
    public static Task<OrganizeChecks> GetOrganizeChecks([Service] OrganizeClient client, Guid sessionId, CancellationToken ct) => client.GetChecksAsync(sessionId, ct);

    /// <summary>Journal d'un rangement (une ligne par opération).</summary>
    public static Task<IReadOnlyList<OrganizeJournalEntry>> GetOrganizeJournal([Service] OrganizeClient client, Guid jobId, CancellationToken ct) =>
        client.GetJournalAsync(jobId, ct);

    /// <summary>Propositions déjà reçues d'une recherche de tags (reprise de la revue après un rechargement).</summary>
    public static async Task<IReadOnlyList<OrganizeProposalRow>> GetOrganizeProposals([Service] OrganizeClient client, Guid jobId, CancellationToken ct) =>
        (await client.GetProposalsAsync(jobId, ct))
        .Select(p => new OrganizeProposalRow(p.JobId, p.ItemKey, p.Status, p.Message, p.Path,
            p.Proposal is null ? null : JsonSerializer.SerializeToNode(p.Proposal, JsonDefaults.Options), p.At))
        .ToList();
}

[MutationType]
public static partial class OrganizeMutations
{
    /// <summary>Lance l'analyse d'un dossier (tâche « organize.analyze ») ; les lots de fichiers arrivent en jobItem.</summary>
    public static Task<OrganizeAnalyzeStart> StartOrganizeAnalysis([Service] OrganizeClient client, string root, CancellationToken ct) =>
        client.AnalyzeAsync(new OrganizeAnalyzeRequest(root), ct);

    /// <summary>Enregistre les options de rangement (clés org_* des Paramètres).</summary>
    public static Task<OrganizeOptions> SaveOrganizeOptions([Service] OrganizeClient client, OrganizeOptionsInput options, CancellationToken ct) =>
        client.SaveOptionsAsync(options.ToOptions(), ct);

    /// <summary>Coche ou décoche des fichiers (mémorisés comme touchés par l'utilisateur).</summary>
    public static Task<OrganizeChecks> SetOrganizeChecks([Service] OrganizeClient client, Guid sessionId, IReadOnlyList<int> indices, bool value, CancellationToken ct) =>
        client.SetChecksAsync(new OrganizeChecksRequest(sessionId, indices, value), ct);

    /// <summary>« Cocher ce qui est à faire » : oublie les cases touchées.</summary>
    public static Task<OrganizeChecks> ResetOrganizeChecks([Service] OrganizeClient client, Guid sessionId, CancellationToken ct) =>
        client.ResetChecksAsync(sessionId, ct);

    /// <summary>« Organiser » : applique le plan aux fichiers cochés (tâche « organize.apply »).</summary>
    public static Task<JobDto> ApplyOrganize([Service] OrganizeClient client, Guid sessionId, IReadOnlyList<int> indices, OrganizeOptionsInput? options, CancellationToken ct) =>
        client.ApplyAsync(new OrganizeApplyRequest(sessionId, indices, options?.ToOptions()), ct);

    /// <summary>« Rechercher les doublons » : tâche « organize.duplicates » ; les groupes sont le résultat de la tâche.</summary>
    public static Task<JobDto> FindOrganizeDuplicates([Service] OrganizeClient client, Guid sessionId, CancellationToken ct) =>
        client.FindDuplicatesAsync(new OrganizeDuplicatesRequest(sessionId), ct);

    /// <summary>Supprime des fichiers (corbeille de la bibliothèque) et les retire de l'analyse indiquée.</summary>
    public static Task<DeleteFilesResult> DeleteOrganizeFiles([Service] OrganizeClient client, IReadOnlyList<string> paths, Guid? sessionId, CancellationToken ct) =>
        client.DeleteFilesAsync(new OrganizeDeleteRequest(paths, sessionId), ct);

    /// <summary>
    /// « Compléter les tags » : lance la recherche (tâche « tags.find » du Catalogue, propositions en jobItem)
    /// sur les fichiers cochés. Renvoie la tâche et les fichiers soumis.
    /// </summary>
    public static Task<OrganizeLookupStart> StartOrganizeLookup([Service] OrganizeClient client, Guid sessionId, IReadOnlyList<int> indices, CancellationToken ct) =>
        client.StartLookupAsync(new OrganizeLookupRequest(sessionId, indices), ct);

    /// <summary>Écrit les tags des correspondances acceptées (champs vides seulement, sauf OverwriteExisting).</summary>
    public static Task<JobDto> WriteOrganizeTags([Service] OrganizeClient client, Guid? sessionId, IReadOnlyList<OrganizeTagWriteInput> items, CancellationToken ct) =>
        client.WriteTagsAsync(new OrganizeWriteTagsRequest(sessionId, items.Select(i => i.ToItem()).ToList()), ct);
}
