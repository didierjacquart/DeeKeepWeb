using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Data;

namespace DeeKeep.Quality.Jobs;

/// <summary>
/// Recherche des durées de référence en arrière-plan (<c>ReferenceLookupThread</c>), par lots de
/// 10, sans bloquer l'affichage : chaque lot met la liste à jour. Le service Catalog porte la
/// cadence (Deezer 5/s, MusicBrainz 1/s) et le cache. Un titre inconnu donne « sans référence » ;
/// un réseau indisponible donne « référence indisponible » et la tâche est retentée plus tard
/// (le défaut de l'original les confondait).
/// </summary>
public sealed class ReferencesHandler(QualityStore store, QualityEngine engine, CatalogClient catalog) : IJobHandler
{
    private const int BatchSize = 10;

    public async Task<JsonNode?> ExecuteAsync(JobContext ctx, CancellationToken ct)
    {
        var total = store.CountReferencesPending();
        var done = 0;
        var network = 0;
        var first = true;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = store.EntriesNeedingReference(includeNetwork: first, BatchSize);
            first = false;
            if (batch.Count == 0) break;
            foreach (var row in batch)
            {
                try
                {
                    var found = await catalog.GetReferenceDurationsAsync(row.Artist.Trim(), row.Title.Trim(), ct);
                    row.RefDurations = JsonSerializer.Serialize(found.Durations);
                    row.RefState = found.Durations.Count > 0 ? QualityReferenceStates.Value : QualityReferenceStates.None;
                }
                catch (ServiceUnavailableException)
                {
                    row.RefState = QualityReferenceStates.Network;
                    network++;
                }
                done++;
            }
            store.UpsertEntries(batch);
            engine.PublishRows(QualityUpdateReasons.References, batch);
            ctx.Report(done, Math.Max(total, done), $"Durée du morceau : {done} titre(s) cherchés");
            // Réseau coupé : inutile d'enchaîner les appels, la tâche sera retentée.
            if (network >= BatchSize) break;
        }
        ctx.Report(done, Math.Max(total, done), $"Durées de référence : {done} titre(s) traités", force: true);
        if (network > 0) throw new TransientJobException($"{network} titre(s) sans réponse de Deezer/MusicBrainz");
        return new JsonObject { ["searched"] = done };
    }
}
