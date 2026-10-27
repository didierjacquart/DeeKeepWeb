using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Catalog;

/// <summary>Type de recherche Deezer (menu de la fenêtre « Rechercher et ajouter des pistes »).</summary>
public enum SearchKind
{
    All,
    Track,
    Artist,
    Album,
    Playlist,
}

public enum EntityKind
{
    Artist,
    Album,
    Playlist,
    Track,
}

/// <summary>Piste Deezer telle que l'affiche la recherche.</summary>
public sealed record DeezerTrack(
    long Id,
    string Title,
    string Artist,
    long? ArtistId,
    string? Album,
    long? AlbumId,
    double Duration,
    int? Rank,
    string? PreviewUrl,
    string? CoverSmall,
    string? CoverBig,
    string? Link,
    bool Explicit = false,
    int? TrackPosition = null,
    int? DiskNumber = null);

/// <summary>Artiste, album ou playlist de la vue « entités ».</summary>
public sealed record DeezerEntity(
    EntityKind Kind,
    long Id,
    string Name,
    string? Subtitle,
    string? Picture,
    int? TrackCount,
    int? FanCount,
    string? Link);

/// <summary>Résultat d'une recherche : des pistes, ou des entités à explorer.</summary>
public sealed record SearchResult(
    string Query,
    SearchKind Kind,
    IReadOnlyList<DeezerTrack> Tracks,
    IReadOnlyList<DeezerEntity> Entities,
    DeezerEntity? Header,
    string? Error);

/// <summary>Métadonnées complètes d'une piste (deuxième appel /track et /album) pour l'écriture des tags.</summary>
public sealed record DeezerTrackDetails(
    long Id,
    string Title,
    string Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    string? Year,
    int? TrackNumber,
    int? TrackTotal,
    int? DiscNumber,
    string? Label,
    double Duration,
    string? CoverUrl,
    string? Isrc);

public sealed record SearchHistoryEntry(string Query, int Position, double UpdatedAt);

/// <summary>
/// Durées de référence d'un morceau (Deezer puis MusicBrainz), en secondes.
/// <see cref="Found"/> : au moins une durée connue. <see cref="Concluded"/> faux : la recherche
/// n'a pas pu aboutir (réseau, service saturé) — ne rien conclure sur les blancs du fichier
/// (<c>lookup</c> rendait alors <c>None</c>).
/// </summary>
public sealed record ReferenceDurations(string Key, IReadOnlyList<double> Durations, double? FetchedAt, bool Found, bool Concluded = true)
{
    /// <summary><c>closest</c> : la durée la plus proche de la durée mesurée (la première en cas d'égalité), null s'il n'y en a pas.</summary>
    public double? Closest(double? measured)
    {
        if (Durations.Count == 0 || measured is not { } m || m == 0) return null;
        var best = Durations[0];
        foreach (var d in Durations)
            if (Math.Abs(d - m) < Math.Abs(best - m)) best = d;
        return best;
    }
}

/// <summary>Fichier à identifier pour « Compléter les tags ».</summary>
public sealed record TagLookupFile(
    string Path,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    string? Year,
    string? TrackNo,
    double? Duration,
    bool? TitleTagged = null,
    string? TrackTotal = null,
    string? Disc = null);

/// <summary>
/// Lancement de « Compléter les tags ». Les tags sont relus sur le disque quand le fichier
/// est accessible au service ; ceux de la demande servent sinon. <paramref name="Extras"/> :
/// chercher aussi les tags complémentaires (artiste d'album, genre, année, piste).
/// </summary>
public sealed record TagLookupRequest(IReadOnlyList<TagLookupFile> Files, bool UseAcoustId = true, bool Extras = true);

/// <summary>Proposition de tags pour un fichier (élément de la tâche « tags.find », donnée de l'événement job.item).</summary>
public sealed record TagProposal(
    string Path,
    string Confidence,
    double Score,
    string Source,
    IReadOnlyDictionary<string, string?> Current,
    IReadOnlyDictionary<string, string?> Proposed,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Contradicted,
    string? CoverUrl,
    long? DeezerId,
    string? Note,
    IReadOnlyDictionary<string, string?>? Replaces = null,
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    int? Seconds = null,
    string? Query = null,
    int? Duration = null,
    int Index = 0,
    bool TitleTagged = false);

/// <summary>Valeurs de <see cref="TagProposal.Confidence"/>.</summary>
public static class TagConfidence
{
    /// <summary>Note ≥ 0,80 : la proposition est cochée d'office.</summary>
    public const string Sure = "sure";
    /// <summary>Note entre 0,55 et 0,80.</summary>
    public const string Probable = "probable";
    /// <summary>Aucune correspondance (la ligne est affichée quand même, sans case).</summary>
    public const string None = "none";
}

