using System.Reflection;
using DeeKeep.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace DeeKeep.Common.Hosting;

public static class ServiceClients
{
    /// <summary>
    /// Enregistre tous les clients typés de DeeKeep.Contracts marqués
    /// <see cref="ServiceClientAttribute"/> : adresse de base, clé interne,
    /// résilience standard (via les défauts de <c>AddDeeKeepService</c>).
    /// </summary>
    public static IServiceCollection AddServiceClients(this IServiceCollection services)
    {
        var method = typeof(ServiceClients).GetMethod(nameof(AddOne), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var type in typeof(ServiceClient).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(ServiceClient).IsAssignableFrom(type)) continue;
            var attr = type.GetCustomAttribute<ServiceClientAttribute>();
            if (attr is null) continue;
            method.MakeGenericMethod(type).Invoke(null, [services, attr.Service]);
        }
        return services;
    }

    private static void AddOne<TClient>(IServiceCollection services, string service) where TClient : ServiceClient
    {
        services.AddHttpClient<TClient>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<ServiceEndpoints>().Resolve(service));
        }).AddHttpMessageHandler<InternalKeyHandler>();
    }
}
