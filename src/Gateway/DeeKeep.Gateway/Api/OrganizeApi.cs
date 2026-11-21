using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Organize;

namespace DeeKeep.Gateway.Api;

public sealed record RootBody(string Root);

public sealed record PlanBody(OrganizeOptions? Options = null, int? Offset = null, int? Limit = null);

public sealed record PreviewBody(Guid? SessionId, string Pattern, bool Rename);

public sealed record ChecksBody(IReadOnlyList<int> Indices, bool Value);

public sealed record ApplyBody(IReadOnlyList<int> Indices, OrganizeOptions? Options = null);

public sealed record IndicesBody(IReadOnlyList<int> Indices);

public sealed record OrganizeDeleteBody(IReadOnlyList<string> Paths, Guid? SessionId = null);

/// <summary>Écriture des tags d'une correspondance acceptée dans la revue.</summary>
public sealed record OrganizeTagWriteBody(string Path, IReadOnlyList<TagValueBody> Fields, bool OverwriteExisting = false);

public sealed record OrganizeWriteTagsBody(Guid? SessionId, IReadOnlyList<OrganizeTagWriteBody> Items);

/// <summary>Proposition reçue de la recherche des tags : mêmes données que l'événement job.item (type « tags.find »).</summary>
public sealed record OrganizeProposalRow(Guid JobId, string ItemKey, string Status, string? Message, string? Path, JsonNode? Data, double At);

public static class OrganizeApi
{
    public static void MapOrganizeApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/organize");

        // Dernière analyse (conservée côté serveur), dossier par défaut, options enregistrées, format cible.
        g.MapGet("/state", (OrganizeClient client, CancellationToken ct) => client.GetStateAsync(ct));
        // Menu « Insérer une balise » et motifs prédéfinis.
        g.MapGet("/name-tags", (OrganizeClient client, CancellationToken ct) => client.GetNameTagsAsync(ct));
        // Enregistre les options de rangement (clés org_* des Paramètres).
        g.MapPut("/options", (OrganizeClient client, OrganizeOptions options, CancellationToken ct) => client.SaveOptionsAsync(options, ct));
        // Aperçu du motif de nommage sur le premier fichier analysé.
        g.MapPost("/preview", (OrganizeClient client, PreviewBody body, CancellationToken ct) =>
            client.GetPreviewAsync(new OrganizePreviewRequest(body.SessionId, body.Pattern, body.Rename), ct));

        // Lance l'analyse d'un dossier (tâche « organize.analyze ») ; les lots de fichiers arrivent en job.item.
        g.MapPost("/analyze", (OrganizeClient client, RootBody body, CancellationToken ct) => client.AnalyzeAsync(new OrganizeAnalyzeRequest(body.Root), ct));

        var s = g.MapGroup("/sessions/{sessionId:guid}");
        // Fichiers lus de l'analyse, dans l'ordre du parcours.
        s.MapGet("/entries", (OrganizeClient client, Guid sessionId, int? offset, int? limit, CancellationToken ct) =>
            client.GetEntriesAsync(sessionId, offset ?? 0, limit ?? 100_000, ct));
        // Plan recalculé avec les options données (sans relire le disque) ; sans options : celles des Paramètres.
        s.MapPost("/plan", (OrganizeClient client, Guid sessionId, PlanBody? body, CancellationToken ct) =>
            client.GetPlanAsync(new OrganizePlanRequest(sessionId, body?.Options, body?.Offset ?? 0, body?.Limit ?? 100_000), ct));
        // Cases posées à la main ; « Cocher ce qui est à faire » les oublie.
        s.MapGet("/checks", (OrganizeClient client, Guid sessionId, CancellationToken ct) => client.GetChecksAsync(sessionId, ct));
        s.MapPost("/checks", (OrganizeClient client, Guid sessionId, ChecksBody body, CancellationToken ct) =>
            client.SetChecksAsync(new OrganizeChecksRequest(sessionId, body.Indices, body.Value), ct));
        s.MapPost("/checks/reset", (OrganizeClient client, Guid sessionId, CancellationToken ct) => client.ResetChecksAsync(sessionId, ct));
        // « Organiser » : applique le plan aux fichiers cochés (tâche « organize.apply »).
        s.MapPost("/apply", (OrganizeClient client, Guid sessionId, ApplyBody body, CancellationToken ct) =>
            client.ApplyAsync(new OrganizeApplyRequest(sessionId, body.Indices, body.Options), ct));
        // « Rechercher les doublons » : les groupes sont le résultat de la tâche.
        s.MapPost("/duplicates", (OrganizeClient client, Guid sessionId, CancellationToken ct) =>
            client.FindDuplicatesAsync(new OrganizeDuplicatesRequest(sessionId), ct));
        // « Compléter les tags » sur les fichiers cochés : la tâche et les fichiers soumis.
        s.MapPost("/lookup", (OrganizeClient client, Guid sessionId, IndicesBody body, CancellationToken ct) =>
            client.StartLookupAsync(new OrganizeLookupRequest(sessionId, body.Indices), ct));

        // Journal d'un rangement ; propositions déjà reçues d'une recherche de tags.
        g.MapGet("/journal/{jobId:guid}", (OrganizeClient client, Guid jobId, CancellationToken ct) => client.GetJournalAsync(jobId, ct));
        g.MapGet("/proposals/{jobId:guid}", async (OrganizeClient client, Guid jobId, CancellationToken ct) =>
            (await client.GetProposalsAsync(jobId, ct))
            .Select(p => new OrganizeProposalRow(p.JobId, p.ItemKey, p.Status, p.Message, p.Path,
                p.Proposal is null ? null : JsonSerializer.SerializeToNode(p.Proposal, JsonDefaults.Options), p.At))
            .ToList());

        // Supprime des fichiers (corbeille) et les retire de l'analyse indiquée.
        g.MapPost("/delete-files", (OrganizeClient client, OrganizeDeleteBody body, CancellationToken ct) =>
            client.DeleteFilesAsync(new OrganizeDeleteRequest(body.Paths, body.SessionId), ct));
        // Écrit les tags des correspondances acceptées (champs vides seulement, sauf OverwriteExisting).
        g.MapPost("/write-tags", (OrganizeClient client, OrganizeWriteTagsBody body, CancellationToken ct) =>
            client.WriteTagsAsync(new OrganizeWriteTagsRequest(body.SessionId, body.Items.Select(i => new WriteTagsItem(
                i.Path, i.Fields.GroupBy(f => f.Field).ToDictionary(f => f.Key, f => f.Last().Value), null, i.OverwriteExisting)).ToList()), ct));
    }
}
