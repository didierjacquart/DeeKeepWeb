using System.Text.Json.Nodes;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Quality;

namespace DeeKeep.Gateway.Api;

/// <summary>Ligne de l'écran Qualité : verdict recalculé avec les seuils courants, textes des cellules et infobulles.</summary>
public sealed record QualityRow(
    long Uid, string Path, string? Title, string? Artist, string? Album, string? Format, int? Bitrate, double? Duration,
    double? ReferenceDuration, string ReferenceState, int Score, IReadOnlyList<DefectInfo> Defects,
    JsonNode? Measures, JsonNode Cells, JsonNode Tooltips, JsonNode? Tags, string? Error, bool OnDisk, double UpdatedAt,
    string? Status, string? StatusDetail, Guid? RedownloadJobId)
{
    public static QualityRow From(QualityEntry e) => new(
        e.Uid, e.Path, e.Title, e.Artist, e.Album, e.Format ?? e.Ext?.ToUpperInvariant(), e.Bitrate, e.Duration, e.ReferenceDuration,
        e.ReferenceState, e.Score, e.Defects, e.Measures, Dict(e.Cells)!, Dict(e.Tooltips)!, Dict(e.Tags), e.Error, e.OnDisk, e.UpdatedAt,
        e.Status, e.StatusDetail, e.RedownloadJobId);

    private static JsonObject? Dict(IReadOnlyDictionary<string, string>? d)
    {
        if (d is null) return null;
        var o = new JsonObject();
        foreach (var (k, v) in d) o[k] = v;
        return o;
    }
}

public sealed record QualityRowsPage(
    IReadOnlyList<QualityRow> Items, int Total, int Flagged, int Errors, int ReferencesPending, string? Folder, QualityThresholds? Thresholds);

/// <summary>Mise à jour poussée (événement quality.updated) : lignes ajoutées ou modifiées, ou motif d'un rechargement complet.</summary>
public sealed record QualityUpdate(string Reason, IReadOnlyList<QualityRow> Entries, IReadOnlyList<string>? Removed)
{
    public static QualityUpdate From(QualityUpdatedEvent e) => new(e.Reason, e.Entries.Select(QualityRow.From).ToList(), e.Removed);
}

public sealed record QualityScanBody(string? Folder = null, bool? Deep = null, bool? Force = null);

public sealed record FolderBody(string Folder);

public static class QualityApi
{
    public static void MapQualityApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/quality");

        // Lignes de l'écran Qualité. defects : défauts cochés (absent = tous) ; includeClean : « Titres sans défaut ».
        g.MapGet("/entries", async (QualityClient client, string[]? defects, bool? includeClean, int? offset, int? limit, string? folder,
            CancellationToken ct) =>
        {
            var page = await client.QueryAsync(new QualityQuery(folder, defects is { Length: > 0 } ? defects : null, includeClean ?? false,
                offset ?? 0, limit ?? 5000), ct);
            return new QualityRowsPage(page.Items.Select(QualityRow.From).ToList(), page.Total, page.Flagged, page.Errors,
                page.ReferencesPending, page.Folder, page.Thresholds);
        });
        // « Vider la liste » (refusé pendant une analyse) : les mesures restent en base.
        g.MapPost("/entries/clear", (QualityClient client, CancellationToken ct) => client.ClearAsync(ct));

        // Seuils de détection (aucun fichier n'est relu quand ils changent : le verdict est recalculé).
        g.MapGet("/thresholds", (QualityClient client, CancellationToken ct) => client.GetThresholdsAsync(ct));
        g.MapPut("/thresholds", (QualityClient client, QualityThresholds thresholds, CancellationToken ct) => client.SetThresholdsAsync(thresholds, ct));

        // « Analyser » (tâche quality.scan), réanalyse de fichiers précis, « Retélécharger la sélection ».
        g.MapPost("/scan", (QualityClient client, QualityScanBody? body, CancellationToken ct) =>
            client.StartScanAsync(new QualityScanRequest(body?.Folder, body?.Deep, body?.Force ?? false), ct));
        g.MapPost("/reanalyze", (QualityClient client, PathsBody body, CancellationToken ct) => client.ReanalyzeAsync(body.Paths, ct));
        g.MapPost("/redownload", (QualityClient client, PathsBody body, CancellationToken ct) => client.RedownloadAsync(body.Paths, ct));

        // « Modifier le dossier » : vide la liste et arrête l'analyse en cours si le dossier change.
        g.MapPut("/folder", async (QualityClient client, FolderBody body, CancellationToken ct) => new { folder = await client.SetFolderAsync(body.Folder, ct) });

        // Importe le quality_cache.json de l'application desktop.
        g.MapPost("/import-desktop", (QualityClient client, CancellationToken ct) => client.ImportDesktopCacheAsync(ct));
    }
}
