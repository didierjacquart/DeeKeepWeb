using System.Globalization;
using DeeKeep.Common.Events;
using DeeKeep.Common.Web;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Sync;

namespace DeeKeep.Library.Files;

/// <summary>
/// Corbeille de la bibliothèque : <c>&lt;racine&gt;/.deekeep-trash/&lt;horodatage&gt;/&lt;chemin relatif&gt;</c>.
/// Un fichier supprimé depuis l'application y reste le nombre de jours réglé dans les Paramètres
/// (<see cref="TrashRetention"/> ; 0 = jusqu'à ce qu'on l'efface), récupérable. Le dossier fait foi.
/// </summary>
public sealed class TrashService(
    LibraryPaths paths, FileRegistrar registrar, EventLog events, WebDb webDb, ILogger<TrashService> logger)
{
    public const string StampFormat = "yyyyMMdd-HHmmss";

    private string Root => paths.LocalDownloadDir();

    private string TrashRoot => Path.Join(Root, LibraryPaths.TrashFolder);

    public TrashSettings Settings() => new(TrashRetention.Get(webDb));

    public TrashSettings SaveSettings(TrashSettings settings) => new(TrashRetention.Set(webDb, settings.RetentionDays));

    /// <summary>Fichiers de la corbeille, les plus récents d'abord.</summary>
    public IReadOnlyList<TrashEntry> List()
    {
        var result = new List<TrashEntry>();
        if (!Directory.Exists(TrashRoot)) return result;
        var days = TrashRetention.Get(webDb);
        foreach (var stampDir in Directory.EnumerateDirectories(TrashRoot))
        {
            if (ParseStamp(Path.GetFileName(stampDir)) is not { } deletedAt) continue;
            foreach (var file in SafeFiles(stampDir))
            {
                var relative = Path.GetRelativePath(stampDir, file).Replace('\\', '/');
                long size;
                try { size = new FileInfo(file).Length; }
                catch (IOException) { continue; }
                var original = Path.Join(Root, relative);
                result.Add(new TrashEntry(
                    $"{Path.GetFileName(stampDir)}/{relative}", paths.ToDb(original), Path.GetFileName(relative),
                    Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "", deletedAt.ToUnixTimeMilliseconds() / 1000.0,
                    days == 0 ? null : deletedAt.AddDays(days).ToUnixTimeMilliseconds() / 1000.0, size));
            }
        }
        return result.OrderByDescending(e => e.DeletedAt).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Remet des fichiers à leur place d'origine et dans la bibliothèque ; un emplacement occupé est refusé.</summary>
    public async Task<TrashResult> RestoreAsync(IReadOnlyList<string> ids, string? userId, CancellationToken ct)
    {
        var errors = new List<string>();
        var restored = new List<string>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (Resolve(id) is not { } item)
            {
                errors.Add($"{id} — introuvable dans la corbeille");
                continue;
            }
            var target = Path.Join(Root, item.Relative);
            try
            {
                if (File.Exists(target)) throw new IOException("un fichier existe déjà à cet emplacement");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(item.File, target);
                // Restauré par un utilisateur : le titre revient dans sa bibliothèque (sinon : à l'administrateur).
                await registrar.RegisterAsync(new RegisterFileRequest(target, "restore", AddToHistory: false, UserId: userId), ct);
                restored.Add(item.Relative);
                logger.LogInformation("Restauré de la corbeille : {Path}", target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(item.Relative)} — {ex.Message}");
            }
        }
        PruneEmpty();
        if (restored.Count > 0) events.Publish(TrashEvents.Restored, new FilesRestoredEvent(restored));
        return new TrashResult(restored.Count, errors);
    }

    /// <summary>Efface définitivement des entrées (toutes si la liste est vide).</summary>
    public TrashResult Purge(IReadOnlyList<string> ids)
    {
        var errors = new List<string>();
        var done = 0;
        var targets = ids.Count == 0 ? List().Select(e => e.Id).ToList() : ids.Distinct(StringComparer.Ordinal).ToList();
        foreach (var id in targets)
        {
            if (Resolve(id) is not { } item) continue;
            try
            {
                File.Delete(item.File);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(item.Relative)} — {ex.Message}");
            }
        }
        PruneEmpty();
        return new TrashResult(done, errors);
    }

    /// <summary>Efface ce qui a dépassé la durée de conservation (rien si elle vaut 0).</summary>
    public int PurgeExpired(DateTimeOffset now)
    {
        var days = TrashRetention.Get(webDb);
        if (days == 0 || !Directory.Exists(TrashRoot)) return 0;
        var removed = 0;
        foreach (var stampDir in Directory.EnumerateDirectories(TrashRoot))
        {
            if (ParseStamp(Path.GetFileName(stampDir)) is not { } at || now - at < TimeSpan.FromDays(days)) continue;
            try
            {
                removed += SafeFiles(stampDir).Count();
                Directory.Delete(stampDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Corbeille : {Dir} non effacé", stampDir);
            }
        }
        if (removed > 0) logger.LogInformation("Corbeille : {Count} fichier(s) de plus de {Days} jours effacé(s)", removed, days);
        return removed;
    }

    public static DateTimeOffset? ParseStamp(string name) =>
        DateTime.TryParseExact(name, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local)
            : null;

    /// <summary>Identifiant « horodatage/chemin relatif » → fichier, sans sortir de la corbeille.</summary>
    private (string File, string Relative)? Resolve(string id)
    {
        var slash = id.IndexOf('/');
        if (slash <= 0 || ParseStamp(id[..slash]) is null) return null;
        var relative = id[(slash + 1)..];
        var file = Path.GetFullPath(Path.Join(TrashRoot, id[..slash], relative));
        if (!LibraryPaths.IsUnder(file, Path.GetFullPath(TrashRoot)) || !File.Exists(file)) return null;
        return (file, relative);
    }

    private void PruneEmpty()
    {
        if (!Directory.Exists(TrashRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(TrashRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>Purge régulière de la corbeille (fichiers plus anciens que la durée de conservation).</summary>
public sealed class TrashPurger(TrashService trash, ILogger<TrashPurger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { trash.PurgeExpired(DateTimeOffset.Now); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Purge de la corbeille impossible"); }
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt du service
        }
    }
}
