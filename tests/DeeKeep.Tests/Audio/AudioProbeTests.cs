using DeeKeep.Common.Audio;
using DeeKeep.Common.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Audio;

public class AudioProbeTests
{
    // (journal, durée déclarée, trailing_silence_start, effective_duration) : audio_probe.py
    // exécuté avec un run() qui rend ce journal.
    public static readonly TheoryData<string, double, double?, int?> Logs = new()
    {
        { "[silencedetect @ 0] silence_start: 217.777646\n[silencedetect @ 0] silence_end: 221.286187 | silence_duration: 3.5\n", 221.29, 217.777646, 218 },
        { "silence_start: 10.5\nsilence_end: 20.0\nsilence_start: 200.25\n", 230.0, 200.25, 200 },
        { "silence_start: 10.5\nsilence_end: 20.0\n", 230.0, null, null },
        { "silence_start: 0\nsilence_end: 0.75\nsilence_start: 218.4\nsilence_end: 219.9\n", 221.3, 218.4, 218 },
        { "no silence here", 100.0, null, null },
        { "silence_start: 3.2\nsilence_end: 101.4\n", 100.0, 3.2, 3 },
        { "silence_start: 0.4\n", 100.0, 0.4, null },
    };

    [Theory]
    [MemberData(nameof(Logs))]
    public async Task Trailing_silence_matches_python(string log, double declared, double? start, int? effective)
    {
        Assert.Equal(start, AudioProbe.ParseTrailingSilence(log, declared));

        var calls = new List<IReadOnlyList<string>>();
        var probe = new AudioProbe(new ToolOptions { Ffmpeg = "ffmpeg" }, (_, args, _, _) =>
        {
            calls.Add(args);
            return Task.FromResult(new ProcessResult(0, "", log, TimeSpan.Zero));
        });
        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(effective, await probe.EffectiveDurationAsync(file, declared, TestContext.Current.CancellationToken));
            // Détection (-version) puis la commande exacte de l'original.
            Assert.Equal(["-hide_banner", "-version"], calls[0]);
            Assert.Equal(["-hide_banner", "-nostats", "-i", file, "-af", "silencedetect=noise=-45dB:d=3", "-f", "null", "-"], calls[1]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Vrai ffmpeg : 5 s de son puis 6 s de silence. Python (effective_duration) rend 5.</summary>
    [Fact]
    public async Task Real_ffmpeg_measures_trailing_silence()
    {
        var ffmpeg = AudioTestSupport.Tool("ffmpeg");
        var ffprobe = AudioTestSupport.Tool("ffprobe");
        if (ffmpeg is null || ffprobe is null) Assert.Skip("ffmpeg absent");
        var dir = AudioTestSupport.TempDir();
        try
        {
            var file = Path.Combine(dir, "silence.mp3");
            var made = AudioTestSupport.Run(ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=5", "-f", "lavfi",
                "-i", "anullsrc=r=44100:cl=mono", "-filter_complex", "[1]atrim=duration=6[s];[0][s]concat=n=2:v=0:a=1",
                "-ar", "44100", "-ac", "1", "-b:a", "64k", file);
            Assert.Equal(0, made.Code);

            var runner = new ProcessRunner(Options.Create(new ToolOptions { Ffmpeg = ffmpeg, Ffprobe = ffprobe, Niceness = 0 }), NullLogger<ProcessRunner>.Instance);
            var probe = new AudioProbe(runner);
            var ct = TestContext.Current.CancellationToken;
            var duration = await probe.DurationAsync(file, ct);
            Assert.NotNull(duration);
            Assert.InRange(duration.Value, 10.9, 11.2);
            Assert.Equal(5, await probe.EffectiveDurationAsync(file, 11.0, ct));
            Assert.Null(await probe.EffectiveDurationAsync(Path.Combine(dir, "absent.mp3"), 11.0, ct));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
