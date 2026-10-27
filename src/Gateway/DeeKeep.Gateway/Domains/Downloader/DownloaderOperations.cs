using System.Text.Json;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Downloader;

namespace DeeKeep.Gateway.Domains.Downloader;

[QueryType]
public static partial class DownloaderQueries
{
    /// <summary>
    /// Tâches d'un type (« download », « convert »…) avec leur charge utile : la piste ou le fichier de chaque ligne
    /// (la requête commune <c>jobs</c> est bornée à 200 tâches). L'avancement se suit par l'abonnement <c>jobUpdated</c>.
    /// </summary>
    public static Task<IReadOnlyList<JobDto>> GetDownloadJobs([Service] DownloaderClient client, string type, int? limit, CancellationToken ct) =>
        client.ListAsync(type, limit ?? 2000, ct);

    /// <summary>Lignes « En attente » de la page Musique (ajoutées par la recherche, pas encore extraites).</summary>
    public static Task<IReadOnlyList<PendingDownload>> GetPendingDownloads([Service] DownloaderClient client, CancellationToken ct) =>
        client.ListPendingAsync(ct);

    /// <summary>yt-dlp, ffmpeg, ffprobe, deno, cookies.txt : version et présence (page Diagnostic).</summary>
    public static Task<IReadOnlyList<ToolStatus>> GetDownloadTools([Service] DownloaderClient client, CancellationToken ct) =>
        client.GetToolsAsync(ct);

    /// <summary>Formats d'export possibles, qualité permise pour chacun, et valeurs courantes des Paramètres.</summary>
    public static Task<ExportChoices> GetExportOptions([Service] DownloaderClient client, CancellationToken ct) =>
        client.GetExportChoicesAsync(ct);
}

[MutationType]
public static partial class DownloaderMutations
{
    /// <summary>« Extraire » : une tâche « download » par piste (groupe = lot). Les lignes en attente désignées sont consommées.</summary>
    public static Task<IReadOnlyList<JobDto>> EnqueueDownloads([Service] DownloaderClient client, IReadOnlyList<DownloadTrackInput> tracks,
        string? batchId, IReadOnlyList<Guid>? pendingIds, ExportOptionsInput? options, CancellationToken ct) =>
        client.EnqueueDownloadsAsync(new EnqueueDownloadsRequest(tracks.Select(t => t.ToTrack()).ToList(),
            options is null ? null : new ExportOptions(options.Format, options.Quality), batchId, pendingIds), ct);

    /// <summary>« Convertir » (Réencoder) : conversion sur place, une tâche « convert » par fichier.</summary>
    public static Task<IReadOnlyList<JobDto>> EnqueueConversions([Service] DownloaderClient client, IReadOnlyList<ConvertItemInput> items,
        string? format, string? quality, string? batchId, CancellationToken ct) =>
        client.EnqueueConversionsAsync(new EnqueueConversionsRequest(items.Select(i => new ConvertItem(i.LocalPath, i.DeezerId)).ToList(),
            string.IsNullOrWhiteSpace(format) ? null : new ExportOptions(format, quality ?? ""), batchId), ct);

    /// <summary>Retéléchargement de titres abîmés (la page Qualité passe plutôt par son propre domaine).</summary>
    public static Task<IReadOnlyList<JobDto>> EnqueueRedownloads([Service] DownloaderClient client, IReadOnlyList<RedownloadItemInput> items,
        string? batchId, CancellationToken ct) =>
        client.EnqueueRedownloadsAsync(new EnqueueRedownloadsRequest(
            items.Select(i => new RedownloadItem(i.DbPath, i.Artist, i.Title, i.Album, i.Duration)).ToList(), null, batchId), ct);

    /// <summary>« Purger » : retire de la liste les tâches terminées d'un type (réussies seulement par défaut).</summary>
    public static Task<int> PurgeDownloads([Service] DownloaderClient client, string type, bool? succeededOnly, CancellationToken ct) =>
        client.PurgeAsync(type, succeededOnly ?? true, ct);

    /// <summary>« Retirer la sélection » : retire des tâches de la liste (une tâche en cours est d'abord arrêtée).</summary>
    public static Task<int> RemoveDownloadJobs([Service] DownloaderClient client, IReadOnlyList<Guid> ids, CancellationToken ct) =>
        client.RemoveJobsAsync(ids, ct);

    /// <summary>« Réessayer » : remet en file des tâches en échec ou annulées.</summary>
    public static Task<IReadOnlyList<JobDto>> RetryDownloadJobs([Service] DownloaderClient client, IReadOnlyList<Guid> ids, CancellationToken ct) =>
        client.RetryAsync(ids, ct);

    /// <summary>Ajoute des pistes à la liste de la page Musique (statut « En attente »), conservée côté serveur.</summary>
    public static Task<IReadOnlyList<PendingDownload>> AddPendingDownloads([Service] DownloaderClient client, IReadOnlyList<DownloadTrackInput> tracks,
        CancellationToken ct) =>
        client.AddPendingAsync(tracks.Select(t => t.ToTrack()).ToList(), ct);

    public static Task<int> RemovePendingDownloads([Service] DownloaderClient client, IReadOnlyList<Guid> ids, CancellationToken ct) =>
        client.RemovePendingAsync(ids, ct);

    /// <summary>Met à jour yt-dlp (tâche « tools.update »).</summary>
    public static Task<JobDto> UpdateYtDlp([Service] DownloaderClient client, CancellationToken ct) => client.UpdateYtDlpAsync(ct);
}

[SubscriptionType]
public static partial class DownloaderSubscriptions
{
    public static async IAsyncEnumerable<DeeKeep.Contracts.ServiceEvent> SubscribeToDownloadQueueChanged(
        [Service] HotChocolate.Subscriptions.ITopicEventReceiver receiver, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<DeeKeep.Contracts.ServiceEvent>(DeeKeep.Gateway.Infrastructure.Topics.For(DownloaderEvents.QueueChanged), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct)) yield return evt;
    }

    /// <summary>Retrait ou purge de tâches, ajout ou retrait de lignes en attente (tous appareils).</summary>
    [Subscribe(With = nameof(SubscribeToDownloadQueueChanged))]
    public static DownloadQueueChanged DownloadQueueChanged([EventMessage] DeeKeep.Contracts.ServiceEvent evt) =>
        evt.Data!.Deserialize<DownloadQueueChanged>(DeeKeep.Contracts.JsonDefaults.Options)!;
}
