using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Storage;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DeeKeep.Storage;

/// <summary>Enregistrement et points d'entrée du service Storage (propriétaire de la table <c>archive</c>).</summary>
public static class StorageService
{
    private const string GroupKey = "archive";

    public static IServiceCollection AddStorageService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<ArchiveSettings>();
        services.AddSingleton<ArchiveIndex>();
        services.AddSingleton<ArchiveLocks>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<IToolStatusProvider, ArchiveToolStatus>();
        services.AddJobHandler<ArchiveUploadHandler>(StorageJobTypes.Archive);
        services.AddJobHandler<ArchiveRestoreHandler>(StorageJobTypes.Restore);
        services.AddJobHandler<ArchiveCheckHandler>(StorageJobTypes.Check);
        services.AddJobHandler<ArchiveUsageHandler>(StorageJobTypes.Usage);
        return services;
    }

    public static WebApplication MapStorageService(this WebApplication app)
    {
        var api = app.MapGroup("/api/archive");

        api.MapGet("/config", (ArchiveSettings settings) => settings.Get());
        api.MapPut("/config", (ArchiveSettings settings, ArchiveConfig config) => settings.Save(config.Root, config.Prefix));
        api.MapPost("/test", (ArchiveService archive, ArchiveRootRequest request, CancellationToken ct) => archive.TestAsync(request.Root, ct));
        api.MapGet("/usage", (ArchiveService archive, string? root) => archive.Usage(root));
        api.MapPost("/plan", (ArchiveService archive, List<string> paths) => archive.Plan(paths));
        api.MapPost("/remove", (ArchiveService archive, List<string> paths, CancellationToken ct) => archive.RemoveAsync(paths, ct));

        api.MapPost("/upload", (ArchiveService archive, JobStore jobs, ArchiveRequest request) =>
        {
            var paths = archive.OnDisk(request.DbPaths);
            if (paths.Count == 0) return Results.BadRequest("Aucun des titres retenus n'est présent sur le disque : il n'y a rien à archiver.");
            return Results.Ok(Enqueue(jobs, StorageJobTypes.Archive, $"Archivage de {paths.Count} titres", new ArchiveRequest(paths, request.BatchId)));
        });
        api.MapPost("/restore", (ArchiveService archive, JobStore jobs, ArchiveRequest request) =>
        {
            var paths = archive.Restorable(request.DbPaths);
            if (paths.Count == 0) return Results.BadRequest("Rien à restaurer : tous les titres que l'archive détient sont déjà sur le disque.");
            return Results.Ok(Enqueue(jobs, StorageJobTypes.Restore, $"Restauration de {paths.Count} titres", new ArchiveRequest(paths, request.BatchId)));
        });
        api.MapPost("/measure", (JobStore jobs, [FromBody] ArchiveRootRequest? request) =>
            Enqueue(jobs, StorageJobTypes.Usage, "Mesure de l'archive", new ArchiveRootPayload(request?.Root)));
        api.MapPost("/check", (JobStore jobs) => Enqueue(jobs, StorageJobTypes.Check, "Vérification de l'archive", null));

        // Flux public (via le proxy de la passerelle) : un titre absent du disque, lu dans l'archive.
        app.MapGet("/archive-media/track", (ArchiveService archive, string path) =>
        {
            (BlobReadStream Stream, string Key, string? Sha1)? found;
            try
            {
                found = archive.OpenTrack(path);
            }
            catch (ArchiveException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            if (found is not { } f) return Results.NotFound();
            return Results.Stream(f.Stream, ArchiveContentType(f.Key), enableRangeProcessing: true,
                entityTag: f.Sha1 is { Length: > 0 } sha ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{sha}\"") : null);
        });
        return app;
    }

    private static JobDto Enqueue(JobStore jobs, string type, string title, object? payload) =>
        jobs.Enqueue(new EnqueueJobRequest(type, title, payload is null ? null : JsonSerializer.SerializeToNode(payload, JsonDefaults.Options),
            GroupKey: GroupKey, MaxAttempts: 1));

    private static string ArchiveContentType(string key) => AudioExtensions.ContentType(key);
}

/// <summary>État de l'archive pour la page Diagnostic : fichier, taille, objets.</summary>
public sealed class ArchiveToolStatus(ArchiveSettings settings) : IToolStatusProvider
{
    public Task<IReadOnlyList<ToolStatus>> GetAsync(CancellationToken ct)
    {
        var config = settings.Get();
        if (config.ArchiveFile is null)
            return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Archive", false, null, null, "Aucune archive configurée")]);
        try
        {
            var store = ArchiveSettings.Open(config);
            if (!store.Exists) return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Archive", true, null, store.Path, "Fichier pas encore créé")]);
            var (count, bytes) = store.Usage();
            return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Archive", true, null, store.Path,
                $"{count} objets, {ArchiveRules.HumanBytes(bytes)} (fichier {ArchiveRules.HumanBytes(store.FileBytes())})")]);
        }
        catch (ArchiveException ex)
        {
            return Task.FromResult<IReadOnlyList<ToolStatus>>([new ToolStatus("Archive", false, null, config.ArchiveFile, ex.Message)]);
        }
    }
}
