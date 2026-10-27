using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.Settings;
using DeeKeep.Library.Tracks;

namespace DeeKeep.Library.Scan;

/// <summary>
/// Import de l'application desktop (<c>library.import</c>) : lit <c>deekeep_config.json</c>
/// (réglages, historique avec jaquettes <c>cover_b64</c>) et les reporte dans la base
/// (<c>collect_settings</c>, <c>merge_history</c> / <c>_mirror_history</c>). Les recherches
/// appartiennent au service Catalog : elles ne sont pas importées ici.
/// </summary>
public sealed class DesktopImportHandler(
    DeeKeepDb db,
    LibraryPaths paths,
    SettingsStore settings,
    TrackWriter writer,
    DiskStateCache disk,
    EventLog events,
    IConfiguration configuration,
    ILogger<DesktopImportHandler> logger) : IJobHandler
{
    public string DefaultDataDir() =>
        configuration["Import:DesktopDataDir"] is { Length: > 0 } c
            ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeeKeep");

    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.Job.Payload is null ? new DesktopImportRequest() : context.GetPayload<DesktopImportRequest>();
        var dir = string.IsNullOrWhiteSpace(request.DataDir) ? DefaultDataDir() : request.DataDir;
        var file = Path.Combine(dir, "deekeep_config.json");
        if (!File.Exists(file)) throw new PermanentJobException($"Fichier introuvable : {file}");
        JsonObject config;
        try
        {
            config = JsonNode.Parse(await File.ReadAllTextAsync(file, ct)) as JsonObject ?? throw new JsonException("objet attendu");
        }
        catch (JsonException ex)
        {
            throw new PermanentJobException($"{file} illisible : {ex.Message}");
        }

        var importedSettings = 0;
        if (request.Settings)
        {
            context.Message("Réglages…");
            var values = new Dictionary<string, JsonNode?>();
            foreach (var (k, v) in config)
                if (!SettingsStore.NonSettingKeys.Contains(k) && !SettingsStore.SecretKeys.Contains(k) && k != "cloud_sync_secrets") values[k] = v?.DeepClone();
            importedSettings = settings.Set(values).Changed.Count;
        }

        var importedHistory = 0;
        if (request.History && config["downloaded_history"] is JsonArray history)
            importedHistory = ImportHistory(history, context, ct);

        disk.InvalidateAll();
        events.Publish(LibraryEvents.LibraryChanged, new { reason = "import" });
        var message = $"{importedSettings} réglage(s) modifié(s), {importedHistory} entrée(s) d'historique importée(s)";
        logger.LogInformation("Import desktop : {Message}", message);
        context.Report(1, 1, message, force: true);
        return new JsonObject { ["settings"] = importedSettings, ["history"] = importedHistory, ["message"] = message };
    }

    private int ImportHistory(JsonArray history, JobContext context, CancellationToken ct)
    {
        // Dédoublonné par chemin (casse ignorée), rang d'origine conservé (iter_history).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<(int Position, JsonObject Entry)>();
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i] is not JsonObject entry) continue;
            var key = LibraryText.MatchKey(Str(entry, "path"));
            if (seen.Add(key)) items.Add((i, entry));
        }

        List<Dictionary<string, object?>> all;
        using (var conn = db.Open(readOnly: true)) all = conn.QueryRows("SELECT * FROM tracks").ToList();
        var byKey = all.GroupBy(r => LibraryPaths.Key(r.Str("path") ?? "")).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Long("deleted") ?? 0).First(), StringComparer.Ordinal);
        var byName = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var row in all.Where(r => (r.Long("deleted") ?? 0) == 0))
        {
            var name = PyPath.Basename(row.Str("path")).ToLowerInvariant();
            if (!byName.TryGetValue(name, out var list)) byName[name] = list = [];
            list.Add(row);
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var covers = new List<CoverRow>();
        var rows = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        var done = 0;
        foreach (var (position, entry) in items)
        {
            ct.ThrowIfCancellationRequested();
            var raw = Str(entry, "path");
            var canonical = LibraryText.CanonicalPath(raw);
            var current = byKey.GetValueOrDefault(LibraryText.MatchKey(raw));
            if (current is null || (current.Long("deleted") ?? 0) != 0)
            {
                var free = byName.GetValueOrDefault(PyPath.Basename(canonical).ToLowerInvariant())?.Where(r => !claimed.Contains(LibraryPaths.Key(r.Str("path")!))).ToList() ?? [];
                current = free.Count == 1 ? free[0] : null;
            }
            var cover = CoverRow.FromBase64(Str(entry, "cover_b64"));
            if (cover is not null) covers.Add(cover);
            var row = TrackWriter.Content(current ?? new Dictionary<string, object?>());
            row["path"] = current?.Str("path") ?? canonical;
            claimed.Add(LibraryPaths.Key(row.Str("path")!));
            foreach (var (field, value) in HistoryTags(entry)) if (string.IsNullOrEmpty(row.Str(field))) row[field] = value;
            foreach (var f in new[] { "title", "artist", "album" }) if (string.IsNullOrEmpty(row.Str(f))) row[f] = Str(entry, f);
            row["in_history"] = 1L;
            row["history_pos"] = (long)position;
            row["history_cover_sha1"] = cover?.Sha1 ?? row.Str("history_cover_sha1");
            if (string.IsNullOrEmpty(row.Str("source"))) row["source"] = "history";
            if (row.Str("source") is "history" or "library" && File.Exists(paths.ToLocal(row.Str("path")!))) row["source"] = "both";
            if (string.IsNullOrEmpty(row.Str("ext"))) row["ext"] = PyPath.SplitExt(raw).Ext.ToLowerInvariant().TrimStart('.');
            if (string.IsNullOrEmpty(row.Str("cover_sha1"))) row["cover_sha1"] = cover?.Sha1;
            rows[row.Str("path")!] = row;
            context.Report(++done, items.Count, $"Historique : {done}/{items.Count}");
        }
        writer.Write(covers, rows.Values.ToList());
        events.Publish(EventTypes.HistoryChanged, new TracksChangedEvent(rows.Keys.ToList(), "import"));
        events.Publish(EventTypes.TracksChanged, new TracksChangedEvent(rows.Keys.ToList(), "import"));
        return rows.Count;
    }

    /// <summary><c>history_tags</c> : tags complémentaires notés au téléchargement, en colonnes de table.</summary>
    public static Dictionary<string, string> HistoryTags(JsonObject entry)
    {
        var values = new Dictionary<string, string>();
        if (entry["tags"] is not JsonObject tags) return values;
        foreach (var (field, column) in new[] { ("albumartist", "album_artist"), ("genre", "genre"), ("date", "year") })
            if (PyText.Strip(Str(tags, field)) is { Length: > 0 } t) values[column] = t;
        var number = PyText.Strip(Str(tags, "track"));
        var total = PyText.Strip(Str(tags, "tracktotal"));
        if (number.Length > 0) values["track_no"] = total.Length > 0 ? $"{number}/{total}" : number;
        return values;
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToString()) : "";
}
