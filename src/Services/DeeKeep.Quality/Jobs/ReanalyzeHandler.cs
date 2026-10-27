using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Analysis;
using DeeKeep.Quality.Data;

namespace DeeKeep.Quality.Jobs;

public sealed record ReanalyzePayload(IReadOnlyList<string> Paths);

/// <summary>
/// Réanalyse de fichiers précis (<c>SingleAnalysisThread</c>) : « Réanalyser » et réanalyse
/// automatique après remplacement. Corrige deux défauts de l'original : la durée de référence
/// est rejouée sur le nouveau fichier (la plus proche des durées connues, pas l'ancienne valeur),
/// et le nouveau chemin est celui qui reçoit la mesure.
/// </summary>
public sealed class ReanalyzeHandler(QualityStore store, QualityEngine engine, QualityMeter meter, PathMap paths) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        var list = ctx.GetPayload<ReanalyzePayload>().Paths;
        var deep = engine.Thresholds().Deep;
        var done = 0;
        var errors = 0;
        ctx.Report(0, list.Count, "Réanalyse…", force: true);
        foreach (var dbPath in list)
        {
            ct.ThrowIfCancellationRequested();
            var local = paths.ToLocal(dbPath);
            var previous = store.Entries([dbPath]).FirstOrDefault();
            var tags = QualityEngine.ReadTags(local);
            var measures = await meter.MeasureAsync(local, deep, tags.Duration, ct);
            var row = engine.BuildRow(tags, local, dbPath, measures, previous);
            if (previous?.Status is QualityStatuses.Reanalyzing or QualityStatuses.Redownload or QualityStatuses.Replaced)
                row.Status = measures.HasError ? QualityStatuses.ReplacedUnreadable : QualityStatuses.Replaced;
            row.RedownloadJob = null;
            if (!measures.HasError)
            {
                var defects = engine.ToDto(row, new StoredMeasure(dbPath, null, measures, 0), engine.Thresholds()).Defects.Select(d => d.Id).ToList();
                store.SaveMeasures([(dbPath, Stamp.Quality(local), measures, defects)]);
            }
            else errors++;
            store.UpsertEntries([row]);
            engine.PublishRows(QualityUpdateReasons.Reanalyzed, [row]);
            ctx.Item(dbPath, measures.HasError ? "error" : "done", measures.HasError ? measures.Error : null);
            ctx.Report(++done, list.Count, $"Réanalyse… {done} / {list.Count}", errors);
        }
        if (store.CountReferencesPending() > 0) engine.EnsureReferenceJob();
        return new JsonObject { ["files"] = done, ["errors"] = errors };
    }
}
