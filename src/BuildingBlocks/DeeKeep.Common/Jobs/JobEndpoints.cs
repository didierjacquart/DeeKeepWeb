using DeeKeep.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DeeKeep.Common.Jobs;

public static class JobEndpoints
{
    /// <summary>Points communs à tous les services : lister, lire, annuler, relancer une tâche.</summary>
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/jobs");
        group.MapGet("", (JobStore store, bool? active, string? type, string? group, int? limit) =>
            store.List(active ?? false, limit ?? 200, type, group));
        group.MapGet("/{id:guid}", (JobStore store, Guid id) => store.Get(id) is { } job ? Results.Ok(job) : Results.NotFound());
        group.MapPost("/{id:guid}/cancel", (JobWorker worker, JobStore store, Guid id) =>
        {
            worker.Cancel(id);
            return store.Get(id) is { } job ? Results.Ok(job) : Results.NotFound();
        });
        group.MapPost("/{id:guid}/retry", (JobStore store, Guid id) => store.Requeue(id) is { } job ? Results.Ok(job) : Results.NotFound());
        group.MapPost("", (JobStore store, JobHandlerRegistry registry, EnqueueJobRequest request) =>
            registry.Handlers.ContainsKey(request.Type)
                ? Results.Ok(store.Enqueue(request))
                : Results.BadRequest($"Type de tâche inconnu : {request.Type}"));
        return app;
    }
}
