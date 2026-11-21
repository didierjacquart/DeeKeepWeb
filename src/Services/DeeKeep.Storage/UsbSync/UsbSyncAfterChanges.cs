using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Synchronisation après les changements de la bibliothèque (téléchargement, conversion,
/// rangement, tags, restauration) : une synchronisation part quand la bibliothèque est restée
/// calme <see cref="UsbSyncOptions.AfterChangesSeconds"/> secondes, pour qu'un album entier
/// parte en un seul passage. Disque absent : rien, le branchement s'en chargera.
/// </summary>
public sealed class UsbSyncAfterChanges(UsbSyncDisk disk, JobStore jobs, ILogger<UsbSyncAfterChanges> logger) : IDisposable
{
    public enum Decision { Skip, Enqueue, Later }

    private readonly Lock _lock = new();
    private Timer? _timer;

    /// <summary>Raison d'un <c>tracks.changed</c> qui touche un fichier de la bibliothèque (<c>register:&lt;origine&gt;</c>).</summary>
    public static bool IsLibraryFileChange(string? reason) =>
        reason is not null && reason.StartsWith("register:", StringComparison.Ordinal);

    /// <summary>
    /// Que faire à l'échéance : rien si le disque ou la bibliothèque manque ; plus tard si une
    /// synchronisation tourne déjà (elle a pu relever les dossiers avant les nouveaux fichiers) ;
    /// sinon en mettre une en file (une déjà en attente les verra).
    /// </summary>
    public static Decision Decide(bool usbPresent, bool libraryPresent, JobStatus? active) =>
        !usbPresent || !libraryPresent ? Decision.Skip
        : active == JobStatus.Running ? Decision.Later
        : Decision.Enqueue;

    /// <summary>Signale un changement : (re)lance le délai de calme.</summary>
    public void Touch()
    {
        var delay = disk.Options.AfterChangesSeconds;
        if (!disk.Enabled || delay <= 0) return;
        lock (_lock)
        {
            _timer ??= new Timer(_ => Fire());
            _timer.Change(TimeSpan.FromSeconds(delay), Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        try
        {
            var active = jobs.List(activeOnly: true, type: SyncJobTypes.Run).FirstOrDefault();
            switch (Decide(disk.UsbPresent(), disk.LibraryPresent(), active?.Status))
            {
                case Decision.Enqueue:
                    UsbSyncService.Enqueue(jobs, "nouveaux titres");
                    break;
                case Decision.Later:
                    Touch();
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Synchronisation USB après changements");
        }
    }

    public void Dispose() => _timer?.Dispose();
}
