using DeeKeep.Library.Tracks;
using Xunit;

namespace DeeKeep.Tests.Library;

/// <summary>État des fichiers sur le disque : lecture parallèle, états expirés relus en arrière-plan.</summary>
public sealed class DiskStateCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deekeep-disk-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string NewFile(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Unknown_files_are_read_now_in_parallel()
    {
        var files = Enumerable.Range(0, 100).Select(i => NewFile($"{i}.mp3", i + 1)).ToList();
        var missing = Path.Combine(_dir, "absent.mp3");
        var cache = new DiskStateCache();

        var states = cache.GetMany([.. files, missing, files[0]]);

        Assert.All(files.Select((f, i) => (f, i)), x => Assert.Equal(new DiskState(true, x.i + 1, states[x.f].MtimeNs), states[x.f]));
        Assert.False(states[missing].Exists);
    }

    [Fact]
    public async Task An_expired_state_is_served_then_refreshed_in_the_background()
    {
        var file = NewFile("a.mp3", 10);
        var cache = new DiskStateCache { Ttl = TimeSpan.Zero };
        Assert.True(cache.GetMany([file])[file].Exists);

        File.Delete(file);
        Assert.True(cache.GetMany([file])[file].Exists);   // ancien état, sans attendre le réseau
        await cache.Refresh;
        Assert.False(cache.GetMany([file])[file].Exists);   // relu en arrière-plan
    }

    [Fact]
    public void An_invalidated_file_is_read_again_at_once()
    {
        var file = NewFile("a.mp3", 10);
        var cache = new DiskStateCache();
        Assert.True(cache.GetMany([file])[file].Exists);

        File.Delete(file);
        cache.Invalidate([file]);

        Assert.False(cache.GetMany([file])[file].Exists);
    }
}
