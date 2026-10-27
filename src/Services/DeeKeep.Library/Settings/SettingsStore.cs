using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Contracts;

namespace DeeKeep.Library.Settings;

/// <summary>
/// Réglages de l'application (table <c>app_settings</c>, une ligne par clé, valeur en JSON
/// canonique) — équivalent web de <c>settings.default_settings</c> + <c>collect_settings</c>
/// + <c>LocalDB.set_settings</c>. La base est la source de vérité ; les défauts comblent
/// les clés absentes à la lecture.
/// </summary>
public sealed class SettingsStore(DeeKeepDb db, EventLog events, IConfiguration configuration, ILogger<SettingsStore> logger)
{
    /// <summary>Clés qui ne sont pas des réglages : elles ont leur propre table.</summary>
    public static readonly IReadOnlySet<string> NonSettingKeys = new HashSet<string>(StringComparer.Ordinal) { "search_history", "downloaded_history" };

    /// <summary>Secrets : ils viennent de la configuration du serveur, jamais de la base.</summary>
    public static readonly IReadOnlySet<string> SecretKeys = new HashSet<string>(StringComparer.Ordinal) { "arl", "acoustid_key" };

    private readonly object _lock = new();

    /// <summary>
    /// Défauts (spec 05 §5.2, <c>default_settings</c> puis seuils <c>qual_*</c> de
    /// <c>quality_scan.DEFAULT_THRESHOLDS</c>). Les flottants restent des flottants
    /// (« -65.0 ») : un changement de type changerait la forme JSON, donc l'empreinte.
    /// </summary>
    public Dictionary<string, JsonNode?> Defaults() => new(StringComparer.Ordinal)
    {
        ["download_dir"] = JsonValue.Create(DefaultDownloadDir()),
        ["file_format"] = JsonValue.Create("MP3"),
        ["quality"] = JsonValue.Create("320 kbps (HQ)"),
        ["org_artist"] = JsonValue.Create(true),
        ["org_album"] = JsonValue.Create(true),
        ["org_divers"] = JsonValue.Create(false),
        ["org_divers_min"] = JsonValue.Create(3),
        ["org_rename"] = JsonValue.Create(false),
        ["org_pattern"] = JsonValue.Create("%titre% - %artiste%"),
        ["org_remove_empty"] = JsonValue.Create(true),
        ["org_report_untagged"] = JsonValue.Create(true),
        ["org_keep_albums"] = JsonValue.Create(true),
        ["org_dedup"] = JsonValue.Create(true),
        ["org_filters_open"] = JsonValue.Create(false),
        ["youtube_browser"] = JsonValue.Create("Aucun (anonyme)"),
        ["cookies_file"] = JsonValue.Create(""),
        ["qual_hiss_db"] = JsonValue.Create(-65.0),
        ["qual_clipping_pct"] = JsonValue.Create(0.2),
        ["qual_crackle_pct"] = JsonValue.Create(0.0025),
        ["qual_gap_density_pct"] = JsonValue.Create(30.0),
        ["qual_tail_seconds"] = JsonValue.Create(5.0),
        ["qual_head_seconds"] = JsonValue.Create(3.0),
        ["qual_duration_excess"] = JsonValue.Create(3.0),
        ["qual_duration_gap"] = JsonValue.Create(10.0),
        ["qual_deep"] = JsonValue.Create(true),
        ["mirror_bitrate"] = JsonValue.Create(128),
    };

