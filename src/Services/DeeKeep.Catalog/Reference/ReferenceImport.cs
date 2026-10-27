using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Jobs;
using Microsoft.Extensions.Options;

namespace DeeKeep.Catalog.Reference;

/// <summary>
/// Import ponctuel de <c>reference_cache.json</c> (cache de l'application desktop) dans
/// <c>reference_durations</c> (<c>collect_references</c>). Les clés déjà en base sont
/// conservées : seules les absentes sont ajoutées. Idempotent.
/// </summary>
public sealed class ReferenceImport(ReferenceStore store, IOptions<ImportOptions> options) : IJobHandler
{
    public Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var path = Path.Combine(options.Value.ResolveDesktopDataDir(), "reference_cache.json");
        if (!File.Exists(path)) throw new PermanentJobException($"Fichier introuvable : {path}");
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Object)
            throw new PermanentJobException("reference_cache.json : format inattendu");
        var known = store.Keys();
        var batch = new List<(string, IReadOnlyList<double>, double)>();
        var total = entries.EnumerateObject().Count();
        int done = 0, added = 0;
        foreach (var entry in entries.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            done++;
            if (entry.Value.ValueKind != JsonValueKind.Object || known.Contains(entry.Name)) continue;
            var durations = entry.Value.TryGetProperty("durations", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToList()
                : [];
            var time = entry.Value.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 0;
            batch.Add((entry.Name, durations, time));
            if (batch.Count < 500) continue;
            added += store.PutMany(batch);
            batch.Clear();
            context.Report(done, total, $"{added} durée(s) importée(s)");
        }
        if (batch.Count > 0) added += store.PutMany(batch);
        context.Report(total, total, $"{added} durée(s) importée(s) sur {total}", force: true);
        return Task.FromResult(JsonSerializer.SerializeToNode(new { total, added }));
    }
}
