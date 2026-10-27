using DeeKeep.Common.Text;
using DeeKeep.Downloader.Extraction;
using DeeKeep.Downloader.Settings;
using Xunit;

namespace DeeKeep.Tests.Downloader;

/// <summary>Logique du téléchargement portée de download_thread.py (vecteurs produits en exécutant le Python d'origine), sans réseau.</summary>
public class DownloaderTests
{
    public static TheoryData<string, string, string[]> PythonQueries => new()
    {
        { "Mad Ice Feat. Irina", "White Knuckle Ride Full Edit [Www Keepvid Com]",
            ["Mad Ice Feat. Irina - White Knuckle Ride Full Edit [Www Keepvid Com] (Official Audio)", "Mad Ice - White Knuckle Ride Full Edit", "Mad Ice - White Knuckle Ride"] },
        { "Daft Punk", "Around the World", ["Daft Punk - Around the World (Official Audio)", "Daft Punk - Around the World"] },
        { "Calvin Harris ft. Rihanna", "We Found Love (Radio Edit)",
            ["Calvin Harris ft. Rihanna - We Found Love (Radio Edit) (Official Audio)", "Calvin Harris - We Found Love (Radio Edit)", "Calvin Harris - We Found Love"] },
        { "AC/DC", "Back in Black", ["AC/DC - Back in Black (Official Audio)", "AC/DC - Back in Black"] },
        { "The Offspring", "Self Esteem - 128", ["The Offspring - Self Esteem - 128 (Official Audio)", "The Offspring - Self Esteem"] },
    };

    [Theory, MemberData(nameof(PythonQueries))]
    public void Queries_match_python(string artist, string title, string[] expected) =>
        Assert.Equal(expected, YouTubeMatching.SearchQueries(artist, title, faithful: true));

    [Fact]
    public void Queries_keep_dotted_titles_whole()
    {
        // Défaut corrigé : l'original réduisait « Mr. Brightside » à « Mr » (clean_stem sur un titre).
        Assert.Equal(["The Killers - Mr. Brightside (Official Audio)", "The Killers - Mr. Brightside"], YouTubeMatching.SearchQueries("The Killers", "Mr. Brightside"));
        Assert.Contains("The Killers - Mr", YouTubeMatching.SearchQueries("The Killers", "Mr. Brightside", faithful: true));
    }

    [Fact]
    public void Score_filters_title_and_rewards_artist()
    {
        Assert.Null(YouTubeMatching.Score("Around the World", "Daft Punk", "Totally Different Song"));
        Assert.Equal(1.0, YouTubeMatching.Score("Around the World", "Daft Punk", "Around the World (Radio Edit)"));
        Assert.Equal(2.0, YouTubeMatching.Score("Around the World", "Daft Punk", "Daft Punk - Around the World"));
        // 2 mots sur 3 = 0,67 < 0,7 : rejeté.
        Assert.Null(YouTubeMatching.Score("Alpha Beta Gamma", "X", "Alpha Beta"));
        Assert.Equal(1.0, YouTubeMatching.Score("Alpha Beta", "X", "Alpha Beta live"));
    }

    [Fact]
    public void Score_accepts_non_latin_titles()
    {
        Assert.Equal(2.0, YouTubeMatching.Score("Мой путь", "Кино", "Кино - Мой путь"));
        Assert.Null(YouTubeMatching.Score("Мой путь", "Кино", "Кино - Мой путь", WordMode.Python));
    }

    [Fact]
    public async Task Select_stops_at_first_query_with_candidates_and_sorts_stably()
    {
        var asked = new List<string>();
        var hits = await YouTubeMatching.SelectAsync("Daft Punk", "Around the World", q =>
        {
            asked.Add(q);
            return Task.FromResult<IReadOnlyList<VideoHit>>(asked.Count == 1
                ? [new("a", "Nothing related"), new("b", "Around the World (Radio Edit)"), new("a", "dup"), new("c", "Daft Punk - Around the World"), new("d", "Around the World remix")]
                : []);
        });
        Assert.Single(asked);
        Assert.Equal(["c", "b", "d"], hits.Select(h => h.Id));
    }

