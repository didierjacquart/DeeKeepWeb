using System.Globalization;
using System.Text.Json;

namespace DeeKeep.Catalog.Deezer;

/// <summary>Lecture tolérante des documents Deezer (champs absents, nuls ou de type inattendu).</summary>
public static class DeezerJson
{
    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    /// <summary>Texte non vide, ou null (<c>x.get(name) or None</c>).</summary>
    public static string? Str(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        var text = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => null,
        };
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static long? Long(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt64(out var l)) return l;
            if (v.TryGetDouble(out var d)) return (long)d;
        }
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    public static bool Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Identifiant en texte (<c>str(x.get("id") or "")</c>).</summary>
    public static string IdText(this JsonElement e, string name = "id") => e.Long(name)?.ToString(CultureInfo.InvariantCulture) ?? e.Str(name) ?? "";
}
