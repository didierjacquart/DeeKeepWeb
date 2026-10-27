using System.Globalization;
using DeeKeep.Common.Data;

namespace DeeKeep.Quality.Analysis;

/// <summary>Identifiants des défauts (valeurs de la colonne <c>quality.defects</c>).</summary>
public static class Defects
{
    public const string Hiss = "hiss";
    public const string Crackle = "crackle";
    public const string Clipping = "clipping";
    public const string Gaps = "gaps";
    public const string Duration = "duration";
    public const string Tail = "tail";
    public const string Head = "head";

    /// <summary>DEFECT_ORDER : ordre d'affichage et des cases de filtrage.</summary>
    public static readonly IReadOnlyList<string> Order = [Hiss, Crackle, Clipping, Gaps, Duration, Tail, Head];

    /// <summary>DEFECT_LABELS.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Hiss] = "Souffle",
        [Crackle] = "Craquements",
        [Clipping] = "Saturation",
        [Gaps] = "Coupures",
        [Duration] = "Durée",
        [Tail] = "Blanc de fin",
        [Head] = "Blanc de début",
    };

    /// <summary>DEEP_DEFECTS : seule l'analyse approfondie sait les voir.</summary>
    public static readonly IReadOnlySet<string> Deep = new HashSet<string> { Crackle };
}

/// <summary>Seuils de détection (DEFAULT_THRESHOLDS fusionnés avec les réglages <c>qual_*</c>).</summary>
public sealed record QualityLimits
{
    public double HissDb { get; init; } = -65.0;
    public double CracklePct { get; init; } = 0.0025;
    public double ClippingPct { get; init; } = 0.20;
    public double GapDensityPct { get; init; } = 30.0;
    public double TailSeconds { get; init; } = 5.0;
    public double HeadSeconds { get; init; } = 3.0;
    public double DurationExcess { get; init; } = 3.0;
    public double DurationGap { get; init; } = 10.0;

    public static readonly QualityLimits Default = new();
}

/// <summary>Verdict : défauts dans l'ordre d'évaluation, gravités (0..1), note sur 100.</summary>
public sealed record Verdict(IReadOnlyList<string> Defects, IReadOnlyDictionary<string, double> Severities, int Score);

/// <summary>
/// Verdict d'une mesure — portage de <c>quality_scan.evaluate</c>, <c>describe</c> et de leurs
/// aides. Le verdict ne se stocke pas : il se rejoue avec les seuils courants et la durée de
/// référence, sans relire le fichier.
/// </summary>
public static class QualityVerdict
{
    /// <summary>HISS_TROUGH_GAP_DB : écart des creux au-delà duquel le bruit n'est pas un souffle.</summary>
    public const double HissTroughGapDb = 12.0;
    /// <summary>HISS_DISPLAY_FLOOR_DB : en deçà, le plancher n'a plus de sens physique (« — »).</summary>
    public const double HissDisplayFloorDb = -120.0;

