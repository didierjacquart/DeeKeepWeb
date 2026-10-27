using System.Diagnostics;
using DeeKeep.Common.Events;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Outbox;
using DeeKeep.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace DeeKeep.Common.Hosting;

/// <summary>Contributeur d'état d'outils pour <c>/internal/info</c> (implémenté par les services qui pilotent ffmpeg, yt-dlp…).</summary>
public interface IToolStatusProvider
{
    Task<IReadOnlyList<ToolStatus>> GetAsync(CancellationToken ct);
}

public static class InfoEndpoint
{
    private static readonly double StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static IEndpointRouteBuilder MapInfo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/info", async (ServiceIdentity identity, JobStore jobs, OutboxQueue outbox, EventSubscriber subscriber,
            IEnumerable<IToolStatusProvider> providers, CancellationToken ct) =>
        {
            var tools = new List<ToolStatus>();
            foreach (var p in providers) tools.AddRange(await p.GetAsync(ct));
            using var process = Process.GetCurrentProcess();
            return new ServiceInfo(
                identity.Name,
                typeof(InfoEndpoint).Assembly.GetName().Version?.ToString() ?? "1.0",
                StartedAt,
                process.WorkingSet64,
                GC.GetTotalMemory(false),
                jobs.List(activeOnly: true).Count,
                outbox.PendingCount(),
                subscriber.States.Select(s => new EventLinkStatus(s.Producer, s.Connected, s.Since?.ToUnixTimeMilliseconds() / 1000.0, s.LastError)).ToList(),
                tools);
        });
        return app;
    }
}
