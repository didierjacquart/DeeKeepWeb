using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Gateway.Api;

namespace DeeKeep.Gateway.Infrastructure;

public static class EventRelay
{
    /// <summary>
    /// Relaie les événements de chaque service vers les navigateurs (flux SSE
    /// <c>/api/events</c>). Non durable : un client qui se reconnecte recharge ses
    /// données, il n'a pas besoin des événements manqués.
    /// </summary>
    public static IServiceCollection AddEventRelay(this IServiceCollection services)
    {
        foreach (var producer in ServiceEndpoints.All)
        {
            services.AddEventSubscription(producer, (sp, evt, _) =>
            {
                sp.GetRequiredService<EventHub>().Publish(evt);
                return Task.CompletedTask;
            }, durable: false);
        }
        return services;
    }
}
