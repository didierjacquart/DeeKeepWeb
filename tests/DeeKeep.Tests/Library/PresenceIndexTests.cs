using DeeKeep.Contracts.Library;
using DeeKeep.Library.Presence;
using Xunit;

namespace DeeKeep.Tests.Library;

/// <summary>Vecteurs de la spec 02 §3.3 (obtenus en exécutant library_index.py).</summary>
public class PresenceIndexTests
{
    private const string Root = @"D:\Depot";

    private static PresenceIndex Build()
    {
        var index = new PresenceIndex(Root);
        index.AddFile(@"D:\Depot\Daft Punk\Discovery\One More Time - Daft Punk.mp3", @"D:\Depot\Daft Punk\Discovery\One More Time - Daft Punk.mp3", null);
        index.AddFile(@"D:\Depot\Divers\Titanium (Radio Edit) - David Guetta Feat. Sia.mp3", @"D:\Depot\Divers\Titanium (Radio Edit) - David Guetta Feat. Sia.mp3", null);
        index.Add(@"D:\Depot\Flo Rida\Right Round - Flo Rida Feat. Ke$ha.mp3", @"D:\Depot\Flo Rida\Right Round - Flo Rida Feat. Ke$ha.mp3",
            "Right Round", "Flo Rida Feat. Ke$ha", "R.O.O.T.S.");
        return index;
    }

    [Theory]
    [InlineData("One More Time", "Daft Punk", "", PresenceState.Present, "même titre, même artiste")]
    [InlineData("Titanium", "David Guetta", "", PresenceState.Possible, "autre version du même titre")]
    [InlineData("Titanium (feat. Sia)", "David Guetta", "", PresenceState.Possible, "autre version du même titre")]
    [InlineData("Right Round", "Flo Rida", "", PresenceState.Possible, "même artiste principal")]
    [InlineData("Right Round (Remix)", "Flo Rida", "", PresenceState.Possible, "autre version")]
    [InlineData("One More Tim", "Daft Punk", "", PresenceState.Possible, "titre proche, artiste compatible")]
    [InlineData("One More Time", "Shakira", "", PresenceState.Absent, "Absent du dossier de dépôt")]
    [InlineData("Right Round", "Unknown", "R.O.O.T.S.", PresenceState.Possible, "même titre, même album")]
    public void Lookup_matches_python(string title, string artist, string album, PresenceState state, string reasonPart)
    {
        var match = Build().Lookup(title, artist, album);
        Assert.Equal(state, match.State);
        Assert.Contains(reasonPart, match.Reason);
    }

    [Fact]
    public void File_with_same_name_is_present()
    {
        var match = Build().Lookup("Yeah", "Queen");
        Assert.Equal(PresenceState.Absent, match.State);
        var index = Build();
        index.AddFile(@"D:\Depot\Yeah - Queen.mp3", @"D:\Depot\Yeah - Queen.mp3", null);
        Assert.Equal(PresenceState.Present, index.Lookup("Yeah", "Queen").State);
    }

    [Fact]
    public void Path_reading_uses_folder_to_decide_sides()
    {
        // Dossier d'artiste « Coldplay » : l'ordre du nom de fichier n'a pas d'importance.
        var a = PresenceIndex.FromPath(@"D:\Depot\Coldplay\Yellow - Coldplay.mp3", Root);
        var b = PresenceIndex.FromPath(@"D:\Depot\Coldplay\Coldplay - Yellow.mp3", Root);
        Assert.Equal([("Yellow", "Coldplay", "")], a);
        Assert.Equal([("Yellow", "Coldplay", "")], b);
        // À la racine : lecture « Titre - Artiste » puis lecture inverse.
        var c = PresenceIndex.FromPath(@"D:\Depot\Get Lucky - Daft Punk.mp3", Root);
        Assert.Equal([("Get Lucky", "Daft Punk", ""), ("Daft Punk", "Get Lucky", "")], c);
        var d = PresenceIndex.FromPath(@"D:\Depot\A - B - C.mp3", Root);
        Assert.Equal([("A - B", "C", ""), ("C", "A - B", "")], d);
    }

    [Fact]
    public void Remove_rebuilds_tables_and_lists_files_by_name()
    {
        var index = Build();
        var path = @"D:\Depot\Daft Punk\Discovery\One More Time - Daft Punk.mp3";
        Assert.True(index.HasPath(path.ToLowerInvariant()));
        Assert.Single(index.PathsNamed("one more time - daft punk.MP3"));
        Assert.True(index.Remove(path));
        Assert.False(index.Remove(path));
        Assert.Equal(PresenceState.Absent, index.Lookup("One More Time", "Daft Punk").State);
        Assert.Equal(2, index.Count);
    }
}
