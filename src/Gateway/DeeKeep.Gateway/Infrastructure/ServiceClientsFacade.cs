using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>Client générique vers n'importe quel service (tâches, info), pour les vues transverses.</summary>
public sealed class AnyServiceClient(HttpClient http, string service) : ServiceClient(http, service)
{
    public Task<ServiceInfo> InfoAsync(CancellationToken ct) => GetAsync<ServiceInfo>("/internal/info", ct);
}

public sealed class ServiceDirectory(IHttpClientFactory factory)
{
    public IReadOnlyList<string> Services { get; } = ServiceEndpoints.All.ToList();

    public AnyServiceClient Get(string service) => new(factory.CreateClient($"svc:{service}"), service);
}
