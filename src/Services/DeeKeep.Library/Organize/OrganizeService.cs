namespace DeeKeep.Library.Organize;

/// <summary>Enregistrement et points d'entrée « Organiser », doublons et tags.</summary>
public static class OrganizeService
{
    public static IServiceCollection AddOrganizeService(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static WebApplication MapOrganizeService(this WebApplication app)
    {
        return app;
    }
}
