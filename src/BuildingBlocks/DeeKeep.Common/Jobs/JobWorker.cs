using DeeKeep.Common.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Jobs;

/// <summary>Exécute un type de tâche. Une instance par exécution (portée DI).</summary>
public interface IJobHandler
{
    Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct);
}

/// <summary>Erreur passagère : la tâche sera remise en file (attente exponentielle) tant qu'il reste des tentatives.</summary>
public sealed class TransientJobException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Erreur définitive : aucune nouvelle tentative.</summary>
public sealed class PermanentJobException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class JobWorkerOptions
{
    /// <summary>Nombre d'exécutions simultanées par type de tâche (défaut 1). Sur un Raspberry Pi, rester bas.</summary>
    public Dictionary<string, int> Concurrency { get; set; } = new();

    /// <summary>Durée de conservation des tâches terminées.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>Contexte d'exécution offert au gestionnaire : charge utile, progression, éléments.</summary>
public sealed class JobContext(JobDto job, JobStore store)
{
    private DateTime _lastProgress = DateTime.MinValue;
    private int _done, _failed, _total;

    public JobDto Job { get; } = job;
    public Guid Id => Job.Id;
    public int Attempt => Job.Attempts;

    public T GetPayload<T>() =>
        Job.Payload is null ? throw new PermanentJobException("Charge utile absente") :
        Job.Payload.Deserialize<T>(JsonDefaults.Options) ?? throw new PermanentJobException("Charge utile illisible");

    public int Done => _done;
    public int Failed => _failed;
    public int Total => _total;

    /// <summary>Publie l'avancement ; limité à 4 mises à jour par seconde sauf <paramref name="force"/>.</summary>
    public void Report(int done, int total, string? message = null, int failed = 0, bool force = false)
    {
        _done = done;
        _total = total;
        _failed = failed;
        var now = DateTime.UtcNow;
        if (!force && (now - _lastProgress).TotalMilliseconds < 250 && done + failed < total) return;
        _lastProgress = now;
        store.Progress(Id, done, failed, total, message);
        var current = Job with
        {
            Status = JobStatus.Running, Done = done, Failed = failed, Total = total, Message = message ?? Job.Message,
            Progress = total > 0 ? Math.Clamp((done + failed) / (double)total, 0, 1) : 0, UpdatedAt = Clock.UnixNow(),
        };
        store.Publish(current);
    }

    public void Message(string message) => Report(_done, _total, message, _failed, force: true);

    public void Item(string itemKey, string status, string? message = null, object? data = null) =>
        store.PublishItem(new JobItemEvent(Id, Job.Type, itemKey, status, message,
            data is null ? null : data as JsonNode ?? JsonSerializer.SerializeToNode(data, JsonDefaults.Options)));
}

public sealed record JobHandlerRegistration(string Type, Type HandlerType);

/// <summary>Registre type → gestionnaire, alimenté par <c>AddJobHandler</c>.</summary>
public sealed class JobHandlerRegistry(IEnumerable<JobHandlerRegistration> registrations)
{
    public IReadOnlyDictionary<string, Type> Handlers { get; } =
        registrations.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Last().HandlerType, StringComparer.Ordinal);
}

/// <summary>
/// File d'exécution durable : interroge <c>jobs</c> (réveillée aussitôt qu'une
/// tâche est ajoutée, sinon toutes les 2 s pour les reprises différées),
/// respecte la concurrence par type, annule sur demande et remet en file
/// les échecs passagers.
/// </summary>
public sealed class JobWorker(
    JobStore store,
    JobHandlerRegistry registry,
    IServiceProvider services,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<string, int> _runningByType = new();
    private readonly SemaphoreSlim _signal = new(0);

    public bool Cancel(Guid id)
    {
        if (_running.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return store.CancelQueued(id);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var recovered = store.RecoverInterrupted();
        if (recovered > 0) logger.LogInformation("{Count} tâche(s) interrompue(s) remise(s) en file", recovered);
        store.Enqueued += () => _signal.Release();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPurge = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (TryStartNext(stoppingToken))
                {
                }
                if ((DateTime.UtcNow - lastPurge).TotalHours >= 1)
                {
                    store.PurgeFinished(Clock.UnixNow() - options.Value.Retention.TotalSeconds);
                    lastPurge = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lecture de la file de tâches impossible, nouvel essai");
            }
            await _signal.WaitQuietlyAsync(TimeSpan.FromSeconds(2), stoppingToken);
        }

        foreach (var cts in _running.Values) cts.Cancel();
    }

    private bool TryStartNext(CancellationToken stoppingToken)
    {
        var available = registry.Handlers.Keys
            .Where(t => _runningByType.GetValueOrDefault(t) < Math.Max(1, options.Value.Concurrency.GetValueOrDefault(t, 1)))
            .ToList();
        var job = store.TryDequeue(available);
        if (job is null) return false;

        _runningByType.AddOrUpdate(job.Type, 1, (_, n) => n + 1);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[job.Id] = cts;
        _ = Task.Run(() => RunAsync(job, cts, stoppingToken), CancellationToken.None);
        return true;
    }

    private async Task RunAsync(JobDto job, CancellationTokenSource cts, CancellationToken stoppingToken)
    {
        var context = new JobContext(job, store);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var handler = (IJobHandler)scope.ServiceProvider.GetRequiredService(registry.Handlers[job.Type]);
            var result = await handler.ExecuteAsync(context, cts.Token);
            store.Complete(job.Id, JobStatus.Succeeded, result, null, context.Total > 0 ? null : "Terminé");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt du service : la tâche reste « Running » et sera reprise au démarrage.
        }
        catch (OperationCanceledException)
        {
            store.Complete(job.Id, JobStatus.Cancelled, null, null, "Arrêtée");
        }
        catch (TransientJobException ex) when (job.Attempts < store.MaxAttempts(job.Id))
        {
            var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(3, job.Attempts - 1)));
            logger.LogInformation("Tâche {Type} {Id} : échec passager ({Error}), nouvel essai dans {Delay}", job.Type, job.Id, ex.Message, delay);
            store.Retry(job.Id, ex.Message, delay);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tâche {Type} {Id} en échec", job.Type, job.Id);
            store.Complete(job.Id, JobStatus.Failed, null, ex.Message);
        }
        finally
        {
            _running.TryRemove(job.Id, out _);
            _runningByType.AddOrUpdate(job.Type, 0, (_, n) => Math.Max(0, n - 1));
            cts.Dispose();
            _signal.Release();
        }
    }
}
