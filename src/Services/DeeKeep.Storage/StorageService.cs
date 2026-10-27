namespace DeeKeep.Storage;

/// <summary>Enregistrement et points d'entrée du service Storage.</summary>
public static class StorageService
{
    public static IServiceCollection AddStorageService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapStorageService(this WebApplication app)
    {
        return app;
    }
}
