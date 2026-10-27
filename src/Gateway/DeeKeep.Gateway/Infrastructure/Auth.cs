using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DeeKeep.Gateway.Infrastructure;

public sealed record LoginRequest(string? User, string Password);

/// <summary>
/// Authentification mono-utilisateur : mot de passe haché PBKDF2 dans
/// <c>deekeep_web.db</c>, cookie HttpOnly de longue durée (l'application est
/// installée sur un téléphone : on ne veut pas se reconnecter chaque jour).
/// Le premier mot de passe se définit à la première visite.
/// </summary>
public static class Auth
{
    private const string DefaultUser = "deekeep";

    public static IServiceCollection AddDeeKeepAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var lifetime = configuration.GetValue<TimeSpan?>("Auth:SessionLifetime") ?? TimeSpan.FromDays(60);
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
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>Exige une session pour l'API et les flux ; la coquille de l'application et <c>/auth</c> restent publiques.</summary>
    public static IApplicationBuilder UseDeeKeepAuthGate(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Auth:Enabled", true)) return app;
        return app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            var guarded = path.StartsWithSegments("/graphql") || path.StartsWithSegments("/media") ||
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

        group.MapGet("/me", (HttpContext ctx, IConfiguration config, WebDb db) =>
        {
            var enabled = config.GetValue("Auth:Enabled", true);
            using var conn = db.Open();
            var hasUser = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0;
            return Results.Ok(new
            {
                authEnabled = enabled,
                authenticated = !enabled || ctx.User.Identity?.IsAuthenticated == true,
                user = ctx.User.Identity?.Name,
                needsSetup = enabled && !hasUser,
            });
        });

        group.MapPost("/setup", async (HttpContext ctx, WebDb db, LoginRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return Results.BadRequest("Le mot de passe doit compter au moins 8 caractères.");
            using var conn = db.Open();
            var user = string.IsNullOrWhiteSpace(request.User) ? DefaultUser : request.User.Trim();
            // Seulement tant qu'aucun compte n'existe : la route ne peut pas servir à prendre la main.
            var inserted = conn.Execute(
                "INSERT INTO users (name, password_hash, created_at) SELECT @user, @hash, @now WHERE NOT EXISTS (SELECT 1 FROM users)",
                new { user, hash = HashPassword(request.Password), now = Clock.UnixNow() });
            if (inserted == 0) return Results.Conflict("Un compte existe déjà.");
            await SignInAsync(ctx, user);
            return Results.Ok();
        });

        group.MapPost("/login", async (HttpContext ctx, WebDb db, LoginRequest request) =>
        {
            using var conn = db.Open();
            var user = string.IsNullOrWhiteSpace(request.User) ? DefaultUser : request.User.Trim();
            var hash = conn.ExecuteScalar<string?>("SELECT password_hash FROM users WHERE name = @user", new { user });
            if (hash is null || !VerifyPassword(request.Password, hash))
            {
                await Task.Delay(Random.Shared.Next(400, 900)); // freine les essais en série
                return Results.Unauthorized();
            }
            await SignInAsync(ctx, user);
            return Results.Ok();
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        group.MapPost("/password", (HttpContext ctx, WebDb db, LoginRequest request) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return Results.BadRequest("Le mot de passe doit compter au moins 8 caractères.");
            using var conn = db.Open();
            conn.Execute("UPDATE users SET password_hash = @hash WHERE name = @user",
                new { hash = HashPassword(request.Password), user = ctx.User.Identity!.Name });
            return Results.Ok();
        });
        return app;
    }

    private static Task SignInAsync(HttpContext ctx, string user)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], CookieAuthenticationDefaults.AuthenticationScheme);
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
