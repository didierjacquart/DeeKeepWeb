using System.Text.Json.Nodes;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Library;

/// <summary>Un titre de la table <c>tracks</c>, enrichi de son état sur le disque et dans l'archive.</summary>
public sealed record TrackDto(
    string Path,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    string? Year,
    string? TrackNo,
    double? Duration,
    int? Bitrate,
    int? SampleRate,
    int? Channels,
    string? Codec,
    string? Ext,
    long? FileSize,
    double? FileMtime,
    string? CoverSha1,
    string? Source,
    bool InHistory,
    int? HistoryPos,
    bool OnDisk,
    string ArchiveState,
    long? ArchiveBytes,
    double UpdatedAt,
    string? Format = null,
    string? ArchiveKey = null,
    long? ArchiveSourceBytes = null,
    int? ArchiveBitrate = null,
    string? HistoryCoverSha1 = null,
    string? LocalPath = null);

public static class ArchiveStates
{
    public const string None = "none";
    public const string UpToDate = "ok";
    public const string Stale = "stale";
}

public sealed record TrackQuery(
    string? Search = null,
    bool IncludeMissing = false,
    bool HistoryOnly = false,
    string? Folder = null,
    string? SortBy = null,
    bool Descending = false,
    int Offset = 0,
    int Limit = 500);

public sealed record TrackPage(IReadOnlyList<TrackDto> Items, int Total, int TotalOnDisk, int TotalMissing);

/// <summary>Proposition de la recherche (valeur de tag réellement présente dans la bibliothèque).</summary>
public sealed record TagSuggestion(string Field, string Label, string Value, int Count);

/// <summary>Pastille de présence : ✓ present, ≈ possible, ○ absent, ⏳ pending.</summary>
public enum PresenceState
{
    Pending,
    Absent,
    Possible,
    Present,
}

public sealed record PresenceQuery(string Key, string Title, string Artist, string? Album, string? Ext = null);

public sealed record PresenceMatchFile(string Path, string Reason, int? Bitrate, long? FileSize, string? Ext);

public sealed record PresenceVerdict(string Key, PresenceState State, string? Reason, IReadOnlyList<PresenceMatchFile> Files);

/// <summary>Demande d'emplacement d'un fichier à extraire, selon les règles « Organiser » (library_rules.destination).</summary>
public sealed record DestinationRequest(
    string Title,
    string Artist,
    string? Album,
    string? AlbumArtist,
    string? Year,
    string? Genre,
    int? TrackNumber,
    int? TrackTotal,
    int? DiscNumber,
    string Extension,
    string? BatchId = null,
    int? Bitrate = null,
    string? ReplacedLocalPath = null,
    string? InPlaceFolder = null,
    int? DiscTotal = null,
    string? Root = null);

/// <summary>Chemin local absolu (serveur) où écrire le fichier, et sa forme stockée en base.</summary>
public sealed record DestinationResponse(string LocalPath, string DbPath, string Folder);

/// <summary>Réserve les titres d'un lot avant le démarrage des extractions (décompte « Divers »).</summary>
public sealed record ReserveBatchRequest(string BatchId, IReadOnlyList<DestinationRequest> Items);

/// <summary>Un fichier vient d'être écrit par une extraction, une conversion, un retéléchargement ou une restauration.</summary>
public sealed record RegisterFileRequest(
    string LocalPath,
    string Origin,
    bool AddToHistory,
    string? ReplacedLocalPath = null,
    long? DeezerId = null,
    string? CoverUrl = null,
    string? HistoryCoverBase64 = null);

/// <summary>Événement <see cref="EventTypes.PresenceChanged"/> : l'index de présence a changé (construit, complété, réduit).</summary>
public sealed record PresenceChangedEvent(bool Ready, int Files, long Version, string Reason);

public sealed record DeleteFilesRequest(IReadOnlyList<string> Paths, string Reason, bool ToTrash = true);

public sealed record DeleteFilesResult(IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed, IReadOnlyList<string> Errors);

public sealed record HistoryEntry(
    string Path,
    string? Title,
    string? Artist,
    string? Album,
    string? CoverSha1,
    int Position,
    bool OnDisk,
    double? DownloadedAt);

public sealed record FsEntry(string Name, string LocalPath, bool IsDirectory, long? Size, double? Mtime, bool IsAudio);

public sealed record FsListing(string LocalPath, string? Parent, IReadOnlyList<FsEntry> Entries, IReadOnlyList<string> Roots);

/// <summary>Fichier local lu par la page Réencoder (tags + caractéristiques).</summary>
public sealed record LocalFileInfo(
    string LocalPath,
    string DbPath,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    string? Year,
    string? TrackNo,
    double? Duration,
    int? Bitrate,
    int? SampleRate,
    string? Codec,
    string Ext,
    long FileSize,
    bool HasCover);

/// <summary>Écriture de tags (revue de « Compléter les tags » ou édition manuelle).</summary>
public sealed record WriteTagsItem(string Path, IReadOnlyDictionary<string, string?> Tags, string? CoverUrl = null, bool OverwriteExisting = false);

public sealed record WriteTagsRequest(IReadOnlyList<WriteTagsItem> Items);

/// <summary>Nombre de lignes d'une table de la base (vivantes, pierres tombales).</summary>
public sealed record TableCount(string Table, long Rows, long Deleted);

/// <summary>Informations sur la base existante (carte « Base de données » des Paramètres, page Diagnostic).</summary>
public sealed record DatabaseInfo(
    string Path,
    long SizeBytes,
    IReadOnlyList<TableCount> Tables,
    int HistoryCount,
    long CoverBytes,
    string? DownloadDir,
    string? LocalDownloadDir,
    bool PresenceReady,
    int PresenceFiles,
    string? DesktopDataDir);

