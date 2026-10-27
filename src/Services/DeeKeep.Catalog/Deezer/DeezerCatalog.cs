using System.Globalization;
using System.Text.Json;
using DeeKeep.Catalog.Tags;
using DeeKeep.Common.Text;
using DeeKeep.Contracts.Catalog;
using Microsoft.Extensions.Options;

namespace DeeKeep.Catalog.Deezer;

/// <summary>
/// Catalogue Deezer : recherche de la fenêtre « Rechercher et ajouter des pistes »
/// (<c>DeezerSearchThread</c>), navigation dans les entités, correspondance d'un fichier
/// local (<c>deezer_best_match</c>), détails d'une piste pour l'écriture des tags
/// (<c>deezer_details</c>), et recherches de « Compléter les tags » (<c>search_deezer</c>).
/// </summary>
public sealed class DeezerCatalog(DeezerApi api, IHttpClientFactory httpFactory, IOptions<CatalogOptions> options, ILogger<DeezerCatalog> logger)
{
    public const string UnknownArtist = "Artiste Inconnu";
    public const string UnknownAlbum = "Album Inconnu";
    public const string EntitiesError = "Impossible de récupérer les résultats.";
    public const string TracksError = "Impossible de récupérer les pistes.";
    public const string QuotaError = "Quota de l'API Deezer dépassé : réessayez dans quelques secondes.";

    /// <summary>Part des mots du morceau qu'un titre du catalogue doit reprendre pour le repli de <c>deezer_best_match</c>.</summary>
    public const double MinTitleMatch = 0.7;

    public const string LinkClientName = "deezer-links";

    private static readonly NumberFormatInfo SpaceThousands = new() { NumberGroupSeparator = " ", NumberGroupSizes = [3] };

    // ------------------------------------------------------------------
    // Recherche (§3.1)
    // ------------------------------------------------------------------

