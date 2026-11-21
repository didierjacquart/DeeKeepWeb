using DeeKeep.Gateway.Api;
using DeeKeep.Gateway.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DeeKeep.Tests.Gateway;

/// <summary>Table des droits de l'API : pages, droits d'action, administration.</summary>
public class PermissionsTests
{
    private static AppUser User(string[]? pages = null, bool download = false, bool delete = false, string role = Roles.User) =>
        new("u1", "invite", role, pages ?? [], download, delete, Disabled: false, CreatedAt: 0);

    private static bool Allowed(AppUser user, string method, string path) => Permissions.Deny(user, method, new PathString(path)) is null;

    [Theory]
    [InlineData("GET", "/api/catalog/search")]
    [InlineData("POST", "/api/library/tracks/query")]
    [InlineData("GET", "/api/jobs")]
    [InlineData("GET", "/api/events")]
    [InlineData("GET", "/api/ping")]
    [InlineData("GET", "/api/settings")]
    [InlineData("GET", "/api/downloads/jobs")]
    [InlineData("GET", "/api/library/browse")]
    public void Routes_communes_ouvertes_a_tout_compte(string method, string path) =>
        Assert.True(Allowed(User(), method, path));

    [Theory]
    [InlineData("GET", "/api/organize/state", PageKeys.Organiser)]
    [InlineData("POST", "/api/organize/analyze", PageKeys.Organiser)]
    [InlineData("GET", "/api/quality/entries", PageKeys.Qualite)]
    [InlineData("PUT", "/api/quality/thresholds", PageKeys.Qualite)]
    [InlineData("GET", "/api/archive/config", PageKeys.Bibliotheque)]
    [InlineData("POST", "/api/archive/archive", PageKeys.Bibliotheque)]
    [InlineData("GET", "/api/services", PageKeys.Diagnostic)]
    [InlineData("POST", "/api/downloads/tools/update-ytdlp", PageKeys.Diagnostic)]
    [InlineData("POST", "/api/downloads/conversions", PageKeys.Reencoder)]
    [InlineData("POST", "/api/library/scan-files", PageKeys.Reencoder)]
    [InlineData("GET", "/api/library/history", PageKeys.Historique)]
    [InlineData("POST", "/api/library/history/clear", PageKeys.Historique)]
    [InlineData("PUT", "/api/settings", PageKeys.Parametres)]
    [InlineData("POST", "/api/library/rescan", PageKeys.Parametres)]
    [InlineData("POST", "/api/library/import-desktop", PageKeys.Parametres)]
    [InlineData("GET", "/api/sync/status", PageKeys.Parametres)]
    [InlineData("POST", "/api/sync/run", PageKeys.Parametres)]
    public void Route_de_page_exige_la_page(string method, string path, string page)
    {
        Assert.False(Allowed(User(), method, path));
        Assert.True(Allowed(User([page]), method, path));
        // Une autre page ne suffit pas.
        var other = PageKeys.All.First(p => p != page);
        Assert.False(Allowed(User([other]), method, path));
    }

    [Theory]
    [InlineData("POST", "/api/downloads/jobs")]
    [InlineData("POST", "/api/downloads/jobs/retry")]
    [InlineData("POST", "/api/downloads/pending")]
    [InlineData("POST", "/api/downloads/redownloads")]
    public void Actions_de_telechargement_exigent_le_droit(string method, string path)
    {
        Assert.False(Allowed(User(PageKeys.All.ToArray()), method, path));
        Assert.True(Allowed(User(download: true), method, path));
    }

    [Fact]
    public void Conversion_releve_de_reencoder_pas_du_droit_de_telecharger()
    {
        Assert.True(Allowed(User([PageKeys.Reencoder]), "POST", "/api/downloads/conversions"));
        Assert.False(Allowed(User(download: true), "POST", "/api/downloads/conversions"));
    }

    [Fact]
    public void Retelechargement_de_qualite_exige_la_page_et_le_droit()
    {
        Assert.False(Allowed(User([PageKeys.Qualite]), "POST", "/api/quality/redownload"));
        Assert.False(Allowed(User(download: true), "POST", "/api/quality/redownload"));
        Assert.True(Allowed(User([PageKeys.Qualite], download: true), "POST", "/api/quality/redownload"));
    }

    [Theory]
    [InlineData("POST", "/api/library/delete-files", null)]
    [InlineData("POST", "/api/organize/delete-files", PageKeys.Organiser)]
    [InlineData("POST", "/api/archive/remove", PageKeys.Bibliotheque)]
    [InlineData("GET", "/api/trash", null)]
    [InlineData("POST", "/api/trash/empty", null)]
    public void Suppressions_exigent_le_droit_de_supprimer(string method, string path, string? page)
    {
        string[] pages = page is null ? [] : [page];
        Assert.False(Allowed(User(PageKeys.All.ToArray(), download: true), method, path));
        Assert.True(Allowed(User(pages, delete: true), method, path));
    }

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users/abc/delete")]
    public void Administration_reservee_aux_administrateurs(string method, string path)
    {
        Assert.False(Allowed(User(PageKeys.All.ToArray(), true, true), method, path));
        Assert.True(Allowed(User(role: Roles.Admin), method, path));
        Assert.True(Allowed(AppUser.Anonymous, method, path));
    }

    [Fact]
    public void Administrateur_a_tous_les_droits_sans_page_ni_droit_coche()
    {
        var admin = User(role: Roles.Admin);
        foreach (var rule in Permissions.Rules)
            Assert.Null(Permissions.Deny(admin, rule.Requirement));
    }

    [Fact]
    public void Prefixes_compares_par_segments_et_sans_casse()
    {
        Assert.False(Allowed(User(), "GET", "/API/Organize/state"));
        // « /api/organizer » n'est pas sous « /api/organize ».
        Assert.Null(Permissions.Match("GET", new PathString("/api/organizer")));
        Assert.True(Allowed(User(), "get", "/api/downloads/jobs"));
    }

    [Fact]
    public void Messages_de_refus_lisibles()
    {
        Assert.Equal("Accès refusé : la page « Organiser » ne vous est pas ouverte.",
            Permissions.Deny(User(), "GET", new PathString("/api/organize/state")));
        Assert.Equal("Accès réservé aux administrateurs.", Permissions.Deny(User(), "GET", new PathString("/api/admin/users")));
        Assert.Equal("Accès refusé : vous n'avez pas le droit de supprimer.",
            Permissions.Deny(User(), "POST", new PathString("/api/library/delete-files")));
    }
}
