using DeeKeep.Contracts.Sync;

namespace DeeKeep.Gateway.Api;

public sealed record TrashIdsBody(IReadOnlyList<string>? Ids);

public static class SyncApi
{
    /// <summary>Synchronisation du disque USB avec la bibliothèque (page Paramètres).</summary>
    public static void MapSyncApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/sync");
        // Disque branché ou non, place libre, synchronisation en cours, bilan du dernier passage.
        g.MapGet("/status", (SyncClient client, CancellationToken ct) => client.GetStatusAsync(ct));
        // « Synchroniser » : tâche usbsync.run (rend celle qui tourne déjà le cas échéant).
        g.MapPost("/run", (SyncClient client, CancellationToken ct) => client.RunAsync(ct));
    }

    /// <summary>Corbeille de la bibliothèque : fichiers supprimés depuis l'application, récupérables pendant la durée réglée.</summary>
    public static void MapTrashApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/trash");
        g.MapGet("/", (TrashClient client, CancellationToken ct) => client.ListAsync(ct));
        // Durée de conservation (jours) ; 0 = jamais d'effacement automatique.
        g.MapGet("/settings", (TrashClient client, CancellationToken ct) => client.GetSettingsAsync(ct));
        g.MapPut("/settings", (TrashClient client, TrashSettings body, CancellationToken ct) => client.SetSettingsAsync(body, ct));
        g.MapPost("/restore", (HttpContext ctx, TrashClient client, TrashIdsBody body, CancellationToken ct) =>
            client.RestoreAsync(body.Ids ?? [], UserTracksApi.UserIdOf(ctx), ct));
        // Effacement définitif : les entrées désignées, ou toute la corbeille si la liste est vide.
        g.MapPost("/purge", (TrashClient client, TrashIdsBody? body, CancellationToken ct) => client.PurgeAsync(body?.Ids ?? [], ct));
    }
}
