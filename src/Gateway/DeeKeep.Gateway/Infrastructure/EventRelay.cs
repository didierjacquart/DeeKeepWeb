using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Contracts;
using HotChocolate.Subscriptions;

namespace DeeKeep.Gateway.Infrastructure;

public static class EventRelay
{
    /// <summary>
    /// Relaie les événements de chaque service vers les abonnements GraphQL
    /// (sujet <c>evt:&lt;type&gt;</c>, charge utile <see cref="ServiceEvent"/>).
    /// Non durable : un client qui se reconnecte rafraîchit ses requêtes, il n'a
    /// pas besoin des événements manqués.
    /// </summary>
    public static IServiceCollection AddEventRelay(this IServiceCollection services)
    {
        foreach (var producer in ServiceEndpoints.All)
        {
            services.AddEventSubscription(producer, async (sp, evt, ct) =>
            {
                var sender = sp.GetRequiredService<ITopicEventSender>();
                await sender.SendAsync(Topics.For(evt.Type), evt, ct);
            }, durable: false);
        }
        return services;
    }
}