/// <summary>Bilan d'une opération sur l'historique (purge, vidage).</summary>
public sealed record HistoryChangeResult(int Removed, string Message);

public sealed record LocalScanRequest(IReadOnlyList<string> Paths);

public sealed record DesktopImportRequest(string? DataDir = null, bool Settings = true, bool History = true);

public static class LibraryJobTypes
{
    public const string Scan = "library.scan";
    public const string ImportDesktop = "library.import";
    public const string OrganizeAnalyze = "organize.analyze";
    public const string OrganizeApply = "organize.apply";
    public const string WriteTags = "tags.write";
    public const string LocalScan = "files.scan";
}

[ServiceClient(ServiceNames.Library)]
public sealed class LibraryClient(HttpClient http) : ServiceClient(http, ServiceNames.Library)
{
    // Paramètres (table app_settings, propriété du service Library).
    public Task<IReadOnlyDictionary<string, JsonNode?>> GetSettingsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyDictionary<string, JsonNode?>>("/api/settings", ct);

    public Task<IReadOnlyDictionary<string, JsonNode?>> SetSettingsAsync(IReadOnlyDictionary<string, JsonNode?> values, CancellationToken ct = default) =>
        PutAsync<IReadOnlyDictionary<string, JsonNode?>>("/api/settings", values, ct);

    // Titres.
    public Task<TrackPage> QueryTracksAsync(TrackQuery query, CancellationToken ct = default) => PostAsync<TrackPage>("/api/tracks/query", query, ct);

    public Task<TrackDto?> GetTrackAsync(string dbPath, CancellationToken ct = default) =>
        GetOrDefaultAsync<TrackDto>($"/api/tracks/one?path={Uri.EscapeDataString(dbPath)}", ct);

    public Task<IReadOnlyList<TagSuggestion>> SuggestAsync(string text, int limit = 5, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TagSuggestion>>($"/api/tracks/suggest?q={Uri.EscapeDataString(text)}&limit={limit}", ct);

    // Présence sur le disque (pastilles).
    public Task<IReadOnlyList<PresenceVerdict>> LookupPresenceAsync(IReadOnlyList<PresenceQuery> items, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<PresenceVerdict>>("/api/presence/lookup", items, ct);

    // Règles de rangement des extractions.
    public Task ReserveBatchAsync(ReserveBatchRequest request, CancellationToken ct = default) => PostAsync("/api/rules/reserve", request, ct);

    public Task<DestinationResponse> ResolveDestinationAsync(DestinationRequest request, CancellationToken ct = default) =>
        PostAsync<DestinationResponse>("/api/rules/destination", request, ct);

    /// <summary>Chemin réel (serveur) correspondant à un chemin stocké en base.</summary>
    public Task<string> ToLocalPathAsync(string dbPath, CancellationToken ct = default) =>
        GetAsync<string>($"/api/paths/local?path={Uri.EscapeDataString(dbPath)}", ct);

    // Fichiers produits / supprimés. À appeler de préférence via la boîte d'envoi (OutboxQueue) :
    // POST /api/files/register et POST /api/files/delete sont idempotents.
    public Task<TrackDto?> RegisterFileAsync(RegisterFileRequest request, CancellationToken ct = default) =>
        PostAsync<TrackDto?>("/api/files/register", request, ct);

    public Task<DeleteFilesResult> DeleteFilesAsync(DeleteFilesRequest request, CancellationToken ct = default) =>
        PostAsync<DeleteFilesResult>("/api/files/delete", request, ct);

    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<HistoryEntry>>("/api/history", ct);

    public Task<FsListing> ListDirectoryAsync(string? localPath, bool audioOnly = false, CancellationToken ct = default) =>
        GetAsync<FsListing>($"/api/fs/list?path={Uri.EscapeDataString(localPath ?? "")}&audioOnly={audioOnly.ToString().ToLowerInvariant()}", ct);

    public Task<LocalFileInfo?> ReadFileInfoAsync(string localPath, CancellationToken ct = default) =>
        GetOrDefaultAsync<LocalFileInfo>($"/api/files/info?path={Uri.EscapeDataString(localPath)}", ct);

    public Task<JobDto> WriteTagsAsync(WriteTagsRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/tags/write", request, ct);

    // Historique des téléchargements.
    public Task<HistoryChangeResult> PurgeMissingHistoryAsync(CancellationToken ct = default) =>
        PostAsync<HistoryChangeResult>("/api/history/purge-missing", null, ct);

    public Task<HistoryChangeResult> ClearHistoryAsync(CancellationToken ct = default) =>
        PostAsync<HistoryChangeResult>("/api/history/clear", null, ct);

    // Tâches : collecte de la bibliothèque, import de l'application desktop, lecture de fichiers locaux.
    public Task<JobDto> StartLibraryScanAsync(CancellationToken ct = default) => PostAsync<JobDto>("/api/library/scan", null, ct);

    public Task<JobDto> StartDesktopImportAsync(DesktopImportRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/library/import", request, ct);

    public Task<JobDto> ScanLocalFilesAsync(LocalScanRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/files/scan", request, ct);

    public Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken ct = default) => GetAsync<DatabaseInfo>("/api/database", ct);
}

/// <summary>Événements propres au service Library (en plus de ceux de <see cref="EventTypes"/>).</summary>
public static class LibraryEvents
{
    /// <summary>La bibliothèque a été relue ou rangée : l'index de présence et les listes sont à recharger.</summary>
    public const string LibraryChanged = "library.changed";
}
