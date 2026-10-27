using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Contracts.Storage;
using DeeKeep.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeeKeep.Tests.Storage;

public class ArchiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sha1(byte[] data) => Convert.ToHexStringLower(SHA1.HashData(data));

    private static async Task<JsonNode?> Upload(StorageHarness h, params string[] paths) =>
        await h.Uploader().ExecuteAsync(h.Context(StorageJobTypes.Archive, new ArchiveRequest(paths)), Ct);

    private static async Task<JsonNode?> Restore(StorageHarness h, params string[] paths) =>
        await h.Restorer().ExecuteAsync(h.Context(StorageJobTypes.Restore, new ArchiveRequest(paths)), Ct);

    [Fact]
    public void Object_keys_follow_the_original_layout()
    {
        const string sha = "eb6daf46789277f1091e4488d796624afd07d2a2";
        Assert.Equal($"deekeep/audio/source/eb/{sha}.mp3", ArchiveRules.ObjectKey("deekeep/", sha, ".MP3"));
        Assert.Equal("deekeep/", ArchiveRules.NormalizePrefix("/deekeep//"));
        Assert.Equal("", ArchiveRules.NormalizePrefix("  "));
        Assert.True(ArchiveRules.IsSourceCopy($"deekeep/audio/source/eb/{sha}.mp3"));
        Assert.False(ArchiveRules.IsSourceCopy($"deekeep/audio/128/eb/{sha}.opus"));
        Assert.Equal((@"D:\a\b.flac", true), ArchiveRules.RestoreTarget(@"D:\a\b.flac", "deekeep/audio/source/eb/x.flac"));
        Assert.Equal((@"D:\a\b.mp3", false), ArchiveRules.RestoreTarget(@"D:\a\b.m4a", "deekeep/audio/128/eb/x.opus"));
        Assert.Equal(ArchiveState.Absent, ArchiveRules.StateOf(null, "1:2", "1:2"));
        Assert.Equal(ArchiveState.Fresh, ArchiveRules.StateOf("k", "1:2", ""));
        Assert.Equal(ArchiveState.Fresh, ArchiveRules.StateOf("k", null, "1:2"));
        Assert.Equal(ArchiveState.Stale, ArchiveRules.StateOf("k", "1:2", "1:3"));
        Assert.Equal("1,2 Go", ArchiveRules.HumanBytes(1_200_000_000));
        Assert.Equal("12 o", ArchiveRules.HumanBytes(12));
    }

    [Fact]
    public void A_folder_or_extensionless_root_gets_the_default_file_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dk-nofile");
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "deekeep-archive.db")), ArchiveRules.ResolveArchiveFile(folder));
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "mine.db")), ArchiveRules.ResolveArchiveFile(Path.Combine(folder, "mine.db")));
        Assert.Null(ArchiveRules.ResolveArchiveFile("  "));
    }

    [Fact]
    public async Task An_archive_written_by_the_python_original_is_readable()
    {
        // Fixture produite par Assets/gen_archive.py (sqlite_store.SqliteStore.put) : 300 000 octets.
        var folder = Path.Combine(Path.GetTempPath(), "dk-py-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "deekeep-archive.db");
        File.Copy(Path.Combine(StorageHarness.AssetsDir(), "python-archive.db"), file);
        var expected = StorageHarness.Pattern(300000, 3);
        var key = $"deekeep/audio/source/{Sha1(expected)[..2]}/{Sha1(expected)}.mp3";

        var store = new ObjectStore(file, "deekeep/");
        Assert.Equal(300000, store.Head(key));
        Assert.Null(store.Head("deekeep/audio/source/00/absent.mp3"));
        Assert.Equal((1L, 300000L), store.Usage());
        Assert.Equal([(key, 300000L)], store.ListObjects());

        var destination = Path.Combine(folder, "out", "rendu.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Assert.Equal(300000, await store.DownloadAsync(key, destination, Sha1(expected), Ct));
        Assert.Equal(expected, await File.ReadAllBytesAsync(destination, Ct));

        // Lecture positionnée (requêtes Range).
        await using (var stream = store.OpenObject(key)!)
        {
            stream.Seek(1000, SeekOrigin.Begin);
            var buffer = new byte[16];
            Assert.Equal(16, await stream.ReadAsync(buffer, Ct));
            Assert.Equal(expected[1000..1016], buffer);
        }
        store.Check();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task An_archive_written_here_keeps_the_python_schema()
    {
        var h = new StorageHarness();
        var data = StorageHarness.Pattern(2_500_000, 11);
        var store = new ObjectStore(Path.Combine(h.ArchiveDir, "deekeep-archive.db"), "deekeep/");
        await using (var source = new MemoryStream(data)) await store.PutAsync("deekeep/audio/source/aa/test.mp3", source, data.Length, Sha1(data), Ct);

        using (var conn = store.OpenRead()!)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            var tables = new Dictionary<string, string>();
            while (reader.Read()) tables[reader.GetString(0)] = reader.GetString(1);
            Assert.Contains("CREATE TABLE objects (    key    TEXT PRIMARY KEY,    bytes  INTEGER NOT NULL,    stored_at REAL NOT NULL,    data   BLOB NOT NULL)", tables["objects"]);
            Assert.Contains("CREATE TABLE archive_meta (    key TEXT PRIMARY KEY,    value TEXT)", tables["archive_meta"]);
        }
        Assert.Equal(2_500_000, store.Head("deekeep/audio/source/aa/test.mp3"));

        // Un fichier modifié entre hachage et copie n'entre pas.
        await Assert.ThrowsAsync<ArchiveDigestException>(async () =>
        {
            await using var changed = new MemoryStream(data);
            await store.PutAsync("deekeep/audio/source/bb/other.mp3", changed, data.Length, new string('0', 40), Ct);
        });
        Assert.Null(store.Head("deekeep/audio/source/bb/other.mp3"));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Identical_files_share_one_object_and_unchanged_files_are_skipped()
    {
        var h = new StorageHarness();
        var same = StorageHarness.Pattern(70_000, 1);
        var a = h.WriteFile(@"A\one.mp3", same);
        var b = h.WriteFile(@"B\copy.mp3", same);
        var c = h.WriteFile(@"C\other.mp3", StorageHarness.Pattern(50_000, 2));

        var first = (await Upload(h, a, b, c))!;
        Assert.Equal(2, (int)first["sent"]!);          // a et c écrits
        Assert.Equal(1, (int)first["skipped"]!);       // b : objet déjà présent (réutilisé)
        var store = ArchiveSettings.Open(h.Settings.Get());
        Assert.Equal(2L, store.Usage().Count);
        var rows = h.Index.Live();
        Assert.Equal(rows.Find(a)!.Key, rows.Find(b)!.Key);
        Assert.Equal(Sha1(same), rows.Find(a)!.Sha1);
        Assert.Equal(Stamp.Archive(a), rows.Find(a)!.Stamp);
        Assert.Equal(70_000, rows.Find(b)!.Bytes);
        Assert.NotNull(rows.Find(a)!.UploadedAt);

        // Deuxième passage : tout est à jour, sans relecture ni écriture.
        var second = (await Upload(h, a, b, c))!;
        Assert.Equal(0, (int)second["sent"]!);
        Assert.Equal(3, (int)second["skipped"]!);

        // Un fichier changé est recopié sous une nouvelle clé.
        File.WriteAllBytes(c, StorageHarness.Pattern(51_000, 9));
        var third = (await Upload(h, c))!;
        Assert.Equal(1, (int)third["sent"]!);
        Assert.Equal(3L, store.Usage().Count);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Removing_a_title_keeps_an_object_still_shared_by_another()
    {
        var h = new StorageHarness();
        var same = StorageHarness.Pattern(40_000, 5);
        var a = h.WriteFile("a.mp3", same);
        var b = h.WriteFile("b.mp3", same);
        await Upload(h, a, b);
        var store = ArchiveSettings.Open(h.Settings.Get());
        var key = h.Index.Live().Find(a)!.Key!;

        Assert.Equal(1, await h.Service.RemoveAsync([a], Ct));
        Assert.NotNull(store.Head(key));                    // b en dépend encore
        Assert.Null(h.Index.Live().Find(a));
        Assert.NotNull(h.Index.Live().Find(b));

        Assert.Equal(1, await h.Service.RemoveAsync([b], Ct));
        Assert.Null(store.Head(key));                       // plus personne
        Assert.True(File.Exists(a) && File.Exists(b));      // les fichiers du disque ne sont jamais touchés
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Restore_writes_only_missing_files_verifies_them_and_keeps_uploaded_at()
    {
        var h = new StorageHarness();
        var dataA = StorageHarness.Pattern(120_000, 4);
        var dataB = StorageHarness.Pattern(30_000, 8);
        var a = h.WriteFile(@"Artist\Album\a.mp3", dataA, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var b = h.WriteFile(@"Artist\Album\b.mp3", dataB);
        await Upload(h, a, b);
        var before = h.Index.Live().Find(a)!;

        File.Delete(b);
        File.Delete(a);
        Directory.Delete(Path.GetDirectoryName(a)!, true);       // l'arborescence disparaît aussi
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllBytes(b, [1, 2, 3]);                        // b est présent : jamais écrasé
        var result = (await Restore(h, a, b))!;
        Assert.Equal(1, (int)result["restored"]!);
        Assert.Equal(1, (int)result["skipped"]!);
        Assert.Equal(dataA, await File.ReadAllBytesAsync(a, Ct));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(b, Ct));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(a)!, ".dk_*"));

        var after = h.Index.Live().Find(a)!;
        Assert.Equal(before.UploadedAt, after.UploadedAt);       // défaut D6 de l'original corrigé
        Assert.Equal(Stamp.Archive(a), after.Stamp);              // sinon « à renvoyer »
        Assert.Equal(ArchiveState.Fresh, ArchiveRules.StateOf(after.Key, after.Stamp, Stamp.Archive(a)));
        Assert.True(h.Outbox.PendingCount() > 0);                // enregistrement auprès de la Bibliothèque
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task A_restored_copy_with_a_wrong_fingerprint_is_discarded()
    {
        var h = new StorageHarness();
        var path = h.WriteFile("x.mp3", StorageHarness.Pattern(20_000, 6));
        await Upload(h, path);
        var row = h.Index.Live().Find(path)!;
        h.Index.Upsert(row with { Sha1 = new string('a', 40) });
        File.Delete(path);

        var result = (await Restore(h, path))!;
        Assert.Equal(1, (int)result["failed"]!);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(h.Library, ".dk_*"));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Plan_counts_what_can_be_archived_and_restored()
    {
        var h = new StorageHarness();
        var a = h.WriteFile("p1.mp3", StorageHarness.Pattern(10_000, 1));
        var b = h.WriteFile("p2.mp3", StorageHarness.Pattern(10_001, 1));
        await Upload(h, a, b);
        File.Delete(b);
        var plan = h.Service.Plan([a, b]);
        Assert.Equal(2, plan.Requested);
        Assert.Equal(1, plan.OnDisk);
        Assert.Equal(1, plan.Restorable);
        Assert.True(plan.RestorableElsewhere >= 1);
        Assert.True(plan.Configured);
        SqliteConnection.ClearAllPools();
    }
}
