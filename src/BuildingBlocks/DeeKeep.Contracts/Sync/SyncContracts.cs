using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Sync;

// ---------------------------------------------------------------- synchronisation du disque USB (Storage)

/// <summary>Bilan d'un passage de synchronisation entre le disque USB et la bibliothèque.</summary>
public sealed record UsbSyncSummary(
    double At,
    int Files,
    int CopiedToUsb,
    int CopiedToLibrary,
    int Replaced,
    int Trashed,
    int Failed,
    long Bytes,
    string Message);

/// <summary>État de la synchronisation pour la page Paramètres.</summary>
public sealed record UsbSyncStatus(
    bool Enabled,
    string? UsbRoot,
    string? LibraryRoot,
    bool UsbPresent,
    bool LibraryPresent,
    long? UsbFreeBytes,
    long? UsbTotalBytes,
    int PendingDeletions,
    JobDto? Running,
    UsbSyncSummary? Last,
    string? Problem,
    bool Ejected = false);

public static class SyncJobTypes
{
    public const string Run = "usbsync.run";
}

[ServiceClient(ServiceNames.Storage)]
public sealed class SyncClient(HttpClient http) : ServiceClient(http, ServiceNames.Storage)
{
    public Task<UsbSyncStatus> GetStatusAsync(CancellationToken ct = default) => GetAsync<UsbSyncStatus>("/api/sync/status", ct);

    /// <summary>Lance une synchronisation (ou rend celle qui tourne déjà).</summary>
    public Task<JobDto> RunAsync(CancellationToken ct = default) => PostAsync<JobDto>("/api/sync/run", null, ct);

    /// <summary>Éjecte le disque (démontage propre) ; rend l'état à jour.</summary>
    public Task<UsbSyncStatus> EjectAsync(CancellationToken ct = default) => PostAsync<UsbSyncStatus>("/api/sync/eject", null, ct);

    /// <summary>Remonte un disque éjecté resté branché.</summary>
    public Task<UsbSyncStatus> MountAsync(CancellationToken ct = default) => PostAsync<UsbSyncStatus>("/api/sync/mount", null, ct);
}

// ---------------------------------------------------------------- corbeille de la bibliothèque (Library)

/// <summary>
/// Fichier mis à la corbeille depuis l'application, récupérable jusqu'à <see cref="ExpiresAt"/>
/// (null : conservé tant qu'on ne l'efface pas, purge automatique désactivée).
/// </summary>
public sealed record TrashEntry(string Id, string DbPath, string Name, string Folder, double DeletedAt, double? ExpiresAt, long Size);

/// <summary>Durée de conservation de la corbeille en jours ; 0 = jamais d'effacement automatique.</summary>
public sealed record TrashSettings(int RetentionDays);

/// <summary><see cref="UserId"/> : qui restaure (le titre revient dans sa bibliothèque).</summary>
public sealed record TrashRestoreRequest(IReadOnlyList<string> Ids, string? UserId = null);

public sealed record TrashResult(int Done, IReadOnlyList<string> Errors);

/// <summary>
/// Suppression explicite (action de l'utilisateur dans l'application) : seule elle se propage au
/// disque USB. <see cref="Relative"/> : chemin sous la racine de la bibliothèque (« / »), null hors racine.
/// </summary>
public sealed record TrashedFile(string DbPath, string? Relative, double DeletedAt);

public sealed record FilesTrashedEvent(IReadOnlyList<TrashedFile> Files);

/// <summary>Fichiers sortis de la corbeille : leur suppression ne doit plus se propager.</summary>
public sealed record FilesRestoredEvent(IReadOnlyList<string> Relatives);

public static class TrashEvents
{
    public const string Trashed = "files.trashed";
    public const string Restored = "files.restored";
}

[ServiceClient(ServiceNames.Library)]
public sealed class TrashClient(HttpClient http) : ServiceClient(http, ServiceNames.Library)
{
    public Task<IReadOnlyList<TrashEntry>> ListAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<TrashEntry>>("/api/trash", ct);

    public Task<TrashSettings> GetSettingsAsync(CancellationToken ct = default) => GetAsync<TrashSettings>("/api/trash/settings", ct);

    public Task<TrashSettings> SetSettingsAsync(TrashSettings settings, CancellationToken ct = default) =>
        PutAsync<TrashSettings>("/api/trash/settings", settings, ct);

    public Task<TrashResult> RestoreAsync(IReadOnlyList<string> ids, string? userId = null, CancellationToken ct = default) =>
        PostAsync<TrashResult>("/api/trash/restore", new TrashRestoreRequest(ids, userId), ct);

    /// <summary>Efface définitivement des entrées (toutes si <paramref name="ids"/> est vide).</summary>
    public Task<TrashResult> PurgeAsync(IReadOnlyList<string> ids, CancellationToken ct = default) =>
        PostAsync<TrashResult>("/api/trash/purge", new TrashRestoreRequest(ids), ct);
}
