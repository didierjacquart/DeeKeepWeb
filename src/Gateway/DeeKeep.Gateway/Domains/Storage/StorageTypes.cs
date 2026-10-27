using System.Runtime.CompilerServices;
using System.Text.Json;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Storage;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.Subscriptions;

namespace DeeKeep.Gateway.Domains.Storage;

[QueryType]
public static partial class StorageQueries
{
    /// <summary>Emplacement de l'archive (équivalent d'archive.json).</summary>
    public static Task<ArchiveConfig> GetArchiveConfig([Service] StorageClient storage, CancellationToken ct) => storage.GetConfigAsync(ct);

    /// <summary>Jauge : occupation, place libre du disque de l'archive, titres archivés / à renvoyer / non archivés. Calculée sans relire les fichiers audio. <c>root</c> : emplacement saisi, pas encore enregistré.</summary>
    public static Task<ArchiveUsage> GetArchiveUsage([Service] StorageClient storage, string? root, CancellationToken ct) => storage.GetUsageAsync(root, ct);

    /// <summary>Ce qu'Archiver et Restaurer feraient des titres retenus (chemins en base).</summary>
    public static Task<ArchivePlan> GetArchivePlan([Service] StorageClient storage, IReadOnlyList<string> paths, CancellationToken ct) => storage.PlanAsync(paths, ct);
}

[MutationType]
public static partial class StorageMutations
{
    /// <summary>Enregistre l'emplacement de l'archive.</summary>
    public static Task<ArchiveConfig> UpdateArchiveConfig([Service] StorageClient storage, string? root, string? prefix, CancellationToken ct) =>
        storage.SetConfigAsync(new ArchiveConfig(root, prefix ?? ArchiveDefaults.Prefix, null, false), ct);

    /// <summary>Vérifie que l'archive (valeur saisie, même non enregistrée) s'ouvre et accepte une écriture.</summary>
    public static Task<ArchiveTestResult> TestArchive([Service] StorageClient storage, string? root, CancellationToken ct) => storage.TestAsync(root, ct);

    /// <summary>Archive les titres (chemins en base) tels quels. Rend la tâche.</summary>
    public static Task<JobDto> ArchiveTracks([Service] StorageClient storage, IReadOnlyList<string> paths, CancellationToken ct) =>
        storage.ArchiveAsync(new ArchiveRequest(paths), ct);

    /// <summary>Restaure les titres absents du disque ; rien n'est écrasé. Rend la tâche.</summary>
    public static Task<JobDto> RestoreTracks([Service] StorageClient storage, IReadOnlyList<string> paths, CancellationToken ct) =>
        storage.RestoreAsync(new ArchiveRequest(paths), ct);

    /// <summary>Retire des titres de l'archive ; une copie partagée par d'autres titres identiques est conservée. Rend le nombre de titres retirés.</summary>
    public static Task<int> RemoveFromArchive([Service] StorageClient storage, IReadOnlyList<string> paths, CancellationToken ct) => storage.RemoveAsync(paths, ct);

    /// <summary>Mesure l'occupation réelle de l'archive (tâche).</summary>
    public static Task<JobDto> MeasureArchive([Service] StorageClient storage, string? root, CancellationToken ct) => storage.MeasureAsync(root, ct);

    /// <summary>Vérifie que chaque titre archivé retrouve sa copie (tâche).</summary>
    public static Task<JobDto> CheckArchive([Service] StorageClient storage, CancellationToken ct) => storage.CheckAsync(ct);
}

[SubscriptionType]
public static partial class StorageSubscriptions
{
    public static async IAsyncEnumerable<ServiceEvent> SubscribeToArchiveUpdated([Service] ITopicEventReceiver receiver, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<ServiceEvent>(Topics.For(EventTypes.ArchiveUpdated), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct)) yield return evt;
    }

    /// <summary>Lignes d'archive écrites ou retirées : la colonne NUAGE se met à jour.</summary>
    [Subscribe(With = nameof(SubscribeToArchiveUpdated))]
    public static ArchiveUpdatedEvent ArchiveUpdated([EventMessage] ServiceEvent evt) =>
        evt.Data!.Deserialize<ArchiveUpdatedEvent>(JsonDefaults.Options)!;
}
