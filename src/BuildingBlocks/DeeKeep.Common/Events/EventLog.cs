using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeeKeep.Common.Events;

/// <summary>
/// Journal d'événements d'un service. Chaque événement est écrit dans
/// <c>deekeep_web.db</c> (rattrapage après coupure grâce à Last-Event-ID) puis
/// diffusé en mémoire aux flux SSE ouverts.
/// </summary>
public sealed class EventLog(WebDb webDb, ServiceIdentity identity, ILogger<EventLog> logger)
{
    private readonly object _subscribersLock = new();
    private readonly List<Channel<ServiceEvent>> _subscribers = [];

    public string Service => identity.Name;

    public ServiceEvent Publish(string type, object? data = null, string? key = null)
    {
        var node = data as JsonNode ?? (data is null ? null : JsonSerializer.SerializeToNode(data, JsonDefaults.Options));
        var at = Clock.UnixNow();
        long id;
        try
        {
            using var conn = webDb.Open();
            id = conn.ExecuteScalar<long>(
                "INSERT INTO events (service, type, key, data, at) VALUES (@service, @type, @key, @data, @at) RETURNING id",
                new { service = identity.Name, type, key, data = node?.ToJsonString(), at });
        }
        catch (Exception ex)
        {
            // Le journal est une commodité : un événement non persisté est tout de même diffusé.
            logger.LogWarning(ex, "Événement {Type} non persisté", type);
            id = -DateTime.UtcNow.Ticks;
        }

        var evt = new ServiceEvent(id, identity.Name, type, key, node, at);
        lock (_subscribersLock)
        {
            foreach (var channel in _subscribers) channel.Writer.TryWrite(evt);
        }
        return evt;
    }

    public IReadOnlyList<ServiceEvent> ReadAfter(long afterId, int limit = 1000)
    {
        using var conn = webDb.Open();
        return conn.Query<(long id, string service, string type, string? key, string? data, double at)>(
                "SELECT id, service, type, key, data, at FROM events WHERE service = @service AND id > @afterId ORDER BY id LIMIT @limit",
                new { service = identity.Name, afterId, limit })
            .Select(r => new ServiceEvent(r.id, r.service, r.type, r.key, r.data is null ? null : JsonNode.Parse(r.data), r.at))
            .ToList();
    }

    public long LastId()
    {
        using var conn = webDb.Open();
        return conn.ExecuteScalar<long?>("SELECT MAX(id) FROM events WHERE service = @service", new { service = identity.Name }) ?? 0;
    }

    public ChannelReader<ServiceEvent> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<ServiceEvent>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_subscribersLock) _subscribers.Add(channel);
        ct.Register(() =>
        {
            lock (_subscribersLock) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    internal void Prune(double olderThan, int keepAtLeast)
    {
        using var conn = webDb.Open();
        conn.Execute("""
            DELETE FROM events WHERE service = @service AND at < @olderThan
              AND id < (SELECT COALESCE(MIN(id), 0) FROM (SELECT id FROM events WHERE service = @service ORDER BY id DESC LIMIT @keepAtLeast))
            """, new { service = identity.Name, olderThan, keepAtLeast });
    }
}

/// <summary>Purge périodique du journal (une journée, au moins 5 000 événements gardés).</summary>
public sealed class EventLogPruner(EventLog log, ILogger<EventLogPruner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                log.Prune(Clock.UnixNow() - 86400, 5000);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Purge du journal d'événements différée");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
