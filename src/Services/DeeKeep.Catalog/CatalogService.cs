using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.History;
using DeeKeep.Catalog.Http;
using DeeKeep.Catalog.Media;
using DeeKeep.Catalog.MusicBrainz;
using DeeKeep.Catalog.Reference;
using DeeKeep.Catalog.Tags;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog;

/// <summary>Enregistrement et points d'entrée du service Catalog.</summary>
public static class CatalogService
{
    public static IServiceCollection AddCatalogService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogOptions>(configuration.GetSection("Catalog"));
        services.Configure<ImportOptions>(configuration.GetSection("Import"));
        services.AddMemoryCache(o => o.SizeLimit = 2000);
        services.AddSingleton<CatalogPaths>();
        services.AddSingleton<CatalogGates>();

        // Clients sortants : résilience standard par défaut ; cadence appliquée par CatalogGates.
        services.AddHttpClient<DeezerApi>(c => c.BaseAddress = new Uri(DeezerApi.BaseUrl))
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(o => ConfigureResilience(o, TimeSpan.FromSeconds(10)));
        services.AddHttpClient<MusicBrainzApi>(c => c.BaseAddress = new Uri(MusicBrainzApi.BaseUrl))
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(o => ConfigureResilience(o, TimeSpan.FromSeconds(15)));
        services.AddHttpClient(ImageProxy.HttpClientName);
        services.AddHttpClient(PreviewProxy.HttpClientName).RemoveAllResilienceHandlers();
        services.AddHttpClient(AcoustIdService.HttpClientName);
        // Liens courts : les redirections sont suivies à la main, hôte par hôte.
        services.AddHttpClient(DeezerCatalog.LinkClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddSingleton<DeezerCatalog>();
        services.AddSingleton<ImageProxy>();
        services.AddSingleton<PreviewProxy>();
        services.AddSingleton<SearchHistory>();
        services.AddSingleton<ReferenceStore>();
        services.AddSingleton<ReferenceService>();
        services.AddSingleton<AcoustIdService>();
        services.AddSingleton<IToolStatusProvider>(sp => sp.GetRequiredService<AcoustIdService>());
        services.AddSingleton<AudioProbe>();
        services.AddSingleton<ITagSources, TagSources>();
        services.AddSingleton<TagFinder>();
        services.AddSingleton<ProposalStore>();

        services.AddJobHandler<TagLookupJob>(CatalogJobTypes.FindTags);
        services.AddJobHandler<ReferenceImport>(CatalogJobTypes.ImportReferences);
        return services;
    }

    private static void ConfigureResilience(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions o, TimeSpan attempt)
    {
        o.AttemptTimeout.Timeout = attempt;
        o.TotalRequestTimeout.Timeout = attempt * 3 + TimeSpan.FromSeconds(10);
        o.CircuitBreaker.SamplingDuration = attempt * 2 + TimeSpan.FromSeconds(10);
        o.Retry.MaxRetryAttempts = 2;
    }

    public static WebApplication MapCatalogService(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/search", (string q, SearchKind? kind, DeezerCatalog catalog, CancellationToken ct) =>
            catalog.SearchAsync(q, kind ?? SearchKind.All, ct));
        api.MapGet("/browse/{kind}/{id:long}", (EntityKind kind, long id, DeezerCatalog catalog, CancellationToken ct) =>
            catalog.BrowseAsync(kind, id, ct));
        api.MapGet("/tracks/{id:long}", async (long id, DeezerCatalog catalog, CancellationToken ct) =>
            await catalog.TrackDetailsAsync(id, ct) is { } details ? Results.Ok(details) : Results.NotFound());
        api.MapGet("/match", async (string artist, string title, double? duration, DeezerCatalog catalog, CancellationToken ct) =>
            await catalog.BestMatchAsync(artist, title, ct) is { } match ? Results.Ok(match) : Results.NotFound());
        api.MapGet("/reference", (string artist, string title, ReferenceService reference, CancellationToken ct) =>
            reference.LookupAsync(artist, title, ct));
        api.MapPost("/reference/import", (JobStore jobs) =>
            jobs.Enqueue(new EnqueueJobRequest(CatalogJobTypes.ImportReferences, "Import des durées de référence", null, MaxAttempts: 1)));

        api.MapGet("/searches", (SearchHistory history) => history.List());
        api.MapPost("/searches", (string q, SearchHistory history) => history.Remember(q));
        api.MapDelete("/searches", (string q, SearchHistory history) => history.Delete(q));
        api.MapDelete("/searches/all", (SearchHistory history) => history.Clear());
        api.MapGet("/searches/cover", async (string q, DeezerCatalog catalog, CancellationToken ct) =>
        {
            try { return new SearchCover(q, await catalog.CoverForQueryAsync(q, ct)); }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException) { return new SearchCover(q, null); }
        });

        api.MapPost("/tags/lookup", (TagLookupRequest request, JobStore jobs) =>
        {
            if (request.Files.Count == 0) return Results.BadRequest("Aucun fichier à examiner.");
            var payload = System.Text.Json.JsonSerializer.SerializeToNode(request, JsonDefaults.Options);
            return Results.Ok(jobs.Enqueue(new EnqueueJobRequest(CatalogJobTypes.FindTags, $"Compléter les tags ({request.Files.Count} fichiers)", payload,
                GroupKey: "tags.find", MaxAttempts: 2)));
        });
        api.MapGet("/tags/proposals/{jobId:guid}", (Guid jobId, ProposalStore store) => store.List(jobId));
        api.MapGet("/tags/acoustid", (AcoustIdService acoustId, CancellationToken ct) => acoustId.StatusAsync(ct));

        // Flux publics (via le proxy de la passerelle).
        app.MapGet("/img", (string? u, ImageProxy proxy, CancellationToken ct) => proxy.ServeAsync(u, ct));
        app.MapGet("/preview/{id:long}", (long id, HttpContext context, PreviewProxy proxy) => proxy.ServeAsync(id, context));
        return app;
    }
}
