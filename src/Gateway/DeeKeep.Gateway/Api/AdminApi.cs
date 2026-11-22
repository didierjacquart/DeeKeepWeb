using DeeKeep.Gateway.Infrastructure;

namespace DeeKeep.Gateway.Api;

/// <summary><see cref="Name"/> : adresse e-mail du compte.</summary>
public sealed record CreateUserBody(
    string Name,
    string Password,
    string Role,
    IReadOnlyList<string>? Pages,
    bool CanDownload,
    bool CanDelete,
    bool Disabled = false);

public sealed record UpdateUserBody(string Role, IReadOnlyList<string>? Pages, bool CanDownload, bool CanDelete, bool Disabled);

public sealed record PasswordBody(string Password);

/// <summary>Compte vu par l'administration (jamais le haché du mot de passe).</summary>
public sealed record UserDto(
    string Id,
    string Name,
    string Role,
    IReadOnlyList<string> Pages,
    bool CanDownload,
    bool CanDelete,
    bool Disabled,
    double CreatedAt,
    bool Pending)
{
    public static UserDto From(AppUser u) => new(u.Id, u.Name, u.Role, u.Pages, u.CanDownload, u.CanDelete, u.Disabled, u.CreatedAt, u.Pending);
}

/// <summary>
/// Gestion des comptes, réservée aux administrateurs (règle <c>/api/admin</c> de
/// <see cref="Permissions"/>). Les gardes (dernier administrateur actif, son propre
/// compte) sont dans <see cref="UserStore"/> et rendent 409 <c>INVALID_OPERATION</c>.
/// </summary>
public static class AdminApi
{
    public static void MapAdminApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin/users");

        g.MapGet("/", (UserStore users) => users.List().Select(UserDto.From));

        g.MapPost("/", (UserStore users, CreateUserBody body) =>
            UserDto.From(users.Create(body.Name, body.Password,
                new UserRights(body.Role, body.Pages, body.CanDownload, body.CanDelete, body.Disabled))));

        g.MapPut("/{id}", (UserStore users, string id, UpdateUserBody body) =>
            Found(() => UserDto.From(users.Update(id, new UserRights(body.Role, body.Pages, body.CanDownload, body.CanDelete, body.Disabled)))));

        g.MapPost("/{id}/password", (UserStore users, string id, PasswordBody body) =>
            Found(() =>
            {
                users.SetPassword(id, body.Password);
                return new { ok = true };
            }));

        g.MapPost("/{id}/delete", (HttpContext ctx, UserStore users, string id) =>
            Found(() =>
            {
                users.Delete(id, Auth.CurrentUser(ctx)?.Id);
                return new { ok = true };
            }));
    }

    /// <summary>Compte inconnu : 404 lisible plutôt qu'une erreur interne.</summary>
    private static IResult Found<T>(Func<T> action)
    {
        try
        {
            return Results.Json(action(), ApiSetup.ClientJson);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Json(new ApiError(ex.Message, "NOT_FOUND"), ApiSetup.ClientJson, statusCode: StatusCodes.Status404NotFound);
        }
    }
}