    [Fact]
    public async Task Select_widens_queries_until_one_matches()
    {
        var asked = new List<string>();
        var hits = await YouTubeMatching.SelectAsync("Mad Ice Feat. Irina", "White Knuckle Ride Full Edit [Www Keepvid Com]", q =>
        {
            asked.Add(q);
            return Task.FromResult<IReadOnlyList<VideoHit>>(asked.Count < 3 ? [] : [new("z", "Mad Ice - White Knuckle Ride")]);
        });
        Assert.Equal(3, asked.Count);
        Assert.Equal("z", Assert.Single(hits).Id);
    }

    [Theory]
    [InlineData("ERROR: [youtube] x: Sign in to confirm your age", true)]
    [InlineData("Video unavailable. This video is private video", true)]
    [InlineData("Not available in your country", true)]
    [InlineData("HTTP Error 403: Forbidden", false)]
    [InlineData("ERROR: unable to download: connection reset", false)]
    [InlineData("This video is Age-Restricted", true)]
    [InlineData("Join this channel to get access to members-only content", true)]
    [InlineData("The uploader who has blocked it on copyright grounds", true)]
    public void Permanent_failures(string message, bool expected) => Assert.Equal(expected, Failures.IsPermanent(message));

    [Fact]
    public void Nine_permanent_markers() => Assert.Equal(9, Failures.PermanentMarkers.Count);

    [Fact]
    public void Server_block_is_detected()
    {
        Assert.True(Failures.IsServerBlocked("ERROR: Sign in to confirm you're not a bot"));
        Assert.False(Failures.IsServerBlocked("HTTP Error 403: Forbidden"));
    }

    [Fact]
    public void Retry_delays_and_texts()
    {
        Assert.Equal(0.5555, Failures.RetryDelaySeconds(1, 10), 3);
        Assert.Equal(4.4444, Failures.RetryDelaySeconds(8, 10), 3);
        Assert.Equal(5.0, Failures.RetryDelaySeconds(9, 10));
        Assert.Equal("Échec, nouvelle tentative dans 1.1 s (essai 3/10)...", Failures.RetryText(2, 10));
        Assert.Equal("Extraction... (essai 2/10)", Failures.WithAttempt("Extraction...", 2, 10));
        Assert.Equal("Extraction...", Failures.WithAttempt("Extraction...", 1, 10));
        Assert.Equal("Abandonné après 10 tentatives : x", Failures.GaveUp(10, "x"));
        Assert.Equal("Erreur", Failures.FailedStatus(1));
        Assert.Equal("Erreur / Abandonné (4 essais)", Failures.FailedStatus(4));
        Assert.Equal("La seule vidéo trouvée n'est pas accessible : m", Failures.AllRefused(1, "m"));
        Assert.Equal("Aucune des 3 vidéos trouvées n'est accessible : m", Failures.AllRefused(3, "m"));
    }

    [Theory]
    [InlineData("MP3", "320 kbps (HQ)", "320")]
    [InlineData("MP3", "128 kbps (Standard)", "128")]
    [InlineData("FLAC", "Lossless (Qualité Source)", "0")]
    [InlineData("WAV", "Lossless (Qualité Source)", "0")]
    [InlineData("M4A", "320 kbps (HQ)", "320")]
    public void Audio_quality(string format, string quality, string expected) => Assert.Equal(expected, YtDlpCommand.AudioQuality(format, quality));

