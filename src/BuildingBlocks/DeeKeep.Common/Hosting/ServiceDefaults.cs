using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Outbox;
using DeeKeep.Common.Processes;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Hosting;

/// <summary>Nom du service courant (sert aux tâches, au journal d'événements, aux curseurs).</summary>
public sealed record ServiceIdentity(string Name);

/// <summary>Adresses internes des services (section <c>Services</c> de la configuration).</summary>
public sealed class ServiceEndpoints(IConfiguration configuration)
{
    private static readonly Dictionary<string, int> DefaultPorts = new(StringComparer.OrdinalIgnoreCase)
    {
        [ServiceNames.Gateway] = 5000,
        [ServiceNames.Catalog] = 5101,
        [ServiceNames.Library] = 5102,
        [ServiceNames.Downloader] = 5103,
        [ServiceNames.Quality] = 5104,
        [ServiceNames.Storage] = 5105,
    };

    public string Resolve(string service) =>
        configuration[$"Services:{service}"] ??
        (DefaultPorts.TryGetValue(service, out var port) ? $"http://127.0.0.1:{port}" : throw new InvalidOperationException($"Service inconnu : {service}"));

    public static int DefaultPort(string service) => DefaultPorts[service];

    public static IEnumerable<string> All => DefaultPorts.Keys.Where(k => k != ServiceNames.Gateway);
}

public static class ServiceDefaults
{
    public const string InternalKeyHeader = "X-Internal-Key";

    /// <summary>
    /// Socle commun d'un microservice : configuration (fichier partagé
    /// <c>/etc/deekeep/appsettings.json</c> + variables <c>DEEKEEP_</c>),
    /// base, chemins, outils, tâches, événements, boîte d'envoi, résilience des
    /// clients HTTP, sondes de santé, écoute locale uniquement.
    /// </summary>
    public static WebApplicationBuilder AddDeeKeepService(this WebApplicationBuilder builder, string serviceName, bool listenLocalOnly = true)
    {
        var shared = Environment.GetEnvironmentVariable("DEEKEEP_CONFIG") ?? "/etc/deekeep/appsettings.json";
        builder.Configuration.AddJsonFile(shared, optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile(Path.Combine(Path.GetDirectoryName(shared) ?? "/etc/deekeep", "secrets.json"), optional: true);
        builder.Configuration.AddEnvironmentVariables("DEEKEEP_");

        // Sous systemd : notification de démarrage (Type=notify) et journal au format journald.
        builder.Host.UseSystemd();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        var services = builder.Services;
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.AddSingleton<ServiceEndpoints>();
        services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
        services.Configure<PathMapOptions>(builder.Configuration.GetSection("PathMap"));
        services.Configure<ToolOptions>(builder.Configuration.GetSection("Tools"));
        services.Configure<JobWorkerOptions>(builder.Configuration.GetSection($"Jobs:{serviceName}"));
        services.AddSingleton<DeeKeepDb>();
        services.AddSingleton<WebDb>();
        services.AddSingleton<PathMap>();
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<EventLog>();
        services.AddHostedService<EventLogPruner>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobHandlerRegistry>();
        services.AddSingleton<JobWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
        services.AddSingleton<OutboxQueue>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddSingleton<EventSubscriber>();
        services.AddHostedService(sp => sp.GetRequiredService<EventSubscriber>());

        var internalKey = builder.Configuration["InternalKey"] ?? "";
        // Tous les clients HTTP sortants sont résilients (délai, réessais, disjoncteur)
        // et portent la clé interne quand ils parlent à un autre service.
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
        });
        services.AddTransient<InternalKeyHandler>();
        foreach (var name in ServiceEndpoints.All)
        {
            services.AddHttpClient($"svc:{name}", (sp, c) => c.BaseAddress = new Uri(sp.GetRequiredService<ServiceEndpoints>().Resolve(name)))
                .AddHttpMessageHandler<InternalKeyHandler>();
        }
        services.AddServiceClients();
        services.AddHttpClient(OutboxDispatcher.HttpClientName).AddHttpMessageHandler<InternalKeyHandler>();
        // Le flux SSE est une connexion longue : pas de délai global ni de réessai automatique,
        // c'est l'abonné qui gère la reconnexion.
        services.AddHttpClient(EventSubscriber.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<InternalKeyHandler>()
            .RemoveAllResilienceHandlers();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(o =>
        {
            foreach (var c in JsonDefaults.Options.Converters) o.SerializerOptions.Converters.Add(c);
            o.SerializerOptions.NumberHandling = JsonDefaults.Options.NumberHandling;
        });

        if (listenLocalOnly && string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
        {
            var port = builder.Configuration.GetValue<int?>($"Ports:{serviceName}") ?? ServiceEndpoints.DefaultPort(serviceName);
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        }

        builder.Services.AddSingleton(new InternalKey(internalKey));
        return builder;
    }

    /// <summary>Points communs : santé, flux d'événements, tâches ; contrôle de la clé interne sur <c>/internal</c> et <c>/api</c>.</summary>
    public static WebApplication MapDeeKeepDefaults(this WebApplication app)
    {
        var key = app.Services.GetRequiredService<InternalKey>().Value;
        if (!string.IsNullOrEmpty(key))
        {
            app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path;
                if ((path.StartsWithSegments("/internal") || path.StartsWithSegments("/api")) &&
                    ctx.Request.Headers[InternalKeyHeader] != key)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next();
            });
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
        app.MapEventStream();
        app.MapJobEndpoints();
        app.MapInfo();
        return app;
    }

    /// <summary>Enregistre un gestionnaire de tâches pour un type donné.</summary>
    public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services, string type) where THandler : class, IJobHandler
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(type, typeof(THandler)));
        return services;
    }

    /// <summary>Abonnement à un autre service (traité par <see cref="EventSubscriber"/>).</summary>
    public static IServiceCollection AddEventSubscription(this IServiceCollection services, string producer,
        Func<IServiceProvider, ServiceEvent, CancellationToken, Task> handler, bool durable = true, params string[] types)
    {
        services.AddSingleton(new EventSubscription(producer, handler, durable, types.Length == 0 ? null : types.ToHashSet()));
        return services;
    }
}

public sealed record InternalKey(string Value);

public sealed class DatabaseHealthCheck(IOptions<DatabaseOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var path = options.Value.MainPath;
        return Task.FromResult(File.Exists(path) ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"Base absente : {path}"));
    }
}

/// <summary>Ajoute l'en-tête de clé interne aux appels entre services.</summary>
public sealed class InternalKeyHandler(InternalKey key) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(key.Value)) request.Headers.TryAddWithoutValidation(ServiceDefaults.InternalKeyHeader, key.Value);
        return base.SendAsync(request, cancellationToken);
    }
}
