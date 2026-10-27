using DeeKeep.Common.Events;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Clients;
using DeeKeep.Gateway.Infrastructure;

namespace DeeKeep.Gateway.Domains.Diagnostics;

public sealed record ServiceHealth(string Service, bool Reachable, string? Error, ServiceInfo? Info, bool EventsConnected);

[QueryType]
public static partial class DiagnosticQueries
{
    /// <summary>État de chaque microservice : joignable, mémoire, tâches, outils, liens d'événements.</summary>
    public static async Task<IReadOnlyList<ServiceHealth>> GetServices([Service] ServiceDirectory directory, [Service] EventSubscriber relay, CancellationToken ct)
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
    }

    /// <summary>Simple sonde de vie de la passerelle (utilisée par le client pour savoir s'il est « en ligne »).</summary>
    public static double Ping() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
