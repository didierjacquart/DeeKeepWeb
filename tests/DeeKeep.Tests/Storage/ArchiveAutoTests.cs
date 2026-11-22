using DeeKeep.Storage;
using Xunit;

namespace DeeKeep.Tests.Storage;

/// <summary>Archivage automatique : ce qui le déclenche, délai de calme, passage quotidien.</summary>
public class ArchiveAutoTests
{
    [Theory]
    [InlineData("register:download", true)]
    [InlineData("register:organize", true)]
    [InlineData("register:tags", true)]
    [InlineData("linked", false)]
    [InlineData("bitrate", false)]
    [InlineData("scan", false)]
    [InlineData(null, false)]
    public void Only_files_written_to_the_library_are_archived(string? reason, bool archived) =>
        Assert.Equal(archived, ArchiveAuto.IsFileWrite(reason));

    [Fact]
    public void Waits_for_the_library_to_be_quiet()
    {
        Assert.False(ArchiveAuto.QuietFor(lastChange: 100_000, now: 159_999, seconds: 60));
        Assert.True(ArchiveAuto.QuietFor(lastChange: 100_000, now: 160_000, seconds: 60));
    }

    [Fact]
    public void Nightly_pass_runs_once_after_the_hour()
    {
        var day = new DateTime(2026, 11, 22);
        Assert.False(ArchiveAuto.NightlyDue("04:00", day.AddHours(3.9), null));
        Assert.True(ArchiveAuto.NightlyDue("04:00", day.AddHours(4.2), null));
        Assert.False(ArchiveAuto.NightlyDue("04:00", day.AddHours(4.2), DateOnly.FromDateTime(day)));
        Assert.False(ArchiveAuto.NightlyDue("04:00", day.AddHours(10), null));
        Assert.False(ArchiveAuto.NightlyDue("", day.AddHours(4.2), null));
    }
}
