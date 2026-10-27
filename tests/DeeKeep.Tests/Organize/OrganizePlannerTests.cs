using System.Text.Json;
using DeeKeep.Contracts.Organize;
using DeeKeep.Library.Organize;
using Xunit;

namespace DeeKeep.Tests.Organize;

/// <summary>
/// Plan de rangement, « Divers », albums préservés, doublons : comparés aux sorties du Python d'origine
/// (<c>organize_page.build_plan</c>, <c>dedup_dialog.duplicate_groups</c>, <c>canonical</c>), produites avec
/// <c>python -I -B</c> depuis D:\000_Logiciel\DeezerMusic (PyQt6 simulé) sur des fichiers fictifs.
/// </summary>
public class OrganizePlannerTests
{
    private static readonly JsonDocument Doc = JsonDocument.Parse(OrganizeVectors.Json);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";

    private static OrganizeEntry ToEntry(JsonElement e, int index) => new()
    {
        Index = index,
        Path = Str(e, "path"),
        Format = Str(e, "format"),
        Title = Str(e, "title"),
        TitleTagged = e.GetProperty("title_tagged").GetBoolean(),
        Artist = Str(e, "artist"),
        Album = Str(e, "album"),
        AlbumArtist = Str(e, "albumartist"),
        Compilation = Str(e, "compilation"),
        Track = Str(e, "track"),
        Genre = Str(e, "genre"),
        Date = Str(e, "date"),
    };

    private static OrganizeOptions ToOptions(JsonElement o)
    {
        var d = new OrganizeOptions();
        return d with
        {
            Artist = o.TryGetProperty("artist", out var a) ? a.GetBoolean() : d.Artist,
            Album = o.TryGetProperty("album", out var al) ? al.GetBoolean() : d.Album,
            Rename = o.TryGetProperty("rename", out var r) && r.GetBoolean(),
            Pattern = o.TryGetProperty("pattern", out var p) ? p.GetString()! : d.Pattern,
            Divers = o.TryGetProperty("divers", out var dv) && dv.GetBoolean(),
            DiversMin = o.TryGetProperty("divers_min", out var dm) ? dm.GetInt32() : d.DiversMin,
        };
    }

    public static IEnumerable<TheoryDataRow<string>> Names() =>
        Doc.RootElement.GetProperty("scenarios").EnumerateArray().Select(s => new TheoryDataRow<string>(Str(s, "name")));

    private static JsonElement Scenario(string name) =>
        Doc.RootElement.GetProperty("scenarios").EnumerateArray().First(s => Str(s, "name") == name);

