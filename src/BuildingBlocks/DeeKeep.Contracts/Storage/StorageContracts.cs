using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Storage;

/// <summary>Configuration de l'archive (équivalent de <c>archive.json</c> : dossier du fichier d'archive, préfixe des clés).</summary>
public sealed record ArchiveConfig(string? Root, string Prefix, string? ArchiveFile, bool Configured);

/// <summary>Jauge de la page Bibliothèque : place occupée, place libre sur le disque de l'archive, titres archivés.</summary>
public sealed record ArchiveUsage(
    long ObjectCount,
    long StoredBytes,
    long ArchiveFileBytes,
    long? DiskFreeBytes,
    long? DiskTotalBytes,
    int TracksArchived,
    int TracksStale,
    int TracksNotArchived,
    long BytesToArchive,
    int TracksOnDisk = 0,
    long BytesOnDisk = 0,
    bool SameVolume = false,
    bool Configured = false,
    string? ArchiveFile = null,
    string? Error = null);

public sealed record ArchiveTestResult(bool Ok, string Message);

/// <summary>
/// Ce qu'Archiver et Restaurer feraient des titres retenus : de quoi reprendre les
/// messages de l'original (« rien à archiver », « … le sont ailleurs dans la bibliothèque »).
/// </summary>
public sealed record ArchivePlan(int Requested, int OnDisk, int Restorable, int RestorableElsewhere, bool Configured);

/// <summary>Charge utile de l'événement <see cref="EventTypes.ArchiveUpdated"/> : lignes de la table <c>archive</c> écrites ou retirées.</summary>
public sealed record ArchiveUpdatedEvent(IReadOnlyList<string> Paths, string Reason);

/// <summary>Raisons de <see cref="ArchiveUpdatedEvent"/>.</summary>
public static class ArchiveUpdateReasons
{
    public const string Archived = "archived";
    public const string Restored = "restored";
    public const string Removed = "removed";
}

/// <summary>Suite d'un titre dans une tâche d'archivage ou de restauration (données de <c>job.item</c>).</summary>
public sealed record ArchiveItemResult(string Path, string State, string? Message, long Bytes, string? Key);

/// <summary>États d'un titre traité (<c>MirrorThread</c> / <c>RestoreThread</c> de l'original).</summary>
public static class ArchiveItemStates
{
    public const string Sent = "sent";
    public const string Reused = "reused";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
    public const string Restored = "restored";
}

/// <summary>Demande de mesure ou de test portant sur un emplacement saisi mais pas encore enregistré.</summary>
public sealed record ArchiveRootRequest(string? Root);

public sealed record ArchiveRequest(IReadOnlyList<string> DbPaths, string? BatchId = null);

public static class ArchiveDefaults
{
    public const string Prefix = "deekeep/";
}

public static class StorageJobTypes
{
    public const string Archive = "archive.upload";
    public const string Restore = "archive.restore";
    public const string Check = "archive.check";
    public const string Usage = "archive.usage";
}

[ServiceClient(ServiceNames.Storage)]
public sealed class StorageClient(HttpClient http) : ServiceClient(http, ServiceNames.Storage)
{
    public Task<ArchiveConfig> GetConfigAsync(CancellationToken ct = default) => GetAsync<ArchiveConfig>("/api/archive/config", ct);

    public Task<ArchiveConfig> SetConfigAsync(ArchiveConfig config, CancellationToken ct = default) => PutAsync<ArchiveConfig>("/api/archive/config", config, ct);

    public Task<ArchiveTestResult> TestAsync(string? root, CancellationToken ct = default) =>
        PostAsync<ArchiveTestResult>("/api/archive/test", new { root }, ct);

    public Task<ArchiveUsage> GetUsageAsync(CancellationToken ct = default) => GetAsync<ArchiveUsage>("/api/archive/usage", ct);

    public Task<JobDto> ArchiveAsync(ArchiveRequest request, CancellationToken ct = default) => PostAsync<JobDto>("/api/archive/upload", request, ct);

    public Task<JobDto> RestoreAsync(ArchiveRequest request, CancellationToken ct = default) => PostAsync<JobDto>("/api/archive/restore", request, ct);

    public Task<int> RemoveAsync(IReadOnlyList<string> dbPaths, CancellationToken ct = default) => PostAsync<int>("/api/archive/remove", dbPaths, ct);

    public Task<JobDto> MeasureAsync(CancellationToken ct = default) => PostAsync<JobDto>("/api/archive/measure", null, ct);

    /// <summary>Jauge calculée pour un emplacement saisi (place libre, même volume) ; null = emplacement enregistré.</summary>
    public Task<ArchiveUsage> GetUsageAsync(string? root, CancellationToken ct = default) =>
        GetAsync<ArchiveUsage>("/api/archive/usage" + (string.IsNullOrWhiteSpace(root) ? "" : "?root=" + Uri.EscapeDataString(root)), ct);

    /// <summary>Mesure de l'occupation réelle d'une archive saisie (null = emplacement enregistré).</summary>
    public Task<JobDto> MeasureAsync(string? root, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/archive/measure", new ArchiveRootRequest(root), ct);

    public Task<ArchivePlan> PlanAsync(IReadOnlyList<string> dbPaths, CancellationToken ct = default) =>
        PostAsync<ArchivePlan>("/api/archive/plan", dbPaths, ct);

    /// <summary>Vérifie que chaque ligne vivante de la table <c>archive</c> retrouve son objet (tâche <c>archive.check</c>).</summary>
    public Task<JobDto> CheckAsync(CancellationToken ct = default) => PostAsync<JobDto>("/api/archive/check", null, ct);
}
