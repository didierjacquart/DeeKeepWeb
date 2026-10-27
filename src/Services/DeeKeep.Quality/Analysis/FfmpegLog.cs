using System.Globalization;
using System.Text.RegularExpressions;

namespace DeeKeep.Quality.Analysis;

/// <summary>
/// Commande ffmpeg de l'analyse qualité et lecture de son journal — portage de
/// <c>quality_scan._filtergraph</c>, <c>_parse</c>, <c>_gap_stats</c>, <c>_error_line</c>.
/// Une seule lecture du fichier : le flux est dédoublé vers deux <c>silencedetect</c>
/// (blancs « @long », coupures « @gap »), deux <c>astats</c> (signal entier « @full »,
/// bande au-dessus de 8 kHz « @hf ») et, en analyse approfondie, <c>adeclick</c>.
/// </summary>
public static partial class FfmpegLog
{
    /// <summary>HISS_HIGHPASS_HZ : coupure du passe-haut isolant le souffle.</summary>
    public const int HissHighpassHz = 8000;
    /// <summary>DECLICK_THRESHOLD (1 = très sensible, 100 = très permissif).</summary>
    public const int DeclickThreshold = 12;
    /// <summary>SILENCE_NOISE_DB / SILENCE_MIN_SECONDS : blancs de début et de fin.</summary>
    public const int SilenceNoiseDb = -50;
    public const int SilenceMinSeconds = 2;
    /// <summary>GAP_NOISE_DB / GAP_MIN_SECONDS : silence numérique au milieu du morceau.</summary>
    public const int GapNoiseDb = -80;
    public const string GapMinSeconds = "0.02";
    /// <summary>GAP_EDGE_MARGIN : coupures collées au début ou à la fin écartées (ce sont les blancs).</summary>
    public const double GapEdgeMargin = 1.0;
    /// <summary>GAP_WINDOW_SECONDS : fenêtre de mesure de la densité de coupures.</summary>
    public const double GapWindowSeconds = 10.0;
    /// <summary>SILENCE_END_MARGIN : ffmpeg clôt la dernière plage sur la dernière trame décodée.</summary>
    public const double SilenceEndMargin = 1.5;
    /// <summary>PROBE_TIMEOUT de l'original, en secondes (plancher du délai accordé à un fichier).</summary>
    public const int ProbeTimeoutSeconds = 900;

