using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Gateway.Api;

/// <summary>Piste Deezer de la recherche, avec ce que le dossier de dépôt contient déjà (✓ ≈ ○ ⏳).</summary>
public sealed record SearchTrack(
    long Id, string Title, string Artist, long? ArtistId, string? Album, long? AlbumId, double Duration, int? Rank,
    string? PreviewUrl, string? CoverSmall, string? CoverBig, string? Link, bool Explicit, int? TrackPosition, int? DiskNumber,
    PresenceVerdict Presence);

/// <summary>Résultat d'une recherche ou d'une exploration, pistes accompagnées de leur pastille.</summary>
public sealed record SearchResponse(
    string Query, SearchKind Kind, IReadOnlyList<SearchTrack> Tracks, IReadOnlyList<DeezerEntity> Entities, DeezerEntity? Header, string? Error);

public sealed record QueryBody(string Query);

public sealed record TagLookupBody(IReadOnlyList<TagLookupFile> Files, bool UseAcoustId = true, bool Extras = true);

public static class CatalogApi
{
    public static void MapCatalogApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/catalog");

        // Recherche Deezer ; un lien Deezer (piste, playlist, album, artiste) est reconnu quel que soit le type.
        g.MapGet("/search", async (CatalogClient catalog, LibraryClient library, string query, string? kind, CancellationToken ct) =>
            await WithPresence(await catalog.SearchAsync(query, ParseEnum(kind, SearchKind.All), ct), library, ct));

        // Pistes d'un artiste (top 50), d'un album (avec en-tête) ou d'une playlist.
        g.MapGet("/browse/{kind}/{id:long}", async (CatalogClient catalog, LibraryClient library, string kind, long id, CancellationToken ct) =>
            await WithPresence(await catalog.BrowseAsync(ParseEnum(kind, EntityKind.Track, required: true), id, ct), library, ct));

        // Historique des recherches, la plus récente d'abord.
        g.MapGet("/searches", (CatalogClient catalog, CancellationToken ct) => catalog.GetSearchHistoryAsync(ct));
        g.MapPost("/searches", (CatalogClient catalog, QueryBody body, CancellationToken ct) => catalog.RememberSearchAsync(body.Query, ct));
        g.MapPost("/searches/delete", (CatalogClient catalog, QueryBody body, CancellationToken ct) => catalog.DeleteSearchAsync(body.Query, ct));
        g.MapPost("/searches/clear", (CatalogClient catalog, CancellationToken ct) => catalog.ClearSearchesAsync(ct));

        // Jaquette du premier résultat Deezer d'une requête (vignette de l'historique).
        g.MapGet("/search-cover", (CatalogClient catalog, string query, CancellationToken ct) => catalog.GetSearchCoverAsync(query, ct));

        // Meilleure correspondance Deezer d'un fichier local (null si aucune).
        g.MapGet("/best-match", async (CatalogClient catalog, string artist, string title, double? duration, CancellationToken ct) =>
            Results.Json(await catalog.BestMatchAsync(artist, title, duration, ct)));

        // Métadonnées complètes d'une piste (genre, année, artiste d'album, numéro de piste…).
        g.MapGet("/tracks/{id:long}", async (CatalogClient catalog, long id, CancellationToken ct) =>
            Results.Json(await catalog.GetTrackDetailsAsync(id, ct)));

        // Durées de référence d'un morceau (Deezer puis MusicBrainz).
        g.MapGet("/reference-durations", (CatalogClient catalog, string artist, string title, CancellationToken ct) =>
            catalog.GetReferenceDurationsAsync(artist, title, ct));
        // Importe reference_cache.json de l'application desktop.
        g.MapPost("/reference-durations/import", (CatalogClient catalog, CancellationToken ct) => catalog.ImportReferenceCacheAsync(ct));

        // Recherche de tags (tâche « tags.find ») ; les propositions arrivent en événements job.item.
        g.MapPost("/tag-lookup", (CatalogClient catalog, TagLookupBody body, CancellationToken ct) =>
            catalog.StartTagLookupAsync(new TagLookupRequest(body.Files, body.UseAcoustId, body.Extras), ct));
        g.MapGet("/tag-lookup/{jobId:guid}", (CatalogClient catalog, Guid jobId, CancellationToken ct) => catalog.GetTagProposalsAsync(jobId, ct));

        // L'empreinte acoustique (AcoustID) est-elle utilisable, sinon pourquoi.
        g.MapGet("/acoustid", (CatalogClient catalog, CancellationToken ct) => catalog.GetAcoustIdStatusAsync(ct));
    }

    /// <summary>Énumération lue dans l'URL, sans tenir compte de la casse (« ALL », « album »…).</summary>
    private static T ParseEnum<T>(string? value, T fallback, bool required = false) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) && !required) return fallback;
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException($"Valeur inconnue : « {value} » (attendu : {string.Join(", ", Enum.GetNames<T>())}).");
    }

    /// <summary>
    /// Pastilles résolues en une seule requête à la bibliothèque. Bibliothèque indisponible :
    /// état PENDING (⏳), jamais une erreur — la recherche reste utilisable.
    /// </summary>
    private static async Task<SearchResponse> WithPresence(SearchResult result, LibraryClient library, CancellationToken ct)
    {
        IReadOnlyDictionary<string, PresenceVerdict> byKey = new Dictionary<string, PresenceVerdict>();
        if (result.Tracks.Count > 0)
        {
            try
            {
                var verdicts = await library.LookupPresenceAsync(
                    result.Tracks.Select(t => new PresenceQuery(t.Id.ToString(), t.Title, t.Artist, t.Album)).ToList(), ct);
                byKey = verdicts.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => g.First());
            }
            catch (ServiceUnavailableException)
            {
                // pastilles en attente
            }
        }
        var tracks = result.Tracks.Select(t =>
        {
            var key = t.Id.ToString();
            var presence = byKey.TryGetValue(key, out var v) ? v : new PresenceVerdict(key, PresenceState.Pending, "Lecture du dossier de dépôt en cours…", []);
            return new SearchTrack(t.Id, t.Title, t.Artist, t.ArtistId, t.Album, t.AlbumId, t.Duration, t.Rank, t.PreviewUrl, t.CoverSmall,
                t.CoverBig, t.Link, t.Explicit, t.TrackPosition, t.DiskNumber, presence);
        }).ToList();
        return new SearchResponse(result.Query, result.Kind, tracks, result.Entities, result.Header, result.Error);
    }
}
