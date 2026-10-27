using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.Reference;
using DeeKeep.Catalog.Tags;
using DeeKeep.Contracts.Catalog;
using Xunit;

namespace DeeKeep.Tests.Catalog;

/// <summary>Valeurs produites par le Python d'origine (tag_lookup.py, reference_duration.py).</summary>
public class CatalogVectorTests
{
    private static TagEntry E(string path, string title, bool tagged, string artist = "", string album = "") =>
        new(path, title, tagged, artist, album);

    private static TagCandidate C(string t, string a, string al, int s) => new(t, a, al, s);

    [Theory]
    [InlineData("https://www.deezer.com/fr/track/3135556", EntityKind.Track, "3135556")]
    [InlineData("https://www.deezer.com/playlist/908622995?utm_source=x", EntityKind.Playlist, "908622995")]
    [InlineData("https://www.deezer.com/album/302127", EntityKind.Album, "302127")]
    [InlineData("https://www.deezer.com/fr/artist/27", EntityKind.Artist, "27")]
    // Ordre exact : « track/ » l'emporte sur « album/ ».
    [InlineData("https://www.deezer.com/album/1?x=track/55", EntityKind.Track, "55")]
    public void Links_follow_the_original_order(string query, EntityKind kind, string id)
    {
        var link = DeezerLinks.Parse(query);
        Assert.NotNull(link);
        Assert.Equal(kind, link!.Kind);
        Assert.Equal(id, link.Id);
    }

    [Fact]
    public void Plain_text_and_short_links()
    {
        Assert.Null(DeezerLinks.Parse("Simon & Garfunkel"));
        Assert.True(DeezerLinks.IsShortLink("https://deezer.page.link/abc"));
        Assert.False(DeezerLinks.IsShortLink("https://evil.example/deezer.page.link/abc"));
    }

    [Fact]
    public void Query_parameters_are_encoded()
    {
        Assert.Equal("search?q=Simon%20%26%20Garfunkel&limit=1", DeezerApi.Path("search", ("q", "Simon & Garfunkel"), ("limit", 1)));
        Assert.Equal("a?x=1", DeezerApi.Path("a", ("x", 1), ("y", null)));
    }

    [Fact]
    public void Thousands_use_spaces() => Assert.Equal("1 234 567", DeezerCatalog.FormatThousands(1234567));

    [Theory]
    [InlineData("Madcon feat. Ray Dalton", "Don't Worry (Radio Version)", "madcon feat ray dalton|don t worry radio version")]
    [InlineData("Michel Teló", "Ai Se Eu Te Pego", "michel tel|ai se eu te pego")]
    [InlineData("Taylor Swift", "22", "taylor swift|22")]
    [InlineData("  AC/DC ", " Back In Black! ", "ac dc|back in black")]
    [InlineData("Beyoncé", "Halo", "beyonc|halo")]
    public void Reference_key_matches_python(string artist, string title, string key) => Assert.Equal(key, ReferenceService.Key(artist, title));

    [Theory]
    [InlineData("blame", "blame feat john newman", true)]
    [InlineData("blame feat john newman", "blame", true)]
    [InlineData("", "x", false)]
    [InlineData("abc", "abd", false)]
    public void Related_titles(string a, string b, bool expected) => Assert.Equal(expected, ReferenceService.Related(a, b));

    [Fact]
    public void Closest_picks_nearest_first_on_ties()
    {
        var r = new ReferenceDurations("k", [280, 329, 286, 265], 0, true);
        Assert.Equal(286, r.Closest(290));
        Assert.Equal(280, new ReferenceDurations("k", [280, 290], 0, true).Closest(285));
        Assert.Null(new ReferenceDurations("k", [], 0, false).Closest(285));
        Assert.Null(r.Closest(null));
    }

    [Theory]
    [InlineData(0, 200, null)]
    [InlineData(200, 203, 1.0)]
    [InlineData(200, 204, 0.962962962962963)]
    [InlineData(200, 210, 0.7407407407407407)]
    [InlineData(200, 230, 0.0)]
    [InlineData(200, 231, -1.0)]
    public void Duration_agreement(int local, int remote, double? expected)
    {
        var actual = TagMatcher.DurationAgreement(local, remote);
        if (expected is null) Assert.Null(actual);
        else Assert.Equal(expected.Value, actual!.Value, 12);
    }

