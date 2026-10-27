using System.Net.Http.Headers;
using System.Text.Json;
using DeeKeep.Catalog.Http;

namespace DeeKeep.Catalog.MusicBrainz;

/// <summary>Réponse brute de <c>/ws/2/recording</c> : code HTTP et tableau <c>recordings</c> (null si illisible).</summary>
public sealed record MusicBrainzResponse(int Status, IReadOnlyList<JsonElement>? Recordings);

/// <summary>
/// Accès à MusicBrainz (<c>https://musicbrainz.org/ws/2/recording</c>). Leur règle : un
/// agent identifiable et au plus une requête par seconde, globalement — la cadence est
/// partagée par la recherche de tags et les durées de référence (l'original tenait deux
/// horloges distinctes, qui pouvaient se chevaucher).
/// </summary>
public sealed class MusicBrainzApi(HttpClient http, CatalogGates gates)
{
    public const string BaseUrl = "https://musicbrainz.org/ws/2/";

    /// <summary>Agent de la recherche de tags (<c>MUSICBRAINZ_AGENT</c>).</summary>
    public const string TagsAgent = "DeeKeep/1.0 ( organiseur de bibliothèque musicale )";

    /// <summary>Agent des durées de référence (<c>reference_duration.USER_AGENT</c>).</summary>
    public const string ReferenceAgent = "DeeKeep/1.0 (bibliotheque musicale personnelle)";

    /// <summary>
    /// Une requête, cadencée. Une panne réseau ou un délai dépassé lève une exception ;
    /// un code HTTP quelconque est rendu tel quel (503 : service saturé, à retenter).
    /// </summary>
    public async Task<MusicBrainzResponse> SearchRecordingsAsync(string query, int limit, string userAgent, TimeSpan timeout, CancellationToken ct)
    {
        await gates.MusicBrainz.WaitAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var url = $"recording?query={Uri.EscapeDataString(query)}&fmt=json&limit={limit}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await http.SendAsync(request, linked.Token);
            var status = (int)response.StatusCode;
            if (status != 200) return new MusicBrainzResponse(status, null);
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(linked.Token));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("recordings", out var recordings) ||
                    recordings.ValueKind != JsonValueKind.Array)
                    return new MusicBrainzResponse(status, []);
                return new MusicBrainzResponse(status, recordings.EnumerateArray().Select(r => r.Clone()).ToList());
            }
            catch (JsonException)
            {
                return new MusicBrainzResponse(status, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Délai dépassé : MusicBrainz ne répond pas.");
        }
    }
}
