namespace DeeKeep.Downloader;

/// <summary>Enregistrement et points d'entrée du service Downloader.</summary>
public static class DownloaderService
{
    public static IServiceCollection AddDownloaderService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapDownloaderService(this WebApplication app)
    {
        return app;
    }
}
