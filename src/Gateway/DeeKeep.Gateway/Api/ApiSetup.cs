using System.Text.Json;
using System.Text.Json.Serialization;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Gateway.Api;

/// <summary>
/// API REST du client web : <c>/api/...</c> en JSON. Chaque domaine déclare ses
/// routes dans son fichier (<c>CatalogApi</c>, <c>LibraryApi</c>…) ; les événements
/// des services arrivent par un seul flux SSE (<c>/api/events</c>).
/// </summary>
public static class ApiSetup
{
    /// <summary>
    /// JSON du client : propriétés en camelCase, énumérations en MAJUSCULES_SOULIGNÉES
    /// (<c>QUEUED</c>, <c>PRESENT</c>, <c>ALL</c>) — à la lecture, la casse est libre.
    /// </summary>
    public static void ConfigureClientJson(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PropertyNameCaseInsensitive = true;
        o.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals;
        // Les réglages communs des services ajoutent déjà un convertisseur d'énumérations (« Queued ») :
        // le premier qui convient l'emporte, celui du client passe donc devant.
        o.Converters.Insert(0, new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
    }

    public static readonly JsonSerializerOptions ClientJson = CreateClientJson();

    private static JsonSerializerOptions CreateClientJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ConfigureClientJson(o);
        return o;
    }

    public static IServiceCollection AddDeeKeepApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o => ConfigureClientJson(o.SerializerOptions));
        services.AddSingleton<EventHub>();
        return services;
    }

    public static IEndpointRouteBuilder MapDeeKeepApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ApiErrorFilter>();
        api.MapCatalogApi();
        api.MapLibraryApi();
        api.MapUserTracksApi();
        api.MapDownloaderApi();
        api.MapJobsApi();
        api.MapOrganizeApi();
        api.MapQualityApi();
        api.MapStorageApi();
        api.MapSyncApi();
        api.MapTrashApi();
        api.MapDiagnosticsApi();
        api.MapEventsApi();
        api.MapAdminApi();
        // Route inconnue sous /api : 404 JSON, jamais la coquille de l'application.
        api.Map("{**rest}", () => Results.Json(new ApiError("Route inconnue.", "NOT_FOUND"), statusCode: StatusCodes.Status404NotFound));
        return app;
    }
}

/// <summary>Corps d'une réponse en erreur : message lisible et code stable.</summary>
public sealed record ApiError(string Error, string Code);

/// <summary>
/// Traduit les pannes en réponses lisibles. <c>SERVICE_UNAVAILABLE</c> (503) signifie
/// « réessayer plus tard » : le client met alors l'action dans sa file hors ligne.
/// </summary>
public sealed class ApiErrorFilter(ILogger<ApiErrorFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ServiceUnavailableException ex) when (ex.Status is null || (int)ex.Status >= 500)
        {
            return Fail(StatusCodes.Status503ServiceUnavailable, $"Service « {ex.Service} » momentanément indisponible.", "SERVICE_UNAVAILABLE");
        }
        catch (ServiceUnavailableException ex)
        {
            return Fail(StatusCodes.Status422UnprocessableEntity, ex.Message, "SERVICE_ERROR");
        }
        catch (ArgumentException ex)
        {
            return Fail(StatusCodes.Status400BadRequest, ex.Message, "INVALID_ARGUMENT");
        }
        catch (InvalidOperationException ex)
        {
            return Fail(StatusCodes.Status409Conflict, ex.Message, "INVALID_OPERATION");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Erreur de l'API {Path}", context.HttpContext.Request.Path);
            return Fail(StatusCodes.Status500InternalServerError, ex.Message, "INTERNAL");
        }
    }

    private static IResult Fail(int status, string message, string code) => Results.Json(new ApiError(message, code), statusCode: status);
}
