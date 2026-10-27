using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Gateway.Infrastructure;
using HotChocolate.Subscriptions;

namespace DeeKeep.Gateway.Domains.Library;

/// <summary>Une valeur de tag à écrire (champ du vocabulaire de tag_write : title, artist, album, albumartist, genre, date, track…).</summary>
public sealed record TagValueInput(string Field, string? Value);

/// <summary>Écriture de tags sur un fichier de la bibliothèque (chemin de la base).</summary>
public sealed record WriteTagsItemInput(string Path, IReadOnlyList<TagValueInput> Tags, string? CoverUrl = null, bool OverwriteExisting = false);

[QueryType]
public static partial class LibraryQueries
{
    /// <summary>Titres de la bibliothèque : recherche multi-tags, tri, pagination, état disque et archive.</summary>
    public static Task<TrackPage> GetTracks([Service] LibraryClient library, TrackQuery? query, CancellationToken ct) =>
        library.QueryTracksAsync(query ?? new TrackQuery(), ct);

    public static Task<TrackDto?> GetTrack([Service] LibraryClient library, string path, CancellationToken ct) => library.GetTrackAsync(path, ct);

    /// <summary>Cinq propositions de valeurs de tags (dès 3 caractères).</summary>
    public static Task<IReadOnlyList<TagSuggestion>> GetTrackSuggestions([Service] LibraryClient library, string text, int? limit, CancellationToken ct) =>
        library.SuggestAsync(text, limit ?? 5, ct);

    /// <summary>Réglages de l'application (JSON canonique, défauts compris).</summary>
    public static async Task<JsonNode?> GetSettings([Service] LibraryClient library, CancellationToken ct)
    {
        var values = await library.GetSettingsAsync(ct);
        var obj = new JsonObject();
        foreach (var (k, v) in values) obj[k] = v?.DeepClone();
        return obj;
    }

    /// <summary>Historique des téléchargements, du plus ancien au plus récent.</summary>
    public static Task<IReadOnlyList<HistoryEntry>> GetHistory([Service] LibraryClient library, CancellationToken ct) => library.GetHistoryAsync(ct);

    /// <summary>Pastilles de présence (✓ ≈ ○ ⏳) des titres proposés.</summary>
    public static Task<IReadOnlyList<PresenceVerdict>> GetPresence([Service] LibraryClient library, IReadOnlyList<PresenceQuery> items, CancellationToken ct) =>
        library.LookupPresenceAsync(items, ct);

    /// <summary>Contenu d'un dossier du serveur (racines autorisées uniquement).</summary>
    public static Task<FsListing> GetBrowseFolder([Service] LibraryClient library, string? path, bool? audioOnly, CancellationToken ct) =>
        library.ListDirectoryAsync(path, audioOnly ?? false, ct);

    /// <summary>Informations sur la base (chemin, taille, lignes par table, index de présence).</summary>
    public static Task<DatabaseInfo> GetDatabaseInfo([Service] LibraryClient library, CancellationToken ct) => library.GetDatabaseInfoAsync(ct);
}

[MutationType]
public static partial class LibraryMutations
{
    /// <summary>Enregistre des réglages ; rend les réglages complets.</summary>
    public static async Task<JsonNode?> UpdateSettings([Service] LibraryClient library, JsonNode values, CancellationToken ct)
    {
        var map = (values as JsonObject ?? throw new ArgumentException("Un objet JSON est attendu."))
            .ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone());
        var result = await library.SetSettingsAsync(map, ct);
        var obj = new JsonObject();
        foreach (var (k, v) in result) obj[k] = v?.DeepClone();
        return obj;
    }

    /// <summary>Retire de l'historique les téléchargements dont le fichier n'existe plus.</summary>
    public static Task<HistoryChangeResult> PurgeMissingHistory([Service] LibraryClient library, CancellationToken ct) => library.PurgeMissingHistoryAsync(ct);

    /// <summary>Vide l'historique (les fichiers du disque sont conservés).</summary>
    public static Task<HistoryChangeResult> ClearHistory([Service] LibraryClient library, CancellationToken ct) => library.ClearHistoryAsync(ct);

    /// <summary>Lit les tags d'une liste de fichiers ou dossiers du serveur (résultats par jobItem).</summary>
    public static Task<JobDto> ScanLocalFiles([Service] LibraryClient library, IReadOnlyList<string> paths, CancellationToken ct) =>
        library.ScanLocalFilesAsync(new LocalScanRequest(paths), ct);

    /// <summary>Relit la bibliothèque : fichiers nouveaux ou modifiés, jaquettes, disparus.</summary>
    public static Task<JobDto> RescanLibrary([Service] LibraryClient library, CancellationToken ct) => library.StartLibraryScanAsync(ct);

    /// <summary>Importe réglages et historique de l'application desktop.</summary>
    public static Task<JobDto> ImportDesktopData([Service] LibraryClient library, string? dataDir, bool? settings, bool? history, CancellationToken ct) =>
        library.StartDesktopImportAsync(new DesktopImportRequest(dataDir, settings ?? true, history ?? true), ct);

    /// <summary>Supprime des fichiers (vers la corbeille du serveur) et leurs lignes de la base.</summary>
    public static Task<DeleteFilesResult> DeleteFiles([Service] LibraryClient library, IReadOnlyList<string> paths, string reason, CancellationToken ct) =>
        library.DeleteFilesAsync(new DeleteFilesRequest(paths, reason), ct);

    /// <summary>Écrit des tags dans des fichiers (tâche).</summary>
    public static Task<JobDto> WriteTags([Service] LibraryClient library, IReadOnlyList<WriteTagsItemInput> items, CancellationToken ct) =>
        library.WriteTagsAsync(new WriteTagsRequest(items.Select(i => new WriteTagsItem(
            i.Path,
            i.Tags.GroupBy(t => t.Field).ToDictionary(g => g.Key, g => g.Last().Value),
            i.CoverUrl, i.OverwriteExisting)).ToList()), ct);
}

