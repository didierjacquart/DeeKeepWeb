using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DeeKeep.Gateway.Infrastructure;

public sealed record LoginRequest(string? User, string Password);

/// <summary>
/// Authentification par comptes : mots de passe hachés PBKDF2 dans
/// <c>deekeep_web.db</c> (<see cref="UserStore"/>), cookie HttpOnly de longue durée
/// (l'application est installée sur un téléphone : on ne veut pas se reconnecter
/// chaque jour). Le premier compte, administrateur, se crée à la première visite.
/// Le cookie est revalidé à chaque requête contre la base (cache de quelques
/// secondes) : un compte supprimé ou désactivé perd aussitôt l'accès.
/// </summary>
public static class Auth
{
    public const string DefaultUser = "deekeep";
    private const string IdClaim = "deekeep:uid";
    private const string UserItem = "deekeep.user";

    public static IServiceCollection AddDeeKeepAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var lifetime = configuration.GetValue<TimeSpan?>("Auth:SessionLifetime") ?? TimeSpan.FromDays(60);
        services.AddSingleton<UserStore>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "deekeep.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = lifetime;
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Relit le compte du cookie : absent, désactivé ou recréé depuis (identifiant
    /// différent), le cookie est refusé et effacé. Les cookies antérieurs aux comptes
    /// multiples (sans identifiant) restent valables s'ils sont plus récents que le compte.
    /// </summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var name = ctx.Principal?.Identity?.Name;
        var store = ctx.HttpContext.RequestServices.GetRequiredService<UserStore>();
        var user = string.IsNullOrEmpty(name) ? null : store.FindCached(name);
        var id = ctx.Principal?.FindFirst(IdClaim)?.Value;
        var valid = user is { Disabled: false } && (id is null
            ? ctx.Properties.IssuedUtc is { } issued && issued.ToUnixTimeMilliseconds() / 1000.0 >= user.CreatedAt
            : id == user.Id);
        if (!valid)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        ctx.HttpContext.Items[UserItem] = user;
    }

    /// <summary>
    /// Compte de la requête : celui du cookie (relu par la validation), l'utilisateur
    /// implicite aux pleins droits si l'authentification est désactivée, sinon null.
    /// </summary>
    public static AppUser? CurrentUser(HttpContext ctx)
    {
        if (!ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:Enabled", true)) return AppUser.Anonymous;
        if (ctx.User.Identity?.IsAuthenticated != true) return null;
        return ctx.Items[UserItem] as AppUser;
    }

    /// <summary>Exige une session pour l'API et les flux ; la coquille de l'application et <c>/auth</c> restent publiques.</summary>
    public static IApplicationBuilder UseDeeKeepAuthGate(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Auth:Enabled", true)) return app;
        return app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            var guarded = path.StartsWithSegments("/api") || path.StartsWithSegments("/media") ||
                          path.StartsWithSegments("/covers") || path.StartsWithSegments("/img") ||
                          path.StartsWithSegments("/preview") || path.StartsWithSegments("/archive-media");
            if (guarded && ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next();
        });
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/me", (HttpContext ctx, IConfiguration config, UserStore users) =>
        {
            var enabled = config.GetValue("Auth:Enabled", true);
            var user = CurrentUser(ctx);
            return Results.Ok(new
            {
                authEnabled = enabled,
                authenticated = user is not null,
                user = enabled ? user?.Name : ctx.User.Identity?.Name,
                needsSetup = enabled && !users.Any(),
                role = user?.Role,
                pages = user?.Pages ?? [],
                canDownload = user?.CanDownload ?? false,
                canDelete = user?.CanDelete ?? false,
            });
        });

        group.MapPost("/setup", async (HttpContext ctx, UserStore users, LoginRequest request) =>
        {
            try
            {
                // Seulement tant qu'aucun compte n'existe : la route ne peut pas servir à prendre la main.
                var name = string.IsNullOrWhiteSpace(request.User) ? DefaultUser : request.User;
                var user = users.CreateFirst(name, request.Password);
                if (user is null) return Results.Conflict("Un compte existe déjà.");
                await SignInAsync(ctx, user);
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        group.MapPost("/login", async (HttpContext ctx, UserStore users, LoginRequest request) =>
        {
            var name = string.IsNullOrWhiteSpace(request.User) ? DefaultUser : request.User.Trim();
            var user = users.Verify(name, request.Password);
            if (user is null)
            {
                await Task.Delay(Random.Shared.Next(400, 900)); // freine les essais en série
                return Results.Unauthorized();
            }
            // Message donné seulement après un mot de passe juste : il ne révèle rien à un inconnu.
            if (user.Disabled) return Results.Json("Ce compte est désactivé.", statusCode: StatusCodes.Status403Forbidden);
            await SignInAsync(ctx, user);
            return Results.Ok();
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        group.MapPost("/password", (HttpContext ctx, IConfiguration config, UserStore users, LoginRequest request) =>
        {
            var user = config.GetValue("Auth:Enabled", true) ? CurrentUser(ctx) : null;
            if (user is null) return Results.Unauthorized();
            try
            {
                users.SetPassword(user.Id, request.Password);
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });
        return app;
    }

    private static Task SignInAsync(HttpContext ctx, AppUser user)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.Name), new Claim(IdClaim, user.Id)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 210_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return string.Join('$', "pbkdf2-sha256", iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        var iterations = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
