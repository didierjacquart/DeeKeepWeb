using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DeeKeep.Contracts.Clients;

/// <summary>Le service appelé est injoignable ou a répondu en erreur.</summary>
public sealed class ServiceUnavailableException(string service, string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception($"Service {service} : {message}", inner)
{
    public string Service { get; } = service;
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Base des clients typés entre services. Le <see cref="HttpClient"/> fourni
/// (client nommé <c>svc:&lt;nom&gt;</c>) porte déjà la résilience standard et la
/// clé interne ; cette classe ne fait que (dé)sérialiser et traduire les erreurs.
/// </summary>
public abstract class ServiceClient(HttpClient http, string service)
{
    protected HttpClient Http { get; } = http;
    public string Service { get; } = service;

    protected async Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        await SendAsync<T>(HttpMethod.Get, path, null, ct);

    protected async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(path, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            await EnsureAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceUnavailableException(Service, ex.Message, ex.StatusCode, ex);
        }
    }

    protected Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Post, path, body, ct);

    protected Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, path, body, ct);

    protected Task<T> DeleteAsync<T>(string path, object? body = null, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Delete, path, body, ct);

    protected async Task PostAsync(string path, object? body, CancellationToken ct = default) =>
        await SendAsync<JsonNode?>(HttpMethod.Post, path, body, ct);

    protected async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonDefaults.Options);
            using var response = await Http.SendAsync(request, ct);
            await EnsureAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, ct))!;
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceUnavailableException(Service, ex.Message, ex.StatusCode, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(Service, "délai dépassé", null, ex);
        }
    }

    private async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(ct);
        throw new ServiceUnavailableException(Service, string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase ?? "erreur" : text, response.StatusCode);
    }

    // Tâches : communes à tous les services.
    public Task<IReadOnlyList<JobDto>> ListJobsAsync(bool active, string? type = null, string? group = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<JobDto>>($"/internal/jobs?active={active.ToString().ToLowerInvariant()}" +
                                        (type is null ? "" : $"&type={Uri.EscapeDataString(type)}") +
                                        (group is null ? "" : $"&group={Uri.EscapeDataString(group)}"), ct);

    public Task<JobDto?> GetJobAsync(Guid id, CancellationToken ct = default) => GetOrDefaultAsync<JobDto>($"/internal/jobs/{id}", ct);

    public Task<JobDto> CancelJobAsync(Guid id, CancellationToken ct = default) => PostAsync<JobDto>($"/internal/jobs/{id}/cancel", null, ct);

    public Task<JobDto> RetryJobAsync(Guid id, CancellationToken ct = default) => PostAsync<JobDto>($"/internal/jobs/{id}/retry", null, ct);

    public Task<JobDto> EnqueueJobAsync(EnqueueJobRequest request, CancellationToken ct = default) => PostAsync<JobDto>("/internal/jobs", request, ct);
}

/// <summary>Désigne le service qu'un client typé appelle ; les clients ainsi marqués sont enregistrés automatiquement.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ServiceClientAttribute(string service) : Attribute
{
    public string Service { get; } = service;
}
