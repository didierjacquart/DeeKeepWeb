using DeeKeep.Contracts;
using DeeKeep.Storage.UsbSync;
using Xunit;

namespace DeeKeep.Tests.Storage;

public class UsbSyncPlannerTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SyncFile F(string path, long size = 100, int minutes = 0) => new(path, size, T0.AddMinutes(minutes));

    private static List<(SyncActionKind, string)> Plan(SyncFile[] lib, SyncFile[] usb, params SyncTombstone[] dead) =>
        UsbSyncPlanner.Plan(lib, usb, dead).Select(a => (a.Kind, a.Relative)).ToList();

    [Fact]
    public void Copies_what_is_missing_on_each_side()
    {
        var actions = Plan([F("Abba/Waterloo.mp3")], [F("Blur/Song 2.mp3")]);
        Assert.Equal([(SyncActionKind.CopyToUsb, "Abba/Waterloo.mp3"), (SyncActionKind.CopyToLibrary, "Blur/Song 2.mp3")], actions);
    }

    [Fact]
    public void Identical_files_are_left_alone_even_with_fat_rounding_or_a_dst_shift()
    {
        var lib = new[] { F("a.mp3"), F("b.mp3"), F("c.mp3") };
        var usb = new[]
        {
            new SyncFile("a.mp3", 100, T0.AddSeconds(1)),   // arrondi FAT (2 s)
            new SyncFile("B.MP3", 100, T0.AddHours(1)),      // casse différente, heure d'été
            new SyncFile("c.mp3", 100, T0.AddHours(-1).AddSeconds(2)),
        };
        Assert.Empty(Plan(lib, usb));
    }

    [Fact]
    public void A_file_deleted_by_hand_is_copied_back_incremental_sync()
    {
        // Effacé à la main du disque USB (aucune pierre tombale) : il y revient.
        Assert.Equal([(SyncActionKind.CopyToUsb, "a.mp3")], Plan([F("a.mp3")], []));
    }

    [Fact]
    public void The_newer_copy_wins()
    {
        Assert.Equal([(SyncActionKind.ReplaceOnLibrary, "a.mp3")], Plan([F("a.mp3", 100)], [F("a.mp3", 120, minutes: 10)]));
        Assert.Equal([(SyncActionKind.ReplaceOnUsb, "a.mp3")], Plan([F("a.mp3", 120, minutes: 10)], [F("a.mp3", 100)]));
    }

    [Fact]
    public void Same_date_different_size_trusts_the_library()
    {
        Assert.Equal([(SyncActionKind.ReplaceOnUsb, "a.mp3")], Plan([F("a.mp3", 120)], [F("a.mp3", 100)]));
    }

    [Fact]
    public void A_deletion_made_in_the_application_reaches_the_other_side()
    {
        var dead = new SyncTombstone("Abba/Waterloo.mp3", T0.AddDays(1));
        Assert.Equal([(SyncActionKind.TrashOnUsb, "Abba/Waterloo.mp3")], Plan([], [F("Abba/Waterloo.mp3")], dead));
        // Encore présent des deux côtés (suppression enregistrée avant que le fichier ne parte) : les deux.
        Assert.Equal([(SyncActionKind.TrashOnLibrary, "a.mp3"), (SyncActionKind.TrashOnUsb, "a.mp3")],
            Plan([F("a.mp3")], [F("a.mp3")], new SyncTombstone("a.mp3", T0.AddDays(1))));
    }

    [Fact]
    public void A_deletion_done_on_both_sides_is_forgotten()
    {
        Assert.Equal([(SyncActionKind.ForgetTombstone, "a.mp3")], Plan([], [], new SyncTombstone("a.mp3", T0)));
    }

    [Fact]
    public void A_file_written_after_the_deletion_is_a_new_file()
    {
        // Retéléchargé après la suppression : la pierre tombale tombe, la nouvelle copie part sur le disque.
        var dead = new SyncTombstone("a.mp3", T0);
        Assert.Equal([(SyncActionKind.ForgetTombstone, "a.mp3"), (SyncActionKind.ReplaceOnUsb, "a.mp3")],
            Plan([F("a.mp3", 200, minutes: 30)], [F("a.mp3", 100, minutes: -60)], dead));
    }

    [Fact]
    public void A_pair_left_identical_by_a_previous_run_is_not_copied_again()
    {
        // Le NAS a remis l'heure de la copie : sans mémoire, la copie passerait pour plus récente.
        var lib = F("a.mp3", 100, minutes: 600);
        var usb = F("a.mp3", 100);
        Assert.Equal([(SyncActionKind.ReplaceOnUsb, "a.mp3")], Plan([lib], [usb]));
        var pair = new SyncedPair("a.mp3", 100, lib.ModifiedUtc, 100, usb.ModifiedUtc);
        Assert.Empty(UsbSyncPlanner.Plan([lib], [usb], [], [pair]));
        // Un côté modifié depuis : la mémoire ne vaut plus.
        Assert.Single(UsbSyncPlanner.Plan([F("a.mp3", 120, minutes: 900)], [usb], [], [pair]));
    }

    [Fact]
    public void Keys_ignore_case_separators_and_accent_composition()
    {
        Assert.Equal(UsbSyncPlanner.Key("Angèle/Balance ton quoi.mp3"), UsbSyncPlanner.Key("ANGÈLE\\Balance Ton Quoi.MP3"));
    }

    [Fact]
    public void Nightly_run_happens_once_after_the_hour()
    {
        var day = new DateTime(2026, 11, 21);
        Assert.False(UsbSyncWatcher.NightlyDue("03:30", day.AddHours(3), null));
        Assert.True(UsbSyncWatcher.NightlyDue("03:30", day.AddHours(3.6), null));
        Assert.False(UsbSyncWatcher.NightlyDue("03:30", day.AddHours(3.6), DateOnly.FromDateTime(day)));
        Assert.False(UsbSyncWatcher.NightlyDue("03:30", day.AddHours(9), null)); // serveur éteint la nuit : pas de rattrapage en journée
        Assert.False(UsbSyncWatcher.NightlyDue(null, day.AddHours(4), null));
    }

    [Theory]
    [InlineData("register:download", true)]
    [InlineData("register:organize", true)]
    [InlineData("register:restore", true)]
    [InlineData("unlinked", false)]   // lien utilisateur seulement : aucun fichier ne change
    [InlineData("bitrate", false)]
    [InlineData("scan-progress", false)]
    [InlineData(null, false)]
    public void Only_file_changes_of_the_library_trigger_a_sync(string? reason, bool triggers) =>
        Assert.Equal(triggers, UsbSyncAfterChanges.IsLibraryFileChange(reason));

    [Fact]
    public void After_changes_a_sync_waits_for_the_running_one_and_skips_without_disk()
    {
        Assert.Equal(UsbSyncAfterChanges.Decision.Enqueue, UsbSyncAfterChanges.Decide(true, true, null));
        Assert.Equal(UsbSyncAfterChanges.Decision.Enqueue, UsbSyncAfterChanges.Decide(true, true, JobStatus.Queued));
        // Une synchronisation en cours a pu relever les dossiers avant l'arrivée des nouveaux fichiers.
        Assert.Equal(UsbSyncAfterChanges.Decision.Later, UsbSyncAfterChanges.Decide(true, true, JobStatus.Running));
        Assert.Equal(UsbSyncAfterChanges.Decision.Skip, UsbSyncAfterChanges.Decide(false, true, null));
        Assert.Equal(UsbSyncAfterChanges.Decision.Skip, UsbSyncAfterChanges.Decide(true, false, null));
    }

    [Fact]
    public void Relative_paths_cannot_escape_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "dk-root");
        Assert.Throws<ArgumentException>(() => UsbSyncDisk.Combine(root, "../outside.mp3"));
        Assert.EndsWith(Path.Combine("dk-root", "a", "b.mp3"), UsbSyncDisk.Combine(root, "a/b.mp3"));
    }
}
