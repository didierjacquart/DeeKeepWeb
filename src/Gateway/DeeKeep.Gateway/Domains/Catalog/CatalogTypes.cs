using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Library;
using GreenDonut;

namespace DeeKeep.Gateway.Domains.Catalog;

/// <summary>Clé de présence d'une piste de recherche (titre, artiste, album : les critères de la pastille).</summary>
public sealed record TrackPresenceKey(long Id, string Title, string Artist, string? Album);

public static class PresenceLoaders
{
    /// <summary>
    /// Pastilles ✓ ≈ ○ ⏳ résolues par lots (une seule requête à la bibliothèque par réponse).
    /// Bibliothèque indisponible : état PENDING (⏳), jamais une erreur — la recherche reste utilisable.
    /// </summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<TrackPresenceKey, PresenceVerdict>> GetPresenceByTrackAsync(
        IReadOnlyList<TrackPresenceKey> keys, LibraryClient library, CancellationToken ct)
    {
        try
        {
            var queries = keys.Select(k => new PresenceQuery(k.Id.ToString(), k.Title, k.Artist, k.Album)).ToList();
            var verdicts = await library.LookupPresenceAsync(queries, ct);
            var byKey = verdicts.ToDictionary(v => v.Key, v => v);
            return keys.ToDictionary(k => k, k => byKey.TryGetValue(k.Id.ToString(), out var v) ? v : Pending(k));
        }
        catch (ServiceUnavailableException)
        {
            return keys.ToDictionary(k => k, Pending);
        }
    }

    private static PresenceVerdict Pending(TrackPresenceKey k) =>
        new(k.Id.ToString(), PresenceState.Pending, "Lecture du dossier de dépôt en cours…", []);
}

/// <summary>Champs ajoutés aux pistes Deezer de la recherche.</summary>
[ObjectType<DeezerTrack>]
public static partial class DeezerTrackNode
{
    /// <summary>Ce que le dossier de dépôt contient déjà (✓ présent, ≈ possible, ○ absent, ⏳ en attente).</summary>
    public static async Task<PresenceVerdict> GetPresenceAsync([Parent] DeezerTrack track, IPresenceByTrackDataLoader loader, CancellationToken ct) =>
        await loader.LoadRequiredAsync(new TrackPresenceKey(track.Id, track.Title, track.Artist, track.Album), ct);
}

[QueryType]
public static partial class CatalogQueries
{
    /// <summary>Recherche Deezer ; un lien Deezer (piste, playlist, album, artiste) est reconnu quel que soit le type.</summary>
    public static Task<SearchResult> Search([Service] CatalogClient catalog, string query, SearchKind kind = SearchKind.All, CancellationToken ct = default) =>
        catalog.SearchAsync(query, kind, ct);

    /// <summary>Pistes d'un artiste (top 50), d'un album (avec en-tête) ou d'une playlist.</summary>
    public static Task<SearchResult> Browse([Service] CatalogClient catalog, EntityKind kind, long id, CancellationToken ct) =>
        catalog.BrowseAsync(kind, id, ct);

    /// <summary>Historique des recherches, la plus récente d'abord.</summary>
    public static Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistory([Service] CatalogClient catalog, CancellationToken ct) =>
        catalog.GetSearchHistoryAsync(ct);

    /// <summary>Jaquette du premier résultat Deezer d'une requête (vignette de l'historique).</summary>
    public static Task<SearchCover> SearchCover([Service] CatalogClient catalog, string query, CancellationToken ct) =>
        catalog.GetSearchCoverAsync(query, ct);

    /// <summary>Meilleure correspondance Deezer d'un fichier local.</summary>
    public static Task<DeezerTrack?> BestMatch([Service] CatalogClient catalog, string artist, string title, double? duration, CancellationToken ct) =>
        catalog.BestMatchAsync(artist, title, duration, ct);

    /// <summary>Métadonnées complètes d'une piste (genre, année, artiste d'album, numéro de piste…).</summary>
    public static Task<DeezerTrackDetails?> DeezerTrackDetails([Service] CatalogClient catalog, long id, CancellationToken ct) =>
        catalog.GetTrackDetailsAsync(id, ct);

    /// <summary>Durées de référence d'un morceau (Deezer puis MusicBrainz).</summary>
    public static Task<ReferenceDurations> ReferenceDurations([Service] CatalogClient catalog, string artist, string title, CancellationToken ct) =>
        catalog.GetReferenceDurationsAsync(artist, title, ct);

    /// <summary>Propositions déjà produites par une tâche « tags.find » (relecture après rechargement de la page).</summary>
    public static Task<IReadOnlyList<TagProposal>> TagProposals([Service] CatalogClient catalog, Guid jobId, CancellationToken ct) =>
        catalog.GetTagProposalsAsync(jobId, ct);

    /// <summary>L'empreinte acoustique (AcoustID) est-elle utilisable, sinon pourquoi.</summary>
    public static Task<AcoustIdStatus> AcoustIdStatus([Service] CatalogClient catalog, CancellationToken ct) =>
        catalog.GetAcoustIdStatusAsync(ct);
}

[MutationType]
public static partial class CatalogMutations
{
    /// <summary>Place une recherche en tête de l'historique.</summary>
    public static Task<IReadOnlyList<SearchHistoryEntry>> RememberSearch([Service] CatalogClient catalog, string query, CancellationToken ct) =>
        catalog.RememberSearchAsync(query, ct);

    public static Task<IReadOnlyList<SearchHistoryEntry>> DeleteSearch([Service] CatalogClient catalog, string query, CancellationToken ct) =>
        catalog.DeleteSearchAsync(query, ct);

    public static Task<IReadOnlyList<SearchHistoryEntry>> ClearSearches([Service] CatalogClient catalog, CancellationToken ct) =>
        catalog.ClearSearchesAsync(ct);

    /// <summary>Lance la tâche « tags.find » ; les propositions arrivent par l'abonnement jobItem (type « tags.find »).</summary>
    public static Task<JobDto> StartTagLookup([Service] CatalogClient catalog, IReadOnlyList<TagLookupFile> files, bool useAcoustId = true, bool extras = true,
        CancellationToken ct = default) =>
        catalog.StartTagLookupAsync(new TagLookupRequest(files, useAcoustId, extras), ct);

    /// <summary>Importe <c>reference_cache.json</c> de l'application desktop dans les durées de référence.</summary>
    public static Task<JobDto> ImportReferenceCache([Service] CatalogClient catalog, CancellationToken ct) =>
        catalog.ImportReferenceCacheAsync(ct);
}
