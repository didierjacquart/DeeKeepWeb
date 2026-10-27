using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DeeKeep.Common.Data;

/// <summary>
/// Empreinte de ligne sur 30 bits, identique à <c>db_schema.row_hash</c> :
/// BLAKE2b-64 du nom de table puis, pour chaque colonne de contenu,
/// <c>0x1F</c> + valeur canonique ; enfin <c>0x1E</c> + drapeau de suppression.
/// Les horodatages n'y entrent pas.
/// </summary>
public static class RowHash
{
    public const long HashMask = (1L << 30) - 1;

    public static long Compute(TableSpec spec, IReadOnlyDictionary<string, object?> row)
    {
        var digest = new Blake2b(8);
        digest.Update(Encoding.UTF8.GetBytes(spec.Name));
        foreach (var column in spec.ContentColumns)
        {
            digest.Update([0x1f]);
            digest.Update(Canonical(row.TryGetValue(column, out var v) ? v : null));
        }
        digest.Update([0x1e]);
        var deleted = row.TryGetValue("deleted", out var d) && IsTruthy(d);
        digest.Update(deleted ? "1"u8 : "0"u8);
        var bytes = digest.Final();
        var value = 0UL;
        foreach (var b in bytes) value = (value << 8) | b;
        return (long)(value & HashMask);
    }

    /// <summary>Représentation stable d'une valeur (portage de <c>_canonical</c>).</summary>
    public static byte[] Canonical(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return [0x00];
            case bool b:
                return b ? "b1"u8.ToArray() : "b0"u8.ToArray();
            case double or float or decimal:
            {
                var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Truncate(d))
                    return Encoding.ASCII.GetBytes("i" + new System.Numerics.BigInteger(d).ToString(CultureInfo.InvariantCulture));
                return Encoding.ASCII.GetBytes("f" + PythonFloat.Repr(PythonFloat.Round(d, 6)));
            }
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Encoding.ASCII.GetBytes("i" + Convert.ToString(value, CultureInfo.InvariantCulture));
            case byte[] bytes:
                return [(byte)'x', .. SHA1.HashData(bytes)];
            case ReadOnlyMemory<byte> mem:
                return [(byte)'x', .. SHA1.HashData(mem.Span)];
            default:
                return [(byte)'s', .. Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")];
        }
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null or DBNull => false,
        bool b => b,
        string s => s.Length > 0,
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0,
    };
}
