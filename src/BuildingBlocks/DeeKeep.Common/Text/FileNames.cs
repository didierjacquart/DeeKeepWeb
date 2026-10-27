using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace DeeKeep.Common.Text;

/// <summary>
/// Noms de fichiers et durées affichées — portage de <c>deekeep/core/utils.py:150-163</c>
/// (<c>sanitize_filename</c>, <c>format_duration</c>) et de <c>library_rules.safe_component</c>.
/// </summary>
public static partial class FileNames
{
    [GeneratedRegex(@"[\\/*?:""<>|]")]
    private static partial Regex ForbiddenRe();

    [GeneratedRegex(@"[\x00-\x1f]+")]
    private static partial Regex ControlRe();

    /// <summary>
    /// <c>sanitize_filename</c> : retire <c>\ / * ? : " &lt; &gt; |</c> sans les remplacer
    /// (« AC/DC » → « ACDC »). Rien d'autre : ni compactage, ni noms réservés Windows.
    /// </summary>
    public static string SanitizeFilename(string? name) => ForbiddenRe().Replace(name ?? "", "");

    /// <summary>
    /// <c>safe_component</c> : <see cref="SanitizeFilename"/>, puis chaque suite de caractères
    /// de contrôle devient une espace, blancs de tête et de fin retirés, puis points et
    /// espaces finaux (interdits sous Windows). Les espaces internes ne sont PAS compactées :
    /// « Whip / Nae Nae » → « Whip  Nae Nae », comme le nom qu'aurait donné le téléchargement.
    /// </summary>
    public static string SafeComponent(string? name)
    {
        var cleaned = ControlRe().Replace(SanitizeFilename(name), " ");
        return PyText.Strip(cleaned).TrimEnd('.', ' ');
    }

    /// <summary><c>format_duration</c> : secondes → « mm:ss » (minutes non bornées), « --:-- » si inexploitable.</summary>
    public static string FormatDuration(double? seconds)
    {
        if (seconds is not { } value || double.IsNaN(value) || double.IsInfinity(value)) return "--:--";
        return Format(new BigInteger(Math.Truncate(value)));     // int() tronque vers zéro
    }

    /// <inheritdoc cref="FormatDuration(double?)"/>
    public static string FormatDuration(long seconds) => Format(seconds);

    /// <summary>
    /// Variante pour une durée textuelle : accepte ce qu'accepte <c>int(str)</c>
    /// (« 185 », « +42 », « 1_000 », blancs autour), refuse « 3.5 » comme lui.
    /// </summary>
    public static string FormatDuration(string? text) =>
        TryParsePythonInt(text, out var value) ? Format(value) : "--:--";

    private static string Format(BigInteger secs)
    {
        // Division entière de Python : arrondie vers -∞, reste toujours positif.
        var mins = BigInteger.DivRem(secs, 60, out var rest);
        if (rest < 0) { mins -= 1; rest += 60; }
        // f"{mins:02d}" : deux chiffres au moins, signe compris (« -1 »).
        var minText = mins < 0 ? "-" + (-mins).ToString(CultureInfo.InvariantCulture) : mins.ToString("00", CultureInfo.InvariantCulture);
        return $"{minText}:{rest.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static bool TryParsePythonInt(string? text, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (text is null) return false;
        var s = PyText.Strip(text);
        var i = 0;
        var negative = false;
        if (s.Length > 0 && s[0] is '+' or '-') { negative = s[0] == '-'; i = 1; }
        if (i >= s.Length) return false;
        var previousUnderscore = true;   // pas de « _ » en tête
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '_')
            {
                if (previousUnderscore) return false;
                previousUnderscore = true;
                continue;
            }
            var digit = CharUnicodeInfo.GetDecimalDigitValue(c);
            if (digit < 0 || !char.IsDigit(c)) return false;
            value = value * 10 + digit;
            previousUnderscore = false;
        }
        if (previousUnderscore) return false;   // ni « _ » final
        if (negative) value = -value;
        return true;
    }
}