[SubscriptionType]
public static partial class LibrarySubscriptions
{
    public static async IAsyncEnumerable<ServiceEvent> SubscribeToEvent(string type, ITopicEventReceiver receiver, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await receiver.SubscribeAsync<ServiceEvent>(Topics.For(type), ct);
        await foreach (var evt in stream.ReadEventsAsync().WithCancellation(ct)) yield return evt;
    }

    public static IAsyncEnumerable<ServiceEvent> SubscribeToTracksChanged([Service] ITopicEventReceiver receiver, CancellationToken ct) =>
        SubscribeToEvent(EventTypes.TracksChanged, receiver, ct);

    /// <summary>Titres modifiés (enregistrement, relecture, débit relu…).</summary>
    [Subscribe(With = nameof(SubscribeToTracksChanged))]
    public static TracksChangedEvent TracksChanged([EventMessage] ServiceEvent evt) => evt.Data!.Deserialize<TracksChangedEvent>(JsonDefaults.Options)!;

    public static IAsyncEnumerable<ServiceEvent> SubscribeToTracksRemoved([Service] ITopicEventReceiver receiver, CancellationToken ct) =>
        SubscribeToEvent(EventTypes.TracksRemoved, receiver, ct);

    /// <summary>Titres retirés de la base (fichier supprimé).</summary>
    [Subscribe(With = nameof(SubscribeToTracksRemoved))]
    public static TracksChangedEvent TracksRemoved([EventMessage] ServiceEvent evt) => evt.Data!.Deserialize<TracksChangedEvent>(JsonDefaults.Options)!;

    public static IAsyncEnumerable<ServiceEvent> SubscribeToSettingsChanged([Service] ITopicEventReceiver receiver, CancellationToken ct) =>
        SubscribeToEvent(EventTypes.SettingsChanged, receiver, ct);

    /// <summary>Réglages modifiés : rend les réglages complets.</summary>
    [Subscribe(With = nameof(SubscribeToSettingsChanged))]
    public static JsonNode? SettingsChanged([EventMessage] ServiceEvent evt) => (evt.Data as JsonObject)?["values"]?.DeepClone();

    public static IAsyncEnumerable<ServiceEvent> SubscribeToPresenceChanged([Service] ITopicEventReceiver receiver, CancellationToken ct) =>
        SubscribeToEvent(EventTypes.PresenceChanged, receiver, ct);

    /// <summary>L'index de présence a changé : les pastilles sont à recalculer.</summary>
    [Subscribe(With = nameof(SubscribeToPresenceChanged))]
    public static PresenceChangedEvent PresenceChanged([EventMessage] ServiceEvent evt) => evt.Data!.Deserialize<PresenceChangedEvent>(JsonDefaults.Options)!;

    public static IAsyncEnumerable<ServiceEvent> SubscribeToHistoryChanged([Service] ITopicEventReceiver receiver, CancellationToken ct) =>
        SubscribeToEvent(EventTypes.HistoryChanged, receiver, ct);

    /// <summary>L'historique des téléchargements a changé.</summary>
    [Subscribe(With = nameof(SubscribeToHistoryChanged))]
    public static TracksChangedEvent HistoryChanged([EventMessage] ServiceEvent evt) => evt.Data!.Deserialize<TracksChangedEvent>(JsonDefaults.Options)!;
}
