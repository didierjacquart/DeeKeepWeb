namespace DeeKeep.Gateway.Infrastructure;

/// <summary>Sujets d'abonnement GraphQL : un par type d'événement de service.</summary>
public static class Topics
{
    public static string For(string eventType) => "evt:" + eventType;
}
