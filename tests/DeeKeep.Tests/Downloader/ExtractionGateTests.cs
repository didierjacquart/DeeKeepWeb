using DeeKeep.Downloader.Extraction;
using DeeKeep.Downloader.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeeKeep.Tests.Downloader;

/// <summary>Parallélisme des extractions adapté à la machine et à sa charge.</summary>
public class ExtractionGateTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class FakeProbe(long? total, params LoadSample[] samples) : ISystemLoadProbe
    {
        private readonly Queue<LoadSample> _samples = new(samples);
        public long? TotalMemory => total;
        public LoadSample Sample() => _samples.Count > 0 ? _samples.Dequeue() : new LoadSample(null, null, null);
    }

    private static ExtractionGate Gate(DownloaderOptions options, FakeProbe probe) =>
        new(Options.Create(options), probe, NullLogger<ExtractionGate>.Instance);

    [Theory]
    [InlineData(4, 4L, 4)] // Raspberry Pi 4, 4 Go : un par cœur.
    [InlineData(4, 1L, 1)] // 1 Go : la mémoire borne.
    [InlineData(4, 2L, 3)]
    [InlineData(16, 32L, ParallelismPolicy.AutoCap)] // Grosse machine : YouTube borne.
    public void Ceiling_follows_cores_and_memory(int cores, long memoryGb, int expected) =>
        Assert.Equal(expected, ParallelismPolicy.Ceiling(new DownloaderOptions(), cores, memoryGb * Gb));

    [Fact]
    public void Configured_maximum_wins_over_the_machine()
    {
        Assert.Equal(10, ParallelismPolicy.Ceiling(new DownloaderOptions { MaxParallel = 10 }, 2, 1 * Gb));
        Assert.Equal(3, ParallelismPolicy.Ceiling(new DownloaderOptions { MinParallel = 3 }, 1, 1 * Gb));
        Assert.Equal(4, ParallelismPolicy.Ceiling(new DownloaderOptions(), 4, null));
    }

    [Fact]
    public void Next_lowers_under_pressure_and_raises_with_headroom()
    {
        var idle = new LoadSample(0.30, 3 * Gb, 4 * Gb);
        var busyCpu = new LoadSample(0.95, 3 * Gb, 4 * Gb);
        var lowMemory = new LoadSample(0.30, (long)(0.05 * 4 * Gb), 4 * Gb);
        var middling = new LoadSample(0.80, 3 * Gb, 4 * Gb);

        Assert.Equal(3, ParallelismPolicy.Next(2, 1, 4, idle, saturated: true, coolingDown: false));
        Assert.Equal(2, ParallelismPolicy.Next(2, 1, 4, idle, saturated: false, coolingDown: false));
        Assert.Equal(2, ParallelismPolicy.Next(2, 1, 4, idle, saturated: true, coolingDown: true));
        Assert.Equal(4, ParallelismPolicy.Next(4, 1, 4, idle, saturated: true, coolingDown: false));
        Assert.Equal(1, ParallelismPolicy.Next(2, 1, 4, busyCpu, saturated: true, coolingDown: false));
        Assert.Equal(1, ParallelismPolicy.Next(1, 1, 4, busyCpu, saturated: true, coolingDown: false));
        Assert.Equal(2, ParallelismPolicy.Next(3, 1, 4, lowMemory, saturated: true, coolingDown: false));
        Assert.Equal(3, ParallelismPolicy.Next(3, 1, 4, middling, saturated: true, coolingDown: false));
        // Mesures inconnues (poste Windows) : la hausse n'est pas bloquée.
        Assert.Equal(3, ParallelismPolicy.Next(2, 1, 4, new LoadSample(null, null, null), saturated: true, coolingDown: false));
    }

    [Fact]
    public async Task Gate_admits_up_to_the_limit_and_follows_its_changes()
    {
        using var gate = Gate(new DownloaderOptions { MaxParallel = 3, AdaptiveParallel = false }, new FakeProbe(null));
        gate.SetLimit(1);
        var first = await gate.EnterAsync(CancellationToken.None);
        var second = gate.EnterAsync(CancellationToken.None);
        var third = gate.EnterAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);

        gate.SetLimit(2);
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(1, gate.Status().Active);

        // Une baisse n'interrompt rien : elle retient les suivantes.
        var other = await gate.EnterAsync(CancellationToken.None);
        gate.SetLimit(1);
        var waiting = gate.EnterAsync(CancellationToken.None);
        first.Dispose();
        Assert.False(waiting.IsCompleted);
        other.Dispose();
        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(0, gate.Status().Active);
    }

    [Fact]
    public async Task Cancelled_waiter_leaves_its_turn()
    {
        using var gate = Gate(new DownloaderOptions { MaxParallel = 1 }, new FakeProbe(null));
        var held = await gate.EnterAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var cancelled = gate.EnterAsync(cts.Token);
        var next = gate.EnterAsync(CancellationToken.None);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        held.Dispose();
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(new ParallelismStatus(1, 1, 0, 0, false, null), gate.Status());
    }

    [Fact]
    public async Task Adjust_raises_when_extractions_wait_and_backs_off_under_load()
    {
        var idle = new LoadSample(0.20, 3 * Gb, 4 * Gb);
        var busy = new LoadSample(0.97, 3 * Gb, 4 * Gb);
        // Le constructeur consomme une première mesure (référence du processeur).
        var probe = new FakeProbe(4 * Gb, idle, idle, busy, idle, idle);
        using var gate = Gate(new DownloaderOptions { MaxParallel = 4 }, probe);
        Assert.True(gate.Adaptive);
        Assert.Equal(2, gate.Limit);

        var slots = new List<IDisposable> { await gate.EnterAsync(CancellationToken.None), await gate.EnterAsync(CancellationToken.None) };
        var waiting = gate.EnterAsync(CancellationToken.None);
        gate.Adjust();
        Assert.Equal(3, gate.Limit);
        slots.Add(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));

        gate.Adjust();
        Assert.Equal(2, gate.Limit);
        // Refroidissement : pas de remontée juste après une baisse.
        var queued = gate.EnterAsync(CancellationToken.None);
        gate.Adjust();
        Assert.Equal(2, gate.Limit);
        foreach (var s in slots) s.Dispose();
        (await queued.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }
}
