using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DeeKeep.Catalog.Media;

/// <summary>
/// Proxy d'images <c>/img?u=&lt;url&gt;</c> avec cache disque borné. Seuls les hôtes d'images
/// de Deezer sont acceptés (pas de proxy ouvert, pas de requête vers le réseau interne) ;
/// la taille d'une image est plafonnée et le cache évince les fichiers les moins récemment servis.
/// </summary>
public sealed class ImageProxy(IHttpClientFactory factory, CatalogPaths paths, IOptions<CatalogOptions> options, ILogger<ImageProxy> logger)
{
    public const string HttpClientName = "images";
    public const int MaxImageBytes = 8 * 1024 * 1024;

    private static readonly string[] AllowedSuffixes = ["dzcdn.net", "deezer.com", "deezerdev.com"];

    private readonly SemaphoreSlim _pruneLock = new(1, 1);
    private int _writes;

    /// <summary>L'hôte est-il un hôte d'images Deezer (domaine ou sous-domaine, en https) ?</summary>
    public static bool IsAllowed(Uri uri) =>
        uri.Scheme is "https" or "http" && AllowedSuffixes.Any(s =>
            uri.Host.Equals(s, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));

    public static string CacheName(string url) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    public async Task<IResult> ServeAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowed(uri))
            return Results.BadRequest("Hôte d'image non autorisé.");
        var file = Path.Combine(paths.ImageDir, CacheName(uri.AbsoluteUri));
        var typeFile = file + ".type";
        if (!File.Exists(file))
        {
            var fetched = await FetchAsync(uri, file, typeFile, ct);
            if (!fetched) return Results.NotFound();
        }
        else
        {
            try { File.SetLastAccessTimeUtc(file, DateTime.UtcNow); } catch (IOException) { /* sans importance */ }
        }
        var contentType = File.Exists(typeFile) ? await File.ReadAllTextAsync(typeFile, ct) : "image/jpeg";
        return new CachedFileResult(file, contentType);
    }

    private async Task<bool> FetchAsync(Uri uri, string file, string typeFile, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.ImageDir);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await factory.CreateClient(HttpClientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode) return false;
            var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;
            var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var output = File.Create(tmp))
            {
                await using var input = await response.Content.ReadAsStreamAsync(linked.Token);
                var buffer = new byte[16 * 1024];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, linked.Token)) > 0)
                {
                    total += read;
                    if (total > MaxImageBytes) { output.Close(); File.Delete(tmp); return false; }
                    await output.WriteAsync(buffer.AsMemory(0, read), linked.Token);
                }
            }
            await File.WriteAllTextAsync(typeFile, type, ct);
            File.Move(tmp, file, overwrite: true);
            if (Interlocked.Increment(ref _writes) % 50 == 0) _ = Task.Run(PruneAsync, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug("Image {Url} indisponible : {Error}", uri, ex.Message);
            return false;
        }
    }

    /// <summary>Ramène le cache sous sa taille maximale en supprimant les fichiers les moins récemment servis.</summary>
    public async Task PruneAsync()
    {
        if (!await _pruneLock.WaitAsync(0)) return;
        try
        {
            var dir = new DirectoryInfo(paths.ImageDir);
            if (!dir.Exists) return;
            var files = dir.EnumerateFiles().Where(f => f.Extension != ".type" && f.Extension != ".tmp").OrderBy(f => f.LastAccessTimeUtc).ToList();
            var limit = Math.Max(8, options.Value.ImageCacheMaxMb) * 1024L * 1024L;
            var size = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (size <= limit * 9 / 10) break;
                size -= f.Length;
                try { f.Delete(); File.Delete(f.FullName + ".type"); } catch (IOException) { /* en cours de lecture */ }
            }
        }
        finally
        {
            _pruneLock.Release();
        }
    }
}

/// <summary>Fichier du cache servi avec en-têtes de cache navigateur et prise en charge de Range.</summary>
public sealed class CachedFileResult(string path, string contentType) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "public, max-age=604800, immutable";
        return Results.File(new FileInfo(path).FullName, contentType, enableRangeProcessing: true).ExecuteAsync(httpContext);
    }
}