    [GeneratedRegex(@"^\[astats@(\w+) @ [^\]]*\] ([A-Za-z][^:]*): (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AstatsRe();

    [GeneratedRegex(@"^\[silencedetect@(\w+) @ [^\]]*\] silence_(start|end):\s*(-?[\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SilenceRe();

    [GeneratedRegex(@"Detected clicks in (\d+) of (\d+) samples", RegexOptions.CultureInvariant)]
    private static partial Regex DeclickRe();

    [GeneratedRegex(@"Duration: (\d+):(\d+):([\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DurationRe();

    [GeneratedRegex(@"Audio: .*?, (\d+) Hz", RegexOptions.CultureInvariant)]
    private static partial Regex RateRe();

    /// <summary><c>_filtergraph(deep)</c>, chaîne exacte de l'original.</summary>
    public static string FilterGraph(bool deep)
    {
        var branches = deep ? 4 : 3;
        var labels = "[a][b][c]" + (deep ? "[d]" : "");
        var graph = $"[0:a:0]asplit={branches}{labels};" +
                    $"[a]silencedetect@long=noise={SilenceNoiseDb}dB:d={SilenceMinSeconds},aformat=sample_fmts=s16," +
                    "astats@full=metadata=0:measure_perchannel=none[s1];" +
                    $"[b]highpass=f={HissHighpassHz},astats@hf=metadata=0:measure_perchannel=none[s2];" +
                    $"[c]silencedetect@gap=noise={GapNoiseDb}dB:d={GapMinSeconds}[s3];";
        return deep
            ? graph + $"[d]adeclick=t={DeclickThreshold}[s4];[s1][s2][s3][s4]amix=inputs=4[out]"
            : graph + "[s1][s2][s3]amix=inputs=3[out]";
    }

    /// <summary>Arguments de <c>measure</c> (liste, sans shell) : rien n'est écrit, le muxer est <c>null</c>.</summary>
    public static IReadOnlyList<string> Arguments(string path, bool deep) =>
        ["-hide_banner", "-nostats", "-i", path, "-filter_complex", FilterGraph(deep), "-map", "[out]", "-f", "null", "-"];

    /// <summary><c>_to_float</c> : <c>float()</c> de Python, puis repli « …inf » → ±∞, sinon NaN.</summary>
    public static double ToFloat(string text)
    {
        var stripped = text.Trim().ToLowerInvariant();
        switch (stripped)
        {
            case "inf" or "+inf" or "infinity" or "+infinity": return double.PositiveInfinity;
            case "-inf" or "-infinity": return double.NegativeInfinity;
            case "nan" or "+nan" or "-nan": return double.NaN;
        }
        if (double.TryParse(stripped, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
        if (stripped.EndsWith("inf", StringComparison.Ordinal))
            return stripped.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
        return double.NaN;
    }

    /// <summary>
    /// <c>splitlines()</c> : le journal est découpé sur \r\n, \n et \r (ffmpeg réécrit parfois
    /// sa ligne d'état avec \r).
    /// </summary>
    public static IEnumerable<string> Lines(string log) => log.Split(["\r\n", "\n", "\r"], StringSplitOptions.None) is var parts &&
        parts.Length > 0 && parts[^1].Length == 0 ? parts[..^1] : parts;

    /// <summary><c>_parse(log)</c> : mesures extraites du journal (sans <c>deep</c> ni <c>error</c>).</summary>
    public static QualityMeasures Parse(string log)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        List<double> silenceStarts = [], silenceEnds = [], gapStarts = [], gapEnds = [];
        double? clicks = null;
        var duration = 0.0;
        var rate = 0;

        foreach (var line in Lines(log))
        {
            var m = AstatsRe().Match(line);
            if (m.Success)
            {
                stats[m.Groups[1].Value + "|" + m.Groups[2].Value] = ToFloat(m.Groups[3].Value);
                continue;
            }
            m = SilenceRe().Match(line);
            if (m.Success)
            {
                // float() refuserait « 1.2.3 » : la ligne est alors ignorée plutôt que fatale.
                if (double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    var start = m.Groups[2].Value == "start";
                    if (m.Groups[1].Value == "gap") (start ? gapStarts : gapEnds).Add(v);
                    else (start ? silenceStarts : silenceEnds).Add(v);
                }
                continue;
            }
            m = DeclickRe().Match(line);
            if (m.Success)
            {
                var total = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (total == 0) total = 1;
                clicks = 100.0 * long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / total;
                continue;
            }
            if (duration == 0)
            {
                m = DurationRe().Match(line);
                if (m.Success && double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    // Même ordre d'opérations que l'original : la somme entière d'abord.
                    long whole = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
                                 long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60;
                    duration = whole + seconds;
                }
            }
            if (rate == 0)
            {
                m = RateRe().Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var r)) rate = r;
            }
        }

        // Silence initial : la première plage muette doit commencer au tout début.
        var head = 0.0;
        if (silenceStarts.Count > 0 && silenceStarts[0] <= 0.2 && silenceEnds.Count > 0)
            head = Math.Max(0.0, silenceEnds[0]);

        // Silence final : plage restée ouverte, ou close sur la dernière trame (ffmpeg 8 ferme
        // la plage ouverte en fin de flux : c'est alors la seconde règle qui joue).
        var tail = 0.0;
        if (silenceStarts.Count > 0 && duration != 0)
        {
            var closedOnEnd = silenceEnds.Count > 0 && Math.Abs(silenceEnds[^1] - duration) <= SilenceEndMargin;
            if (silenceStarts.Count > silenceEnds.Count || closedOnEnd)
                tail = Math.Max(0.0, duration - silenceStarts[^1]);
            // Un fichier entièrement muet n'a pas de « blanc de fin » : il n'est que silence.
            if (head != 0 && tail >= duration - 0.5) tail = 0.0;
        }

        // « x or 0 » : seule une valeur absente ou nulle vaut 0 (un NaN reste NaN).
        var samples = stats.GetValueOrDefault("full|Number of samples", 0.0);
        var peakCount = stats.GetValueOrDefault("full|Peak count", 0.0);
        var peakDb = stats.GetValueOrDefault("full|Peak level dB", double.NaN);

        // « Peak count » ne compte des échantillons saturés que si le signal touche la butée.
        // Formule conservée telle quelle (pics cumulés sur les canaux / échantillons par canal)
        // pour rester comparable aux mesures existantes.
        var clipping = 0.0;
        if (samples != 0 && !double.IsNaN(peakDb) && peakDb >= -0.5)
            clipping = 100.0 * peakCount / samples;

        var (gapCount, gapSeconds, gapDensity) = GapStats(gapStarts, gapEnds, duration);

        return new QualityMeasures
        {
            Duration = duration,
            SampleRate = rate,
            PeakDb = peakDb,
            ClippingPct = clipping,
            FlatFactor = stats.GetValueOrDefault("full|Flat factor", 0.0),
            HissDb = stats.GetValueOrDefault("hf|Noise floor dB", double.NegativeInfinity),
            HissTroughDb = stats.GetValueOrDefault("hf|RMS through dB", double.NegativeInfinity),
            FullTroughDb = stats.GetValueOrDefault("full|RMS through dB", double.NegativeInfinity),
            CracklePct = clicks,
            GapCount = gapCount,
            GapSeconds = gapSeconds,
            GapDensity = gapDensity,
            HeadSeconds = head,
            TailSeconds = tail,
        };
    }

    /// <summary>
    /// <c>_gap_stats</c> : coupures internes (nombre, durée totale, densité maximale sur dix
    /// secondes). Appariement par position (<c>zip</c>) ; une plage sans fin est le blanc final,
    /// pas une coupure. C'est la densité, et non le nombre, qui fait le verdict.
    /// </summary>
    public static (int Count, double Seconds, double Density) GapStats(IReadOnlyList<double> starts, IReadOnlyList<double> ends, double duration)
    {
        if (duration == 0) return (0, 0.0, 0.0);
        var segments = new List<(double Start, double End)>();
        for (var i = 0; i < Math.Min(starts.Count, ends.Count); i++)
        {
            if (starts[i] >= GapEdgeMargin && ends[i] <= duration - GapEdgeMargin) segments.Add((starts[i], ends[i]));
        }
        if (segments.Count == 0) return (0, 0.0, 0.0);

        var total = 0.0;
        foreach (var (s, e) in segments) total += e - s;
        // Fenêtre ancrée sur chaque coupure : la plus trouée commence forcément sur l'une d'elles.
        var density = 0.0;
        foreach (var (origin, _) in segments)
        {
            var covered = 0.0;
            foreach (var (s, e) in segments)
                covered += Math.Max(0.0, Math.Min(e, origin + GapWindowSeconds) - Math.Max(s, origin));
            density = Math.Max(density, covered / GapWindowSeconds);
        }
        return (segments.Count, total, Math.Min(1.0, density));
    }

    /// <summary><c>_error_line</c> : dernière ligne non vide exploitable du journal, 160 caractères au plus.</summary>
    public static string ErrorLine(string log)
    {
        foreach (var line in Lines(log).Reverse())
        {
            var text = line.Trim();
            // Le test « deux espaces » porte, comme dans l'original, sur la ligne déjà nettoyée.
            if (text.Length > 0 && !text.StartsWith("frame=", StringComparison.Ordinal) && !text.StartsWith("size=", StringComparison.Ordinal))
                return text.Length > 160 ? text[..160] : text;
        }
        return "";
    }
}
