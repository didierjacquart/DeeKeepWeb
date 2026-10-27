using System.Runtime.CompilerServices;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Jobs;
using Xunit;

namespace DeeKeep.Tests.Quality;

/// <summary>
/// Journaux ffmpeg réels (ffmpeg 8.1.2, capturés en lecture sur des fichiers de la bibliothèque) et
/// vecteurs produits par le Python d'origine (<c>quality_scan._parse / _gap_stats / evaluate / describe</c>).
/// </summary>
public class QualityTests
{
    private static string Log(string name, [CallerFilePath] string source = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "Assets", name + ".log"));

    private static QualityMeasures Fighter(Func<QualityMeasures, QualityMeasures>? edit = null)
    {
        var m = FfmpegLog.Parse(Log("fighter_deep")) with { Deep = true };
        return edit?.Invoke(m) ?? m;
    }

    [Fact]
    public void FilterGraph_is_verbatim()
    {
        Assert.Equal("[0:a:0]asplit=4[a][b][c][d];[a]silencedetect@long=noise=-50dB:d=2,aformat=sample_fmts=s16,astats@full=metadata=0:measure_perchannel=none[s1];" +
                     "[b]highpass=f=8000,astats@hf=metadata=0:measure_perchannel=none[s2];[c]silencedetect@gap=noise=-80dB:d=0.02[s3];[d]adeclick=t=12[s4];[s1][s2][s3][s4]amix=inputs=4[out]",
            FfmpegLog.FilterGraph(true));
        Assert.Equal("[0:a:0]asplit=3[a][b][c];[a]silencedetect@long=noise=-50dB:d=2,aformat=sample_fmts=s16,astats@full=metadata=0:measure_perchannel=none[s1];" +
                     "[b]highpass=f=8000,astats@hf=metadata=0:measure_perchannel=none[s2];[c]silencedetect@gap=noise=-80dB:d=0.02[s3];[s1][s2][s3]amix=inputs=3[out]",
            FfmpegLog.FilterGraph(false));
    }

    [Fact]
    public void Parse_real_log_beautiful_life()
    {
        var m = FfmpegLog.Parse(Log("beautiful_life_deep"));
        Assert.Equal(221.29, m.Duration, 6);
        Assert.Equal(48000, m.SampleRate);
        Assert.Equal(0.000265, m.PeakDb, 9);
        Assert.Equal(0.0005460500481230141, m.ClippingPct, 12);
        Assert.Equal(3.621094, m.FlatFactor, 9);
        Assert.Equal(-129.159566, m.HissDb, 9);
        Assert.Equal(-113.376532, m.HissTroughDb, 9);
        Assert.Equal(-110.211159, m.FullTroughDb, 9);
        Assert.Equal(1.41219840031814e-05, m.CracklePct!.Value, 15);
        // Coupures de début (< 1 s) et de fin (> durée - 1) exclues ; blanc de fin fermé sur la dernière trame.
        Assert.Equal(0, m.GapCount);
        Assert.Equal(0.0, m.HeadSeconds);
        Assert.Equal(3.5123539999999878, m.TailSeconds, 9);
    }

    [Fact]
    public void Parse_real_log_fighter_and_fast_mode()
    {
        var m = FfmpegLog.Parse(Log("fighter_deep"));
        Assert.Equal(246.05, m.Duration, 6);
        Assert.Equal(0.10076701811926629, m.ClippingPct, 12);
        Assert.Equal(-59.787714, m.HissDb, 9);
        Assert.Equal(-71.05837, m.HissTroughDb, 9);
        Assert.Equal(8.890460383600504e-05, m.CracklePct!.Value, 15);
        Assert.Equal(0.0, m.TailSeconds);

        var fast = FfmpegLog.Parse(Log("beautiful_life_fast"));
        Assert.Null(fast.CracklePct);
        Assert.Equal(221.29, fast.Duration, 6);
    }

    [Fact]
    public void Error_line_of_missing_file()
    {
        Assert.Equal("Error opening input files: No such file or directory", FfmpegLog.ErrorLine(Log("missing")));
    }

    [Theory]
    [InlineData("-inf", double.NegativeInfinity)]
    [InlineData("inf", double.PositiveInfinity)]
    [InlineData("-897.069387", -897.069387)]
    public void ToFloat_handles_infinities(string text, double expected) => Assert.Equal(expected, FfmpegLog.ToFloat(text));

    [Fact]
    public void ToFloat_unparsable_is_nan() => Assert.True(double.IsNaN(FfmpegLog.ToFloat("N/A")));

    [Fact]
    public void Parse_tolerates_unknown_duration_and_inf()
    {
        var m = FfmpegLog.Parse("  Duration: N/A, bitrate: N/A\n[astats@hf @ 0x1] Noise floor dB: -inf\n[astats@full @ 0x1] Peak level dB: nan\n");
        Assert.Equal(0.0, m.Duration);
        Assert.Equal(double.NegativeInfinity, m.HissDb);
        Assert.True(double.IsNaN(m.PeakDb));
    }

    [Fact]
    public void Open_ended_silence_is_a_tail_and_muted_file_has_none()
    {
        var open = FfmpegLog.Parse("  Duration: 00:01:40.00, start: 0\n[silencedetect@long @ 0x1] silence_start: 90.5\n");
        Assert.Equal(9.5, open.TailSeconds, 9);
        var muted = FfmpegLog.Parse("  Duration: 00:00:10.00, start: 0\n[silencedetect@long @ 0x1] silence_start: 0\n[silencedetect@long @ 0x1] silence_end: 10.0 | silence_duration: 10\n");
        Assert.Equal(10.0, muted.HeadSeconds);
        Assert.Equal(0.0, muted.TailSeconds);
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void GapStats_matches_python(double[] starts, double[] ends, double duration, int count, double seconds, double density)
    {
        var (c, s, d) = FfmpegLog.GapStats(starts, ends, duration);
        Assert.Equal(count, c);
        Assert.Equal(seconds, s, 9);
        Assert.Equal(density, d, 9);
    }

    public static TheoryData<double[], double[], double, int, double, double> GapCases => new()
    {
        { [], [], 100.0, 0, 0.0, 0.0 },
        { [0.0, 5.0, 12.0, 50.0], [0.7, 6.5, 14.0], 60.0, 2, 3.5, 0.35 },
        { [2.0, 3.0, 4.0, 5.0, 6.0], [2.5, 3.6, 4.9, 5.2, 7.1], 30.0, 5, 3.3, 0.33 },
        { [1.0, 99.5], [2.0, 100.0], 100.0, 1, 1.0, 0.1 },
        { [10.0], [30.0], 100.0, 1, 20.0, 1.0 },
    };

    // ------------------------------------------------------------------ verdict

    [Fact]
    public void Beautiful_life_scores_100_with_closest_reference()
    {
        var m = FfmpegLog.Parse(Log("beautiful_life_deep"));
        var reference = References.Closest([221, 179, 227, 348, 405, 232, 221, 417, 421, 503, 424, 227, 500, 217, 220, 284, 341, 477], m.Duration);
        Assert.Equal(221, reference);
        var v = QualityVerdict.Evaluate(m, null, reference);
        Assert.Empty(v.Defects);
        Assert.Equal(100, v.Score);
    }

    [Fact]
    public void Fighter_closest_reference_gives_hiss_only_score_81()
    {
        var m = Fighter();
        var reference = References.Closest([245, 266], m.Duration);
        Assert.Equal(245, reference);
        var v = QualityVerdict.Evaluate(m, null, reference);
        Assert.Equal(["hiss"], v.Defects);
        Assert.Equal(0.34748573333333327, v.Severities["hiss"], 12);
        Assert.Equal(81, v.Score);
        // Sans référence : même verdict (la durée n'est comparée à rien).
        Assert.Equal(81, QualityVerdict.Evaluate(m, null, null).Score);
    }

    [Fact]
    public void Stored_defects_were_wrong_median_reference_gives_duration()
    {
        // La colonne « defects » de la base venait de la médiane (266) : avec elle, la durée est mise en cause.
        var v = QualityVerdict.Evaluate(Fighter(), null, 266);
        Assert.Equal(["hiss", "duration"], v.Defects);
        Assert.Equal(0.3316666666666663, v.Severities["duration"], 12);
        Assert.Equal(62, v.Score);
    }

    [Fact]
    public void All_seven_defects_in_evaluation_order()
    {
        var m = Fighter(x => x with { CracklePct = 0.004, ClippingPct = 0.5, GapDensity = 0.42, GapCount = 7, TailSeconds = 12.4, HeadSeconds = 4.25 });
        var v = QualityVerdict.Evaluate(m, null, 220);
        Assert.Equal(["hiss", "crackle", "clipping", "gaps", "duration", "tail", "head"], v.Defects);
        Assert.Equal(0.075, v.Severities["crackle"], 12);
        Assert.Equal(0.48, v.Severities["gaps"], 12);
        Assert.Equal(0.535, v.Severities["duration"], 12);
        Assert.Equal(0.37, v.Severities["tail"], 12);
        Assert.Equal(0.15625, v.Severities["head"], 12);
        Assert.Equal(0, v.Score);
    }

    [Fact]
    public void Blanks_only_count_when_file_is_longer_than_reference()
    {
        var m = Fighter(x => x with { Duration = 250.0, TailSeconds = 7.0 });
        var withTail = QualityVerdict.Evaluate(m, null, 245);
        Assert.Equal(["hiss", "tail"], withTail.Defects);
        Assert.Equal(67, withTail.Score);
        var tolerant = QualityVerdict.Evaluate(m, new QualityLimits { DurationExcess = 6.0, HissDb = -50.0 }, 245);
        Assert.Empty(tolerant.Defects);
        Assert.Equal(100, tolerant.Score);
    }

    [Fact]
    public void Hiss_rules_sample_rate_broadband_and_digital_silence()
    {
        Assert.Empty(QualityVerdict.Evaluate(Fighter(x => x with { SampleRate = 16000 }), null, 245).Defects);
        Assert.Equal(["hiss"], QualityVerdict.Evaluate(Fighter(x => x with { FullTroughDb = double.NegativeInfinity }), null, 245).Defects);
        // Creux entier à -40 dB : 31 dB d'écart, aigus absents des passages calmes, donc de la musique.
        Assert.Empty(QualityVerdict.Evaluate(Fighter(x => x with { FullTroughDb = -40.0 }), null, 245).Defects);
        Assert.Empty(QualityVerdict.Evaluate(Fighter(x => x with { HissDb = double.NaN }), null, 245).Defects);
        Assert.Empty(QualityVerdict.Evaluate(Fighter(x => x with { HissDb = -897.069387, HissTroughDb = -442.507419, FullTroughDb = double.NegativeInfinity }), null, null).Defects);
    }

    [Fact]
    public void Custom_thresholds_and_rapid_analysis()
    {
        var m = Fighter(x => x with { CracklePct = null, ClippingPct = 0.25 });
        var v = QualityVerdict.Evaluate(m, new QualityLimits { ClippingPct = 0.3, DurationGap = 60.0 }, 300);
        Assert.Equal(["hiss"], v.Defects);
        Assert.Equal(81, v.Score);
        // Mesure approfondie en cache, analyse rapide demandée : le craquement ne baisse pas la note.
        var deepMeasure = Fighter(x => x with { CracklePct = 0.1 });
        Assert.Contains("crackle", QualityVerdict.Evaluate(deepMeasure, null, null).Defects);
        Assert.DoesNotContain("crackle", QualityVerdict.Evaluate(deepMeasure, null, null, includeCrackle: false).Defects);
    }

    [Fact]
    public void Many_defects_clamp_severity_to_one()
    {
        var m = Fighter(x => x with { CracklePct = 0.1, ClippingPct = 3.0, GapDensity = 1.0, GapCount = 2, TailSeconds = 40, HeadSeconds = 20, HissDb = -40 });
        var v = QualityVerdict.Evaluate(m, null, 150);
        Assert.All(v.Severities.Values, s => Assert.Equal(1.0, s));
        Assert.Equal(0, v.Score);
    }

    [Fact]
    public void Describe_matches_python()
    {
        var m = Fighter();
        Assert.Equal("-60 dB", QualityVerdict.Describe(m, "hiss"));
        Assert.Equal("0.0001 %", QualityVerdict.Describe(m, "crackle"));
        Assert.Equal("0.101 %", QualityVerdict.Describe(m, "clipping"));
        Assert.Equal("—", QualityVerdict.Describe(m, "gaps"));
        Assert.Equal("0 s", QualityVerdict.Describe(m, "tail"));
        Assert.Equal("", QualityVerdict.Describe(m, "duration"));
        var big = Fighter(x => x with { CracklePct = 0.004, GapDensity = 0.42, GapCount = 7, TailSeconds = 12.4, HeadSeconds = 4.25 });
        Assert.Equal("0.0040 %", QualityVerdict.Describe(big, "crackle"));
        Assert.Equal("7 · 42 %", QualityVerdict.Describe(big, "gaps"));
        Assert.Equal("12 s", QualityVerdict.Describe(big, "tail"));
        Assert.Equal("4 s", QualityVerdict.Describe(big, "head"));
        Assert.Equal("—", QualityVerdict.Describe(Fighter(x => x with { HissDb = -897.07 }), "hiss"));
        Assert.Equal("-66 dB", QualityVerdict.Describe(Fighter(x => x with { HissDb = -65.5 }), "hiss"));
        Assert.Equal("—", QualityVerdict.Describe(Fighter(x => x with { CracklePct = null }), "crackle"));
    }

    // ------------------------------------------------------------------ JSON, cellules

    [Fact]
    public void Measures_json_roundtrip_matches_stored_format()
    {
        const string stored = "{\"clipping_pct\":0.10076701811926629,\"crackle_pct\":8.890460383600504e-05,\"deep\":true,\"duration\":246.05,\"error\":\"\"," +
                              "\"flat_factor\":8.721454,\"full_trough_db\":-59.812481,\"gap_count\":0,\"gap_density\":0.0,\"gap_seconds\":0.0,\"head_seconds\":0.0," +
                              "\"hiss_db\":-59.787714,\"hiss_trough_db\":-71.05837,\"peak_db\":0.000265,\"sample_rate\":48000,\"tail_seconds\":0.0}";
        var m = QualityMeasures.FromJson(stored)!;
        Assert.Equal(stored, m.ToJsonText());
        const string withInf = "{\"clipping_pct\":0.0,\"crackle_pct\":0.0003,\"deep\":true,\"duration\":336.65,\"error\":\"\",\"flat_factor\":0.0,\"full_trough_db\":\"-inf\"," +
                               "\"gap_count\":0,\"gap_density\":0.0,\"gap_seconds\":0.0,\"head_seconds\":0.0,\"hiss_db\":\"-inf\",\"hiss_trough_db\":\"-inf\"," +
                               "\"peak_db\":-3.111265,\"sample_rate\":48000,\"tail_seconds\":0.0}";
        var n = QualityMeasures.FromJson(withInf)!;
        Assert.Equal(double.NegativeInfinity, n.HissDb);
        Assert.Equal(withInf, n.ToJsonText());
    }

    [Fact]
    public void Cells_and_tooltips_follow_the_original_texts()
    {
        var m = Fighter();
        var v = QualityVerdict.Evaluate(m, null, 245);
        var (cells, tips) = QualityPresenter.Present(m, v, QualityLimits.Default, "value", 245, "MP3", 128, null, null);
        Assert.Equal("81", cells["score"]);
        Assert.Equal("Souffle", cells["defects"]);
        Assert.Equal("4:06  (+1 s)", cells["duration"]);
        Assert.Equal("ok", cells["blanks"]);
        Assert.Equal("MP3 · 128 kbps", cells["format"]);
        Assert.Equal("MP3 · 128 kbps  ·  note 81  ·  souffle", cells["context"]);
        Assert.Contains("le fichier dure ce que dure le morceau".ToLowerInvariant(), tips["duration"].ToLowerInvariant());
        Assert.Contains("bruit large bande : c'est un souffle", tips["hiss"]);
        Assert.StartsWith("Durée du fichier : 4:06 — morceau : 4:05 (+1 s).", tips["duration"]);
    }

    [Fact]
    public void Reference_states_are_distinguished_in_cells()
    {
        var m = Fighter();
        var v = QualityVerdict.Evaluate(m, null, null);
        Assert.Equal("4:06 — référence…", QualityPresenter.DurationCell(m, "pending", null));
        Assert.Equal("4:06 — sans référence", QualityPresenter.DurationCell(m, "none", null));
        Assert.Equal("4:06 — référence indisponible", QualityPresenter.DurationCell(m, "network", null));
        var blank = Fighter(x => x with { TailSeconds = 7 });
        Assert.Equal("7 s — dans le master", QualityPresenter.BlanksCell(blank, v, "value", QualityLimits.Default));
        Assert.Equal("7 s — sans référence", QualityPresenter.BlanksCell(blank, v, "none", QualityLimits.Default));
        Assert.Equal("—", QualityPresenter.DurationCell(QualityMeasures.Empty(true, "x"), "none", null));
    }

    [Fact]
    public void Replacement_status_reflects_current_verdict()
    {
        var clean = new Verdict([], new Dictionary<string, double>(), 100);
        var flagged = new Verdict(["hiss"], new Dictionary<string, double> { ["hiss"] = 0.3 }, 80);
        Assert.Equal("Remplacé ✓ défaut corrigé", QualityPresenter.StatusText("replaced", null, clean, false).Text);
        Assert.Equal("Remplacé ✓ défaut persistant", QualityPresenter.StatusText("replaced", null, flagged, false).Text);
        Assert.Equal("Remplacé ✓ (réanalyse impossible)", QualityPresenter.StatusText("replaced", null, clean, true).Text);
    }

    // ------------------------------------------------------------------ références, divers

    [Theory]
    [InlineData("Madcon feat. Ray Dalton", "madcon feat ray dalton")]
    [InlineData("Michel Teló", "michel tel")]
    [InlineData("  Don't   Worry (Radio Version) ", "don t worry radio version")]
    public void Reference_key_is_canonical(string text, string expected) => Assert.Equal(expected, References.Canonical(text));

    [Fact]
    public void Reference_key_joins_artist_and_title() => Assert.Equal("cutting crew|i just died in your arms", References.Key("Cutting Crew", "I Just Died In Your Arms"));

    [Fact]
    public void Closest_takes_first_on_tie_and_null_without_data()
    {
        Assert.Equal(240, References.Closest([240, 250], 245));
        Assert.Null(References.Closest([], 245));
        Assert.Null(References.Closest([245], 0));
    }

    [Fact]
    public void Negative_search_expires_after_30_days()
    {
        Assert.True(References.IsFresh([], 1000, 1000 + 29 * 86400));
        Assert.False(References.IsFresh([], 1000, 1000 + 31 * 86400));
        Assert.True(References.IsFresh([200], 1000, 1000 + 400 * 86400));
    }

    [Theory]
    [InlineData(3, 100, 10.0, "")]
    [InlineData(50, 100, 50.0, "moins d'une minute restante")]
    [InlineData(10, 100, 60.0, "environ 9 min restantes")]
    [InlineData(50, 100, 100.0, "environ 2 min restantes")]
    [InlineData(50, 100, 70.0, "environ 1 min restante")]
    public void Remaining_time_text(int done, int total, double elapsed, string expected)
    {
        // Cas « 50/100 en 50 s » : 50 s restantes (≥ 45) → 1 min ; on teste les bornes avec des valeurs sans ambiguïté.
        var text = ScanHandler.RemainingText(done, total, elapsed);
        if (done == 50 && elapsed == 50.0) Assert.Equal("environ 1 min restante", text);
        else Assert.Equal(expected, text);
    }

    [Fact]
    public void Cache_stamp_tolerates_two_seconds_on_same_size()
    {
        Assert.True(ScanHandler.SameFile("1784568360000000000:3550085", "1784568361500000000:3550085"));
        Assert.False(ScanHandler.SameFile("1784568360000000000:3550085", "1784568363500000000:3550085"));
        Assert.False(ScanHandler.SameFile("1784568360000000000:3550085", "1784568360000000000:3550086"));
    }

    [Theory]
    [InlineData("d:\\$recycle.bin\\s-1-5\\$rabc.mp3", true)]
    [InlineData("D:\\System Volume Information\\x.mp3", true)]
    [InlineData("D:\\Queen\\Yeah - Queen.mp3", false)]
    [InlineData("D:\\$Titre.mp3", false)]
    public void Import_excludes_system_folders(string path, bool excluded) => Assert.Equal(excluded, ImportHandler.IsExcluded(path));
}
