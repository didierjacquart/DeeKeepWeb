using System.Globalization;
using DeeKeep.Common.Data;
using DeeKeep.Common.Files;
using Microsoft.Extensions.Options;

namespace DeeKeep.Storage.UsbSync;

/// <summary>
/// Accès disque de la synchronisation : racines, présence du disque USB, inventaire,
/// copie sûre (fichier temporaire puis renommage, date d'origine conservée), corbeilles.
/// </summary>
public sealed class UsbSyncDisk(IOptionsMonitor<UsbSyncOptions> options, DeeKeepDb db, PathMap pathMap, ILogger<UsbSyncDisk> logger)
{
    /// <summary>Corbeille de chaque côté : même dossier que celle de la bibliothèque.</summary>
    public const string TrashFolder = ".deekeep-trash";

    public const string StampFormat = "yyyyMMdd-HHmmss";

    private const string PartSuffix = ".deekeep-part";

    /// <summary>FAT32 refuse les fichiers de 4 Gio ou plus.</summary>
    private const long FatMaxFile = (1L << 32) - 1;

    public UsbSyncOptions Options => options.CurrentValue;

    public bool Enabled => !string.IsNullOrWhiteSpace(Options.UsbRoot);

    public string? UsbRoot => string.IsNullOrWhiteSpace(Options.UsbRoot) ? null : Path.GetFullPath(Options.UsbRoot);

