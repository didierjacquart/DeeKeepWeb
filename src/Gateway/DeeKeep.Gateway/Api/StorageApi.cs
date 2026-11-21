using DeeKeep.Contracts.Storage;

namespace DeeKeep.Gateway.Api;

public sealed record ArchiveConfigBody(string? Root, string? Prefix);

public sealed record ArchiveRootBody(string? Root = null);

public static class StorageApi
{
    public static void MapStorageApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/archive");

        // Emplacement de l'archive (équivalent d'archive.json).
        g.MapGet("/config", (StorageClient storage, CancellationToken ct) => storage.GetConfigAsync(ct));
        g.MapPut("/config", (StorageClient storage, ArchiveConfigBody body, CancellationToken ct) =>
            storage.SetConfigAsync(new ArchiveConfig(body.Root, body.Prefix ?? ArchiveDefaults.Prefix, null, false), ct));
        // Vérifie que l'archive (valeur saisie, même non enregistrée) s'ouvre et accepte une écriture.
        g.MapPost("/test", (StorageClient storage, ArchiveRootBody? body, CancellationToken ct) => storage.TestAsync(body?.Root, ct));

        // Jauge : occupation, place libre, titres archivés / à renvoyer / non archivés. root : emplacement saisi, pas encore enregistré.
        g.MapGet("/usage", (StorageClient storage, string? root, CancellationToken ct) => storage.GetUsageAsync(root, ct));
        // Ce qu'Archiver et Restaurer feraient des titres retenus (chemins en base).
        g.MapPost("/plan", (StorageClient storage, PathsBody body, CancellationToken ct) => storage.PlanAsync(body.Paths, ct));

        // Tâches : archiver, restaurer (rien n'est écrasé), mesurer, vérifier.
        g.MapPost("/archive", (StorageClient storage, PathsBody body, CancellationToken ct) => storage.ArchiveAsync(new ArchiveRequest(body.Paths), ct));
        g.MapPost("/restore", (StorageClient storage, PathsBody body, CancellationToken ct) => storage.RestoreAsync(new ArchiveRequest(body.Paths), ct));
        g.MapPost("/measure", (StorageClient storage, ArchiveRootBody? body, CancellationToken ct) => storage.MeasureAsync(body?.Root, ct));
        g.MapPost("/check", (StorageClient storage, CancellationToken ct) => storage.CheckAsync(ct));
        // Retire des titres de l'archive ; rend le nombre de titres retirés.
        g.MapPost("/remove", (StorageClient storage, PathsBody body, CancellationToken ct) => storage.RemoveAsync(body.Paths, ct));
    }
}
