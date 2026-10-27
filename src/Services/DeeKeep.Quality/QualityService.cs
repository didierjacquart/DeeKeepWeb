namespace DeeKeep.Quality;

/// <summary>Enregistrement et points d'entrée du service Quality.</summary>
public static class QualityService
{
    public static IServiceCollection AddQualityService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapQualityService(this WebApplication app)
    {
        return app;
    }
}