    /// <summary><c>_severity</c> : 0 au seuil, 1 à seuil + span.</summary>
    public static double Severity(double value, double threshold, double span) =>
        span <= 0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, (value - threshold) / span));

    /// <summary><c>trough_gap</c> : creux du signal entier moins creux de la bande haute ; null si l'un est infini ou NaN.</summary>
    public static double? TroughGap(QualityMeasures m)
    {
        if (!double.IsFinite(m.FullTroughDb) || !double.IsFinite(m.HissTroughDb)) return null;
        return m.FullTroughDb - m.HissTroughDb;
    }

    /// <summary><c>hiss_measurable</c> : sous 20 kHz d'échantillonnage, il n'y a rien au-dessus de 8 kHz.</summary>
    public static bool HissMeasurable(QualityMeasures m) => m.SampleRate == 0 || m.SampleRate >= 2.5 * FfmpegLog.HissHighpassHz;

    /// <summary><c>_is_broadband</c> : bruit large bande, donc vrai souffle (creux indisponible : le plancher fait foi).</summary>
    public static bool IsBroadband(QualityMeasures m) => TroughGap(m) is not { } gap || gap <= HissTroughGapDb;

    /// <summary><c>duration_excess</c> : secondes dont le fichier dépasse sa référence (null si inconnue).</summary>
    public static double? DurationExcess(QualityMeasures m, double? reference) =>
        reference is not { } r || m.Duration == 0 || double.IsNaN(m.Duration) ? null : m.Duration - r;

    /// <summary>
    /// <c>evaluate(measures, thresholds, reference)</c>. Sans référence, ni la durée ni les blancs
    /// ne sont mis en cause ; les blancs ne le sont que si le fichier dépasse le morceau.
    /// <paramref name="includeCrackle"/> à faux écarte les craquements (analyse rapide demandée :
    /// une mesure approfondie en cache ne doit pas abaisser une note sans défaut visible).
    /// </summary>
    public static Verdict Evaluate(QualityMeasures m, QualityLimits? limits = null, double? reference = null, bool includeCrackle = true)
    {
        limits ??= QualityLimits.Default;
        var defects = new List<string>();
        var severities = new Dictionary<string, double>();

        var hiss = m.HissDb;
        if (!double.IsNaN(hiss) && hiss > limits.HissDb && HissMeasurable(m) && IsBroadband(m))
        {
            defects.Add(Defects.Hiss);
            severities[Defects.Hiss] = Severity(hiss, limits.HissDb, 15.0);
        }

        if (includeCrackle && m.CracklePct is { } crackle && crackle > limits.CracklePct)
        {
            defects.Add(Defects.Crackle);
            severities[Defects.Crackle] = Severity(crackle, limits.CracklePct, limits.CracklePct * 8);
        }

        if (m.ClippingPct > limits.ClippingPct)
        {
            defects.Add(Defects.Clipping);
            severities[Defects.Clipping] = Severity(m.ClippingPct, limits.ClippingPct, limits.ClippingPct * 8);
        }

        var density = 100.0 * (double.IsNaN(m.GapDensity) ? double.NaN : m.GapDensity);
        if (density > limits.GapDensityPct)
        {
            defects.Add(Defects.Gaps);
            severities[Defects.Gaps] = Severity(density, limits.GapDensityPct, 25.0);
        }

        var excess = DurationExcess(m, reference);
        if (excess is { } e1 && Math.Abs(e1) > limits.DurationGap)
        {
            defects.Add(Defects.Duration);
            severities[Defects.Duration] = Severity(Math.Abs(e1), limits.DurationGap, 30.0);
        }

        if (excess is { } e2 && e2 > limits.DurationExcess)
        {
            if (m.TailSeconds > limits.TailSeconds)
            {
                defects.Add(Defects.Tail);
                severities[Defects.Tail] = Severity(m.TailSeconds, limits.TailSeconds, 20.0);
            }
            if (m.HeadSeconds > limits.HeadSeconds)
            {
                defects.Add(Defects.Head);
                severities[Defects.Head] = Severity(m.HeadSeconds, limits.HeadSeconds, 8.0);
            }
        }

        var penalty = 0.0;
        foreach (var d in defects) penalty += 12 + 20 * severities[d];
        var score = Math.Max(0, (int)Math.Round(100 - penalty, MidpointRounding.ToEven));
        return new Verdict(defects, severities, score);
    }

    /// <summary><c>describe(measures, defect)</c> : texte court chiffrant un défaut (point décimal).</summary>
    public static string Describe(QualityMeasures m, string defect) => defect switch
    {
        Defects.Hiss => double.IsNaN(m.HissDb) || m.HissDb <= HissDisplayFloorDb ? "—" : Py.F(m.HissDb, 0) + " dB",
        Defects.Crackle => m.CracklePct is { } c ? Py.F(c, 4) + " %" : "—",
        Defects.Clipping => Py.F(m.ClippingPct, 3) + " %",
        Defects.Gaps => m.GapCount == 0 ? "—" : $"{m.GapCount} · {Py.F(100.0 * m.GapDensity, 0)} %",
        Defects.Tail => Py.F(m.TailSeconds, 0) + " s",
        Defects.Head => Py.F(m.HeadSeconds, 0) + " s",
        _ => "",
    };
}

/// <summary>Formatage « à la C » de Python (<c>%.Nf</c>, <c>%+.0f</c>) : arrondi au pair sur la valeur binaire exacte.</summary>
public static class Py
{
    public static string F(double value, int digits)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        return PythonFloat.Round(value, digits).ToString("F" + digits, CultureInfo.InvariantCulture);
    }

    /// <summary><c>%+.Nf</c> : signe toujours écrit.</summary>
    public static string Signed(double value, int digits)
    {
        var text = F(value, digits);
        return text.StartsWith('-') ? text : "+" + text;
    }

    /// <summary><c>int(round(x))</c>.</summary>
    public static long RoundInt(double value) => (long)Math.Round(value, MidpointRounding.ToEven);
}