    private static PlanOutcome Run(JsonElement s)
    {
        var entries = s.GetProperty("entries").EnumerateArray().Select(ToEntry).ToList();
        return OrganizePlanner.BuildPlan(entries, Str(s, "root"), ToOptions(s.GetProperty("options")), new PlanEnvironment());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Plan_statuses_and_stats_match_python(string name)
    {
        var s = Scenario(name);
        var outcome = Run(s);
        var expected = s.GetProperty("expected").EnumerateArray().ToList();
        Assert.Equal(expected.Count, outcome.Results.Count);
        // Dans ces deux scénarios l'exemplaire gardé diffère volontairement du Python (règle unifiée, voir BestCopy) :
        // seuls les décomptes se comparent.
        var unifiedChoice = name is "dedup" or "rename";
        for (var i = 0; i < expected.Count && !unifiedChoice; i++)
        {
            Assert.Equal(Str(expected[i], "status"), outcome.Results[i].Status);
            Assert.Equal(Str(expected[i], "dst"), outcome.Results[i].Dst);
            Assert.Equal(expected[i].GetProperty("missing").EnumerateArray().Select(m => m.GetString()), outcome.Results[i].Missing);
        }
        var st = s.GetProperty("stats");
        if (!unifiedChoice) Assert.Equal(st.GetProperty("move").GetInt32(), outcome.Stats.Move);
        if (!unifiedChoice) Assert.Equal(st.GetProperty("inplace").GetInt32(), outcome.Stats.InPlace);
        Assert.Equal(st.GetProperty("untagged").GetInt32(), outcome.Stats.Untagged);
        Assert.Equal(st.GetProperty("duplicate").GetInt32(), outcome.Stats.Duplicate);
        if (!unifiedChoice) Assert.Equal(st.GetProperty("divers").GetInt32(), outcome.Stats.Divers);
        Assert.Equal(st.GetProperty("from_divers").GetInt32(), outcome.Stats.FromDivers);
        Assert.Equal(st.GetProperty("album").GetInt32(), outcome.Stats.Album);
        Assert.Equal(st.GetProperty("album_dirs").GetInt32(), outcome.Stats.AlbumDirs);
    }

    [Fact]
    public void Divers_works_both_ways()
    {
        var outcome = Run(Scenario("divers"));
        // Solo (1 titre) et Duo (2) vont dans Divers, Trio (3 avec celui de Divers... 4) en sort.
        Assert.Equal(3, outcome.Stats.Divers);
        Assert.Equal(1, outcome.Stats.FromDivers);
        Assert.Equal(OrganizeStatuses.InPlace, outcome.Results[7].Status);   // Solo sans album, déjà dans Divers : album non requis
    }

    [Fact]
    public void Preserved_albums_are_left_alone_but_single_artist_best_of_is_sorted()
    {
        var outcome = Run(Scenario("albums"));
        Assert.Equal(2, outcome.ProtectedDirs.Count);   // Footloose (musique de film) et Zen 2 (compilation)
        Assert.All(outcome.Results.Take(5), r => Assert.Equal(OrganizeStatuses.Album, r.Status));
        Assert.Equal(OrganizeStatuses.Move, outcome.Results[5].Status);   // Best of Blondie : un artiste domine
    }

    [Fact]
    public void Albums_are_not_preserved_when_the_option_is_off()
    {
        var s = Scenario("albums");
        var entries = s.GetProperty("entries").EnumerateArray().Select(ToEntry).ToList();
        var outcome = OrganizePlanner.BuildPlan(entries, Str(s, "root"), new OrganizeOptions(KeepAlbums: false), new PlanEnvironment());
        Assert.Equal(0, outcome.Stats.Album);
    }

    [Fact]
    public void Duplicates_keep_the_copy_stored_in_the_artist_folder()
    {
        var outcome = Run(Scenario("dedup"));
        Assert.Equal(2, outcome.Stats.Duplicate);
        // Python gardait le moins enfoui (x\y) ; la règle unifiée préfère le dossier de l'artiste à qualité égale.
        Assert.Equal(OrganizeStatuses.Move, outcome.Results[0].Status);   // l'exemplaire gardé est rangé (Toto n'a que 2 titres : Divers)
        Assert.Equal(@"D:\Lib\Divers\Africa.mp3", outcome.Results[0].Dst);
        Assert.Equal(OrganizeStatuses.Duplicate, outcome.Results[1].Status);
        Assert.Equal(OrganizeStatuses.Duplicate, outcome.Results[2].Status);
        Assert.Equal(@"D:\Lib\Toto\Toto IV\Africa.mp3", outcome.Results[1].Dst);
    }

    // ---- doublons fichier/fichier

    [Fact]
    public void Union_find_groups_match_python()
    {
        var entries = Doc.RootElement.GetProperty("dupEntries").EnumerateArray().Select(ToEntry).ToList();
        var index = new ArtistIndex(entries);
        var groups = DuplicateFinder.Groups(entries, index.EntryKey);
        var expected = Doc.RootElement.GetProperty("dupGroups").EnumerateArray()
            .Select(g => g.EnumerateArray().Select(i => i.GetInt32()).ToList()).ToList();
        Assert.Equal(expected.Count, groups.Count);
        for (var g = 0; g < expected.Count; g++)
            Assert.Equal(expected[g], groups[g].Select(e => e.Index).ToList());
    }

    [Fact]
    public void Duplicate_keys_match_python()
    {
        var entries = Doc.RootElement.GetProperty("dupEntries").EnumerateArray().Select(ToEntry).ToList();
        var index = new ArtistIndex(entries);
        var expected = Doc.RootElement.GetProperty("dupKeys").EnumerateArray().ToList();
        for (var i = 0; i < entries.Count; i++)
            Assert.Equal(expected[i].EnumerateArray().Select(k => k.GetString()!).Order(StringComparer.Ordinal),
                DuplicateFinder.Keys(entries[i], index.EntryKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Canonical_matches_python()
    {
        var canon = Doc.RootElement.GetProperty("canon");
        Assert.Equal("t 2020", OrganizeText.DedupCanonical("\u00c9t\u00e9 2020"));
        foreach (var p in canon.EnumerateObject().Where(p => p.Name.All(char.IsAscii)))
            Assert.Equal(p.Value.GetString(), OrganizeText.DedupCanonical(p.Name));
    }

    // ---- meilleur exemplaire

    [Fact]
    public void Best_copy_prefers_bitrate_then_size_class_then_layout()
    {
        var low = new CopyCandidate(@"D:\Lib\Toto\Africa.mp3", 128, 4_000_000, "MP3", "Toto");
        var high = new CopyCandidate(@"D:\Lib\Divers\x\Africa.mp3", 320, 9_000_000, "MP3", "Toto");
        Assert.Equal(1, BestCopy.Choose([low, high]));

        // À débit égal, un fichier à moins de 97 % du plus gros est un exemplaire appauvri.
        var small = new CopyCandidate(@"D:\Lib\Toto\a.mp3", 320, 8_000_000, "MP3", "Toto");
        var big = new CopyCandidate(@"D:\Lib\Divers\b.mp3", 320, 9_000_000, "MP3", "Toto");
        Assert.Equal(1, BestCopy.Choose([small, big]));

        // À 3 % près, le mieux rangé gagne (le seuil est effectif, contrairement à l'original).
        var nearly = new CopyCandidate(@"D:\Lib\Toto\Toto IV\a.mp3", 320, 8_900_000, "MP3", "Toto");
        Assert.Equal(1, BestCopy.Choose([big, nearly], diversRoot: @"D:\Lib\Divers"));
    }

    [Fact]
    public void Best_copy_target_format_beats_layout_but_not_quality()
    {
        var mp3 = new CopyCandidate(@"D:\Lib\Toto\a.mp3", 320, 9_000_000, "MP3", "Toto");
        var flac = new CopyCandidate(@"D:\Lib\Toto\a.flac", 320, 9_000_000, "FLAC", "Toto");
        Assert.Equal(1, BestCopy.Choose([mp3, flac], "flac"));
        var weakFlac = flac with { Bitrate = 128 };
        Assert.Equal(0, BestCopy.Choose([mp3, weakFlac], "flac"));
    }

    [Fact]
    public void Best_copy_is_stable_on_total_ties()
    {
        var a = new CopyCandidate(@"D:\Lib\x\a.mp3", 0, 0, "MP3", null);
        var b = new CopyCandidate(@"D:\Lib\x\a.mp3", 0, 0, "MP3", null);
        Assert.Equal(0, BestCopy.Choose([a, b]));
    }

    // ---- mises en forme

    [Fact]
    public void Status_text_and_filters_summary_follow_the_original_wording()
    {
        var stats = new OrganizePlanStats(10, 4, 3, 1, 2, 1, 1, 5, 2);
        Assert.Equal("10 fichiers analysés · 4 à ranger · 3 déjà en place · 2 doublon(s) à supprimer · 1 vers « Divers » · 1 sortent de « Divers » · 5 dans 2 album(s) préservé(s) · 1 sans tags exploitables",
            OrganizeEngine.StatusText(stats));
        Assert.Equal("dossier par artiste · dossier par album · musiques de film et compilations préservées · un seul exemplaire des doublons · dossiers vides supprimés · fichiers sans tags signalés",
            OrganizeEngine.FiltersSummary(new OrganizeOptions()));
        Assert.Equal("aucun", OrganizeEngine.FiltersSummary(new OrganizeOptions(false, false, false, "x", false, 3, false, false, false, false)));
    }

    [Fact]
    public void Name_tags_expose_the_menu_and_presets()
    {
        var tags = OrganizeEngine.NameTags();
        Assert.Equal(5, tags.Categories.Count);
        Assert.Equal(10, tags.Presets.Count);
        Assert.Equal("%titre% - %artiste%", tags.DefaultPattern);
    }
}
