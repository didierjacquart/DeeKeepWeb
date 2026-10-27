using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeeKeep.Contracts;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

/// <summary>Événement publié par un service (journal + flux SSE).</summary>
public sealed record ServiceEvent(long Id, string Service, string Type, string? Key, JsonNode? Data, double At);

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>État d'une tâche de fond, tel que le voient la passerelle et le client.</summary>
public sealed record JobDto(
    Guid Id,
    string Service,
    string Type,
    string? Title,
    JobStatus Status,
    double Progress,
    int Done,
    int Failed,
    int Total,
    string? Message,
    string? Error,
    int Attempts,
    string? GroupKey,
    JsonNode? Payload,
    JsonNode? Result,
    double CreatedAt,
    double UpdatedAt,
    double? StartedAt,
    double? FinishedAt);

/// <summary>Avancement d'un élément d'une tâche par lot (une piste extraite, un fichier analysé…).</summary>
public sealed record JobItemEvent(Guid JobId, string Type, string ItemKey, string Status, string? Message, JsonNode? Data);

public sealed record EnqueueJobRequest(string Type, string? Title, JsonNode? Payload, int Priority = 0, string? GroupKey = null, int MaxAttempts = 3);

public static class EventTypes
{
    public const string JobUpdated = "job.updated";
    public const string JobItem = "job.item";
    public const string TracksChanged = "tracks.changed";
    public const string TracksRemoved = "tracks.removed";
    public const string SettingsChanged = "settings.changed";
    public const string PresenceChanged = "presence.changed";
    public const string HistoryChanged = "history.changed";
    public const string SearchesChanged = "searches.changed";
    public const string QualityUpdated = "quality.updated";
    public const string ArchiveUpdated = "archive.updated";
    public const string FileProduced = "file.produced";
    public const string FileReplaced = "file.replaced";
    public const string FilesDeleted = "files.deleted";
    public const string ReferenceDurationFound = "reference.found";
}

public static class ServiceNames
{
    public const string Gateway = "gateway";
    public const string Catalog = "catalog";
    public const string Library = "library";
    public const string Downloader = "downloader";
    public const string Quality = "quality";
    public const string Storage = "storage";
}

/// <summary>Charge utile des événements <see cref="EventTypes.TracksChanged"/> / <see cref="EventTypes.TracksRemoved"/>.</summary>
public sealed record TracksChangedEvent(IReadOnlyList<string> Paths, string Reason);

/// <summary>Un fichier vient d'être produit sur le disque (extraction, conversion, retéléchargement, restauration).</summary>
public sealed record FileProducedEvent(string Path, string Origin, string? ReplacedPath, Guid? JobId);

/// <summary>Fichiers supprimés du disque (dédoublonnage, remplacement).</summary>
public sealed record FilesDeletedEvent(IReadOnlyList<string> Paths, string Reason);
