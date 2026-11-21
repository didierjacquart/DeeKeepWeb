using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// <c>usbsync.run</c> — synchronisation incrémentale dans les deux sens entre la bibliothèque
/// (NAS) et le disque USB (règles : <see cref="UsbSyncPlanner"/>). Chaque fichier est traité
/// indépendamment : un échec n'arrête pas le reste, une reprise ne recopie rien. Les fichiers
/// arrivés dans la bibliothèque sont ensuite lus par une relecture de la bibliothèque.
/// </summary>
public sealed class UsbSyncHandler(UsbSyncDisk disk, UsbSyncStore store, LibraryClient library, WebDb webDb, ILogger<UsbSyncHandler> logger)
    : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        if (!disk.Enabled) throw new PermanentJobException("Synchronisation non configurée (Sync:UsbRoot).");
        var usb = disk.UsbRoot!;
        var lib = disk.LibraryRoot ?? throw new PermanentJobException("Dossier de la bibliothèque inconnu (Sync:LibraryRoot ou dossier de dépôt).");
        if (!disk.UsbPresent()) throw new PermanentJobException($"Disque USB absent ({usb}) : branchez-le, la synchronisation partira d'elle-même.");
        if (!disk.LibraryPresent()) throw new PermanentJobException($"Bibliothèque inaccessible ({lib}) : le NAS est-il joignable ?");

        context.Report(0, 0, "Inventaire de la bibliothèque…", force: true);
        disk.CleanParts(lib, ct);
        disk.CleanParts(usb, ct);
        var unreadable = new List<string>();
        var libraryFiles = await Task.Run(() => disk.Inventory(lib, unreadable, ct), ct);
        context.Report(0, 0, $"Inventaire du disque USB… ({libraryFiles.Count} fichiers dans la bibliothèque)", force: true);
        var usbFiles = await Task.Run(() => disk.Inventory(usb, unreadable, ct), ct);
        foreach (var u in unreadable) context.Item("unreadable:" + u, "warning", "Dossier illisible : " + u);

        var actions = UsbSyncPlanner.Plan(libraryFiles, usbFiles, store.Tombstones(), store.SyncedPairs());
        var work = actions.Where(a => a.Kind != SyncActionKind.ForgetTombstone).ToList();
        store.ForgetTombstones(actions.Where(a => a.Kind == SyncActionKind.ForgetTombstone).Select(a => a.Relative));
        logger.LogInformation("Synchronisation USB : {Lib} fichiers dans la bibliothèque, {Usb} sur le disque, {Work} opérations",
            libraryFiles.Count, usbFiles.Count, work.Count);

        var totalBytes = work.Where(a => a.Kind is not (SyncActionKind.TrashOnUsb or SyncActionKind.TrashOnLibrary)).Sum(a => a.Bytes);
        if (disk.UsbSpace() is { } space)
        {
            var toUsb = work.Where(a => a.Kind is SyncActionKind.CopyToUsb or SyncActionKind.ReplaceOnUsb).Sum(a => a.Bytes);
            if (toUsb > space.Free) throw new PermanentJobException(
                $"Place insuffisante sur le disque USB : {Human(toUsb)} à copier, {Human(space.Free)} libres.");
        }

        var fat = disk.UsbIsFat();
        var stamp = DateTime.Now.ToString(UsbSyncDisk.StampFormat);
        int done = 0, failed = 0, toUsbCount = 0, toLibCount = 0, replaced = 0, trashed = 0;
        long bytes = 0;
        foreach (var action in work)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (action.Kind)
                {
                    case SyncActionKind.CopyToUsb:
                        bytes += await disk.CopyAsync(lib, usb, action.Relative, fat, ct);
                        toUsbCount++;
                        break;
                    case SyncActionKind.CopyToLibrary:
                        bytes += await disk.CopyAsync(usb, lib, action.Relative, false, ct);
                        toLibCount++;
                        break;
                    case SyncActionKind.ReplaceOnUsb:
                        disk.MoveToTrash(usb, action.Relative, stamp);
                        bytes += await disk.CopyAsync(lib, usb, action.Relative, fat, ct);
                        replaced++;
                        break;
                    case SyncActionKind.ReplaceOnLibrary:
                        disk.MoveToTrash(lib, action.Relative, stamp);
                        bytes += await disk.CopyAsync(usb, lib, action.Relative, false, ct);
                        replaced++;
                        break;
                    case SyncActionKind.TrashOnUsb:
                        disk.MoveToTrash(usb, action.Relative, stamp);
                        trashed++;
                        break;
                    case SyncActionKind.TrashOnLibrary:
                        disk.MoveToTrash(lib, action.Relative, stamp);
                        trashed++;
                        break;
                }
                // État des deux côtés après l'opération : le passage suivant les saura identiques.
                store.SavePair(action.Relative, UsbSyncDisk.Stat(lib, action.Relative), UsbSyncDisk.Stat(usb, action.Relative));
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                failed++;
                context.Item(action.Relative, "failed", $"{action.Relative} — {ex.Message}");
                logger.LogWarning("Synchronisation USB : {Path} — {Error}", action.Relative, ex.Message);
            }
            context.Report(done, work.Count, $"{Verb(action.Kind)} {action.Relative} ({Human(bytes)} / {Human(totalBytes)})", failed);
        }

        // Même durée que la corbeille de la bibliothèque ; 0 = rien n'est effacé automatiquement.
        if (TrashRetention.Get(webDb) is var days and > 0) disk.PurgeTrash(usb, days, DateTime.Now);
        var message = work.Count == 0
            ? $"Déjà à jour ({libraryFiles.Count} fichiers des deux côtés)."
            : $"{toUsbCount} copié(s) vers le disque, {toLibCount} vers la bibliothèque, {replaced} remplacé(s), {trashed} mis à la corbeille" +
              (failed > 0 ? $", {failed} en échec." : ".");
        var summary = new UsbSyncSummary(Clock.UnixNow(), Math.Max(libraryFiles.Count, usbFiles.Count), toUsbCount, toLibCount, replaced, trashed,
            failed, bytes, message);
        store.SaveSummary(summary);

        // Fichiers arrivés ou remplacés dans la bibliothèque : la relecture les enregistre (tags, jaquettes).
        if (toLibCount > 0 || work.Any(a => a.Kind is SyncActionKind.ReplaceOnLibrary or SyncActionKind.TrashOnLibrary))
        {
            try { await library.StartLibraryScanAsync(ct); }
            catch (ServiceUnavailableException ex) { logger.LogWarning("Relecture de la bibliothèque non lancée : {Error}", ex.Message); }
        }
        context.Report(done, work.Count, message, failed, force: true);
        return JsonSerializer.SerializeToNode(summary, JsonDefaults.Options);
    }

    private static string Verb(SyncActionKind kind) => kind switch
    {
        SyncActionKind.CopyToUsb => "Vers le disque :",
        SyncActionKind.CopyToLibrary => "Vers la bibliothèque :",
        SyncActionKind.ReplaceOnUsb or SyncActionKind.ReplaceOnLibrary => "Remplacé :",
        _ => "Corbeille :",
    };

    private static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} Go",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} Mo",
        _ => $"{bytes / 1024.0:0} Ko",
    };
}
