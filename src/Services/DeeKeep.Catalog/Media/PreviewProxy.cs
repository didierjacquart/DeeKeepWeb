using DeeKeep.Catalog.Deezer;

namespace DeeKeep.Catalog.Media;

/// <summary>
/// Extrait de 30 s <c>/preview/{deezerId}</c> : l'URL signée est résolue à chaque lecture via
/// <c>/track/{id}</c> (elle expire : jamais conservée), puis le flux MP3 est relayé, avec
/// prise en charge de Range pour que le navigateur puisse se positionner. Seul le CDN
/// Deezer est contacté.
/// </summary>
public sealed class PreviewProxy(DeezerCatalog catalog, IHttpClientFactory factory, ILogger<PreviewProxy> logger)
{
    public const string HttpClientName = "previews";

    public async Task ServeAsync(long id, HttpContext context)
    {
        var ct = context.RequestAborted;
        string? url;
        try
        {
            url = await catalog.PreviewUrlAsync(id, refresh: false, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !ImageProxy.IsAllowed(uri))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var http = factory.CreateClient(HttpClientName);
        // Une URL périmée (403/404) est redemandée une fois, sans cache.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (context.Request.Headers.Range.Count > 0) request.Headers.TryAddWithoutValidation("Range", context.Request.Headers.Range.ToString());
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (attempt == 0 && (int)response.StatusCode is 403 or 404 or 410)
            {
                var fresh = await catalog.PreviewUrlAsync(id, refresh: true, ct);
                if (fresh is not null && Uri.TryCreate(fresh, UriKind.Absolute, out var next) && ImageProxy.IsAllowed(next)) { uri = next; continue; }
            }
            if (!response.IsSuccessStatusCode)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
            context.Response.Headers.AcceptRanges = "bytes";
            if (response.Content.Headers.ContentLength is { } length) context.Response.ContentLength = length;
            if (response.Content.Headers.ContentRange is { } range) context.Response.Headers.ContentRange = range.ToString();
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await stream.CopyToAsync(context.Response.Body, ct);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Extrait {Id} : lecture interrompue par le client", id);
            }
            return;
        }
    }
}
