using System.Net;
using System.Text;
using DeeKeep.Catalog;
using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.Http;
using DeeKeep.Catalog.Media;
using DeeKeep.Catalog.Tags;
using DeeKeep.Contracts.Catalog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Catalog;

/// <summary>HttpMessageHandler factice : aucun appel réseau réel dans les tests.</summary>
public sealed class FakeHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        var (status, body) = respond(request);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public class DeezerCatalogTests
{
    private static (DeezerCatalog Catalog, FakeHandler Handler) Make(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(DeezerApi.BaseUrl) };
        var options = Options.Create(new CatalogOptions { DeezerIntervalMs = 0 });
        var api = new DeezerApi(http, new CatalogGates(options), new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<DeezerApi>.Instance);
        return (new DeezerCatalog(api, null!, options, NullLogger<DeezerCatalog>.Instance), handler);
    }

    private const string TrackJson = """
        {"id":3135556,"title":"Harder","duration":224,"rank":812345,"preview":"https://cdnt-preview.dzcdn.net/x.mp3","explicit_lyrics":true,
         "track_position":3,"disk_number":1,"link":"https://www.deezer.com/track/3135556",
         "artist":{"id":27,"name":"Daft Punk"},"album":{"id":302127,"title":"Discovery","cover_medium":"https://e-cdns-images.dzcdn.net/m.jpg","cover_big":"https://e-cdns-images.dzcdn.net/b.jpg"}}
        """;

    [Fact]
    public async Task Search_encodes_the_query()
    {
        var (catalog, handler) = Make(_ => (HttpStatusCode.OK, "{\"data\":[]}"));
        await catalog.SearchAsync("Simon & Garfunkel", SearchKind.All, TestContext.Current.CancellationToken);
        Assert.Equal("/search?q=Simon%20%26%20Garfunkel", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task Track_link_returns_one_track()
    {
        var (catalog, handler) = Make(_ => (HttpStatusCode.OK, TrackJson));
        var result = await catalog.SearchAsync("https://www.deezer.com/fr/track/3135556", SearchKind.Artist, TestContext.Current.CancellationToken);
        Assert.Equal("/track/3135556", Assert.Single(handler.Requests));
        var track = Assert.Single(result.Tracks);
        Assert.Equal("Harder", track.Title);
        Assert.Equal(27, track.ArtistId);
        Assert.Equal(302127, track.AlbumId);
        Assert.True(track.Explicit);
        Assert.Equal(812345, track.Rank);
        Assert.Equal("https://e-cdns-images.dzcdn.net/b.jpg", track.CoverBig);
    }

    [Fact]
    public async Task Track_link_http_error_is_reported()
    {
        var (catalog, _) = Make(_ => (HttpStatusCode.InternalServerError, ""));
        var result = await catalog.SearchAsync("deezer.com/track/1", SearchKind.All, TestContext.Current.CancellationToken);
        Assert.Equal(DeezerCatalog.TracksError, result.Error);
    }

    [Fact]
    public async Task Artist_search_formats_entities()
    {
        var (catalog, _) = Make(_ => (HttpStatusCode.OK,
            "{\"data\":[{\"id\":27,\"name\":\"Daft Punk\",\"nb_album\":12,\"nb_fan\":1234567,\"picture_medium\":\"https://x.dzcdn.net/p.jpg\"}]}"));
        var result = await catalog.SearchAsync("daft", SearchKind.Artist, TestContext.Current.CancellationToken);
        var entity = Assert.Single(result.Entities);
        Assert.Equal("12 albums · 1 234 567 fans", entity.Subtitle);
    }

    [Fact]
    public async Task Album_browse_applies_title_and_cover_to_all_tracks()
    {
        var (catalog, handler) = Make(r => r.RequestUri!.AbsolutePath switch
        {
            "/album/302127" => (HttpStatusCode.OK, "{\"id\":302127,\"title\":\"Discovery\",\"cover_medium\":\"https://x.dzcdn.net/m.jpg\",\"cover_xl\":\"https://x.dzcdn.net/xl.jpg\",\"artist\":{\"name\":\"Daft Punk\"}}"),
            _ => (HttpStatusCode.OK, "{\"data\":[{\"id\":1,\"title\":\"One More Time\",\"duration\":320,\"artist\":{\"id\":27,\"name\":\"Daft Punk\"}}]}"),
        });
        var result = await catalog.BrowseAsync(EntityKind.Album, 302127, TestContext.Current.CancellationToken);
        Assert.Equal("Discovery", result.Header?.Name);
        var track = Assert.Single(result.Tracks);
        Assert.Equal("Discovery", track.Album);
        Assert.Equal(302127, track.AlbumId);
        Assert.Equal("https://x.dzcdn.net/m.jpg", track.CoverSmall);
        Assert.Equal("https://x.dzcdn.net/xl.jpg", track.CoverBig);
        Assert.Contains(handler.Requests, p => p.StartsWith("/album/302127/tracks?limit=100"));
    }

    [Fact]
    public async Task Quota_error_is_retried_then_reported()
    {
        var calls = 0;
        var (catalog, _) = Make(_ =>
        {
            calls++;
            return (HttpStatusCode.OK, calls < 2 ? "{\"error\":{\"type\":\"Exception\",\"message\":\"Quota limit exceeded\",\"code\":4}}" : "{\"data\":[]}");
        });
        var result = await catalog.SearchAsync("x", SearchKind.All, TestContext.Current.CancellationToken);
        Assert.Null(result.Error);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Best_match_falls_back_to_cleaned_title_with_verification()
    {
        var (catalog, handler) = Make(r =>
        {
            var q = Uri.UnescapeDataString(r.RequestUri!.Query);
            if (q.Contains("Full Edit") || q.Contains("[")) return (HttpStatusCode.OK, "{\"data\":[]}");
            return (HttpStatusCode.OK, "{\"data\":[{\"id\":9,\"title\":\"White Knuckle Ride\",\"duration\":200,\"artist\":{\"id\":1,\"name\":\"Mad Ice\"}}]}");
        });
        var match = await catalog.BestMatchAsync("Mad Ice Feat. Irina", "White Knuckle Ride Full Edit [Www Keepvid Com]", TestContext.Current.CancellationToken);
        Assert.Equal(9, match?.Id);
        Assert.True(handler.Requests.Count >= 3);
    }

    [Fact]
    public async Task Best_match_rejects_unrelated_fallback()
    {
        var (catalog, _) = Make(r => Uri.UnescapeDataString(r.RequestUri!.Query).Contains("Zzz")
            ? (HttpStatusCode.OK, "{\"data\":[]}")
            : (HttpStatusCode.OK, "{\"data\":[{\"id\":9,\"title\":\"Autre chose\",\"duration\":200,\"artist\":{\"name\":\"X\"}}]}"));
        // 1re requête vide (Zzz), requête élargie qui rend un titre sans rapport : refusée.
        var match = await catalog.BestMatchAsync("Zzz", "Zzz (Radio Edit)", TestContext.Current.CancellationToken);
        Assert.Null(match);
    }

    [Fact]
    public async Task Track_details_merge_track_and_album()
    {
        var (catalog, _) = Make(r => r.RequestUri!.AbsolutePath == "/track/3135556"
            ? (HttpStatusCode.OK, TrackJson.Replace("\"link\"", "\"release_date\":\"2001-03-07\",\"isrc\":\"X1\",\"link\""))
            : (HttpStatusCode.OK, "{\"id\":302127,\"genres\":{\"data\":[{\"name\":\"Electro\"}]},\"nb_tracks\":14,\"release_date\":\"2021-01-01\",\"label\":\"Virgin\",\"artist\":{\"name\":\"Daft Punk\"}}"));
        var details = await catalog.TrackDetailsAsync(3135556, TestContext.Current.CancellationToken);
        Assert.Equal("Electro", details?.Genre);
        Assert.Equal("2001", details?.Year);
        Assert.Equal(14, details?.TrackTotal);
        Assert.Equal(3, details?.TrackNumber);
        Assert.Equal("Daft Punk", details?.AlbumArtist);
    }

    [Theory]
    [InlineData("https://e-cdns-images.dzcdn.net/images/cover/x/250x250.jpg", true)]
    [InlineData("https://cdnt-preview.dzcdn.net/api/1/1/a.mp3", true)]
    [InlineData("https://api.deezer.com/track/1", true)]
    [InlineData("https://evil.example/x.jpg", false)]
    [InlineData("https://dzcdn.net.evil.example/x.jpg", false)]
    [InlineData("http://127.0.0.1:5101/api/search", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Image_proxy_only_accepts_deezer_hosts(string url, bool allowed) => Assert.Equal(allowed, ImageProxy.IsAllowed(new Uri(url)));

    private sealed class FakeSources : ITagSources
    {
        public int DeezerCalls, MusicBrainzCalls, FingerprintCalls;

        public Task<IReadOnlyList<TagCandidate>> SearchDeezerAsync(string query, int limit, CancellationToken ct)
        {
            DeezerCalls++;
            return Task.FromResult<IReadOnlyList<TagCandidate>>([new TagCandidate("Losing My Religion", "R.E.M.", "Out of Time", 269, "c.jpg", "7", "70")]);
        }

        public Task<IReadOnlyList<TagCandidate>> SearchMusicBrainzAsync(string query, int limit, CancellationToken ct)
        {
            MusicBrainzCalls++;
            return Task.FromResult<IReadOnlyList<TagCandidate>>([]);
        }

        public Task<IReadOnlyDictionary<string, string>> DeezerDetailsAsync(string trackId, string albumId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["genre"] = "Rock", ["date"] = "1991", ["track"] = "3" });

        public Task<IReadOnlyList<TagCandidate>> FingerprintAsync(string localPath, int duration, CancellationToken ct)
        {
            FingerprintCalls++;
            return Task.FromResult<IReadOnlyList<TagCandidate>>([]);
        }

        public Task<int?> EffectiveDurationAsync(string localPath, int declared, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    [Fact]
    public async Task Find_tags_stops_at_deezer_when_sure_and_fills_extras()
    {
        var sources = new FakeSources();
        var entry = new TagEntry(@"D:\Musique\R.E.M\03 - Losing My Religion.mp3", "Losing My Religion", false);
        var found = await new TagFinder(sources).FindAsync(entry, null, 268, useAcoustId: true, extras: true, TestContext.Current.CancellationToken);
        Assert.NotNull(found);
        Assert.True(found!.Sure);
        Assert.Equal("Deezer", found.Source);
        Assert.Equal("R.E.M.", found.Values["artist"]);
        Assert.Equal("Out of Time", found.Values["album"]);
        Assert.Equal("Rock", found.Values["genre"]);
        Assert.Equal(0, sources.MusicBrainzCalls);
    }

    [Fact]
    public async Task Find_tags_for_extras_only_never_asks_musicbrainz()
    {
        var sources = new FakeSources();
        var entry = new TagEntry(@"D:\Musique\R.E.M\Losing My Religion.mp3", "Losing My Religion", true, "R.E.M.", "Out of Time");
        var found = await new TagFinder(sources).FindAsync(entry, null, 268, true, true, TestContext.Current.CancellationToken);
        Assert.Equal(["date", "genre", "track"], found!.Values.Keys.Order().ToArray());
        Assert.Equal(0, sources.MusicBrainzCalls);
        Assert.Equal(0, sources.FingerprintCalls);
    }
}
