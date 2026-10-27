using System.Text.Json.Nodes;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.History;
using DeeKeep.Library.Presence;
using DeeKeep.Library.Settings;
using DeeKeep.Library.Tracks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Library;

/// <summary>Recherche multi-tags, propositions, réglages et écritures en base (ligne complète, empreinte h conforme).</summary>
public class LibraryDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deekeep-tests", Guid.NewGuid().ToString("N"));
    private readonly string _music;
    private readonly DeeKeepDb _db;
    private readonly SettingsStore _settings;
    private readonly LibraryPaths _paths;
    private readonly DiskStateCache _disk = new();
    private readonly TrackWriter _writer;
    private readonly PresenceService _presence;
    private readonly EventLog _events;

    public LibraryDataTests()
    {
        _music = Path.Combine(_dir, "music");
        Directory.CreateDirectory(_music);
        var options = Options.Create(new DatabaseOptions { MainPath = Path.Combine(_dir, "main.db"), WebPath = Path.Combine(_dir, "web.db") });
        _db = new DeeKeepDb(options);
        _db.EnsureSchema();
        var web = new WebDb(options);
        _events = new EventLog(web, new ServiceIdentity("library"), NullLogger<EventLog>.Instance);
        var config = new ConfigurationBuilder().Build();
        _settings = new SettingsStore(_db, _events, config, NullLogger<SettingsStore>.Instance);
        _settings.Set(new Dictionary<string, JsonNode?> { ["download_dir"] = _music });
        _paths = new LibraryPaths(new PathMap(Options.Create(new PathMapOptions())), _settings, config);
        _writer = new TrackWriter(_db);
        _presence = new PresenceService(_db, _paths, _events, new ServiceCollection().BuildServiceProvider(), NullLogger<PresenceService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static LibraryEntry Entry(string path, string? title, string? artist, string? album = null, string? genre = null, string? year = null,
        bool exists = true, string? codec = "mp3", int? bitrate = 128) =>
        new() { Path = path, Title = title, Artist = artist, Album = album, Genre = genre, Year = year, Exists = exists, Codec = codec, Bitrate = bitrate };

    [Fact]
    public void Search_requires_all_words_in_any_order_ignoring_accents()
    {
        var e = Entry(@"D:\Daft Punk\One More Time - Daft Punk.mp3", "One More Time", "Daft Punk", "Discovery", "Électro", "2001");
        var hay = TrackSearch.Haystack(e);
        Assert.True(TrackSearch.Matches(hay, TrackSearch.Tokens("daft one more")));
        Assert.True(TrackSearch.Matches(hay, TrackSearch.Tokens("ELECTRO 2001 mp3 128")));
        Assert.True(TrackSearch.Matches(hay, TrackSearch.Tokens("ore")));
        Assert.False(TrackSearch.Matches(hay, TrackSearch.Tokens("daft shakira")));
    }

    [Fact]
    public void Suggestions_count_each_value_once_under_best_field_and_cap_at_five()
    {
        var rows = new List<LibraryEntry>();
        for (var i = 0; i < 4; i++) rows.Add(Entry($@"D:\a{i}.mp3", $"T{i}", "Madonna"));
        rows.Add(new LibraryEntry { Path = @"D:\x.mp3", Title = "Madness", Artist = "Madness", AlbumArtist = "Madness", Exists = true });
        rows.Add(new LibraryEntry { Path = @"D:\y.mp3", Title = "Made", Artist = "Other", Album = "Made in Heaven", Exists = true });
        var values = TrackSearch.BuildValueIndex(rows);
        var found = TrackSearch.Suggest(values, "mad");
        Assert.Equal("Madonna", found[0].Value);
        Assert.Equal(4, found[0].Count);
        Assert.Equal(1, found.Count(s => s.Value == "Madness"));
        Assert.Equal("artist", found.First(s => s.Value == "Madness").Field);
        Assert.True(found.Count <= 5);
        Assert.Equal("Artiste · Madonna   (4 titres)", TrackSearch.SuggestionText(found[0]));
    }

    [Fact]
    public void Sort_is_stable_with_empty_values_last_and_numeric_cloud()
    {
        var a = Entry(@"D:\1.mp3", "a", "Beta");
        var b = Entry(@"D:\2.mp3", "b", null);
        var c = Entry(@"D:\3.mp3", "c", "Alpha");
        Assert.Equal(["c", "a", "b"], TrackSearch.Sort([a, b, c], "artist", false).Select(e => e.Title));
        Assert.Equal(["b", "a", "c"], TrackSearch.Sort([a, b, c], "artist", true).Select(e => e.Title));
        a.Archive = new ArchiveRow(a.Path, "x", "1:2", "k", 2, 128, 2);
        a.Stamp = "1:2";
        c.Archive = new ArchiveRow(c.Path, "x", "9:9", "k", 2, 128, 2);
        c.Stamp = "1:2";
        Assert.Equal(["a", "c", "b"], TrackSearch.Sort([b, c, a], "cloud", false).Select(e => e.Title));
    }

    [Fact]
    public void Archive_state_follows_stamp_rules()
    {
        var e = Entry(@"D:\a.mp3", "a", "x");
        Assert.Equal(ArchiveStates.None, e.ArchiveState);
        e.Archive = new ArchiveRow(e.Path, "s", "10:5", "deekeep/audio/source/aa/a.mp3", 5, 128, 5);
        e.Stamp = "10:5";
        Assert.Equal(ArchiveStates.UpToDate, e.ArchiveState);
        e.Stamp = "11:5";
        Assert.Equal(ArchiveStates.Stale, e.ArchiveState);
        e.Stamp = "";   // fichier absent : une ligne avec clé reste « à jour » (spec 02 §2.9)
        Assert.Equal(ArchiveStates.UpToDate, e.ArchiveState);
        Assert.True(e.Selectable(false) || !e.Exists);
        Assert.Equal("MP3 128", e.Format);
    }

    [Fact]
    public void Settings_keep_python_number_types_and_canonical_json()
    {
        var (all, changed) = _settings.Set(new Dictionary<string, JsonNode?>
        {
            ["qual_hiss_db"] = JsonNode.Parse("-65"),        // un client JavaScript envoie « -65 » pour -65.0
            ["org_divers_min"] = JsonNode.Parse("4.0"),
            ["acoustid_key"] = "secret",
        });
        Assert.Contains("qual_hiss_db", changed);
        Assert.DoesNotContain("acoustid_key", changed);
        using var conn = _db.Open(readOnly: true);
        Assert.Equal("-65.0", conn.ExecuteScalar<string>("SELECT value FROM app_settings WHERE key = 'qual_hiss_db'"));
        Assert.Equal("4", conn.ExecuteScalar<string>("SELECT value FROM app_settings WHERE key = 'org_divers_min'"));
        // Réécrire la même valeur ne change rien.
        Assert.Empty(_settings.Set(new Dictionary<string, JsonNode?> { ["qual_hiss_db"] = JsonNode.Parse("-65.0") }).Changed);
        Assert.Equal(-65.0, all["qual_hiss_db"]!.GetValue<double>());
        Assert.True(all.ContainsKey("qual_deep"));
    }

    [Fact]
    public void Track_rows_are_complete_with_valid_hash_and_tombstones_keep_the_key()
    {
        var row = new Dictionary<string, object?> { ["path"] = @"D:\a.mp3", ["title"] = "A", ["artist"] = "B", ["in_history"] = 0L, ["source"] = "library" };
        _writer.Write([], [row], []);
        _writer.Write([], [], [@"D:\a.mp3"]);
        using var conn = _db.Open(readOnly: true);
        var stored = conn.QueryRows("SELECT * FROM tracks WHERE path = 'D:\\a.mp3'").Single();
        Assert.Equal(1L, stored.Long("deleted"));
        Assert.Null(stored.Str("title"));
        Assert.True(stored.Dbl("updated_at") > 0);
        Assert.Equal(RowHash.Compute(Schema.Tracks, stored), stored.Long("h"));
    }

    private static string? SampleMp3 => File.Exists(DeeKeep.Tests.Audio.AudioTestSupport.SampleMp3) ? DeeKeep.Tests.Audio.AudioTestSupport.SampleMp3 : null;

    [Fact]
    public async Task Register_is_idempotent_keeps_history_and_dedupes_covers_then_delete_goes_to_trash()
    {
        if (SampleMp3 is not { } sample) return;
        var file = Path.Combine(_music, "Queen", "Yeah - Queen.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Copy(sample, file);
        var catalog = new TrackCatalog(_db, _paths, _disk, new BitrateBackfill(_db, new PathMap(Options.Create(new PathMapOptions())), _events, NullLogger<BitrateBackfill>.Instance));
        var registrar = new FileRegistrar(_db, _paths, _disk, _writer, catalog, _presence, _events, NullLogger<FileRegistrar>.Instance);

        var first = await registrar.RegisterAsync(new RegisterFileRequest(file, "download", AddToHistory: true), TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        Assert.True(first!.InHistory);
        Assert.Equal(0, first.HistoryPos);
        var second = await registrar.RegisterAsync(new RegisterFileRequest(file, "download", AddToHistory: true), TestContext.Current.CancellationToken);
        Assert.Equal(0, second!.HistoryPos);   // rejoué : pas de seconde entrée d'historique

        using (var conn = _db.Open(readOnly: true))
        {
            Assert.Equal(1L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM tracks WHERE deleted = 0"));
            var row = conn.QueryRows("SELECT * FROM tracks WHERE deleted = 0").Single();
            Assert.Equal("both", row.Str("source"));
            Assert.Equal(RowHash.Compute(Schema.Tracks, row), row.Long("h"));
            Assert.Equal(conn.ExecuteScalar<long>("SELECT COUNT(DISTINCT sha1) FROM covers"), conn.ExecuteScalar<long>("SELECT COUNT(*) FROM covers"));
        }

        var result = await registrar.DeleteAsync(new DeleteFilesRequest([first.Path], "test"), TestContext.Current.CancellationToken);
        Assert.Equal([first.Path], result.Deleted);
        Assert.False(File.Exists(file));
        Assert.True(Directory.EnumerateFiles(Path.Combine(_music, LibraryPaths.TrashFolder), "*.mp3", SearchOption.AllDirectories).Any());
        using var after = _db.Open(readOnly: true);
        // Titre de l'historique : la ligne reste consultable (source = history).
        Assert.Equal("history", after.ExecuteScalar<string>("SELECT source FROM tracks WHERE deleted = 0"));

        var history = new HistoryService(_db, _paths, _disk, _writer, _events, NullLogger<HistoryService>.Instance);
        Assert.Single(history.List());
        Assert.Equal(1, history.PurgeMissing().Removed);
        Assert.Empty(history.List());
        Assert.Equal(1L, after.ExecuteScalar<long>("SELECT COUNT(*) FROM tracks WHERE deleted = 1"));
    }

    [Fact]
    public void Fs_browser_refuses_paths_outside_roots_and_hides_system_folders()
    {
        Directory.CreateDirectory(Path.Combine(_music, "$RECYCLE.BIN"));
        Directory.CreateDirectory(Path.Combine(_music, "Album"));
        File.WriteAllText(Path.Combine(_music, "a.mp3"), "");
        File.WriteAllText(Path.Combine(_music, "notes.txt"), "");
        var fs = new FsService(_paths);
        var listing = fs.List(_music, audioOnly: true);
        Assert.Equal(["Album", "a.mp3"], listing.Entries.Select(e => e.Name));
        Assert.Throws<ArgumentException>(() => fs.List(_dir, false));
        Assert.Throws<ArgumentException>(() => fs.List(Path.GetPathRoot(_dir), false));
    }
}