    [Fact]
    public void Extract_command_line()
    {
        var yt = new YtDlpSettings("/c/cookies.txt", "/opt/ffmpeg", "/opt/deno");
        var args = YtDlpCommand.ExtractArguments("abc123", "/w", "mp3", "320 kbps (HQ)", yt);
        string After(string flag) => args[args.ToList().IndexOf(flag) + 1];
        Assert.Equal("bestaudio/best", After("-f"));
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--force-overwrites", args);
        Assert.Equal("ejs:github", After("--remote-components"));
        Assert.Equal("youtube:player_client=web_embedded,mweb,tv_simply", After("--extractor-args"));
        Assert.Contains("-x", args);
        Assert.Equal("mp3", After("--audio-format"));
        Assert.Equal("320K", After("--audio-quality"));
        Assert.Equal("/c/cookies.txt", After("--cookies"));
        Assert.Equal("/opt/ffmpeg", After("--ffmpeg-location"));
        Assert.Equal("deno:/opt/deno", After("--js-runtimes"));
        Assert.Equal("https://www.youtube.com/watch?v=abc123", args[^1]);
        Assert.Equal(Path.Combine("/w", "audio.%(ext)s"), After("-o"));
    }

    [Fact]
    public void Lossless_is_vbr_zero_and_128_is_a_bitrate()
    {
        var args = YtDlpCommand.ExtractArguments("v", "/w", "flac", "Lossless (Qualité Source)", new YtDlpSettings());
        Assert.Equal("0", args[args.ToList().IndexOf("--audio-quality") + 1]);
        Assert.DoesNotContain("--cookies", args);
        Assert.Equal("128K", YtDlpCommand.AudioQualityArgument("128"));
    }

    [Fact]
    public void Search_command_line()
    {
        var args = YtDlpCommand.SearchArguments("Daft Punk - Around the World", new YtDlpSettings());
        Assert.Contains("--flat-playlist", args);
        Assert.Contains("-J", args);
        Assert.Equal("ytsearch3:Daft Punk - Around the World", args[^1]);
    }

    [Fact]
    public void Progress_parsing_follows_python_hook()
    {
        Assert.Equal(45, YtDlpCommand.ParseProgress("DKP 500 1000 NA"));
        Assert.Equal(90, YtDlpCommand.ParseProgress("DKP 1000 NA 1000"));
        Assert.Null(YtDlpCommand.ParseProgress("[download] 50%"));
        Assert.True(YtDlpCommand.IsPostProcessing("[ExtractAudio] Destination: x.mp3"));
    }

    [Fact]
    public void Search_json_and_errors()
    {
        var hits = YtDlpCommand.ParseSearch("""{"entries":[{"id":"a","title":"T1"},{"title":"sans id"},null,{"id":"b"}]}""");
        Assert.Equal(["a", "b"], hits.Select(h => h.Id));
        Assert.Empty(YtDlpCommand.ParseSearch("pas du json"));
        Assert.Equal("ERROR: [youtube] a: Video unavailable", YtDlpCommand.ErrorMessage("WARNING: x\nERROR: [youtube] a: Video unavailable\n", 1));
        Assert.Equal("yt-dlp a échoué (code 2)", YtDlpCommand.ErrorMessage("", 2));
    }

    [Fact]
    public void Target_stem_and_export_choices()
    {
        Assert.Equal("Around the World - Daft Punk", Stem("Around the World", "Daft Punk"));
        Assert.Equal("piste", Stem("", ""));
        Assert.Equal("FLAC", ExportFormats.Normalize("flac", "320 kbps (HQ)").Format);
        Assert.Equal("Lossless (Qualité Source)", ExportFormats.Normalize("flac", "320 kbps (HQ)").Quality);
        Assert.Equal("MP3", ExportFormats.Normalize("???", null).Format);
        Assert.Equal(128, ExportFormats.TargetBitrate("128 kbps (Standard)"));
        Assert.Equal(0, ExportFormats.TargetBitrate("Lossless (Qualité Source)"));
    }

    private static string Stem(string title, string artist) =>
        TrackMetadata.TargetStem(new Dictionary<string, string?> { ["title"] = title, ["artist"] = artist });
}
