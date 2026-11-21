using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Gateway.Api;

/// <summary>Piste Deezer à extraire (telle que la renvoie la recherche).</summary>
public sealed record DownloadTrackBody(
    long Id, string Title, string Artist, string? Album, double Duration, long? ArtistId = null, long? AlbumId = null,
    string? PreviewUrl = null, string? CoverSmall = null, string? CoverBig = null, string? Link = null, int? TrackPosition = null, int? DiskNumber = null)
{
    public DeezerTrack ToTrack() => new(Id, Title, Artist, ArtistId, Album, AlbumId, Duration, null, PreviewUrl, CoverSmall, CoverBig, Link,
        false, TrackPosition, DiskNumber);
}

/// <summary>Format et qualité d'export ; omis = valeurs des Paramètres.</summary>
public sealed record ExportOptionsBody(string Format, string Quality);

public sealed record EnqueueDownloadsBody(IReadOnlyList<DownloadTrackBody> Tracks, string? BatchId = null, IReadOnlyList<Guid>? PendingIds = null,
    ExportOptionsBody? Options = null);

public sealed record ConvertItemBody(string LocalPath, long? DeezerId = null);

public sealed record EnqueueConversionsBody(IReadOnlyList<ConvertItemBody> Items, string? Format = null, string? Quality = null, string? BatchId = null);

public sealed record RedownloadItemBody(string DbPath, string Artist, string Title, string? Album = null, double? Duration = null);

public sealed record EnqueueRedownloadsBody(IReadOnlyList<RedownloadItemBody> Items, string? BatchId = null);

public sealed record TracksBody(IReadOnlyList<DownloadTrackBody> Tracks);

public sealed record IdsBody(IReadOnlyList<Guid> Ids);

public sealed record PurgeBody(string Type, bool? SucceededOnly = null);

public static class DownloaderApi
{
    public static void MapDownloaderApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/downloads");

        // Tâches d'un type (« download », « convert »…) avec leur charge utile.
        g.MapGet("/jobs", (DownloaderClient client, string type, int? limit, CancellationToken ct) => client.ListAsync(type, limit ?? 2000, ct));
        // « Extraire » : une tâche « download » par piste ; les lignes en attente désignées sont consommées.
        g.MapPost("/jobs", async (HttpContext ctx, DownloaderClient client, UserTracksClient userTracks, EnqueueDownloadsBody body,
            CancellationToken ct) =>
        {
            var tracks = body.Tracks.Select(t => t.ToTrack()).ToList();
            var userId = UserTracksApi.UserIdOf(ctx);
            if (userId is not null && tracks.Count > 0)
            {
                // Un fichier par titre : déjà présent (✓) → associé sans téléchargement ; déjà en cours
                // d'extraction pour quelqu'un d'autre → associé à l'arrivée du fichier.
                var running = (await client.ListAsync("download", 2000, ct))
                    .Where(j => j.Status is JobStatus.Queued or JobStatus.Running)
                    .Select(j => j.Payload?["track"]?["id"]?.GetValue<long>())
                    .OfType<long>()
                    .ToHashSet();
                var claim = await userTracks.PostClaimAsync(new ClaimRequest(userId,
                    tracks.Where(t => !running.Contains(t.Id)).Select(t => new ClaimItem(t.Id.ToString(), t.Title, t.Artist, t.Album, t.Id)).ToList(),
                    tracks.Where(t => running.Contains(t.Id)).Select(t => t.Id).ToList()), ct);
                var skip = claim.Claimed.Select(long.Parse).Concat(running).ToHashSet();
                tracks = tracks.Where(t => !skip.Contains(t.Id)).ToList();
                if (tracks.Count == 0)
                {
                    if (body.PendingIds is { Count: > 0 } pending) await client.RemovePendingAsync(pending, ct);
                    return (IReadOnlyList<JobDto>)[];
                }
            }
            return await client.EnqueueDownloadsAsync(new EnqueueDownloadsRequest(tracks,
                body.Options is null ? null : new ExportOptions(body.Options.Format, body.Options.Quality), body.BatchId, body.PendingIds, userId), ct);
        });
        // « Réessayer », « Retirer la sélection », « Purger ».
        g.MapPost("/jobs/retry", (DownloaderClient client, IdsBody body, CancellationToken ct) => client.RetryAsync(body.Ids, ct));
        g.MapPost("/jobs/remove", (DownloaderClient client, IdsBody body, CancellationToken ct) => client.RemoveJobsAsync(body.Ids, ct));
        g.MapPost("/jobs/purge", (DownloaderClient client, PurgeBody body, CancellationToken ct) => client.PurgeAsync(body.Type, body.SucceededOnly ?? true, ct));

        // « Convertir » (Réencoder) : une tâche « convert » par fichier.
        g.MapPost("/conversions", (DownloaderClient client, EnqueueConversionsBody body, CancellationToken ct) =>
            client.EnqueueConversionsAsync(new EnqueueConversionsRequest(body.Items.Select(i => new ConvertItem(i.LocalPath, i.DeezerId)).ToList(),
                string.IsNullOrWhiteSpace(body.Format) ? null : new ExportOptions(body.Format, body.Quality ?? ""), body.BatchId), ct));
        // Retéléchargement de titres abîmés.
        g.MapPost("/redownloads", (DownloaderClient client, EnqueueRedownloadsBody body, CancellationToken ct) =>
            client.EnqueueRedownloadsAsync(new EnqueueRedownloadsRequest(
                body.Items.Select(i => new RedownloadItem(i.DbPath, i.Artist, i.Title, i.Album, i.Duration)).ToList(), null, body.BatchId), ct));

        // Lignes « En attente » de la page Musique (conservées côté serveur).
        g.MapGet("/pending", (DownloaderClient client, CancellationToken ct) => client.ListPendingAsync(ct));
        g.MapPost("/pending", (DownloaderClient client, TracksBody body, CancellationToken ct) =>
            client.AddPendingAsync(body.Tracks.Select(t => t.ToTrack()).ToList(), ct));
        g.MapPost("/pending/remove", (DownloaderClient client, IdsBody body, CancellationToken ct) => client.RemovePendingAsync(body.Ids, ct));

        // yt-dlp, ffmpeg, ffprobe, deno, cookies.txt ; mise à jour de yt-dlp (tâche « tools.update »).
        g.MapGet("/tools", (DownloaderClient client, CancellationToken ct) => client.GetToolsAsync(ct));
        g.MapPost("/tools/update-ytdlp", (DownloaderClient client, CancellationToken ct) => client.UpdateYtDlpAsync(ct));

        // Formats d'export possibles et valeurs courantes des Paramètres.
        g.MapGet("/export-options", (DownloaderClient client, CancellationToken ct) => client.GetExportChoicesAsync(ct));
    }
}
