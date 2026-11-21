using System.Globalization;
using System.Text.Json;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Synchronisation du disque USB : tâche <c>usbsync.run</c>, détection du branchement,
/// passage quotidien et passage après les changements de la bibliothèque, propagation
/// immédiate des suppressions faites dans l'application.
/// </summary>
public static class UsbSyncService
{
    private const string GroupKey = "usbsync";

    public static IServiceCollection AddUsbSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UsbSyncOptions>(configuration.GetSection("Sync"));
        services.AddSingleton<UsbSyncDisk>();
        services.AddSingleton<UsbSyncStore>();
        services.AddJobHandler<UsbSyncHandler>(SyncJobTypes.Run);
        services.AddHostedService<UsbSyncWatcher>();

        // Suppressions et restaurations faites dans l'application (corbeille de la bibliothèque).
        services.AddEventSubscription(ServiceNames.Library, (sp, evt, _) =>
        {
            var store = sp.GetRequiredService<UsbSyncStore>();
            if (evt.Type == TrashEvents.Trashed && evt.Data?.Deserialize<FilesTrashedEvent>(JsonDefaults.Options) is { } trashed)
            {
                var items = trashed.Files.Where(f => f.Relative is not null).Select(f => (f.Relative!, f.DeletedAt)).ToList();
                if (items.Count > 0)
                {
                    store.AddTombstones(items);
                    TrashOnUsbNow(sp, items.Select(i => i.Item1).ToList());
                }
            }
            else if (evt.Type == TrashEvents.Restored && evt.Data?.Deserialize<FilesRestoredEvent>(JsonDefaults.Options) is { } restored)
            {
                // Le fichier revient dans la bibliothèque : la prochaine synchronisation le recopiera sur le disque.
                store.ForgetTombstones(restored.Relatives);
            }
            return Task.CompletedTask;
        }, durable: true, TrashEvents.Trashed, TrashEvents.Restored);

        // Nouveaux fichiers ou fichiers modifiés dans la bibliothèque : synchronisation après un délai de calme.
        services.AddSingleton<UsbSyncAfterChanges>();
        services.AddEventSubscription(ServiceNames.Library, (sp, evt, _) =>
        {
            if (evt.Data?.Deserialize<TracksChangedEvent>(JsonDefaults.Options) is { } changed
                && UsbSyncAfterChanges.IsLibraryFileChange(changed.Reason))
                sp.GetRequiredService<UsbSyncAfterChanges>().Touch();
            return Task.CompletedTask;
        }, durable: false, EventTypes.TracksChanged);
        return services;
    }

    /// <summary>Disque branché : sa copie part tout de suite à sa corbeille ; sinon la pierre tombale attend le prochain passage.</summary>
    private static void TrashOnUsbNow(IServiceProvider sp, IReadOnlyList<string> relatives)
    {
        var disk = sp.GetRequiredService<UsbSyncDisk>();
        if (!disk.Enabled || !disk.UsbPresent()) return;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UsbSyncService));
        var stamp = DateTime.Now.ToString(UsbSyncDisk.StampFormat);
        foreach (var relative in relatives)
        {
            try { disk.MoveToTrash(disk.UsbRoot!, relative, stamp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning("Disque USB : {Path} non mis à la corbeille ({Error}) ; ce sera refait à la synchronisation", relative, ex.Message);
            }
        }
    }

    public static WebApplication MapUsbSync(this WebApplication app)
    {
        var api = app.MapGroup("/api/sync");
        api.MapGet("/status", (UsbSyncDisk disk, UsbSyncStore store, JobStore jobs) => Status(disk, store, jobs));
        api.MapPost("/run", (UsbSyncDisk disk, JobStore jobs) =>
        {
            if (!disk.Enabled) return Results.BadRequest("Synchronisation non configurée : renseignez Sync:UsbRoot.");
            if (!disk.UsbPresent()) return Results.BadRequest("Disque USB absent : branchez-le, la synchronisation partira d'elle-même.");
            return Results.Ok(Enqueue(jobs, "demande"));
        });
        return app;
    }

    private static UsbSyncStatus Status(UsbSyncDisk disk, UsbSyncStore store, JobStore jobs)
    {
        var usbPresent = disk.Enabled && disk.UsbPresent();
        var libraryPresent = disk.LibraryPresent();
        var space = usbPresent ? disk.UsbSpace() : null;
        string? problem = !disk.Enabled ? "Synchronisation non configurée (Sync:UsbRoot)."
            : !usbPresent ? "Disque USB absent."
            : !libraryPresent ? $"Bibliothèque inaccessible ({disk.LibraryRoot ?? "dossier inconnu"})."
            : null;
        return new UsbSyncStatus(disk.Enabled, disk.UsbRoot, disk.LibraryRoot, usbPresent, libraryPresent, space?.Free, space?.Total,
            store.TombstoneCount(), Active(jobs), store.LastSummary(), problem);
    }

    private static JobDto? Active(JobStore jobs) => jobs.List(activeOnly: true, type: SyncJobTypes.Run).FirstOrDefault();

    /// <summary>Met une synchronisation en file, sauf s'il y en a déjà une en attente ou en cours.</summary>
    public static JobDto Enqueue(JobStore jobs, string reason) =>
        Active(jobs) ?? jobs.Enqueue(new EnqueueJobRequest(SyncJobTypes.Run, $"Synchronisation du disque USB ({reason})", null,
            GroupKey: GroupKey, MaxAttempts: 1));
}

/// <summary>
/// Surveille le disque USB : une synchronisation part quand il apparaît (branchement, ou
/// démarrage du serveur disque branché) et chaque jour à l'heure prévue s'il est présent.
/// </summary>
public sealed class UsbSyncWatcher(UsbSyncDisk disk, JobStore jobs, ILogger<UsbSyncWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasPresent = false;
        DateOnly? lastNightly = null;
        try
        {
            // Laisse les autres services démarrer avant un premier passage.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (disk.Enabled)
                    {
                        var present = await Task.Run(disk.UsbPresent, stoppingToken);
                        if (present && !wasPresent)
                        {
                            logger.LogInformation("Disque USB détecté : {Root}", disk.UsbRoot);
                            if (disk.Options.OnPlug && disk.LibraryPresent()) UsbSyncService.Enqueue(jobs, "disque branché");
                        }
                        else if (!present && wasPresent) logger.LogInformation("Disque USB retiré : {Root}", disk.UsbRoot);
                        wasPresent = present;

                        var now = DateTime.Now;
                        if (present && NightlyDue(disk.Options.NightlyAt, now, lastNightly) && disk.LibraryPresent())
                        {
                            lastNightly = DateOnly.FromDateTime(now);
                            UsbSyncService.Enqueue(jobs, "passage quotidien");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Surveillance du disque USB");
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, disk.Options.PollSeconds)), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt du service
        }
    }

    /// <summary>L'heure du passage quotidien est-elle dépassée sans qu'il ait eu lieu aujourd'hui ?</summary>
    public static bool NightlyDue(string? at, DateTime now, DateOnly? lastRun)
    {
        if (string.IsNullOrWhiteSpace(at) || !TimeOnly.TryParseExact(at, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        var today = DateOnly.FromDateTime(now);
        return lastRun != today && TimeOnly.FromDateTime(now) >= time && TimeOnly.FromDateTime(now) < time.AddHours(2);
    }
}
