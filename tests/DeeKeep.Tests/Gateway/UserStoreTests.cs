using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Gateway.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Gateway;

/// <summary>Comptes : migration de l'ancienne table, création, inscription, gardes de l'administration.</summary>
public class UserStoreTests : IDisposable
{
    private const string Admin = "admin@example.com";
    private const string Invite = "invite@example.com";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deekeep-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public UserStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "web.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private UserStore NewStore() => new(Options.Create(new DatabaseOptions { MainPath = Path.Combine(_dir, "main.db"), WebPath = _path }));

    private static UserRights Rights(string role = Roles.User, string[]? pages = null, bool download = false, bool delete = false, bool disabled = false) =>
        new(role, pages ?? [], download, delete, disabled);

    [Fact]
    public void Migration_garde_les_comptes_existants_en_administrateurs()
    {
        using (var conn = DeeKeepDb.OpenPath(_path, false, 5000))
        {
            conn.Execute("CREATE TABLE users (name TEXT PRIMARY KEY, password_hash TEXT NOT NULL, created_at REAL NOT NULL)");
            conn.Execute("INSERT INTO users VALUES ('deekeep', @hash, 1)", new { hash = Auth.HashPassword("ancien-mdp") });
        }
        var store = NewStore();
        var user = store.Verify("deekeep", "ancien-mdp");
        Assert.NotNull(user);
        Assert.Equal(Roles.Admin, user.Role);
        Assert.Equal(PageKeys.All, user.Pages);
        Assert.True(user.CanDownload && user.CanDelete && !user.Disabled && !user.Pending);
        Assert.False(string.IsNullOrEmpty(user.Id));
        // Une seconde migration (nouveau processus) ne change rien.
        var again = NewStore().FindByName("deekeep")!;
        Assert.Equal(user.Id, again.Id);
        Assert.Equal(Roles.Admin, again.Role);
    }

    [Fact]
    public void Premier_compte_administrateur_une_seule_fois()
    {
        var store = NewStore();
        Assert.False(store.Any());
        var first = store.CreateFirst("Didier@Example.com", "motdepasse");
        Assert.NotNull(first);
        Assert.True(first.IsAdmin);
        Assert.Equal("didier@example.com", first.Name);
        Assert.Null(store.CreateFirst("autre@example.com", "motdepasse"));
        Assert.Throws<ArgumentException>(() => NewStore().CreateFirst("x@example.com", "court"));
        Assert.Throws<ArgumentException>(() => NewStore().CreateFirst("pas-un-email", "motdepasse"));
    }

    [Fact]
    public void Creation_valide_l_adresse_le_mot_de_passe_et_les_pages()
    {
        var store = NewStore();
        var user = store.Create(" Invite@Example.COM ", "motdepasse", Rights(pages: ["qualite", "musique"], download: true));
        Assert.Equal(Invite, user.Name);
        Assert.Equal(["musique", "qualite"], user.Pages);
        Assert.True(user.CanDownload);
        Assert.False(user.CanDelete);
        Assert.NotNull(store.Verify("INVITE@example.com", "motdepasse"));
        Assert.Null(store.Verify(Invite, "mauvais-mdp"));

        Assert.Throws<InvalidOperationException>(() => store.Create("invite@EXAMPLE.com", "motdepasse", Rights()));
        Assert.Throws<ArgumentException>(() => store.Create("autre@example.com", "court", Rights()));
        Assert.Throws<ArgumentException>(() => store.Create("  ", "motdepasse", Rights()));
        Assert.Throws<ArgumentException>(() => store.Create("autre", "motdepasse", Rights()));
        Assert.Throws<ArgumentException>(() => store.Create("autre@example.com", "motdepasse", Rights(role: "chef")));
        Assert.Throws<ArgumentException>(() => store.Create("autre@example.com", "motdepasse", Rights(pages: ["inconnue"])));
    }

    [Fact]
    public void Inscription_en_attente_jusqu_a_la_validation()
    {
        var store = NewStore();
        store.CreateFirst(Admin, "motdepasse");

        var (user, created) = store.Register("Nouveau@Example.com", "motdepasse");
        Assert.True(created);
        Assert.Equal("nouveau@example.com", user.Name);
        Assert.True(user.Pending && user.Disabled);
        Assert.Equal(Roles.User, user.Role);
        Assert.Equal(UserStore.SelfServicePages, user.Pages);
        Assert.Equal([Admin], store.AdminEmails());

        // L'administrateur active le compte : il est validé.
        var approved = store.Update(user.Id, Rights(pages: ["musique"]));
        Assert.False(approved.Pending || approved.Disabled);
        // Désactivé plus tard, il n'est pas « en attente » pour autant.
        Assert.False(store.Update(user.Id, Rights(disabled: true)).Pending);
    }

