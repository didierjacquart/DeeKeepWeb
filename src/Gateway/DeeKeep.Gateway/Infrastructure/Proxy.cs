using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>
/// Flux binaires servis directement par les services, sans passer par GraphQL :
/// audio (requêtes Range), jaquettes, images et extraits Deezer mis en cache,
/// titres lus depuis l'archive.
/// </summary>
public static class Proxy
{
    public static IServiceCollection AddDeeKeepProxy(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoints = new ServiceEndpoints(configuration);
        var internalKey = configuration["InternalKey"] ?? "";

        static RouteConfig Route(string id, string path, string cluster) =>
            new() { RouteId = id, ClusterId = cluster, Match = new RouteMatch { Path = path } };

        ClusterConfig Cluster(string service) => new()
        {
            ClusterId = service,
            Destinations = new Dictionary<string, DestinationConfig> { ["d"] = new() { Address = endpoints.Resolve(service) } },
            HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) },
        };

        services.AddReverseProxy()
            .LoadFromMemory(
            [
                Route("media", "/media/{**rest}", ServiceNames.Library),
                Route("covers", "/covers/{**rest}", ServiceNames.Library),
                Route("img", "/img/{**rest}", ServiceNames.Catalog),
                Route("preview", "/preview/{**rest}", ServiceNames.Catalog),
                Route("archive-media", "/archive-media/{**rest}", ServiceNames.Storage),
            ],
            [Cluster(ServiceNames.Library), Cluster(ServiceNames.Catalog), Cluster(ServiceNames.Storage)])
            .AddTransforms(t =>
            {
                if (!string.IsNullOrEmpty(internalKey)) t.AddRequestHeader(ServiceDefaults.InternalKeyHeader, internalKey);
                t.AddRequestHeaderRemove("Cookie");
            });
        return services;
    }
}
