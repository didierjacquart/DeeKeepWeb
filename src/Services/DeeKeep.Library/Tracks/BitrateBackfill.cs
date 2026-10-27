using System.Collections.Concurrent;
using System.Threading.Channels;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Contracts;

namespace DeeKeep.Library.Tracks;

/// <summary>
/// Relecture du débit des titres dont la base l'ignore (<c>probe_bitrate</c>, spec 02 §3.5).
/// L'original la refaisait à chaque affichage sans l'enregistrer ; ici elle est faite une
/// fois, en tâche de fond, et la ligne est mise à jour (colonne FORMAT complète, jauge juste).
/// </summary>
public sealed class BitrateBackfill(DeeKeepDb db, PathMap map, EventLog events, ILogger<BitrateBackfill> logger) : BackgroundService
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    // Un fichier dont l'en-tête ne dit rien n'est pas relu à chaque chargement de la page.
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);

    public void Enqueue(IEnumerable<string> dbPaths)
    {
        foreach (var path in dbPaths)
            if (_seen.TryAdd(path, 0)) _queue.Writer.TryWrite(path);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<string>();
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < 200 && _queue.Reader.TryRead(out var path)) batch.Add(path);
                try
                {
                    await Task.Run(() => Process(batch, stoppingToken), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Relecture des débits interrompue");
                }
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt du service : les débits restants seront relus au prochain chargement.
        }
    }

    private void Process(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var local = map.ToLocal(path);
            if (!File.Exists(local)) continue;
            var rate = AudioTags.Read(local, withProperties: true, withCover: false).Bitrate;
            if (rate <= 0) continue;
            using var conn = db.Open(readOnly: true);
            var row = conn.QueryRows("SELECT * FROM tracks WHERE path = @p AND deleted = 0", new { p = path }).FirstOrDefault();
            if (row is null || (row.Long("bitrate") ?? 0) != 0) continue;
            var updated = TrackWriter.Content(row);
            updated["bitrate"] = (long)rate;
            rows.Add(updated);
        }
        if (rows.Count == 0) return;
        db.UpsertMany(Schema.Tracks, rows);
        logger.LogInformation("Débit relu pour {Count} titre(s)", rows.Count);
        events.Publish(EventTypes.TracksChanged, new TracksChangedEvent(rows.Select(r => (string)r["path"]!).ToList(), "bitrate"));
    }
}
