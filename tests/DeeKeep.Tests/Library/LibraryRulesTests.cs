using System.Text.Json.Nodes;
using DeeKeep.Library.Rules;
using Xunit;

namespace DeeKeep.Tests.Library;

/// <summary>Vecteurs produits en exécutant library_rules.py (Python d'origine) sur le même arbre de dossiers.</summary>
public class LibraryRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deekeep-tests", Guid.NewGuid().ToString("N"));

    public LibraryRulesTests()
    {
        foreach (var (dir, count) in new[] { ("Inna", 3), ("Madonna", 1) })
        {
            Directory.CreateDirectory(Path.Combine(_root, dir, "Album"));
            for (var i = 0; i < count; i++) File.WriteAllText(Path.Combine(_root, dir, "Album", $"t{i}.mp3"), "");
        }
        Directory.CreateDirectory(Path.Combine(_root, "Divers"));
        File.WriteAllText(Path.Combine(_root, "Divers", "Song - Madonna.mp3"), "");
        File.WriteAllText(Path.Combine(_root, "Loose - Inna Feat. Flo Rida.mp3"), "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static Dictionary<string, string?> Tags(string artist, string title = "X", string album = "Alb") =>
        new() { ["title"] = title, ["artist"] = artist, ["album"] = album };

    private string Rel(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

    private static OrganizeRules DiversRules => LibraryRules.FromSettings(new Dictionary<string, JsonNode?>
    {
        ["org_divers"] = true, ["org_divers_min"] = 3, ["org_album"] = true,
    });

    [Theory]
    [InlineData("Madonna Featuring Justin Timberlake and Timbaland", "Madonna")]
    [InlineData("Flo Rida Feat. Ke$ha", "Flo Rida")]
    public void MainArtist_cuts_guests(string name, string expected) =>
        Assert.Equal(expected, LibraryRules.MainArtist(name, new HashSet<string>()));

    [Fact]
    public void MainArtist_cuts_ambiguous_separators_only_for_known_artists()
    {
        Assert.Equal("Calvin Harris", LibraryRules.MainArtist("Calvin Harris & Sam Smith", new HashSet<string> { "calvin harris" }));
        Assert.Equal("Earth, Wind & Fire", LibraryRules.MainArtist("Earth, Wind & Fire", new HashSet<string>()));
    }

    [Theory]
    [InlineData("AC/DC", "ACDC")]
    [InlineData("Whip / Nae Nae", "Whip  Nae Nae")]
    [InlineData("Mr. Brightside.", "Mr. Brightside")]
    [InlineData(" x\ty ", "x y")]
    public void SafeComponent_matches_python(string name, string expected) => Assert.Equal(expected, LibraryRules.SafeComponent(name));

    [Fact]
    public void Destination_matches_python()
    {
        var tally = new Tally(_root);
        // Les titres épars (« Loose - Inna Feat. Flo Rida ») comptent pour Inna : 3 + 1 ; Madonna : 1 + 1 « Divers » + le titre.
        Assert.Equal("Inna/Alb/X - Inna Feat. Flo Rida.mp3", Rel(LibraryRules.Destination(_root, Tags("Inna Feat. Flo Rida"), DiversRules, ".mp3", tally: tally)));
        Assert.Equal("Madonna/Alb/X - Madonna.mp3", Rel(LibraryRules.Destination(_root, Tags("Madonna"), DiversRules, ".mp3", tally: tally)));
        Assert.Equal("Divers/X - New Guy.mp3", Rel(LibraryRules.Destination(_root, Tags("New Guy"), DiversRules, ".mp3", tally: new Tally(_root))));
        Assert.Equal("X.mp3", Rel(LibraryRules.Destination(_root, Tags(""), DiversRules, ".mp3")));
    }

    [Fact]
    public void Reserved_batch_leaves_divers_in_both_directions()
    {
        var batch = new Tally(_root);
        for (var i = 0; i < 3; i++) batch.Reserve("New Guy");
        Assert.Equal("New Guy/Alb/X - New Guy.mp3", Rel(LibraryRules.Destination(_root, Tags("New Guy"), DiversRules, ".mp3", tally: batch)));
        Assert.Equal(3, batch.Count("New Guy"));
    }

    [Fact]
    public void Tally_counts_match_python()
    {
        var tally = new Tally(_root);
        Assert.Equal(5, tally.Count("Inna"));
        Assert.Equal(3, tally.Count("Madonna"));
        // Le fichier remplacé n'est pas compté deux fois.
        Assert.Equal(2, tally.Count("Madonna", Path.Combine(_root, "Divers", "Song - Madonna.mp3")));
    }

    [Fact]
    public void Rename_pattern_and_sanitized_names()
    {
        var rules = LibraryRules.FromSettings(new Dictionary<string, JsonNode?> { ["org_rename"] = true, ["org_pattern"] = "%artiste% - %titre%" });
        Assert.Equal("Inna/Z/Inna - AB.flac", Rel(LibraryRules.Destination(_root, Tags("Inna", "A/B", "Z?"), rules, ".flac", tally: new Tally(_root))));
    }
}
