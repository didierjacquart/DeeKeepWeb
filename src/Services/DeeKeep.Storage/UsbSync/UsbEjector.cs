using System.Diagnostics;
using System.Text;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Éjection du disque USB depuis l'application : le montage (et son montage automatique) est
/// arrêté par systemd, ce qui vide les tampons et démonte proprement ; le disque peut alors être
/// débranché. Au débranchement (ou sur « Remonter »), le montage automatique est réarmé.
/// Le service tourne sous l'utilisateur <c>deekeep</c> : une règle polkit
/// (<c>deekeep-usb-setup.sh</c>) l'autorise à démarrer et arrêter ces deux unités, et elles seules.
/// </summary>
public sealed class UsbEjector(UsbSyncDisk disk, JobStore jobs, ILogger<UsbEjector> logger)
{
    /// <summary>Nom d'unité systemd d'un chemin (<c>systemd-escape -p</c>) : <c>/mnt/deekeep-usb</c> → <c>mnt-deekeep\x2dusb</c>.</summary>
    public static string UnitName(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0) return "-";
        var sb = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '/') sb.Append('-');
            else if ((char.IsAsciiLetterOrDigit(c) || c is ':' or '_' || (c == '.' && i > 0))) sb.Append(c);
            else foreach (var b in Encoding.UTF8.GetBytes(c.ToString())) sb.Append($"\\x{b:x2}");
        }
        return sb.ToString();
    }

    private string Units(string suffix) => UnitName(disk.UsbRoot!) + suffix;

    /// <summary>Éjecte le disque ; rend un message d'erreur, ou null.</summary>
    public async Task<string?> EjectAsync(CancellationToken ct)
    {
        if (!disk.Enabled) return "Synchronisation non configurée : renseignez Sync:UsbRoot.";
        if (!OperatingSystem.IsLinux()) return "L'éjection n'est possible que sur le serveur Linux.";
        if (disk.EjectedDevice is not null) return "Le disque est déjà éjecté : vous pouvez le débrancher.";
        if (jobs.List(activeOnly: true, type: SyncJobTypes.Run).Any(j => j.Status == Contracts.JobStatus.Running))
            return "Synchronisation en cours : attendez sa fin (ou arrêtez-la) avant d'éjecter le disque.";
        if (!disk.UsbPresent() || UsbSyncDisk.MountSource(disk.UsbRoot!) is not { } device) return "Disque USB absent.";

        // Arrêter le montage automatique d'abord : sinon le moindre accès remonterait le disque.
        var (code, error) = await SystemctlAsync(ct, "stop", Units(".automount"), Units(".mount"));
        if (code != 0)
        {
            logger.LogWarning("Éjection du disque USB refusée : {Error}", error);
            // Le disque reste utilisable : le montage automatique est rétabli.
            await SystemctlAsync(ct, "start", Units(".automount"));
            return error.Contains("busy", StringComparison.OrdinalIgnoreCase) || error.Contains("occup", StringComparison.OrdinalIgnoreCase)
                ? "Disque occupé : fermez ce qui l'utilise (explorateur de fichiers, lecture en cours…) puis réessayez."
                : $"Éjection impossible : {error.Trim()}";
        }
        disk.MarkEjected(device);
        logger.LogInformation("Disque USB éjecté ({Device}) : il peut être débranché", device);
        return null;
    }

    /// <summary>Réarme le montage automatique (disque débranché, ou « Remonter »).</summary>
    public async Task<string?> MountAsync(CancellationToken ct)
    {
        if (!disk.Enabled) return "Synchronisation non configurée : renseignez Sync:UsbRoot.";
        if (!OperatingSystem.IsLinux()) return null;
        var (code, error) = await SystemctlAsync(ct, "start", Units(".automount"));
        if (code != 0) return $"Montage impossible : {error.Trim()}";
        disk.ClearEjected();
        logger.LogInformation("Montage automatique du disque USB réarmé");
        return null;
    }

    /// <summary>Disque éjecté puis débranché : le montage automatique est réarmé pour le prochain branchement.</summary>
    public async Task RearmIfUnpluggedAsync(CancellationToken ct)
    {
        if (disk.EjectedDevice is { } device && !File.Exists(device))
        {
            logger.LogInformation("Disque USB débranché après éjection");
            await MountAsync(ct);
        }
    }

    private static async Task<(int Code, string Error)> SystemctlAsync(CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("systemctl") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("systemctl introuvable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stderr);
    }
}
