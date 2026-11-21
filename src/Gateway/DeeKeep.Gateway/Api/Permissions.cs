using DeeKeep.Gateway.Infrastructure;

namespace DeeKeep.Gateway.Api;

/// <summary>Droits d'action donnés compte par compte, en plus des pages.</summary>
public enum Capability
{
    /// <summary>Lancer ou gérer des téléchargements (extraction, retéléchargement, liste d'attente).</summary>
    Download,

    /// <summary>Supprimer des fichiers ou des données (fichiers du disque, archive, corbeille).</summary>
    Delete,
}

/// <summary>Ce qu'exige une route : être administrateur, avoir une page, un droit d'action (cumulables).</summary>
public sealed record Requirement(bool Admin = false, string? Page = null, Capability? Capability = null)
{
    public static Requirement AdminOnly => new(Admin: true);

    public static Requirement ForPage(string page, Capability? capability = null) => new(Page: page, Capability: capability);

    public static Requirement For(Capability capability) => new(Capability: capability);
}

/// <summary>Règle : préfixe de chemin sous <c>/api</c> (par segments), méthodes visées (toutes si vide).</summary>
public sealed record PermissionRule(string Prefix, Requirement Requirement, params string[] Methods)
{
    public bool Matches(string method, PathString path) =>
        path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase) &&
        (Methods.Length == 0 || Methods.Contains(method, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Table des droits de l'API, appliquée par la passerelle à chaque requête <c>/api</c>
/// (masquer un bouton côté client ne protège rien). La **première** règle qui
/// correspond s'applique : les règles précises passent donc avant les règles
/// générales. Une route sans règle est ouverte à tout compte connecté (recherche,
/// titres de la bibliothèque, tâches, événements…). Les flux binaires (<c>/media</c>,
/// <c>/covers</c>…) ne sont pas sous <c>/api</c> : tout compte connecté y accède.
/// </summary>
public static class Permissions
{
    private const string Get = "GET", Post = "POST", Put = "PUT", Delete = "DELETE";

    public static readonly IReadOnlyList<PermissionRule> Rules =
    [
        new("/api/admin", Requirement.AdminOnly),
        // Bibliothèques personnelles : qui utilise quel titre, liens posés à la main.
        new("/api/library/track-users", Requirement.AdminOnly),
        new("/api/library/user-counts", Requirement.AdminOnly),
        new("/api/library/user-tracks", Requirement.AdminOnly),

        // Suppressions : le droit de supprimer, en plus de la page quand elle est propre au domaine.
        // Réglage de la durée de conservation : page Paramètres (lecture comprise, pour l'afficher).
        new("/api/trash/settings", Requirement.ForPage(PageKeys.Parametres)),
        new("/api/trash", Requirement.For(Capability.Delete)),
        new("/api/library/delete-files", Requirement.For(Capability.Delete), Post),
        new("/api/organize/delete-files", Requirement.ForPage(PageKeys.Organiser, Capability.Delete), Post),
        new("/api/archive/remove", Requirement.ForPage(PageKeys.Bibliotheque, Capability.Delete), Post),

        new("/api/sync", Requirement.ForPage(PageKeys.Parametres)),
        new("/api/organize", Requirement.ForPage(PageKeys.Organiser)),
        new("/api/quality/redownload", Requirement.ForPage(PageKeys.Qualite, Capability.Download), Post),
        new("/api/quality", Requirement.ForPage(PageKeys.Qualite)),
        new("/api/archive", Requirement.ForPage(PageKeys.Bibliotheque)),
        new("/api/services", Requirement.ForPage(PageKeys.Diagnostic)),

        // Téléchargeur : la conversion appartient à Réencoder, la mise à jour de yt-dlp au Diagnostic ;
        // toute autre action (extraction, liste d'attente, retéléchargement) exige le droit de télécharger.
        new("/api/downloads/conversions", Requirement.ForPage(PageKeys.Reencoder), Post),
        new("/api/downloads/tools", Requirement.ForPage(PageKeys.Diagnostic), Post, Put),
        new("/api/downloads", Requirement.For(Capability.Download), Post, Put, Delete),

        new("/api/library/scan-files", Requirement.ForPage(PageKeys.Reencoder), Post),
        new("/api/library/history", Requirement.ForPage(PageKeys.Historique)),
        new("/api/library/rescan", Requirement.ForPage(PageKeys.Parametres), Post),
        new("/api/library/import-desktop", Requirement.ForPage(PageKeys.Parametres), Post),
        new("/api/settings", Requirement.ForPage(PageKeys.Parametres), Put),
    ];

    /// <summary>Règle applicable à une requête, ou null (route ouverte à tout compte connecté).</summary>
    public static PermissionRule? Match(string method, PathString path) => Rules.FirstOrDefault(r => r.Matches(method, path));

    /// <summary>Motif du refus (message lisible), ou null si le compte satisfait l'exigence.</summary>
    public static string? Deny(AppUser user, Requirement requirement)
    {
        if (user.IsAdmin) return null;
        if (requirement.Admin) return "Accès réservé aux administrateurs.";
        if (requirement.Page is { } page && !user.HasPage(page))
            return $"Accès refusé : la page « {PageKeys.Label(page)} » ne vous est pas ouverte.";
        return requirement.Capability switch
        {
            Capability.Download when !user.CanDownload => "Accès refusé : vous n'avez pas le droit de télécharger.",
            Capability.Delete when !user.CanDelete => "Accès refusé : vous n'avez pas le droit de supprimer.",
            _ => null,
        };
    }

    /// <summary>Motif du refus d'une requête pour ce compte, ou null si elle est permise.</summary>
    public static string? Deny(AppUser user, string method, PathString path) =>
        Match(method, path) is { } rule ? Deny(user, rule.Requirement) : null;

    /// <summary>
    /// Applique la table après l'authentification : 401 sans compte valide, 403 JSON
    /// (<c>{ error, code: "FORBIDDEN" }</c>) si le compte n'a pas le droit. Authentification
    /// désactivée : tout est permis (compte implicite administrateur).
    /// </summary>
    public static IApplicationBuilder UseDeeKeepPermissions(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api"))
            {
                await next(ctx);
                return;
            }
            var user = Auth.CurrentUser(ctx);
            if (user is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (Deny(user, ctx.Request.Method, ctx.Request.Path) is { } reason)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ApiError(reason, "FORBIDDEN"), ApiSetup.ClientJson);
                return;
            }
            await next(ctx);
        });
}
