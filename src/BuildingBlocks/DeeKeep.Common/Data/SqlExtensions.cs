using System.Data;
using Dapper;

namespace DeeKeep.Common.Data;

public static class SqlExtensions
{
    /// <summary>Lignes sous forme de dictionnaires (valeurs SQLite brutes : long, double, string, byte[], null).</summary>
    public static IEnumerable<Dictionary<string, object?>> QueryRows(this IDbConnection conn, string sql, object? param = null, IDbTransaction? tx = null)
    {
        foreach (IDictionary<string, object?> row in conn.Query(sql, param, tx))
            yield return row.ToDictionary(kv => kv.Key, kv => kv.Value is DBNull ? null : kv.Value);
    }
}

/// <summary>Accès typé tolérant aux valeurs d'une ligne brute.</summary>
public static class RowValues
{
    public static string? Str(this IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static long? Long(this IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null && v is not string { Length: 0 }
            ? Convert.ToInt64(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture))
            : null;

    public static double? Dbl(this IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null && v is not string { Length: 0 }
            ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)
            : null;
}
