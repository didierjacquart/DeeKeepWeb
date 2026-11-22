using System.Collections.Concurrent;
using System.Globalization;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Storage;
using Microsoft.Extensions.Options;

namespace DeeKeep.Storage;

/// <summary>Section <c>Archive</c> de la configuration : archivage automatique.</summary>
public sealed class ArchiveAutoOptions
{
    /// <summary>Archiver les titres nouveaux ou modifiés, une fois la bibliothèque calme ce nombre de secondes ; 0 = jamais.</summary>
    public int AfterChangesSeconds { get; set; } = 60;

    /// <summary>Heure du passage quotidien qui rattrape tout titre manquant (« HH:mm ») ; vide = pas de passage.</summary>
    public string? NightlyAt { get; set; } = "04:00";
}

/// <summary>
/// Archivage automatique : l'archive (copie exacte du son de chaque titre) suit la bibliothèque sans
/// qu'on ait à cocher puis « Archiver ». Les titres enregistrés par Library (téléchargement,
/// conversion, rangement, tags, restauration) partent une minute après le dernier changement ;
/// un passage quotidien rattrape le reste (événement perdu, fichier venu du disque USB). Un titre
/// déjà à jour est sauté sans relecture (date et taille) : ces passages coûtent peu.
/// </summary>
public sealed class ArchiveAuto(ArchiveSettings settings, ArchiveService archive, ArchiveIndex index, JobStore jobs,
    IOptionsMonitor<ArchiveAutoOptions> options, ILogger<ArchiveAuto> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private long _lastChange;

    /// <summary>Raison d'un <c>tracks.changed</c> où un fichier de la bibliothèque a été écrit (<c>register:&lt;origine&gt;</c>).</summary>
    public static bool IsFileWrite(string? reason) => reason is not null && reason.StartsWith("register:", StringComparison.Ordinal);

    /// <summary>Titres (chemins de la base) à archiver au prochain passage.</summary>
    public void Add(IEnumerable<string> dbPaths)
    {
        if (options.CurrentValue.AfterChangesSeconds <= 0) return;
        foreach (var p in dbPaths) _pending[p] = 0;
        Interlocked.Exchange(ref _lastChange, Environment.TickCount64);
    }

    /// <summary>Le délai de calme est-il écoulé depuis le dernier changement ?</summary>
    public static bool QuietFor(long lastChange, long now, int seconds) => now - lastChange >= seconds * 1000L;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateOnly? lastNightly = null;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var o = options.CurrentValue;
                    if (!_pending.IsEmpty && QuietFor(Interlocked.Read(ref _lastChange), Environment.TickCount64, o.AfterChangesSeconds))
                    {
                        var paths = _pending.Keys.ToList();
                        foreach (var p in paths) _pending.TryRemove(p, out _);
                        Archive(paths, "nouveaux titres");
                    }
                    var now = DateTime.Now;
                    if (NightlyDue(o.NightlyAt, now, lastNightly))
                    {
                        lastNightly = DateOnly.FromDateTime(now);
                        Archive(index.Tracks().Select(t => t.Path).ToList(), "passage quotidien");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Archivage automatique");
                }
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt du service
        }
    }

    private void Archive(IReadOnlyList<string> dbPaths, string reason)
    {
        if (!settings.Get().Configured) return;
        var onDisk = archive.OnDisk(dbPaths);
        if (onDisk.Count == 0) return;
        StorageService.Enqueue(jobs, StorageJobTypes.Archive, $"Archivage automatique de {onDisk.Count} titre(s) ({reason})",
            new ArchiveRequest(onDisk));
        logger.LogInformation("Archivage automatique ({Reason}) : {Count} titre(s) en file", reason, onDisk.Count);
    }

    /// <summary>L'heure du passage quotidien est-elle dépassée sans qu'il ait eu lieu aujourd'hui (fenêtre de 2 h) ?</summary>
    public static bool NightlyDue(string? at, DateTime now, DateOnly? lastRun)
    {
        if (string.IsNullOrWhiteSpace(at) || !TimeOnly.TryParseExact(at, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        var today = DateOnly.FromDateTime(now);
        var clock = TimeOnly.FromDateTime(now);
        return lastRun != today && clock >= time && clock < time.AddHours(2);
    }
}
