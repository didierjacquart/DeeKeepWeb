using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeeKeep.Common.Events;

/// <summary>Abonnement d'un consommateur au journal d'un autre service.</summary>
public sealed record EventSubscription(
    string Producer,
    Func<IServiceProvider, ServiceEvent, CancellationToken, Task> Handler,
    bool Durable = true,
    IReadOnlySet<string>? Types = null);

public sealed record EventConnectionState(string Producer, bool Connected, DateTimeOffset? Since, string? LastError);

/// <summary>
/// Lit en continu les flux SSE des services producteurs, avec reconnexion
/// exponentielle (1 s → 30 s). Un abonnement durable mémorise son curseur
/// dans <c>event_cursors</c> : après un redémarrage ou une coupure, il reprend
/// là où il s'était arrêté (livraison « au moins une fois » — les
/// gestionnaires doivent être idempotents).
/// </summary>
public sealed class EventSubscriber(
    IEnumerable<EventSubscription> subscriptions,
    IHttpClientFactory httpFactory,
    ServiceIdentity identity,
    WebDb webDb,
    IServiceProvider services,
    ILogger<EventSubscriber> logger) : BackgroundService
{
    public const string HttpClientName = "events";

    private readonly Dictionary<string, EventConnectionState> _states = new();

    public IReadOnlyCollection<EventConnectionState> States
    {
        get { lock (_states) return _states.Values.ToList(); }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var groups = subscriptions.GroupBy(s => s.Producer, StringComparer.OrdinalIgnoreCase);
        return Task.WhenAll(groups.Select(g => RunProducerAsync(g.Key, g.ToList(), stoppingToken)));
    }

    private async Task RunProducerAsync(string producer, List<EventSubscription> subs, CancellationToken ct)
    {
        var durable = subs.Any(s => s.Durable);
        long? cursor = durable ? LoadCursor(producer) : null;
        var delay = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = httpFactory.CreateClient(HttpClientName);
                var baseUrl = services.GetRequiredService<ServiceEndpoints>().Resolve(producer);
                var url = $"{baseUrl.TrimEnd('/')}/internal/events" + (cursor is { } c ? $"?after={c}" : "");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                SetState(producer, true, null);
                delay = TimeSpan.FromSeconds(1);

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var data = new StringBuilder();
                var lastSaved = DateTime.UtcNow;
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (line.Length == 0)
                    {
                        if (data.Length == 0) continue;
                        var evt = JsonSerializer.Deserialize<ServiceEvent>(data.ToString(), JsonDefaults.Options);
                        data.Clear();
                        if (evt is null) continue;
                        foreach (var sub in subs)
                        {
                            if (sub.Types is not null && !sub.Types.Contains(evt.Type)) continue;
                            await InvokeAsync(sub, evt, ct);
                        }
                        cursor = evt.Id;
                        if (durable && (DateTime.UtcNow - lastSaved).TotalSeconds >= 1)
                        {
                            SaveCursor(producer, evt.Id);
                            lastSaved = DateTime.UtcNow;
                        }
                        continue;
                    }
                    if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(line.AsSpan(5).TrimStart());
                }
                if (durable && cursor is { } final) SaveCursor(producer, final);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(producer, false, ex.Message);
                logger.LogDebug("Flux d'événements {Producer} indisponible : {Error}", producer, ex.Message);
            }

            SetState(producer, false, null);
            await Task.Delay(delay, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task InvokeAsync(EventSubscription sub, ServiceEvent evt, CancellationToken ct)
    {
        // Un gestionnaire qui échoue est réessayé quelques fois, puis l'événement
        // est abandonné : bloquer le flux entier pour un seul événement serait pire.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await sub.Handler(scope.ServiceProvider, evt, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gestionnaire de {Type} ({Producer}) en échec, tentative {Attempt}", evt.Type, sub.Producer, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }
    }

    private void SetState(string producer, bool connected, string? error)
    {
        lock (_states)
        {
            _states.TryGetValue(producer, out var previous);
            _states[producer] = new EventConnectionState(producer, connected,
                connected ? previous?.Connected == true ? previous.Since : DateTimeOffset.UtcNow : null,
                error ?? (connected ? null : previous?.LastError));
        }
    }

    private long? LoadCursor(string producer)
    {
        try
        {
            using var conn = webDb.Open();
            return conn.ExecuteScalar<long?>("SELECT last_id FROM event_cursors WHERE consumer = @c AND producer = @p",
                new { c = identity.Name, p = producer });
        }
        catch
        {
            return null;
        }
    }

    private void SaveCursor(string producer, long id)
    {
        if (id <= 0) return;
        try
        {
            using var conn = webDb.Open();
            conn.Execute("INSERT INTO event_cursors (consumer, producer, last_id) VALUES (@c, @p, @id) " +
                         "ON CONFLICT(consumer, producer) DO UPDATE SET last_id = excluded.last_id",
                new { c = identity.Name, p = producer, id });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Curseur {Producer} non enregistré", producer);
        }
    }
}
