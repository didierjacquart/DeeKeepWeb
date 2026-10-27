using System.Text;
using System.Text.Json;
using DeeKeep.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DeeKeep.Common.Events;

public static class EventStreamEndpoint
{
    /// <summary>
    /// <c>GET /internal/events?after=N</c> : flux SSE des événements du service.
    /// Rejoue d'abord ce qui a été manqué depuis <c>after</c> (ou l'en-tête
    /// <c>Last-Event-ID</c>), puis diffuse en direct ; un commentaire toutes les
    /// 15 s garde la connexion ouverte à travers les proxys.
    /// </summary>
    public static IEndpointRouteBuilder MapEventStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/events", async (HttpContext http, EventLog log, long? after) =>
        {
            var ct = http.RequestAborted;
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var lastId = after ?? (long.TryParse(http.Request.Headers["Last-Event-ID"], out var h) ? h : -1);
            var live = log.Subscribe(ct);
            if (lastId < 0) lastId = log.LastId();

            async Task Send(ServiceEvent evt)
            {
                var sb = new StringBuilder();
                sb.Append("id: ").Append(evt.Id).Append('\n');
                sb.Append("event: ").Append(evt.Type).Append('\n');
                sb.Append("data: ").Append(JsonSerializer.Serialize(evt, JsonDefaults.Options)).Append("\n\n");
                await http.Response.WriteAsync(sb.ToString(), ct);
            }

            await http.Response.WriteAsync(": connecté\n\n", ct);
            await http.Response.Body.FlushAsync(ct);

            foreach (var evt in log.ReadAfter(lastId, 5000))
            {
                await Send(evt);
                lastId = evt.Id;
            }
            await http.Response.Body.FlushAsync(ct);

            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var waitHeartbeat = heartbeat.WaitForNextTickAsync(ct).AsTask();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var waitEvent = live.WaitToReadAsync(ct).AsTask();
                    var done = await Task.WhenAny(waitEvent, waitHeartbeat);
                    if (done == waitHeartbeat)
                    {
                        await http.Response.WriteAsync(": ping\n\n", ct);
                        await http.Response.Body.FlushAsync(ct);
                        waitHeartbeat = heartbeat.WaitForNextTickAsync(ct).AsTask();
                        continue;
                    }
                    if (!await waitEvent) break;
                    while (live.TryRead(out var evt))
                    {
                        if (evt.Id > 0 && evt.Id <= lastId) continue;
                        await Send(evt);
                        if (evt.Id > 0) lastId = evt.Id;
                    }
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client parti.
            }
        }).ExcludeFromDescription();
        return app;
    }
}
