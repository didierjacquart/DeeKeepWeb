using System.Globalization;
using System.Numerics;

namespace DeeKeep.Common.Data;

/// <summary>
/// Reproduit à l'octet près <c>repr(round(x, 6))</c> de Python, utilisé par
/// l'empreinte de ligne de la base existante pour les colonnes REAL.
/// </summary>
public static class PythonFloat
{
    /// <summary><c>round(x, ndigits)</c> : arrondi correct au demi pair sur la valeur binaire exacte.</summary>
    public static double Round(double value, int ndigits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0) return value;
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++;
        else mantissa |= 1L << 52;
        exponent -= 1075;

        var scale = BigInteger.Pow(10, ndigits);
        BigInteger rounded;
        if (exponent >= 0)
        {
            rounded = new BigInteger(mantissa) * BigInteger.Pow(2, exponent) * scale;
        }
        else
        {
            var numerator = new BigInteger(mantissa) * scale;
            var denominator = BigInteger.Pow(2, -exponent);
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            var twice = remainder * 2;
            var cmp = twice.CompareTo(denominator);
            if (cmp > 0 || (cmp == 0 && !quotient.IsEven)) quotient += 1;
            rounded = quotient;
        }

        var digits = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(ndigits + 1, '0');
        var text = digits[..^ndigits] + "." + digits[^ndigits..];
        var result = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -result : result;
    }

    /// <summary><c>repr(float)</c> de Python (représentation la plus courte, règles d'exposant de CPython).</summary>
    public static string Repr(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (value == 0) return BitConverter.DoubleToInt64Bits(value) < 0 ? "-0.0" : "0.0";

        // Chiffres significatifs les plus courts qui redonnent la même valeur.
        var r = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        string digits;
        int decpt;
        {
            var s = r.ToUpperInvariant();
            var exp = 0;
            var e = s.IndexOf('E');
            if (e >= 0)
            {
                exp = int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture);
                s = s[..e];
            }
            var dot = s.IndexOf('.');
            var intPart = dot >= 0 ? s[..dot] : s;
            var fracPart = dot >= 0 ? s[(dot + 1)..] : "";
            var all = intPart + fracPart;
            var lead = all.Length - all.TrimStart('0').Length;
            digits = all.Trim('0');
            if (digits.Length == 0) digits = "0";
            decpt = intPart.Length - lead + exp;
        }

        var sign = value < 0 ? "-" : "";
        if (decpt <= -4 || decpt > 16)
        {
            var mant = digits.Length == 1 ? digits : digits[0] + "." + digits[1..];
            var exp = decpt - 1;
            return sign + mant + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
        }
        if (decpt <= 0) return sign + "0." + new string('0', -decpt) + digits;
        if (decpt < digits.Length) return sign + digits[..decpt] + "." + digits[decpt..];
        return sign + digits + new string('0', decpt - digits.Length) + ".0";
    }
}
