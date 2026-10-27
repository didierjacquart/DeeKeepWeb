namespace DeeKeep.Library;

/// <summary>Enregistrement et points d'entrée du service Library.</summary>
public static class LibraryService
{
    public static IServiceCollection AddLibraryService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapLibraryService(this WebApplication app)
    {
        return app;
    }
}