/// <summary>Empreinte acoustique utilisable ? (ligne d'état de l'écran « Compléter les tags »).</summary>
public sealed record AcoustIdStatus(bool Available, string Message);

/// <summary>Vignette d'une recherche de l'historique : jaquette du premier résultat Deezer.</summary>
public sealed record SearchCover(string Query, string? Url);

public static class CatalogJobTypes
{
    /// <summary>Recherche en ligne des tags manquants (Deezer, MusicBrainz, AcoustID).</summary>
    public const string FindTags = "tags.find";

    /// <summary>Import ponctuel de <c>reference_cache.json</c> de l'application desktop dans <c>reference_durations</c>.</summary>
    public const string ImportReferences = "reference.import";
}

[ServiceClient(ServiceNames.Catalog)]
public sealed class CatalogClient(HttpClient http) : ServiceClient(http, ServiceNames.Catalog)
{
    public Task<SearchResult> SearchAsync(string query, SearchKind kind, CancellationToken ct = default) =>
        GetAsync<SearchResult>($"/api/search?q={Uri.EscapeDataString(query)}&kind={kind}", ct);

    /// <summary>Pistes d'un artiste (top), d'un album ou d'une playlist.</summary>
    public Task<SearchResult> BrowseAsync(EntityKind kind, long id, CancellationToken ct = default) =>
        GetAsync<SearchResult>($"/api/browse/{kind}/{id}", ct);

    public Task<DeezerTrackDetails?> GetTrackDetailsAsync(long deezerId, CancellationToken ct = default) =>
        GetOrDefaultAsync<DeezerTrackDetails>($"/api/tracks/{deezerId}", ct);

    /// <summary>Meilleure correspondance Deezer pour un fichier local (artiste, titre, durée facultative).</summary>
    public Task<DeezerTrack?> BestMatchAsync(string artist, string title, double? duration = null, CancellationToken ct = default) =>
        GetOrDefaultAsync<DeezerTrack>($"/api/match?artist={Uri.EscapeDataString(artist)}&title={Uri.EscapeDataString(title)}" +
                                       (duration is { } d ? $"&duration={d.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : ""), ct);

    public Task<ReferenceDurations> GetReferenceDurationsAsync(string artist, string title, CancellationToken ct = default) =>
        GetAsync<ReferenceDurations>($"/api/reference?artist={Uri.EscapeDataString(artist)}&title={Uri.EscapeDataString(title)}", ct);

    /// <summary>Octets d'une image distante (jaquette), via le cache disque du service.</summary>
    public async Task<byte[]?> GetImageAsync(string url, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"/img?u={Uri.EscapeDataString(url)}", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    public Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<SearchHistoryEntry>>("/api/searches", ct);

    public Task<IReadOnlyList<SearchHistoryEntry>> DeleteSearchAsync(string query, CancellationToken ct = default) =>
        DeleteAsync<IReadOnlyList<SearchHistoryEntry>>($"/api/searches?q={Uri.EscapeDataString(query)}", null, ct);

    public Task<IReadOnlyList<SearchHistoryEntry>> ClearSearchesAsync(CancellationToken ct = default) =>
        DeleteAsync<IReadOnlyList<SearchHistoryEntry>>("/api/searches/all", null, ct);

    /// <summary>Lance la tâche « tags.find » ; les propositions arrivent en événements job.item (<see cref="TagProposal"/>).</summary>
    public Task<JobDto> StartTagLookupAsync(TagLookupRequest request, CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/tags/lookup", request, ct);

    /// <summary>Propositions déjà produites par une tâche « tags.find » (dans l'ordre des fichiers).</summary>
    public Task<IReadOnlyList<TagProposal>> GetTagProposalsAsync(Guid jobId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TagProposal>>($"/api/tags/proposals/{jobId}", ct);

    public Task<AcoustIdStatus> GetAcoustIdStatusAsync(CancellationToken ct = default) =>
        GetAsync<AcoustIdStatus>("/api/tags/acoustid", ct);

    /// <summary>Place une recherche en tête de l'historique (doublon exact retiré, casse ignorée).</summary>
    public Task<IReadOnlyList<SearchHistoryEntry>> RememberSearchAsync(string query, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<SearchHistoryEntry>>($"/api/searches?q={Uri.EscapeDataString(query)}", null, ct);

    /// <summary>Jaquette du premier résultat Deezer d'une requête (vignettes de l'historique).</summary>
    public Task<SearchCover> GetSearchCoverAsync(string query, CancellationToken ct = default) =>
        GetAsync<SearchCover>($"/api/searches/cover?q={Uri.EscapeDataString(query)}", ct);

    /// <summary>Lance l'import de <c>reference_cache.json</c> (dossier <c>Import:DesktopDataDir</c>).</summary>
    public Task<JobDto> ImportReferenceCacheAsync(CancellationToken ct = default) =>
        PostAsync<JobDto>("/api/reference/import", null, ct);
}
