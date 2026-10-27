using System.Text.Json.Nodes;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Data;

namespace DeeKeep.Quality.Jobs;

/// <summary>
/// Import de <c>quality_cache.json</c> de l'application desktop (version 5) dans la table
/// <c>quality</c>. Les clés (minuscules) sont rapprochées de <c>tracks</c> pour retrouver la
/// casse réelle ; les corbeilles et dossiers système (« $RECYCLE.BIN »…) sont écartés. La colonne
/// <c>defects</c> est recalculée (sans référence), pas copiée.
/// </summary>
public sealed class ImportHandler(QualityStore store, IConfiguration configuration) : IJobHandler
{
    public static string DataDir(IConfiguration c) =>
        c["Import:DesktopDataDir"] is { Length: > 0 } d ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeeKeep");

    public Task<JsonNode?> ExecuteAsync(JobContext ctx, CancellationToken ct) => Task.Run<JsonNode?>(() =>
    {
        var file = Path.Combine(DataDir(configuration), "quality_cache.json");
        if (!File.Exists(file)) throw new PermanentJobException($"Fichier introuvable : {file}");
        JsonObject? entries;
        using (var stream = File.OpenRead(file))
        {
            var root = JsonNode.Parse(stream) as JsonObject;
            if ((int?)root?["version"] != 5) throw new PermanentJobException("Version de cache non reconnue (5 attendue)");
            entries = root["entries"] as JsonObject;
        }
        if (entries is null) throw new PermanentJobException("Cache vide");

        var tracks = store.TrackPaths();
        var known = store.Measures();
        var batch = new List<(string, string?, QualityMeasures, IReadOnlyList<string>)>();
        int done = 0, skipped = 0, imported = 0;
        ctx.Report(0, entries.Count, "Import du cache de mesures…", force: true);
        foreach (var (key, node) in entries)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            if (node is not JsonObject e || e["measures"] is not JsonObject m || IsExcluded(key)) { skipped++; continue; }
            var measures = QualityMeasures.FromJson(m);
            if (measures.HasError) { skipped++; continue; }
            var match = LibraryText.MatchKey(key);
            if (known.ContainsKey(match)) { skipped++; continue; }
            var path = tracks.TryGetValue(match, out var real) ? real : LibraryText.CanonicalPath(key);
            var defects = QualityVerdict.Evaluate(measures, QualityLimits.Default, null).Defects;
            batch.Add((path, e["stamp"]?.GetValue<string>(), measures, defects));
            imported++;
            if (batch.Count >= 200)
            {
                store.SaveMeasures(batch);
                batch.Clear();
                ctx.Report(done, entries.Count, $"Import : {imported} mesure(s)");
            }
        }
        store.SaveMeasures(batch);
        ctx.Report(done, entries.Count, $"Import terminé : {imported} mesure(s) importée(s), {skipped} ignorée(s)", force: true);
        return new JsonObject { ["imported"] = imported, ["skipped"] = skipped };
    }, ct);

    /// <summary>Corbeilles et dossiers système : un segment « $… », « .… » ou « System Volume Information ».</summary>
    public static bool IsExcluded(string path) =>
        path.Split('\\', '/').SkipLast(1).Any(s => s.StartsWith('$') || (s.StartsWith('.') && s.Length > 1) ||
                                                   s.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase));
}