    [Fact]
    public void Mot_de_passe_oublie_garde_le_compte_et_ses_droits()
    {
        var store = NewStore();
        var admin = store.CreateFirst(Admin, "motdepasse")!;

        var (user, created) = store.Register("ADMIN@example.com", "nouveau-mdp");
        Assert.False(created);
        Assert.Equal(admin.Id, user.Id);
        Assert.True(user.IsAdmin && !user.Disabled && !user.Pending);
        Assert.Null(store.Verify(Admin, "motdepasse"));
        Assert.NotNull(store.Verify(Admin, "nouveau-mdp"));
        Assert.Throws<ArgumentException>(() => store.Register(Admin, "court"));
    }

    [Fact]
    public void Premiere_inscription_sans_compte_donne_un_administrateur_actif()
    {
        var (user, created) = NewStore().Register("didier@example.com", "motdepasse");
        Assert.True(created);
        Assert.True(user.IsAdmin && !user.Disabled && !user.Pending);
    }

    [Fact]
    public void Dernier_administrateur_actif_protege()
    {
        var store = NewStore();
        var admin = store.CreateFirst(Admin, "motdepasse")!;
        var other = store.Create(Invite, "motdepasse", Rights());

        Assert.Throws<InvalidOperationException>(() => store.Update(admin.Id, Rights(role: Roles.User)));
        Assert.Throws<InvalidOperationException>(() => store.Update(admin.Id, Rights(role: Roles.Admin, disabled: true)));
        Assert.Throws<InvalidOperationException>(() => store.Delete(admin.Id, other.Id));

        // Avec un second administrateur actif, le premier peut être rétrogradé puis supprimé.
        store.Update(other.Id, Rights(role: Roles.Admin));
        var demoted = store.Update(admin.Id, Rights(role: Roles.User, pages: ["musique"]));
        Assert.False(demoted.IsAdmin);
        store.Delete(admin.Id, other.Id);
        Assert.Null(store.FindById(admin.Id));

        // Un administrateur désactivé ne compte pas comme actif.
        var third = store.Create("tiers@example.com", "motdepasse", Rights(role: Roles.Admin, disabled: true));
        Assert.Throws<InvalidOperationException>(() => store.Update(other.Id, Rights(role: Roles.User)));
        Assert.True(store.FindById(third.Id)!.Disabled);
    }

    [Fact]
    public void Impossible_de_supprimer_son_propre_compte()
    {
        var store = NewStore();
        var admin = store.CreateFirst(Admin, "motdepasse")!;
        store.Create("second@example.com", "motdepasse", Rights(role: Roles.Admin));
        Assert.Throws<InvalidOperationException>(() => store.Delete(admin.Id, admin.Id));
        Assert.Throws<KeyNotFoundException>(() => store.Delete("inconnu", admin.Id));
    }

    [Fact]
    public void Modifications_visibles_aussitot_malgre_le_cache()
    {
        var store = NewStore();
        store.CreateFirst(Admin, "motdepasse");
        var user = store.Create(Invite, "motdepasse", Rights());
        Assert.False(store.FindCached(Invite)!.Disabled);
        store.Update(user.Id, Rights(disabled: true));
        Assert.True(store.FindCached(Invite)!.Disabled);
        store.Delete(user.Id, null);
        Assert.Null(store.FindCached(Invite));
    }

    [Fact]
    public void Reinitialisation_du_mot_de_passe()
    {
        var store = NewStore();
        var admin = store.CreateFirst(Admin, "motdepasse")!;
        store.SetPassword(admin.Id, "nouveau-mdp");
        Assert.Null(store.Verify(Admin, "motdepasse"));
        Assert.NotNull(store.Verify(Admin, "nouveau-mdp"));
        Assert.Throws<ArgumentException>(() => store.SetPassword(admin.Id, "court"));
        Assert.Throws<KeyNotFoundException>(() => store.SetPassword("inconnu", "nouveau-mdp"));
    }

    [Theory]
    [InlineData("a@b.fr", true)]
    [InlineData("didier.jacquart78@gmail.com", true)]
    [InlineData("sans-arobase.fr", false)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.fr", false)]
    [InlineData("", false)]
    public void Forme_des_adresses(string email, bool valid)
    {
        if (valid) Assert.Equal(email.ToLowerInvariant(), Emails.Normalize(email));
        else Assert.Throws<ArgumentException>(() => Emails.Normalize(email));
    }
}