    /// <summary>Dossier de dépôt par défaut : configuration <c>Library:DefaultDownloadDir</c>, sinon <c>~/Music</c>.</summary>
    public string DefaultDownloadDir() =>
        configuration["Library:DefaultDownloadDir"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");

    /// <summary>Réglages effectifs : défauts, recouverts par ce que la base contient.</summary>
    public Dictionary<string, JsonNode?> GetAll()
    {
        var result = Defaults();
        foreach (var (key, value) in db.AllSettings())
        {
            if (SecretKeys.Contains(key) || NonSettingKeys.Contains(key)) continue;
            result[key] = value;
        }
        return result;
    }

    public JsonNode? Get(string key) => GetAll().GetValueOrDefault(key);

    public string GetString(string key, string fallback = "") =>
        Get(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    /// <summary>Dossier de dépôt tel que stocké (forme de la base, ex. « D:/ »).</summary>
    public string DownloadDir() => GetString("download_dir", DefaultDownloadDir()) is { Length: > 0 } dir ? dir : DefaultDownloadDir();

    /// <summary>
    /// Enregistre des valeurs (<c>set_settings</c>) : seules les clés dont le JSON canonique
    /// change sont réécrites. Secrets et historiques sont ignorés. Publie <c>settings.changed</c>
    /// avec les réglages complets si quelque chose a changé.
    /// </summary>
    public (Dictionary<string, JsonNode?> All, IReadOnlyList<string> Changed) Set(IReadOnlyDictionary<string, JsonNode?> values)
    {
        List<string> changed;
        lock (_lock)
        {
            var stored = StoredRaw();
            var defaults = Defaults();
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            changed = [];
            foreach (var (key, raw) in values)
            {
                if (string.IsNullOrWhiteSpace(key) || SecretKeys.Contains(key) || NonSettingKeys.Contains(key)) continue;
                var reference = stored.TryGetValue(key, out var s) ? SettingsJson.Parse(s) : defaults.GetValueOrDefault(key);
                var value = CoerceLike(raw, reference);
                var text = SettingsJson.Dump(value);
                if (stored.TryGetValue(key, out var current) && current == text) continue;
                rows.Add(new Dictionary<string, object?> { ["key"] = key, ["value"] = text });
                changed.Add(key);
            }
            if (rows.Count > 0) db.UpsertMany(Schema.AppSettings, rows);
        }
        var all = GetAll();
        if (changed.Count > 0)
        {
            logger.LogInformation("Réglages modifiés : {Keys}", string.Join(", ", changed));
            events.Publish(EventTypes.SettingsChanged, new JsonObject
            {
                ["changed"] = new JsonArray(changed.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
                ["values"] = ToObject(all),
            });
        }
        return (all, changed);
    }

    public static JsonObject ToObject(IReadOnlyDictionary<string, JsonNode?> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal)) obj[key] = value?.DeepClone();
        return obj;
    }

    /// <summary>Valeurs brutes (JSON canonique) des lignes vivantes.</summary>
    private Dictionary<string, string?> StoredRaw()
    {
        using var conn = db.Open(readOnly: true);
        return conn.QueryRows("SELECT key, value FROM app_settings WHERE deleted = 0")
            .ToDictionary(r => (string)r["key"]!, r => r["value"] as string, StringComparer.Ordinal);
    }

    /// <summary>
    /// Donne à <paramref name="value"/> le type numérique de la valeur de référence : un
    /// client JavaScript envoie « -65 » pour -65.0, qu'il faut réécrire « -65.0 » comme le
    /// Python, sans quoi l'empreinte de la ligne changerait à chaque enregistrement.
    /// </summary>
    public static JsonNode? CoerceLike(JsonNode? value, JsonNode? reference)
    {
        if (value is not JsonValue v || reference is not JsonValue r) return value?.DeepClone();
        // Raisonne sur le texte JSON : le type CLR d'un nombre relu dépend de sa source (JsonElement, int, double).
        var text = v.ToJsonString();
        var refText = r.ToJsonString();
        if (!IsNumber(text) || !IsNumber(refText)) return value.DeepClone();
        var d = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (IsFloat(r, refText)) return JsonValue.Create(d);
        return d == Math.Truncate(d) && Math.Abs(d) < 9e15 ? JsonValue.Create((long)d) : value.DeepClone();
    }

    // Un flottant rond (« -65.0 ») s'imprime « -65 » en .NET : le type CLR, ou le texte source, en décide.
    private static bool IsFloat(JsonValue value, string text) =>
        value.GetValue<object>() switch
        {
            double or float or decimal => true,
            JsonElement e => e.GetRawText().AsSpan().IndexOfAny('.', 'e', 'E') >= 0,
            _ => text.AsSpan().IndexOfAny('.', 'e', 'E') >= 0,
        };

    private static bool IsNumber(string json) =>
        json.Length > 0 && (char.IsDigit(json[0]) || json[0] == '-') && double.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
}
