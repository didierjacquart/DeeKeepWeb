using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Contracts.Library;
using DeeKeep.Common.Hosting;
using DeeKeep.Downloader.Data;
using DeeKeep.Downloader.Extraction;
using DeeKeep.Downloader.Jobs;
using DeeKeep.Downloader.Settings;
using DeeKeep.Downloader.Tools;
using Microsoft.Extensions.Options;

namespace DeeKeep.Downloader;

/// <summary>Enregistrement et points d'entrée du service Downloader (extraction yt-dlp, conversion, retéléchargement, outils).</summary>
public static class DownloaderService
{
    public static IServiceCollection AddDownloaderService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DownloaderOptions>(configuration.GetSection("Downloader"));
        // Concurrence prudente par défaut (Raspberry Pi) ; configurable : Jobs:downloader:Concurrency:<type>.
        // Le plafond commun (Downloader:MaxParallel) borne de toute façon yt-dlp + ffmpeg.
        services.PostConfigure<JobWorkerOptions>(o =>
        {
            foreach (var type in new[] { DownloaderJobTypes.Download, DownloaderJobTypes.Convert, DownloaderJobTypes.Redownload })
                o.Concurrency.TryAdd(type, 2);
            o.Concurrency.TryAdd(DownloaderJobTypes.UpdateYtDlp, 1);
        });
        services.AddSingleton<DownloaderSettings>();
        services.AddSingleton<DownloaderStore>();
        services.AddSingleton<ExtractionGate>();
        services.AddSingleton<DownloaderToolStatus>();
        services.AddSingleton<IToolStatusProvider>(sp => sp.GetRequiredService<DownloaderToolStatus>());
        services.AddScoped<TrackExtractor>();
        services.AddHostedService<WorkDirCleanup>();
        services.AddJobHandler<DownloadJobHandler>(DownloaderJobTypes.Download);
        services.AddJobHandler<ConvertJobHandler>(DownloaderJobTypes.Convert);
        services.AddJobHandler<RedownloadJobHandler>(DownloaderJobTypes.Redownload);
        services.AddJobHandler<UpdateYtDlpJobHandler>(DownloaderJobTypes.UpdateYtDlp);
        services.AddHttpClient(UpdateYtDlpJobHandler.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromMinutes(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DeeKeep/1.0");
        });
        return services;
    }

    public static WebApplication MapDownloaderService(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/downloads", async (EnqueueDownloadsRequest request, JobStore jobs, DownloaderStore store, LibraryClient library,
            DownloaderSettings settings, ILoggerFactory logs, CancellationToken ct) =>
        {
            var batch = request.BatchId ?? Guid.NewGuid().ToString("N");
            var export = settings.Export(request.Options);
            // Réservation avant tout démarrage : le décompte « Divers » connaît tout le lot.
            try
            {
                await library.ReserveBatchAsync(new ReserveBatchRequest(batch, request.Tracks.Select(t => new DestinationRequest(
                    t.Title, t.Artist, t.Album, null, null, null, null, null, null, "." + export.Format.ToLowerInvariant(), batch)).ToList()), ct);
            }
            catch (ServiceUnavailableException ex)
            {
                logs.CreateLogger("Downloads").LogWarning("Réservation du lot impossible : {Error}", ex.Message);
            }
            var created = request.Tracks.Select(t => jobs.Enqueue(new EnqueueJobRequest(DownloaderJobTypes.Download, $"{t.Artist} - {t.Title}",
                Node(new DownloadJobPayload(t, request.Options, batch)), 0, batch, MaxAttempts: 5))).ToList();
            if (request.PendingIds is { Count: > 0 }) store.RemovePending(request.PendingIds);
            return created;
        });

        api.MapPost("/conversions", (EnqueueConversionsRequest request, JobStore jobs) =>
        {
            var batch = request.BatchId ?? Guid.NewGuid().ToString("N");
            return request.Items.Select(i => jobs.Enqueue(new EnqueueJobRequest(DownloaderJobTypes.Convert, Path.GetFileName(i.LocalPath),
                Node(new ConvertJobPayload(i, request.Options, batch)), 0, batch, MaxAttempts: 5))).ToList();
        });

        api.MapPost("/redownloads", (EnqueueRedownloadsRequest request, JobStore jobs) =>
        {
            var batch = request.BatchId ?? Guid.NewGuid().ToString("N");
            return request.Items.Select(i => jobs.Enqueue(new EnqueueJobRequest(DownloaderJobTypes.Redownload, $"{i.Artist} - {i.Title}",
                Node(new RedownloadJobPayload(i, request.Options, batch)), 0, batch, MaxAttempts: 5))).ToList();
        });

        api.MapPost("/purge", (string type, bool? succeededOnly, DownloaderStore store) => store.Purge(type, succeededOnly ?? true));
        api.MapPost("/remove", (List<Guid> ids, DownloaderStore store) => store.Remove(ids));

        api.MapGet("/jobs", (string type, int? limit, JobStore jobs) => jobs.List(false, Math.Clamp(limit ?? 2000, 1, 5000), type));
        api.MapPost("/retry", (List<Guid> ids, JobStore jobs) =>
            ids.Select(jobs.Requeue).Where(j => j is not null).Select(j => j!).ToList());

        api.MapGet("/pending", (DownloaderStore store) => store.ListPending());
        api.MapPost("/pending", (List<DeezerTrack> tracks, DownloaderStore store) => store.AddPending(tracks));
        api.MapPost("/pending/remove", (List<Guid> ids, DownloaderStore store) => store.RemovePending(ids));

        api.MapGet("/export-options", (DownloaderSettings settings) => new ExportChoices(ExportFormats.Choices, settings.Export()));
        api.MapGet("/tools", (DownloaderToolStatus tools, CancellationToken ct) => tools.GetAsync(ct));
        api.MapPost("/tools/update", (JobStore jobs) =>
            jobs.Enqueue(new EnqueueJobRequest(DownloaderJobTypes.UpdateYtDlp, "Mise à jour de yt-dlp", null, 5, null, MaxAttempts: 2)));
        return app;
    }

    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
}
