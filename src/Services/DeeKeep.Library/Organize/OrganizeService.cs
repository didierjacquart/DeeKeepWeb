using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Organize;
using DeeKeep.Library.Files;

namespace DeeKeep.Library.Organize;

/// <summary>Enregistrement et points d'entrée « Organiser », doublons et tags.</summary>
public static class OrganizeService
{
    public static IServiceCollection AddOrganizeService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<OrganizeStore>();
        services.AddSingleton<OrganizeEngine>();
        services.AddJobHandler<OrganizeAnalyzeHandler>(OrganizeJobTypes.Analyze);
        services.AddJobHandler<OrganizeApplyHandler>(OrganizeJobTypes.Apply);
        services.AddJobHandler<OrganizeDuplicatesHandler>(OrganizeJobTypes.Duplicates);
        services.AddJobHandler<OrganizeWriteTagsHandler>(OrganizeJobTypes.WriteTags);

        // Les propositions de la recherche des tags (tâche « tags.find » du Catalogue) sont gardées côté
        // serveur : la revue se retrouve après un rechargement ou une coupure.
        services.AddEventSubscription(ServiceNames.Catalog, (sp, evt, _) =>
        {
            if (evt.Data?.Deserialize<JobItemEvent>(JsonDefaults.Options) is not { } item || item.Type != CatalogJobTypes.FindTags) return Task.CompletedTask;
            var store = sp.GetRequiredService<OrganizeStore>();
            if (!store.IsLookup(item.JobId)) return Task.CompletedTask;
            var proposal = item.Data?.Deserialize<TagProposal>(JsonDefaults.Options);
            store.SaveProposal(item, proposal);
            return Task.CompletedTask;
        }, durable: true, EventTypes.JobItem);
        return services;
    }

    private static readonly string[] ExclusiveTypes = [OrganizeJobTypes.Analyze, OrganizeJobTypes.Apply];

    public static WebApplication MapOrganizeService(this WebApplication app)
    {
        var api = app.MapGroup("/api/organize");

        api.MapGet("/state", (OrganizeStore store, OrganizeEngine engine) =>
            new OrganizeStateDto(store.LatestSession(), engine.DefaultRoot(), engine.LoadOptions(), engine.TargetFormat()));

        api.MapGet("/name-tags", () => OrganizeEngine.NameTags());

        api.MapPut("/options", (OrganizeEngine engine, OrganizeOptions options) => engine.SaveOptions(options));

        api.MapGet("/entries", (OrganizeStore store, LibraryPaths paths, Guid session, int? offset, int? limit) =>
        {
            var s = store.GetSession(session);
            if (s is null) return Results.NotFound();
            var all = store.Entries(session);
            var page = all.Skip(Math.Max(0, offset ?? 0)).Take(Math.Clamp(limit ?? 100_000, 0, 100_000)).Select(e => OrganizeMapping.ToDto(e, s.Root, paths)).ToList();
            return Results.Ok(new OrganizeEntryPage(session, page, all.Count));
        });

        api.MapPost("/plan", (OrganizeStore store, OrganizeEngine engine, OrganizePlanRequest request) =>
            store.GetSession(request.SessionId) is { } s
                ? Results.Ok(engine.PlanDto(s, request.Options, request.Offset, request.Limit))
                : Results.NotFound());

        api.MapPost("/preview", (OrganizeEngine engine, OrganizePreviewRequest request) => engine.Preview(request.SessionId, request.Pattern, request.Rename));

        api.MapGet("/checks", (OrganizeStore store, Guid session) => store.Checks(session));
        api.MapPost("/checks", (OrganizeStore store, OrganizeChecksRequest request) => store.SetChecks(request.SessionId, request.Indices, request.Checked));
        api.MapPost("/checks/reset", (OrganizeStore store, Guid session) => store.ResetChecks(session));

        api.MapPost("/analyze", (OrganizeStore store, JobStore jobs, LibraryPaths paths, OrganizeAnalyzeRequest request) =>
        {
            if (Busy(jobs)) return Results.Conflict("Une analyse ou un rangement est déjà en cours.");
            var root = request.Root?.Trim() ?? "";
            if (!Directory.Exists(root)) return Results.BadRequest($"Le dossier suivant n'existe pas :\n{root}");
            if (!paths.IsAllowed(root)) return Results.BadRequest("Ce dossier est hors des dossiers de la bibliothèque.");
            var session = store.CreateSession(root, paths.ToDb(root));
            var job = jobs.Enqueue(new EnqueueJobRequest(OrganizeJobTypes.Analyze, $"Analyse de {root}",
                JsonSerializer.SerializeToNode(new OrganizeAnalyzePayload(session.Id, root), JsonDefaults.Options), GroupKey: "organize", MaxAttempts: 3));
            store.UpdateSession(session.Id, jobId: job.Id);
            return Results.Ok(new OrganizeAnalyzeStart(store.GetSession(session.Id)!, job));
        });

        api.MapPost("/apply", (OrganizeStore store, OrganizeEngine engine, JobStore jobs, OrganizeApplyRequest request) =>
        {
            if (Busy(jobs)) return Results.Conflict("Une analyse ou un rangement est déjà en cours.");
            if (store.GetSession(request.SessionId) is not { } s) return Results.NotFound();
            if (s.State == "running") return Results.Conflict("Attendez la fin de l'analyse.");
            var wanted = request.Indices.ToHashSet();
            var plan = engine.Plan(s, request.Options ?? engine.LoadOptions());
            var ops = new List<OrganizeOperation>();
            foreach (var r in plan.Results.Where(r => wanted.Contains(r.Entry.Index)))
            {
                if (r.Status == OrganizeStatuses.Move) ops.Add(new OrganizeOperation(r.Entry.Index, OrganizeOperationKinds.Move, r.Src, r.Dst));
                else if (r.Status == OrganizeStatuses.Duplicate) ops.Add(new OrganizeOperation(r.Entry.Index, OrganizeOperationKinds.Delete, r.Src, r.Dst));
            }
            if (ops.Count == 0) return Results.BadRequest("Cochez au moins un fichier à ranger.");
            var options = request.Options ?? engine.LoadOptions();
            var job = jobs.Enqueue(new EnqueueJobRequest(OrganizeJobTypes.Apply, $"Rangement de {ops.Count} fichier(s)",
                JsonSerializer.SerializeToNode(new OrganizeApplyPayload(s.Id, s.Root, ops, options.RemoveEmpty), JsonDefaults.Options), GroupKey: "organize", MaxAttempts: 3));
            return Results.Ok(job);
        });

        api.MapGet("/journal", (OrganizeStore store, Guid job) => store.JournalList(job));

        api.MapPost("/duplicates", (OrganizeStore store, JobStore jobs, OrganizeDuplicatesRequest request) =>
        {
            if (store.GetSession(request.SessionId) is not { } s) return Results.NotFound();
            if (s.State == "running") return Results.Conflict("Attendez la fin de l'analyse avant de chercher les doublons.");
            return Results.Ok(jobs.Enqueue(new EnqueueJobRequest(OrganizeJobTypes.Duplicates, "Recherche des doublons",
                JsonSerializer.SerializeToNode(request, JsonDefaults.Options), GroupKey: "organize")));
        });

        // Suppression (corbeille) de fichiers cochés : écran fichier/fichier et écran piste/fichiers de la recherche.
        api.MapPost("/delete", async (OrganizeStore store, FileRegistrar registrar, LibraryPaths paths, OrganizeDeleteRequest request, CancellationToken ct) =>
        {
            var result = await registrar.DeleteAsync(new DeleteFilesRequest(request.Paths, request.Reason), ct);
            if (request.SessionId is { } sid && store.GetSession(sid) is not null)
            {
                // Les fichiers disparus ne pèsent plus sur le plan (ils comptaient pour leur artiste, donc pour « Divers »).
                var gone = result.Deleted.Select(LibraryPaths.Key).ToHashSet(StringComparer.Ordinal);
                var indices = store.Entries(sid).Where(e => gone.Contains(LibraryPaths.Key(paths.ToDb(e.Path)))).Select(e => e.Index).ToList();
                store.RemoveEntries(sid, indices);
            }
            return result;
        });

        // Compléter les tags : la recherche est la tâche « tags.find » du Catalogue ; ici on choisit les fichiers.
        api.MapPost("/lookup", async (OrganizeStore store, LibraryPaths paths, CatalogClient catalog, OrganizeLookupRequest request, CancellationToken ct) =>
        {
            if (store.GetSession(request.SessionId) is not { } s) return Results.NotFound();
            var wanted = request.Indices.ToHashSet();
            var files = store.Entries(s.Id).Where(e => wanted.Contains(e.Index)).Select(e => new OrganizeLookupFile(
                e.Index, e.Path, paths.ToDb(e.Path), OrganizeText.Relative(e.Path, s.Root), e.Title, e.TitleTagged, e.Artist, e.Album,
                e.MissingCore().Count > 0)).ToList();
            if (files.Count == 0) return Results.BadRequest("Aucun fichier coché ne manque de tag.");
            var lookup = files.Select(f =>
            {
                var e = store.Entries(s.Id)[f.Index];
                return new TagLookupFile(f.DbPath, e.TitleTagged ? e.Title : null, e.Artist, e.Album, e.AlbumArtist, e.Genre, e.Date, e.Track, null);
            }).ToList();
            var job = await catalog.StartTagLookupAsync(new TagLookupRequest(lookup), ct);
            store.AddLookup(job.Id, s.Id);
            return Results.Ok(new OrganizeLookupStart(job, files));
        });

        api.MapGet("/lookup/proposals", (OrganizeStore store, Guid job) => store.Proposals(job));

        api.MapPost("/tags", (JobStore jobs, OrganizeWriteTagsRequest request) =>
        {
            if (request.Items.Count == 0) return Results.BadRequest("Cochez au moins une correspondance à écrire.");
            return Results.Ok(jobs.Enqueue(new EnqueueJobRequest(OrganizeJobTypes.WriteTags, $"Écriture de tags ({request.Items.Count} fichier(s))",
                JsonSerializer.SerializeToNode(request, JsonDefaults.Options), GroupKey: "organize")));
        });

        return app;
    }

    /// <summary>Un seul traitement d'écriture à la fois (équivalent de <c>_busy</c>, côté serveur).</summary>
    private static bool Busy(JobStore jobs) =>
        jobs.List(activeOnly: true, 50).Any(j => ExclusiveTypes.Contains(j.Type));
}
