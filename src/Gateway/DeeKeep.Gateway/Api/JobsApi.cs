using DeeKeep.Common.Events;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Clients;
using DeeKeep.Gateway.Infrastructure;

namespace DeeKeep.Gateway.Api;

public sealed record ServiceHealth(string Service, bool Reachable, string? Error, ServiceInfo? Info, bool EventsConnected);

public static class JobsApi
{
    public static void MapJobsApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/jobs");

        // Tâches de fond de tous les services (ou d'un seul). Un service injoignable est ignoré.
        g.MapGet("/", async (ServiceDirectory directory, string? service, bool? activeOnly, string? type, string? groupKey, CancellationToken ct) =>
        {
            var targets = service is null ? directory.Services : [service];
            var results = await Task.WhenAll(targets.Select(async s =>
            {
                try { return await directory.Get(s).ListJobsAsync(activeOnly ?? false, type, groupKey, ct); }
                catch (ServiceUnavailableException) { return (IReadOnlyList<JobDto>)[]; }
            }));
            return results.SelectMany(r => r).OrderByDescending(j => j.CreatedAt).ToList();
        });

        g.MapGet("/{service}/{id:guid}", async (ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
            await directory.Get(service).GetJobAsync(id, ct) is { } job ? Results.Json(job) : Results.NotFound());
        g.MapPost("/{service}/{id:guid}/cancel", (ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
            directory.Get(service).CancelJobAsync(id, ct));
        g.MapPost("/{service}/{id:guid}/retry", (ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
            directory.Get(service).RetryJobAsync(id, ct));
    }

    public static void MapDiagnosticsApi(this RouteGroupBuilder api)
    {
        // État de chaque microservice : joignable, mémoire, tâches, outils, liens d'événements.
        api.MapGet("/services", async (ServiceDirectory directory, EventSubscriber relay, CancellationToken ct) =>
        {
            var links = relay.States.ToDictionary(s => s.Producer, s => s.Connected, StringComparer.OrdinalIgnoreCase);
            return await Task.WhenAll(directory.Services.Select(async s =>
            {
                try
                {
                    var info = await directory.Get(s).InfoAsync(ct);
                    return new ServiceHealth(s, true, null, info, links.GetValueOrDefault(s));
                }
                catch (ServiceUnavailableException ex)
                {
                    return new ServiceHealth(s, false, ex.Message, null, links.GetValueOrDefault(s));
                }
            }));
        });

        // Sonde de vie de la passerelle.
        api.MapGet("/ping", () => new { at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 });
    }
}
