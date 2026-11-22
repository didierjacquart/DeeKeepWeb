using DeeKeep.Common.Data;
using DeeKeep.Common.Hosting;
using DeeKeep.Gateway.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Gateway;

/// <summary>Codes à usage unique envoyés par e-mail : validité, essais, renvoi, limites.</summary>
public class AuthCodeStoreTests : IDisposable
{
    private const string Email = "didier@example.com";
    private const double T0 = 1_800_000_000;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deekeep-tests", Guid.NewGuid().ToString("N"));

    public AuthCodeStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private AuthCodeStore NewStore() => new(
        Options.Create(new DatabaseOptions { MainPath = Path.Combine(_dir, "main.db"), WebPath = Path.Combine(_dir, "web.db") }),
        new InternalKey("cle-de-test"));

    private static string Other(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("000000");

    [Fact]
    public void Un_code_juste_se_verifie_puis_se_consomme()
    {
        var store = NewStore();
        var code = store.Issue(Email, T0);
        Assert.Matches("^[0-9]{6}$", code);

        Assert.Equal(CodeCheck.Ok, store.Check(Email, code, consume: false, T0 + 10));
        Assert.Equal(CodeCheck.Ok, store.Check(Email, $" {code[..3]} {code[3..]} ", consume: true, T0 + 20));
        // Consommé : il ne sert plus.
        Assert.Equal(CodeCheck.Missing, store.Check(Email, code, consume: true, T0 + 30));
        Assert.Equal(CodeCheck.Missing, store.Check("autre@example.com", code, consume: true, T0 + 30));
    }

    [Fact]
    public void Un_code_expire_apres_sa_duree()
    {
        var store = NewStore();
        var code = store.Issue(Email, T0);
        Assert.Equal(CodeCheck.Expired, store.Check(Email, code, consume: true, T0 + AuthCodeStore.Lifetime.TotalSeconds + 1));
    }

    [Fact]
    public void Trop_d_erreurs_detruisent_le_code()
    {
        var store = NewStore();
        var code = store.Issue(Email, T0);
        for (var i = 1; i < AuthCodeStore.MaxAttempts; i++) Assert.Equal(CodeCheck.Wrong, store.Check(Email, Other(code), consume: false, T0 + i));
        Assert.Equal(CodeCheck.TooManyAttempts, store.Check(Email, Other(code), consume: false, T0 + 10));
        // Même le bon code est refusé ensuite.
        Assert.Equal(CodeCheck.TooManyAttempts, store.Check(Email, code, consume: true, T0 + 11));
    }

    [Fact]
    public void Redemander_un_code_invalide_le_precedent_apres_le_delai()
    {
        var store = NewStore();
        var first = store.Issue(Email, T0);
        var tooSoon = Assert.Throws<CodeRateLimitException>(() => store.Issue(Email, T0 + 5));
        Assert.Equal(25, tooSoon.RetryAfterSeconds);

        var second = store.Issue(Email, T0 + AuthCodeStore.ResendDelay.TotalSeconds);
        if (second != first) Assert.Equal(CodeCheck.Wrong, store.Check(Email, first, consume: false, T0 + 40));
        Assert.Equal(CodeCheck.Ok, store.Check(Email, second, consume: true, T0 + 41));
    }

    [Fact]
    public void Nombre_d_envois_limite_par_heure()
    {
        var store = NewStore();
        var t = T0;
        for (var i = 0; i < AuthCodeStore.MaxPerHour; i++, t += 60) store.Issue(Email, t);
        Assert.Throws<CodeRateLimitException>(() => store.Issue(Email, t));
        // Une heure après le premier envoi, la fenêtre repart.
        store.Issue(Email, T0 + 3600);
    }

    [Fact]
    public void Un_code_non_envoye_ne_vaut_rien_et_ne_bloque_pas_le_renvoi()
    {
        var store = NewStore();
        var code = store.Issue(Email, T0);
        store.Cancel(Email);
        Assert.Equal(CodeCheck.Missing, store.Check(Email, code, consume: true, T0 + 1));
        store.Issue(Email, T0 + 2);
    }
}
