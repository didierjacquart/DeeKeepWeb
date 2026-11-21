using DeeKeep.Contracts.Library;
using DeeKeep.Gateway.Infrastructure;

namespace DeeKeep.Gateway.Api;

public sealed record UserTracksBody(string UserId, IReadOnlyList<string> Paths);

/// <summary>
/// Bibliothèques personnelles côté passerelle : qui fait la requête, et vues de l'administrateur
/// (utilisateurs d'un titre, nombre de titres par utilisateur, liens posés ou rompus à la main).
/// </summary>
public static class UserTracksApi
{
    /// <summary>Identifiant du compte de la requête ; null sans comptes (authentification désactivée).</summary>
    public static string? UserIdOf(HttpContext ctx) => Auth.CurrentUser(ctx) is { Id.Length: > 0 } u ? u.Id : null;

    /// <summary>Administrateur, ou application sans comptes : voit toute la bibliothèque.</summary>
    public static bool SeesEverything(HttpContext ctx) => Auth.CurrentUser(ctx) is not { } u || u.IsAdmin;

    public static void MapUserTracksApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/library");
        // Utilisateurs d'un titre (menu du titre, vue de l'administrateur).
        g.MapGet("/track-users", (UserTracksClient client, string path, CancellationToken ct) => client.UsersOfAsync(path, ct));
        // Nombre de titres par utilisateur (filtre de la Bibliothèque).
        g.MapGet("/user-counts", (UserTracksClient client, CancellationToken ct) => client.CountsAsync(ct));
        g.MapPost("/user-tracks/link", (UserTracksClient client, UserTracksBody body, CancellationToken ct) =>
            client.LinkAsync(new UserTracksRequest(body.UserId, body.Paths, "admin"), ct));
        g.MapPost("/user-tracks/unlink", (UserTracksClient client, UserTracksBody body, CancellationToken ct) =>
            client.UnlinkAsync(new UserTracksRequest(body.UserId, body.Paths), ct));
    }
}
