using System.Runtime.CompilerServices;
using System.Text.Json;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Clients;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.Subscriptions;

namespace DeeKeep.Gateway.Domains.Jobs;

[QueryType]
public static partial class JobQueries
{
    /// <summary>Tâches de fond de tous les services (ou d'un seul). Un service injoignable est ignoré.</summary>
    public static async Task<IReadOnlyList<JobDto>> GetJobs(
        [Service] ServiceDirectory directory, string? service = null, bool activeOnly = false, string? type = null, string? groupKey = null,
        CancellationToken ct = default)
    {
        var targets = service is null ? directory.Services : [service];
        var results = await Task.WhenAll(targets.Select(async s =>
        {
            try { return await directory.Get(s).ListJobsAsync(activeOnly, type, groupKey, ct); }
            catch (ServiceUnavailableException) { return (IReadOnlyList<JobDto>)[]; }
        }));
        return results.SelectMany(r => r).OrderByDescending(j => j.CreatedAt).ToList();
    }

    public static Task<JobDto?> GetJob([Service] ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
        directory.Get(service).GetJobAsync(id, ct);
}

[MutationType]
public static partial class JobMutations
{
    public static Task<JobDto> CancelJob([Service] ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
        directory.Get(service).CancelJobAsync(id, ct);

    public static Task<JobDto> RetryJob([Service] ServiceDirectory directory, string service, Guid id, CancellationToken ct) =>
        directory.Get(service).RetryJobAsync(id, ct);
}

[SubscriptionType]
public static partial class JobSubscriptions
{
    public static async IAsyncEnumerable<ServiceEvent> SubscribeToJobUpdated(
        string? service, string? type, string? groupKey, [Service] ITopicEventReceiver receiver, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<ServiceEvent>(Topics.For(EventTypes.JobUpdated), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct))
        {
            var job = evt.Data?.Deserialize<JobDto>(JsonDefaults.Options);
            if (job is null) continue;
            if (service is not null && job.Service != service) continue;
            if (type is not null && job.Type != type) continue;
            if (groupKey is not null && job.GroupKey != groupKey) continue;
            yield return evt;
        }
    }

    /// <summary>Avancement d'une tâche (filtrable par service, type, groupe).</summary>
    [Subscribe(With = nameof(SubscribeToJobUpdated))]
    public static JobDto JobUpdated(string? service, string? type, string? groupKey, [EventMessage] ServiceEvent evt) =>
        evt.Data!.Deserialize<JobDto>(JsonDefaults.Options)!;

    public static async IAsyncEnumerable<ServiceEvent> SubscribeToJobItem(
        Guid? jobId, string? type, [Service] ITopicEventReceiver receiver, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<ServiceEvent>(Topics.For(EventTypes.JobItem), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct))
        {
            var item = evt.Data?.Deserialize<JobItemEvent>(JsonDefaults.Options);
            if (item is null) continue;
            if (jobId is not null && item.JobId != jobId) continue;
            if (type is not null && item.Type != type) continue;
            yield return evt;
        }
    }

    /// <summary>Résultat élément par élément d'une tâche par lot.</summary>
    [Subscribe(With = nameof(SubscribeToJobItem))]
    public static JobItemEvent JobItem(Guid? jobId, string? type, [EventMessage] ServiceEvent evt) =>
        evt.Data!.Deserialize<JobItemEvent>(JsonDefaults.Options)!;
}
