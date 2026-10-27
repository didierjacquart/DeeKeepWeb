namespace DeeKeep.Catalog;

/// <summary>Enregistrement et points d'entrée du service Catalog.</summary>
public static class CatalogService
{
    public static IServiceCollection AddCatalogService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapCatalogService(this WebApplication app)
    {
        return app;
    }
}
