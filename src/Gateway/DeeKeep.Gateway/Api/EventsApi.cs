using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Quality;
using DeeKeep.Contracts.Storage;

namespace DeeKeep.Gateway.Api;

/// <summary>
/// Diffusion en mémoire des événements des services vers les navigateurs connectés.
/// Non durable : un client qui se reconnecte recharge ses données, il n'a pas besoin
/// des événements manqués.
/// </summary>
public sealed class EventHub(ILogger<EventHub> logger)
{
    private readonly Lock _gate = new();
    private readonly List<Channel<string>> _clients = [];

    public int ClientCount
    {
        get { lock (_gate) return _clients.Count; }
    }

    public ChannelReader<string> Connect(out Action disconnect)
    {
        // Un navigateur trop lent perd les événements les plus anciens plutôt que de freiner la passerelle.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_gate) _clients.Add(channel);
        disconnect = () =>
        {
            lock (_gate) _clients.Remove(channel);
            channel.Writer.TryComplete();
        };
        return channel.Reader;
    }

    /// <summary>Publie un événement de service (charge utile remise au format du client).</summary>
    public void Publish(ServiceEvent evt)
    {
        string frame;
        try
        {
            var payload = ClientPayload(evt);
            frame = $"event: {evt.Type}\ndata: {JsonSerializer.Serialize(payload, ApiSetup.ClientJson)}\n\n";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Événement {Type} illisible, ignoré", evt.Type);
            return;
        }
        lock (_gate)
        {
            foreach (var client in _clients) client.Writer.TryWrite(frame);
        }
    }

    /// <summary>
    /// Les services publient leurs charges utiles avec leurs propres conventions (énumérations
    /// « Queued ») ; elles sont relues dans leur type puis réécrites comme le reste de l'API.
    /// </summary>
    private static object? ClientPayload(ServiceEvent evt)
    {
        var data = evt.Data;
        if (data is null) return null;
        T Read<T>() => data.Deserialize<T>(JsonDefaults.Options)!;
        return evt.Type switch
        {
            EventTypes.JobUpdated => Read<JobDto>(),
            EventTypes.JobItem => Read<JobItemEvent>(),
            EventTypes.TracksChanged or EventTypes.TracksRemoved or EventTypes.HistoryChanged => Read<TracksChangedEvent>(),
            EventTypes.PresenceChanged => Read<PresenceChangedEvent>(),
            EventTypes.ArchiveUpdated => Read<ArchiveUpdatedEvent>(),
            EventTypes.SettingsChanged => (data as JsonObject)?["values"]?.DeepClone(),
            EventTypes.QualityUpdated => QualityUpdate.From(Read<QualityUpdatedEvent>()),
            DownloaderEvents.QueueChanged => Read<DownloadQueueChanged>(),
            _ => data,
        };
    }
}

public static class EventsApi
{
    /// <summary>
    /// <c>GET /api/events</c> : flux SSE de tous les événements (<c>event: job.updated</c>,
    /// <c>data: {…}</c>). Un commentaire part toutes les 15 s pour garder la connexion ouverte.
    /// </summary>
    public static void MapEventsApi(this RouteGroupBuilder api)
    {
        api.MapGet("/events", async (HttpContext ctx, EventHub hub, IHostApplicationLifetime lifetime) =>
        {
            // Fermé aussi à l'arrêt de la passerelle : un navigateur resté ouvert ne doit pas la retenir
            // (il se reconnecte de lui-même au redémarrage).
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, lifetime.ApplicationStopping);
            var ct = stop.Token;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache, no-transform";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var reader = hub.Connect(out var disconnect);
            try
            {
                await ctx.Response.WriteAsync("retry: 3000\n: connecté\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    beat.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        while (await reader.WaitToReadAsync(beat.Token))
                        {
                            while (reader.TryRead(out var frame)) await ctx.Response.WriteAsync(frame, ct);
                            await ctx.Response.Body.FlushAsync(ct);
                        }
                        return; // flux fermé
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Navigateur parti.
            }
            finally
            {
                disconnect();
            }
        });
    }
}
