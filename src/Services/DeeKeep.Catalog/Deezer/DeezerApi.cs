using System.Text;
using System.Text.Json;
using DeeKeep.Catalog.Http;
using Microsoft.Extensions.Caching.Memory;

namespace DeeKeep.Catalog.Deezer;

/// <summary>Réponse de l'API Deezer : code HTTP, document JSON, erreur signalée dans le corps.</summary>
public sealed record DeezerResponse(int Status, JsonElement Body, int? ErrorCode = null, string? ErrorMessage = null)
{
    /// <summary>HTTP 200, document objet, sans clé <c>error</c> (c'est ainsi que l'original jugeait une réponse exploitable).</summary>
    public bool Ok => Status == 200 && ErrorCode is null && Body.ValueKind == JsonValueKind.Object;

    /// <summary>Quota dépassé (« Quota limit exceeded », code 4, rendu en HTTP 200).</summary>
    public bool QuotaExceeded => ErrorCode == DeezerApi.QuotaErrorCode;

    /// <summary>Tableau <c>data</c> d'une réponse de liste (vide si absent).</summary>
    public IEnumerable<JsonElement> Data =>
        Ok && Body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : [];
}

/// <summary>
/// Accès à l'API publique Deezer (<c>https://api.deezer.com</c>, anonyme, sans jeton) :
/// cadence globale (5 requêtes par seconde, quota public ≈ 50 / 5 s par adresse IP),
/// paramètres réellement encodés (l'original interpolait la requête telle quelle :
/// « Simon & Garfunkel » devenait « Simon »), relance sur dépassement de quota (que
/// l'original prenait pour « aucun résultat ») et cache mémoire court des réponses
/// réussies — ouvrir un album puis revenir, ou relancer une recherche, ne coûte rien.
/// </summary>
public sealed class DeezerApi(HttpClient http, CatalogGates gates, IMemoryCache cache, ILogger<DeezerApi> logger)
{
    public const int QuotaErrorCode = 4;
    public const string BaseUrl = "https://api.deezer.com/";

    /// <summary>Recherches, pistes : les URL d'extrait qu'elles portent sont signées et expirent.</summary>
    public static readonly TimeSpan ShortCache = TimeSpan.FromMinutes(5);

    /// <summary>Métadonnées d'album (genre, artiste d'album, nombre de pistes) : stables.</summary>
    public static readonly TimeSpan LongCache = TimeSpan.FromHours(6);

    public static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(8);

    private const int QuotaRetries = 3;

    /// <summary>Chemin relatif avec paramètres encodés (valeurs vides ou nulles omises).</summary>
    public static string Path(string path, params (string Key, object? Value)[] parameters)
    {
        var sb = new StringBuilder(path);
        var first = !path.Contains('?');
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (text is null) continue;
            sb.Append(first ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(text));
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// GET d'un document Deezer. Une panne réseau ou un délai dépassé lève une exception
    /// (<see cref="HttpRequestException"/>, <see cref="TimeoutException"/>) : à l'appelant de
    /// décider si elle vaut « aucun résultat » ou « erreur de connexion ».
    /// </summary>
    public async Task<DeezerResponse> GetAsync(string path, TimeSpan? cacheFor, TimeSpan timeout, CancellationToken ct)
    {
        var key = "dz:" + path;
        if (cacheFor is not null && cache.TryGetValue(key, out DeezerResponse? cached) && cached is not null) return cached;

        for (var attempt = 0; ; attempt++)
        {
            await gates.Deezer.WaitAsync(ct);
            var response = await SendAsync(path, timeout, ct);
            if (response.QuotaExceeded && attempt < QuotaRetries)
            {
                logger.LogInformation("Quota Deezer dépassé, nouvel essai ({Attempt})", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(1.5 * (attempt + 1)), ct);
                continue;
            }
            if (response.Ok && cacheFor is { } ttl)
                cache.Set(key, response, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl, Size = 1 });
            return response;
        }
    }

    private async Task<DeezerResponse> SendAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseContentRead, linked.Token);
            var status = (int)response.StatusCode;
            if (status != 200) return new DeezerResponse(status, default);
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token);
            JsonElement body;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                body = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Corps illisible : traité comme une absence de réponse, sans être mis en cache.
                return new DeezerResponse(status, default, -1, "Réponse Deezer illisible");
            }
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error))
            {
                int? code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : -1;
                var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.ToString() : "erreur";
                return new DeezerResponse(status, body, code, message);
            }
            return new DeezerResponse(status, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Délai dépassé : Deezer ne répond pas.");
        }
    }
}
