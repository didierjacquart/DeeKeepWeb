using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;

namespace DeeKeep.Quality.Analysis;

/// <summary>
/// Mesures brutes d'un fichier (<c>quality_scan.measure</c>) : une lecture ffmpeg, dont le
/// verdict se rejoue à volonté (<see cref="QualityVerdict.Evaluate"/>) sans relire le fichier.
/// </summary>
/// <remarks>
/// Les valeurs « neutres » sont celles de l'original (<c>empty</c>) : un fichier illisible ne
/// doit faire accuser aucun défaut. ±∞ et NaN sont des valeurs normales (silence numérique) :
/// elles ne sont jamais « nettoyées », elles participent au calcul de l'écart des creux.
/// </remarks>
public sealed record QualityMeasures
{
    public double Duration { get; init; }
    public int SampleRate { get; init; }
    public double PeakDb { get; init; } = double.NaN;
    public double ClippingPct { get; init; }
    public double FlatFactor { get; init; }
    public double HissDb { get; init; } = double.NegativeInfinity;
    public double HissTroughDb { get; init; } = double.NegativeInfinity;
    public double FullTroughDb { get; init; } = double.NegativeInfinity;

    /// <summary>Part d'échantillons signalés par adeclick ; null en analyse rapide (« non mesuré »).</summary>
    public double? CracklePct { get; init; }

    public int GapCount { get; init; }
    public double GapSeconds { get; init; }

    /// <summary>Fraction (0..1) de silence numérique du passage de dix secondes le plus troué.</summary>
    public double GapDensity { get; init; }

    public double HeadSeconds { get; init; }
    public double TailSeconds { get; init; }
    public bool Deep { get; init; }
    public string Error { get; init; } = "";

    public bool HasError => Error.Length > 0;

    public static QualityMeasures Empty(bool deep, string error = "") => new() { Deep = deep, Error = error };

    // ------------------------------------------------------------------ JSON (colonne quality.measures)

    /// <summary>
    /// <c>_encode_measures</c> : ±∞ → « -inf » / « inf », NaN → null ; entiers pour
    /// <c>sample_rate</c> et <c>gap_count</c>, booléen pour <c>deep</c>. À écrire par
    /// <see cref="SettingsJson.Dump(JsonNode?)"/> (compact, clés triées, flottants à la Python).
    /// </summary>
    public JsonObject ToJson() => new()
    {
        ["clipping_pct"] = Num(ClippingPct),
        ["crackle_pct"] = CracklePct is { } c ? Num(c) : null,
        ["deep"] = Deep,
        ["duration"] = Num(Duration),
        ["error"] = Error,
        ["flat_factor"] = Num(FlatFactor),
        ["full_trough_db"] = Num(FullTroughDb),
        ["gap_count"] = (long)GapCount,
        ["gap_density"] = Num(GapDensity),
        ["gap_seconds"] = Num(GapSeconds),
        ["head_seconds"] = Num(HeadSeconds),
        ["hiss_db"] = Num(HissDb),
        ["hiss_trough_db"] = Num(HissTroughDb),
        ["peak_db"] = Num(PeakDb),
        ["sample_rate"] = (long)SampleRate,
        ["tail_seconds"] = Num(TailSeconds),
    };

    /// <summary>Texte de la colonne <c>quality.measures</c>.</summary>
    public string ToJsonText() => SettingsJson.Dump(ToJson());

    private static JsonNode? Num(double value) =>
        double.IsNaN(value) ? null
        : double.IsNegativeInfinity(value) ? JsonValue.Create("-inf")
        : double.IsPositiveInfinity(value) ? JsonValue.Create("inf")
        : JsonValue.Create(value);

    /// <summary>
    /// <c>_decode_measures</c>, tolérant : chaînes « -inf » / « inf » sur toutes les clés,
    /// <c>peak_db</c> absent ou null → NaN, clés absentes → valeurs neutres. Null si le texte
    /// n'est pas un objet JSON.
    /// </summary>
    public static QualityMeasures? FromJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        JsonObject? obj;
        try
        {
            obj = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        return obj is null ? null : FromJson(obj);
    }

    public static QualityMeasures FromJson(JsonObject o) => new()
    {
        Duration = Dbl(o, "duration", 0.0),
        SampleRate = (int)Dbl(o, "sample_rate", 0.0),
        PeakDb = Dbl(o, "peak_db", double.NaN),
        ClippingPct = Dbl(o, "clipping_pct", 0.0),
        FlatFactor = Dbl(o, "flat_factor", 0.0),
        HissDb = Dbl(o, "hiss_db", double.NegativeInfinity),
        HissTroughDb = Dbl(o, "hiss_trough_db", double.NegativeInfinity),
        FullTroughDb = Dbl(o, "full_trough_db", double.NegativeInfinity),
        CracklePct = o["crackle_pct"] is null ? null : Dbl(o, "crackle_pct", double.NaN),
        GapCount = (int)Dbl(o, "gap_count", 0.0),
        GapSeconds = Dbl(o, "gap_seconds", 0.0),
        GapDensity = Dbl(o, "gap_density", 0.0),
        HeadSeconds = Dbl(o, "head_seconds", 0.0),
        TailSeconds = Dbl(o, "tail_seconds", 0.0),
        Deep = o["deep"] is JsonValue v && v.TryGetValue<bool>(out var b) && b,
        Error = o["error"] is JsonValue e && e.TryGetValue<string>(out var s) ? s : "",
    };

    /// <summary>
    /// Valeur numérique d'une clé : absente → défaut ; null → NaN (encodage d'un NaN), sauf
    /// pour les clés dont le défaut est neutre et non numérique ; chaînes ±inf reconnues.
    /// </summary>
    private static double Dbl(JsonObject o, string key, double fallback)
    {
        if (!o.TryGetPropertyValue(key, out var node)) return fallback;
        if (node is null) return key == "peak_db" ? double.NaN : fallback is 0.0 ? 0.0 : double.NaN;
        if (node is not JsonValue value) return fallback;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<string>(out var text))
        {
            return text switch
            {
                "-inf" => double.NegativeInfinity,
                "inf" => double.PositiveInfinity,
                _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback,
            };
        }
        if (value.TryGetValue<bool>(out var flag)) return flag ? 1 : 0;
        return fallback;
    }
}
