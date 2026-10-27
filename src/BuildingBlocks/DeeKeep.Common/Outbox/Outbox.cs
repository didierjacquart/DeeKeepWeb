using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeeKeep.Common.Outbox;

/// <summary>
/// Boîte d'envoi durable : un appel vers un autre service qui DOIT aboutir
/// (enregistrer un fichier extrait, invalider une mesure…) est d'abord écrit
/// dans <c>deekeep_web.db</c>, puis livré par <see cref="OutboxDispatcher"/>
/// avec réessais exponentiels — même si le destinataire est arrêté pendant
/// des heures, ou si l'émetteur redémarre entre-temps.
/// </summary>
public sealed class OutboxQueue(WebDb webDb, ServiceIdentity identity)
{
    public event Action? Enqueued;

    public void Enqueue(string target, HttpMethod method, string path, object? body = null)
    {
        using (var conn = webDb.Open())
        {
            var now = Clock.UnixNow();
            conn.Execute("INSERT INTO outbox (service, target, method, path, body, next_at, created_at) VALUES (@s, @t, @m, @p, @b, @now, @now)",
                new
                {
                    s = identity.Name, t = target, m = method.Method, p = path,
                    b = body is null ? null : JsonSerializer.Serialize(body, JsonDefaults.Options), now,
                });
        }
        Enqueued?.Invoke();
    }

    public int PendingCount()
    {
        using var conn = webDb.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox WHERE service = @s AND dead = 0", new { s = identity.Name });
    }
}

public sealed class OutboxDispatcher(
    WebDb webDb,
    OutboxQueue queue,
    ServiceIdentity identity,
    ServiceEndpoints endpoints,
    IHttpClientFactory httpFactory,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "outbox";
    private const int MaxAttempts = 50;
    private readonly SemaphoreSlim _signal = new(0);

    private sealed record Entry(long id, string target, string method, string path, string? body, long attempts);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        queue.Enqueued += () => _signal.Release();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Boîte d'envoi : lecture impossible");
            }
            await _signal.WaitQuietlyAsync(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        List<Entry> due;
        using (var conn = webDb.Open())
        {
            due = conn.Query<Entry>("SELECT id, target, method, path, body, attempts FROM outbox WHERE service = @s AND dead = 0 AND next_at <= @now ORDER BY id LIMIT 50",
                new { s = identity.Name, now = Clock.UnixNow() }).ToList();
        }
        // Un destinataire en panne ne bloque pas les autres, mais l'ordre est
        // conservé pour un même destinataire : on s'arrête à son premier échec.
        var blocked = new HashSet<string>();
        foreach (var entry in due)
        {
            if (blocked.Contains(entry.target)) continue;
            string? error = null;
            var permanent = false;
            try
            {
                var client = httpFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(new HttpMethod(entry.method), endpoints.Resolve(entry.target).TrimEnd('/') + entry.path);
                if (entry.body is not null) request.Content = new StringContent(entry.body, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    error = $"HTTP {(int)response.StatusCode}";
                    permanent = (int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not
                        (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.Conflict);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            using var conn = webDb.Open();
            if (error is null)
            {
                conn.Execute("DELETE FROM outbox WHERE id = @id", new { entry.id });
                continue;
            }
            blocked.Add(entry.target);
            var attempts = entry.attempts + 1;
            var dead = permanent || attempts >= MaxAttempts;
            var delay = Math.Min(600, Math.Pow(2, Math.Min(attempts, 10)));
            conn.Execute("UPDATE outbox SET attempts = @attempts, last_error = @error, next_at = @next, dead = @dead WHERE id = @id",
                new { entry.id, attempts, error, next = Clock.UnixNow() + delay, dead = dead ? 1 : 0 });
            if (dead) logger.LogError("Boîte d'envoi : abandon de {Method} {Target}{Path} ({Error})", entry.method, entry.target, entry.path, error);
        }
    }
}