    [Fact]
    public void Queries_match_python()
    {
        var losing = E(@"D:\Musique\R.E.M\03 - Losing My Religion (Official Video) [320kbps].mp3", "Losing My Religion", false);
        Assert.Equal(["Losing My Religion", "R.E.M Losing My Religion"], TagMatcher.DeezerQueries(losing));
        Assert.Equal(["recording:\"Losing My Religion\"", "Losing My Religion", "recording:\"Losing My Religion\" AND artist:\"R.E.M\""],
            TagMatcher.MusicBrainzQueries(losing));
        Assert.Equal("Losing My Religion R.E.M", TagMatcher.TitleSource(losing));

        var queen = E(@"D:\Queen\One Vision.mp3", "One Vision", true, "Queen", "A Kind of Magic");
        Assert.Equal(["artist:\"Queen\" album:\"A Kind of Magic\"", "Queen One Vision", "One Vision"], TagMatcher.DeezerQueries(queen));
        Assert.Equal(["artist:\"Queen\" AND release:\"A Kind of Magic\"", "recording:\"One Vision\" AND artist:\"Queen\"", "One Vision"],
            TagMatcher.MusicBrainzQueries(queen));

        var mad = E(@"D:\Electro\Mad Ice - Feel It (Radio Edit).mp3", "", false, "", "Hits 2004");
        Assert.Equal(["Mad Ice - Feel It (Radio Edit)"], TagMatcher.DeezerQueries(mad));
        Assert.Equal(["recording:\"Mad Ice\" AND artist:\"Feel It \\(Radio Edit\\)\"", "recording:\"Feel It \\(Radio Edit\\)\" AND artist:\"Mad Ice\"",
            "Mad Ice \\- Feel It \\(Radio Edit\\)"], TagMatcher.MusicBrainzQueries(mad));

        var boxer = E(@"D:\Simon & Garfunkel\The Boxer.flac", "", false);
        Assert.Equal(["The Boxer", "Simon & Garfunkel The Boxer"], TagMatcher.DeezerQueries(boxer));
        Assert.Equal("recording:\"The Boxer\" AND artist:\"Simon \\& Garfunkel\"", TagMatcher.MusicBrainzQueries(boxer)[2]);

        var daft = E(@"D:\Pop Rock\Daft Punk.mp3", "", false, "Daft Punk", "Piste");
        Assert.Equal(["artist:\"Daft Punk\" album:\"Piste\"", "artist:\"Daft Punk\"", "Daft Punk"], TagMatcher.DeezerQueries(daft));
    }

    [Fact]
    public void Evaluate_matches_python()
    {
        var losing = E(@"D:\Musique\R.E.M\03 - Losing My Religion (Official Video) [320kbps].mp3", "Losing My Religion", false);
        var src = TagMatcher.TitleSource(losing);
        Check(TagMatcher.Evaluate(C("Losing My Religion", "R.E.M.", "Out of Time", 268), losing, 268, src), 1.0, "titre reconnu, durée identique");
        Check(TagMatcher.Evaluate(C("Losing My Religion", "R.E.M.", "Out of Time", 268), losing, 0, src), 0.875, "titre et artiste reconnus dans le nom du fichier");
        Check(TagMatcher.Evaluate(C("Losing My Religion (Live)", "R.E.M.", "Unplugged", 300), losing, 268, src), 0.71671875,
            "titre et artiste reconnus, mais durée très différente (32 s d'écart)");

        var queen = E(@"D:\Queen\One Vision.mp3", "One Vision", true, "Queen", "A Kind of Magic");
        var qs = TagMatcher.TitleSource(queen);
        Check(TagMatcher.Evaluate(C("Spread Your Wings", "Queen", "A Kind of Magic", 300), queen, 301, qs), 0.78, "durée identique dans l'album déjà connu");
        Check(TagMatcher.Evaluate(C("One Vision", "Queen", "A Kind of Magic", 243), queen, 263, qs), 0.8425925925925926,
            "titre et artiste reconnus dans le nom du fichier");

        var french = E(@"D:\Musique\15 French Love.mp3", "", false);
        var fs = TagMatcher.TitleSource(french);
        Check(TagMatcher.Evaluate(C("French Love", "Somebody", "X", 180), french, 181, fs), 0.78,
            "titre exact et durée identique — artiste non vérifiable dans le nom du fichier");
        Check(TagMatcher.Evaluate(C("French Love", "Somebody", "X", 180), french, 190, fs), 0.0, "");

        var mad = E(@"D:\Electro\Mad Ice - Feel It (Radio Edit).mp3", "", false, "", "Hits 2004");
        Check(TagMatcher.Evaluate(C("Feel It (Radio Edit)", "Mad Ice", "Hits", 200), mad, 207, TagMatcher.TitleSource(mad)), 0.962962962962963,
            "titre et artiste reconnus dans le nom du fichier");

        var baez = E(@"D:\Portishead\All Mine.mp3", "All Mine", true, "Joan Baez");
        Check(TagMatcher.Evaluate(C("All Mine", "Portishead", "Portishead", 240), baez, 241, TagMatcher.TitleSource(baez)), 1.0, "titre reconnu, durée identique");
    }

    private static void Check((double Score, string Reason) actual, double score, string reason)
    {
        Assert.Equal(score, actual.Score, 9);
        Assert.Equal(reason, actual.Reason);
    }

    [Fact]
    public void Missing_fields_and_extras()
    {
        var e = E(@"D:\x\a.mp3", "a", false, "A", "");
        Assert.Equal(["title", "album"], TagMatcher.MissingFields(e));
        Assert.True(TagMatcher.NeedsExtras(e));
        // Le nombre de pistes et le disque ne déclenchent jamais une recherche à eux seuls.
        var full = new TagEntry(@"D:\x\a.mp3", "a", true, "A", "B", "A", "Pop", "1995", "3");
        Assert.False(TagMatcher.NeedsExtras(full));
        Assert.Equal(["tracktotal", "disc"], TagMatcher.MissingExtras(full));
    }

    [Fact]
    public void Fingerprint_candidate_scoring()
    {
        var m = TagMatcher.BestFromFingerprint([new TagCandidate("T", "A", "Al", 200, Match: 0.9)], 202);
        Assert.Equal(0.9, m!.Score, 9);
        Assert.Equal("empreinte acoustique (concordance 90%), durée identique", m.Reason);
        var far = TagMatcher.BestFromFingerprint([new TagCandidate("T", "A", "Al", 200, Match: 0.9)], 215);
        Assert.Equal(0.9 * 0.75, far!.Score, 9);
        Assert.Null(TagMatcher.BestFromFingerprint([new TagCandidate("T", "A", "Al", 200, Match: 0.9)], 300));
    }
}