    public async Task<SearchResult> SearchAsync(string query, SearchKind kind, CancellationToken ct)
    {
        var q = PyText.Strip(query ?? "");
        if (q.Length == 0) return Empty(q, kind);
        try
        {
            if (DeezerLinks.IsShortLink(q)) q = await ResolveShortLinkAsync(q, ct) ?? q;
            var link = DeezerLinks.Parse(q);
            if (link is not null) return (await BrowseCoreAsync(link.Kind, link.Id, ct)) with { Query = query ?? "", Kind = kind };

            switch (kind)
            {
                case SearchKind.Artist:
                    return await EntitiesAsync(query!, kind, DeezerApi.Path("search/artist", ("q", q), ("limit", 25)), EntityKind.Artist, ct);
                case SearchKind.Album:
                    return await EntitiesAsync(query!, kind, DeezerApi.Path("search/album", ("q", q), ("limit", 25)), EntityKind.Album, ct);
                case SearchKind.Playlist:
                {
                    string playlistId;
                    if (PyText.IsDigit(q)) playlistId = q;
                    else
                    {
                        var found = await api.GetAsync(DeezerApi.Path("search/playlist", ("q", q)), DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
                        if (found.QuotaExceeded) return Failed(query!, kind, QuotaError);
                        var first = found.Data.FirstOrDefault();
                        if (first.ValueKind != JsonValueKind.Object) return Empty(query!, kind);
                        playlistId = first.IdText();
                    }
                    var (tracks, error) = await TrackListAsync($"playlist/{playlistId}/tracks", null, null, null, paged: true, ct);
                    return new SearchResult(query!, kind, tracks, [], null, error);
                }
                default:
                {
                    // Recherche libre : première page, taille par défaut de Deezer (comme l'original).
                    var (tracks, error) = await TrackListAsync(DeezerApi.Path("search", ("q", q)), null, null, null, paged: false, ct);
                    return new SearchResult(query!, kind, tracks, [], null, error);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException or JsonException)
        {
            return Failed(query ?? "", kind, ex.Message);
        }
    }

    /// <summary>Pistes d'une entité explorée : top 50 d'un artiste, album (avec son en-tête), playlist, piste seule.</summary>
    public async Task<SearchResult> BrowseAsync(EntityKind kind, long id, CancellationToken ct)
    {
        try
        {
            return await BrowseCoreAsync(kind, id.ToString(CultureInfo.InvariantCulture), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException or JsonException)
        {
            return Failed($"{kind.ToString().ToLowerInvariant()}/{id}", SearchKind.All, ex.Message);
        }
    }

    private async Task<SearchResult> BrowseCoreAsync(EntityKind kind, string id, CancellationToken ct)
    {
        var label = $"{kind.ToString().ToLowerInvariant()}/{id}";
        switch (kind)
        {
            case EntityKind.Track:
            {
                var response = await api.GetAsync($"track/{Uri.EscapeDataString(id)}", DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
                if (response.QuotaExceeded) return Failed(label, SearchKind.Track, QuotaError);
                // L'original restait « en cours » indéfiniment sur un HTTP ≠ 200 : une erreur est rendue.
                if (response.Status != 200) return Failed(label, SearchKind.Track, TracksError);
                if (!response.Ok) return Empty(label, SearchKind.Track);
                return new SearchResult(label, SearchKind.Track, [FormatTrack(response.Body, null, null, null, null)], [], null, null);
            }
            case EntityKind.Playlist:
            {
                var (tracks, error) = await TrackListAsync($"playlist/{Uri.EscapeDataString(id)}/tracks", null, null, null, paged: true, ct);
                DeezerEntity? header = null;
                if (error is null && tracks.Count > 0)
                {
                    var meta = await api.GetAsync($"playlist/{Uri.EscapeDataString(id)}", DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
                    if (meta.Ok) header = PlaylistEntity(meta.Body);
                }
                return new SearchResult(label, SearchKind.Playlist, tracks, [], header, error);
            }
            case EntityKind.Album:
            {
                // Titre et jaquette de l'album : toutes ses pistes les reçoivent (la liste
                // /album/{id}/tracks ne les porte pas).
                var meta = await api.GetAsync($"album/{Uri.EscapeDataString(id)}", DeezerApi.LongCache, DeezerApi.SearchTimeout, ct);
                if (meta.QuotaExceeded) return Failed(label, SearchKind.Album, QuotaError);
                string? title = null, cover = null, coverBig = null;
                DeezerEntity? header = null;
                if (meta.Ok)
                {
                    title = meta.Body.Str("title");
                    cover = meta.Body.Str("cover_medium") ?? meta.Body.Str("cover_small");
                    coverBig = meta.Body.Str("cover_xl") ?? meta.Body.Str("cover_big") ?? cover;
                    header = AlbumEntity(meta.Body) with { Picture = meta.Body.Str("cover_big") ?? coverBig };
                }
                var (tracks, error) = await TrackListAsync($"album/{Uri.EscapeDataString(id)}/tracks", title, cover, coverBig, paged: true, ct,
                    albumId: DeezerLinks.NumericId(id));
                return new SearchResult(label, SearchKind.Album, tracks, [], header, error);
            }
            default:
            {
                var (tracks, error) = await TrackListAsync(DeezerApi.Path($"artist/{Uri.EscapeDataString(id)}/top", ("limit", 50)), null, null, null, paged: false, ct);
                DeezerEntity? header = null;
                if (error is null)
                {
                    var meta = await api.GetAsync($"artist/{Uri.EscapeDataString(id)}", DeezerApi.LongCache, DeezerApi.SearchTimeout, ct);
                    if (meta.Ok) header = ArtistEntity(meta.Body);
                }
                return new SearchResult(label, SearchKind.Artist, tracks, [], header, error);
            }
        }
    }

    private async Task<SearchResult> EntitiesAsync(string query, SearchKind kind, string path, EntityKind entityKind, CancellationToken ct)
    {
        var response = await api.GetAsync(path, DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
        if (response.QuotaExceeded) return Failed(query, kind, QuotaError);
        if (response.Status != 200) return Failed(query, kind, EntitiesError);
        var entities = response.Data
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => entityKind == EntityKind.Artist ? ArtistEntity(e) : AlbumEntity(e))
            .ToList();
        return new SearchResult(query, kind, [], entities, null, null);
    }

    /// <summary>
    /// Liste de pistes (<c>fetch_and_emit_tracks_list</c>). Pour une playlist ou un album, la
    /// pagination (<c>next</c>), ignorée par l'original, est suivie jusqu'au plafond configuré :
    /// une playlist de 300 titres n'est plus tronquée.
    /// </summary>
    private async Task<(IReadOnlyList<DeezerTrack> Tracks, string? Error)> TrackListAsync(string path, string? albumTitle, string? albumCover,
        string? albumCoverBig, bool paged, CancellationToken ct, long? albumId = null)
    {
        var tracks = new List<DeezerTrack>();
        var max = Math.Max(1, options.Value.MaxTracks);
        var index = 0;
        while (true)
        {
            var page = paged ? DeezerApi.Path(path, ("index", index == 0 ? null : index), ("limit", 100)) : path;
            var response = await api.GetAsync(page, DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
            if (response.QuotaExceeded) return (tracks, tracks.Count == 0 ? QuotaError : null);
            if (response.Status != 200) return (tracks, tracks.Count == 0 ? TracksError : null);
            var data = response.Data.Where(t => t.ValueKind == JsonValueKind.Object).ToList();
            tracks.AddRange(data.Select(t => FormatTrack(t, albumTitle, albumCover, albumCoverBig, albumId)));
            if (!paged || data.Count == 0 || tracks.Count >= max) break;
            if (!response.Body.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.String) break;
            index += data.Count;
        }
        return (tracks.Count > max ? tracks[..max] : tracks, null);
    }

    // ------------------------------------------------------------------
    // Mise en forme
    // ------------------------------------------------------------------

    public static DeezerTrack FormatTrack(JsonElement t, string? albumTitle, string? albumCover, string? albumCoverBig, long? albumId)
    {
        var artist = t.Obj("artist");
        var album = t.Obj("album");
        var coverSmall = albumCover ?? album.Str("cover_medium") ?? album.Str("cover_small");
        var coverBig = albumCoverBig ?? album.Str("cover_xl") ?? album.Str("cover_big") ?? album.Str("cover_medium") ?? coverSmall;
        return new DeezerTrack(
            t.Long("id") ?? 0,
            t.Str("title") ?? "",
            artist.Str("name") ?? UnknownArtist,
            artist.Long("id"),
            !string.IsNullOrEmpty(albumTitle) ? albumTitle : album.Str("title") ?? UnknownAlbum,
            album.Long("id") ?? albumId,
            t.Long("duration") ?? 0,
            (int?)(t.Long("rank") ?? 0),
            t.Str("preview"),
            coverSmall,
            coverBig,
            t.Str("link"),
            t.Bool("explicit_lyrics"),
            (int?)t.Long("track_position"),
            (int?)t.Long("disk_number"));
    }

    public static DeezerEntity ArtistEntity(JsonElement e)
    {
        var albums = e.Long("nb_album") ?? 0;
        var fans = e.Long("nb_fan") ?? 0;
        return new DeezerEntity(EntityKind.Artist, e.Long("id") ?? 0, e.Str("name") ?? "Artiste inconnu",
            $"{albums} albums · {FormatThousands(fans)} fans",
            e.Str("picture_medium") ?? e.Str("picture_small"), null, (int?)fans, e.Str("link"));
    }

    public static DeezerEntity AlbumEntity(JsonElement e) =>
        new(EntityKind.Album, e.Long("id") ?? 0, e.Str("title") ?? "Album inconnu", e.Obj("artist").Str("name") ?? "Artiste inconnu",
            e.Str("cover_medium") ?? e.Str("cover_small"), (int?)e.Long("nb_tracks"), (int?)e.Long("fans"), e.Str("link"));

    public static DeezerEntity PlaylistEntity(JsonElement e) =>
        new(EntityKind.Playlist, e.Long("id") ?? 0, e.Str("title") ?? "Playlist", e.Obj("creator").Str("name") ?? e.Obj("user").Str("name"),
            e.Str("picture_big") ?? e.Str("picture_medium") ?? e.Str("picture_small"), (int?)e.Long("nb_tracks"), (int?)e.Long("fans"), e.Str("link"));

    /// <summary>Premier genre d'un album (<c>genres.data[0].name</c>), vide sinon.</summary>
    public static string FirstGenre(JsonElement album)
    {
        var genres = album.Obj("genres");
        if (genres.ValueKind != JsonValueKind.Object || !genres.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return "";
        var first = data.EnumerateArray().FirstOrDefault();
        return first.Str("name") ?? "";
    }

    /// <summary><c>f"{n:,}".replace(",", " ")</c>.</summary>
    public static string FormatThousands(long n) => n.ToString("#,0", SpaceThousands);

    private static SearchResult Empty(string query, SearchKind kind) => new(query, kind, [], [], null, null);

    private static SearchResult Failed(string query, SearchKind kind, string error) => new(query, kind, [], [], null, error);

    // ------------------------------------------------------------------
    // Liens courts
    // ------------------------------------------------------------------

    /// <summary>
    /// Lien de partage court (<c>deezer.page.link</c>, <c>link.deezer.com</c>) : la redirection
    /// est suivie pas à pas, sans jamais charger de page hors de Deezer, jusqu'à l'adresse
    /// longue (<c>www.deezer.com/…/track/…</c>) que reconnaissent les règles ordinaires.
    /// </summary>
    public async Task<string?> ResolveShortLinkAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(LinkClientName);
        var current = new Uri(url);
        for (var hop = 0; hop < 5; hop++)
        {
            if (DeezerLinks.IsDeezerHost(current.Host) && DeezerLinks.Parse(current.AbsoluteUri) is not null) return current.AbsoluteUri;
            if (!DeezerLinks.IsShortLink(current.AbsoluteUri) && !DeezerLinks.IsDeezerHost(current.Host)) return null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(DeezerApi.SearchTimeout);
            using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var location = response.Headers.Location;
            if (location is null)
            {
                logger.LogDebug("Lien court {Url} sans redirection", url);
                return null;
            }
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (current.Scheme is not ("https" or "http")) return null;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Correspondance d'un fichier local (§3.2)
    // ------------------------------------------------------------------

    /// <summary>Première piste que Deezer rend pour la requête, ou null (erreur, HTTP ≠ 200, aucun résultat).</summary>
    private async Task<JsonElement?> FirstAsync(string query, CancellationToken ct)
    {
        try
        {
            var response = await api.GetAsync(DeezerApi.Path("search", ("q", query), ("limit", 1)), DeezerApi.ShortCache, DeezerApi.MatchTimeout, ct);
            var first = response.Data.FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object ? first : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>deezer_best_match</c> : les tags tels quels d'abord (sans vérification), puis le
    /// morceau débarrassé de son bruit, vérifié (« contained ≥ 0,7 ») car une requête élargie
    /// finit toujours par rendre quelque chose. La durée n'intervient pas (comme l'original).
    /// </summary>
    public async Task<DeezerTrack?> BestMatchAsync(string? artist, string? title, CancellationToken ct)
    {
        artist ??= "";
        title ??= "";
        var queries = new List<string>();
        if (artist.Length > 0 && title.Length > 0) queries.Add($"artist:\"{artist}\" track:\"{title}\"");
        var free = PyText.Strip($"{artist} {title}");
        if (free.Length > 0) queries.Add(free);

        foreach (var query in queries.ToList())
        {
            if (await FirstAsync(query, ct) is { } track) return FormatTrack(track, null, null, null, null);
        }

        var wanted = TagText.CoreTitle(title);
        var performer = TagText.MainArtist(artist);
        foreach (var query in new[] { $"artist:\"{performer}\" track:\"{wanted}\"", PyText.Strip($"{performer} {wanted}") })
        {
            if (query.Length == 0 || queries.Contains(query)) continue;
            queries.Add(query);
            if (await FirstAsync(query, ct) is not { } track) continue;
            if (TagText.Contained(wanted, track.Str("title") ?? "") >= MinTitleMatch) return FormatTrack(track, null, null, null, null);
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Détails d'une piste (deezer_details) et extraits
    // ------------------------------------------------------------------

    public async Task<DeezerTrackDetails?> TrackDetailsAsync(long id, CancellationToken ct)
    {
        var track = await api.GetAsync($"track/{id}", DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
        if (!track.Ok) return null;
        var t = track.Body;
        var albumRef = t.Obj("album");
        var albumId = albumRef.Long("id");
        JsonElement album = default;
        if (albumId is { } aid)
        {
            var response = await api.GetAsync($"album/{aid}", DeezerApi.LongCache, DeezerApi.SearchTimeout, ct);
            if (response.Ok) album = response.Body;
        }
        var genre = FirstGenre(album);
        var year = TagText.Year(t.Str("release_date"));
        if (year.Length == 0) year = TagText.Year(album.Str("release_date"));
        return new DeezerTrackDetails(
            id,
            t.Str("title") ?? "",
            t.Obj("artist").Str("name") ?? UnknownArtist,
            albumRef.Str("title"),
            album.Obj("artist").Str("name"),
            genre.Length > 0 ? genre : null,
            year.Length > 0 ? year : null,
            (int?)t.Long("track_position"),
            (int?)album.Long("nb_tracks"),
            (int?)t.Long("disk_number"),
            album.Str("label"),
            t.Long("duration") ?? 0,
            album.Str("cover_xl") ?? album.Str("cover_big") ?? albumRef.Str("cover_xl") ?? albumRef.Str("cover_big") ?? albumRef.Str("cover_medium"),
            t.Str("isrc"));
    }

    /// <summary>URL fraîche de l'extrait de 30 s (signée, à durée limitée : jamais conservée longtemps).</summary>
    public async Task<string?> PreviewUrlAsync(long id, bool refresh, CancellationToken ct)
    {
        var response = await api.GetAsync($"track/{id}", refresh ? null : DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
        return response.Ok ? response.Body.Str("preview") : null;
    }

    /// <summary>
    /// Vignette d'une recherche de l'historique (<c>CoverLookupThread("", requête)</c>) :
    /// <c>artist:"" track:"{requête}"</c> puis <c>" {requête}"</c>, jaquette du premier résultat.
    /// Un lien Deezer est résolu comme une recherche (première piste de la liste).
    /// </summary>
    public async Task<string?> CoverForQueryAsync(string query, CancellationToken ct)
    {
        if (DeezerLinks.Parse(query) is not null || DeezerLinks.IsShortLink(query))
        {
            var result = await SearchAsync(query, SearchKind.All, ct);
            return result.Header?.Picture ?? result.Tracks.FirstOrDefault()?.CoverSmall;
        }
        foreach (var q in new[] { $"artist:\"\" track:\"{query}\"", $" {query}" })
        {
            if (await FirstAsync(q, ct) is not { } track) continue;
            var album = track.Obj("album");
            return album.Str("cover_medium") ?? album.Str("cover_big") ?? album.Str("cover_small");
        }
        return null;
    }

    // ------------------------------------------------------------------
    // « Compléter les tags »
    // ------------------------------------------------------------------

    /// <summary><c>search_deezer</c> : pistes candidates (liste vide en cas d'échec).</summary>
    public async Task<IReadOnlyList<TagCandidate>> SearchForTagsAsync(string query, int limit, CancellationToken ct)
    {
        try
        {
            var response = await api.GetAsync(DeezerApi.Path("search", ("q", query), ("limit", limit)), DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
            return response.Data.Where(t => t.ValueKind == JsonValueKind.Object).Select(t =>
            {
                var album = t.Obj("album");
                return new TagCandidate(
                    t.Str("title") ?? "",
                    t.Obj("artist").Str("name") ?? "",
                    album.Str("title") ?? "",
                    (int)(t.Long("duration") ?? 0),
                    album.Str("cover_medium") ?? album.Str("cover_small") ?? "",
                    t.IdText(),
                    album.IdText());
            }).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// <c>deezer_details</c> : tags complémentaires d'une piste (piste, disque, année d'origine,
    /// genre, artiste d'album, nombre de pistes) ; champs inconnus absents, jamais vides.
    /// L'album est mis en cache (un échec ne l'est pas).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> DetailsForTagsAsync(string trackId, string albumId, CancellationToken ct)
    {
        var details = new Dictionary<string, string>();
        try
        {
            if (trackId.Length > 0)
            {
                var track = await api.GetAsync($"track/{Uri.EscapeDataString(trackId)}", DeezerApi.ShortCache, DeezerApi.SearchTimeout, ct);
                if (track.Ok)
                {
                    var t = track.Body;
                    if (t.Long("track_position") is { } position and not 0) details["track"] = position.ToString(CultureInfo.InvariantCulture);
                    if (t.Long("disk_number") is { } disk and not 0) details["disc"] = disk.ToString(CultureInfo.InvariantCulture);
                    details["date"] = TagText.Year(t.Str("release_date"));
                    if (albumId.Length == 0) albumId = t.Obj("album").IdText();
                }
            }
            if (albumId.Length > 0)
            {
                var response = await api.GetAsync($"album/{Uri.EscapeDataString(albumId)}", DeezerApi.LongCache, DeezerApi.SearchTimeout, ct);
                var album = response.Ok ? response.Body : default;
                var genre = FirstGenre(album);
                var total = album.Long("nb_tracks");
                var fromAlbum = new Dictionary<string, string>
                {
                    ["genre"] = genre,
                    ["albumartist"] = album.Obj("artist").Str("name") ?? "",
                    ["tracktotal"] = total is { } n and not 0 ? n.ToString(CultureInfo.InvariantCulture) : "",
                };
                foreach (var (field, value) in fromAlbum)
                    if (value.Length > 0) details[field] = value;
                if (!details.TryGetValue("date", out var date) || date.Length == 0) details["date"] = TagText.Year(album.Str("release_date"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException)
        {
            // Comme _deezer_get : toute panne vaut absence de réponse.
        }
        return details.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