    /// <summary>Racine de la bibliothèque : configuration, sinon dossier de dépôt des Paramètres.</summary>
    public string? LibraryRoot
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Options.LibraryRoot)) return Path.GetFullPath(Options.LibraryRoot);
            try
            {
                var node = db.AllSettings().GetValueOrDefault("download_dir");
                var dir = node is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                return string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(pathMap.ToLocal(dir));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Dossier de dépôt illisible");
                return null;
            }
        }
    }

    /// <summary>
    /// Le disque est-il branché et monté ? Lire le dossier déclenche le montage automatique ;
    /// sous Linux, le dossier doit alors être un vrai point de montage (sinon on écrirait
    /// sur la carte SD, dans le dossier vide qui attend le disque).
    /// </summary>
    public bool UsbPresent()
    {
        var root = UsbRoot;
        if (root is null) return false;
        try
        {
            if (!Directory.Exists(root)) return false;
            _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return !OperatingSystem.IsLinux() || IsMountPoint(root);
    }

    public bool LibraryPresent() => LibraryRoot is { } root && Directory.Exists(root);

    private static bool IsMountPoint(string path)
    {
        try
        {
            var target = path.TrimEnd('/');
            foreach (var line in File.ReadLines("/proc/self/mounts"))
            {
                var parts = line.Split(' ');
                if (parts.Length < 3) continue;
                if (Unescape(parts[1]) == target && parts[2] != "autofs") return true;
            }
        }
        catch (IOException) { }
        return false;
    }

    private static string Unescape(string mountField) =>
        mountField.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");

    /// <summary>Place libre et totale du disque USB.</summary>
    public (long Free, long Total)? UsbSpace()
    {
        if (UsbRoot is not { } root) return null;
        try
        {
            var drive = new DriveInfo(root);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Inventaire d'un côté : fichiers de musique et jaquettes, hors dossiers cachés, corbeilles,
    /// dossiers système et exclus. Un dossier illisible est sauté et signalé, jamais fatal.
    /// </summary>
    public List<SyncFile> Inventory(string root, List<string> unreadable, CancellationToken ct)
    {
        var extensions = new HashSet<string>(Options.Extensions.Select(e => e.StartsWith('.') ? e : "." + e), StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(Options.Exclude, StringComparer.OrdinalIgnoreCase);
        var files = new List<SyncFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable.Add($"{Path.GetRelativePath(root, dir)} — {ex.Message}");
                continue;
            }
            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo sub)
                {
                    if (Skipped(sub.Name) || (dir == root && exclude.Contains(sub.Name))) continue;
                    if (sub.LinkTarget is not null) continue;
                    pending.Push(sub.FullName);
                }
                else if (entry is FileInfo file && extensions.Contains(file.Extension) && !file.Name.StartsWith('.'))
                {
                    try
                    {
                        files.Add(new SyncFile(Path.GetRelativePath(root, file.FullName).Replace('\\', '/'), file.Length, file.LastWriteTimeUtc));
                    }
                    catch (IOException) { }
                }
            }
        }
        return files;
    }

    private static bool Skipped(string name) =>
        name.StartsWith('.') || name.StartsWith('$') || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("#recycle", StringComparison.OrdinalIgnoreCase) || name.Equals("@eaDir", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copie <paramref name="relative"/> de <paramref name="fromRoot"/> vers <paramref name="toRoot"/> :
    /// fichier temporaire puis renommage (une coupure ne laisse jamais un fichier tronqué sous le
    /// vrai nom), date de modification d'origine conservée. Rend les octets copiés.
    /// </summary>
    public async Task<long> CopyAsync(string fromRoot, string toRoot, string relative, bool targetIsFat, CancellationToken ct)
    {
        var source = Combine(fromRoot, relative);
        var target = Combine(toRoot, relative);
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("fichier disparu pendant la synchronisation", source);
        if (targetIsFat && info.Length > FatMaxFile) throw new IOException("plus de 4 Go : trop gros pour un disque FAT32");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = target + PartSuffix;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await input.CopyToAsync(output, 1 << 20, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(part, target, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
        await KeepDateAsync(target, info.LastWriteTimeUtc, ct);
        return info.Length;
    }

    /// <summary>
    /// Donne à la copie la date de l'original, et vérifie qu'elle a tenu. Sur un partage SMB, le
    /// client Linux diffère la fermeture du fichier écrit : le NAS y remet alors l'heure courante,
    /// après coup. Sans date juste, la copie passerait pour « plus récente » et remplacerait
    /// l'original au passage suivant.
    /// </summary>
    private async Task KeepDateAsync(string path, DateTime modifiedUtc, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            File.SetLastWriteTimeUtc(path, modifiedUtc);
            if ((File.GetLastWriteTimeUtc(path) - modifiedUtc).Duration() <= TimeSpan.FromSeconds(2)) return;
            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
        }
        // La mémoire des paires synchronisées (UsbSyncStore) évite quand même un faux « plus récent ».
        logger.LogWarning("Date d'origine non conservée sur {Path} (montage SMB sans closetimeo=0 ?)", path);
    }

    /// <summary>État actuel d'un fichier (taille, date), null s'il n'existe pas.</summary>
    public static SyncFile? Stat(string root, string relative)
    {
        var info = new FileInfo(Combine(root, relative));
        return info.Exists ? new SyncFile(relative, info.Length, info.LastWriteTimeUtc) : null;
    }

    /// <summary>Déplace un fichier dans la corbeille de son côté (<c>.deekeep-trash/&lt;horodatage&gt;/…</c>).</summary>
    public void MoveToTrash(string root, string relative, string stamp)
    {
        var source = Combine(root, relative);
        if (!File.Exists(source)) return;
        var target = Combine(Path.Join(root, TrashFolder, stamp), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
            target = Path.Join(Path.GetDirectoryName(target), $"{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}{Path.GetExtension(target)}");
        File.Move(source, target);
    }

    /// <summary>Efface les corbeilles plus anciennes que <paramref name="days"/> jours ; rend le nombre de dossiers effacés.</summary>
    public int PurgeTrash(string root, int days, DateTime now)
    {
        var trash = Path.Join(root, TrashFolder);
        if (!Directory.Exists(trash)) return 0;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(trash))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dir), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) continue;
            if (now - at < TimeSpan.FromDays(days)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Corbeille {Dir} non effacée", dir);
            }
        }
        return removed;
    }

    /// <summary>Fichiers temporaires laissés par une copie interrompue.</summary>
    public void CleanParts(string root, CancellationToken ct)
    {
        try
        {
            foreach (var part in Directory.EnumerateFiles(root, "*" + PartSuffix,
                         new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                ct.ThrowIfCancellationRequested();
                TryDelete(part);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Le disque USB est-il en FAT (taille de fichier bornée) ?</summary>
    public bool UsbIsFat()
    {
        if (UsbRoot is not { } root) return false;
        try
        {
            var format = new DriveInfo(root).DriveFormat;
            return format.Contains("fat", StringComparison.OrdinalIgnoreCase) || format.Contains("msdos", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Chemin sous une racine, sans pouvoir en sortir (« .. » refusé).</summary>
    public static string Combine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Join(root, relative));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException($"chemin hors de la racine : {relative}");
        return full;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
