using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Downloader;

/// <summary>Format et qualité d'export (Paramètres : <c>file_format</c>, <c>quality</c>).</summary>
public sealed record ExportOptions(string Format, string Quality);

/// <summary>
/// Ajout de pistes Deezer à la file d'extraction (page Musique). Chaque piste
/// devient une tâche « download » du groupe <see cref="BatchId"/>.
/// </summary>
public sealed record EnqueueDownloadsRequest(IReadOnlyList<DeezerTrack> Tracks, ExportOptions? Options = null, string? BatchId = null,
    IReadOnlyList<Guid>? PendingIds = null);

/// <summary>Conversion sur place de fichiers locaux (page Réencoder).</summary>
public sealed record ConvertItem(string LocalPath, long? DeezerId = null);

public sealed record EnqueueConversionsRequest(IReadOnlyList<ConvertItem> Items, ExportOptions? Options = null, string? BatchId = null);

/// <summary>Retéléchargement d'un titre abîmé (page Qualité) : recherche à partir de ses tags, remplacement du fichier.</summary>
public sealed record RedownloadItem(string DbPath, string Artist, string Title, string? Album, double? Duration);

public sealed record EnqueueRedownloadsRequest(IReadOnlyList<RedownloadItem> Items, ExportOptions? Options = null, string? BatchId = null);

/// <summary>Charge utile et résultat d'une tâche d'extraction, lisibles par le client (colonne STATUT, infobulle).</summary>
public sealed record DownloadJobResult(string? LocalPath, string? DbPath, string? VideoId, string? VideoTitle, int Attempts, string? Warning,
    string? Title = null, string? Artist = null, string? Album = null, string? Format = null, int? Bitrate = null, string? CoverUrl = null,
    long? DeezerId = null);

/// <summary>Charge utile d'une tâche « download » (une ligne de la page Musique).</summary>
public sealed record DownloadJobPayload(DeezerTrack Track, ExportOptions? Options, string BatchId);

/// <summary>Charge utile d'une tâche « convert » (une ligne de la page Réencoder).</summary>
public sealed record ConvertJobPayload(ConvertItem Item, ExportOptions? Options, string BatchId);

/// <summary>Charge utile d'une tâche « redownload » (une ligne de la page Qualité).</summary>
public sealed record RedownloadJobPayload(RedownloadItem Item, ExportOptions? Options, string BatchId);

/// <summary>
/// Ligne « En attente » de la page Musique : ajoutée depuis la recherche, pas
/// encore extraite (« Extraire » en fait une tâche « download »). Conservée sur
/// le serveur pour survivre à un rechargement et se voir depuis un autre appareil.
/// </summary>
public sealed record PendingDownload(Guid Id, DeezerTrack Track, double AddedAt);

/// <summary>Formats proposés par les Paramètres et qualités permises pour chacun.</summary>
public sealed record ExportFormatChoice(string Format, IReadOnlyList<string> Qualities);

/// <summary>Choix d'export possibles et valeurs courantes (<c>file_format</c>, <c>quality</c>).</summary>
public sealed record ExportChoices(IReadOnlyList<ExportFormatChoice> Formats, ExportOptions Current);

/// <summary>La file de la page Musique ou Réencoder a changé hors des événements de tâche (retrait, purge, lignes en attente).</summary>
public sealed record DownloadQueueChanged(IReadOnlyList<Guid> RemovedJobIds, bool PendingChanged);

public static class DownloaderEvents
{
    /// <summary>Retrait ou purge de tâches, ajout ou retrait de lignes en attente (<see cref="DownloadQueueChanged"/>).</summary>
    public const string QueueChanged = "downloads.changed";
}

public static class DownloaderJobTypes
{
    public const string Download = "download";
    public const string Convert = "convert";
    public const string Redownload = "redownload";
    public const string UpdateYtDlp = "tools.update";
}

[ServiceClient(ServiceNames.Downloader)]
public sealed class DownloaderClient(HttpClient http) : ServiceClient(http, ServiceNames.Downloader)
{
    public Task<IReadOnlyList<JobDto>> EnqueueDownloadsAsync(EnqueueDownloadsRequest request, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<JobDto>>("/api/downloads", request, ct);

    public Task<IReadOnlyList<JobDto>> EnqueueConversionsAsync(EnqueueConversionsRequest request, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<JobDto>>("/api/conversions", request, ct);

    public Task<IReadOnlyList<JobDto>> EnqueueRedownloadsAsync(EnqueueRedownloadsRequest request, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<JobDto>>("/api/redownloads", request, ct);

    /// <summary>Retire de la liste les tâches terminées d'un type (« Purger les téléchargés »).</summary>
    public Task<int> PurgeAsync(string type, bool succeededOnly, CancellationToken ct = default) =>
        PostAsync<int>($"/api/purge?type={Uri.EscapeDataString(type)}&succeededOnly={succeededOnly.ToString().ToLowerInvariant()}", null, ct);

    public Task<int> RemoveJobsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        PostAsync<int>("/api/remove", ids, ct);

    /// <summary>Tâches d'un type avec leur charge utile (la liste commune des tâches est bornée à 200 et sans charge utile).</summary>
    public Task<IReadOnlyList<JobDto>> ListAsync(string type, int limit = 2000, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<JobDto>>($"/api/jobs?type={Uri.EscapeDataString(type)}&limit={limit}", ct);

    /// <summary>« Réessayer » : remet en file des tâches en échec ou annulées (et les compte dans le lot en cours).</summary>
    public Task<IReadOnlyList<JobDto>> RetryAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<JobDto>>("/api/retry", ids, ct);

    public Task<IReadOnlyList<PendingDownload>> ListPendingAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<PendingDownload>>("/api/pending", ct);

    public Task<IReadOnlyList<PendingDownload>> AddPendingAsync(IReadOnlyList<DeezerTrack> tracks, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<PendingDownload>>("/api/pending", tracks, ct);

    public Task<int> RemovePendingAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        PostAsync<int>("/api/pending/remove", ids, ct);

    public Task<ExportChoices> GetExportChoicesAsync(CancellationToken ct = default) =>
        GetAsync<ExportChoices>("/api/export-options", ct);

    /// <summary>yt-dlp, ffmpeg, ffprobe, deno, cookies.txt (page Diagnostic).</summary>
    public Task<IReadOnlyList<ToolStatus>> GetToolsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ToolStatus>>("/api/tools", ct);

    /// <summary>Lance la tâche « tools.update » (mise à jour de yt-dlp).</summary>
    public Task<JobDto> UpdateYtDlpAsync(CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/tools/update", null, ct);
}
