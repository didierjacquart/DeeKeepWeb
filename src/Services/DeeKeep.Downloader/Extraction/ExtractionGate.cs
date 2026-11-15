using System.Globalization;
using DeeKeep.Downloader.Settings;
using Microsoft.Extensions.Options;

namespace DeeKeep.Downloader.Extraction;

/// <summary>Mesure de la machine : part du processeur occupée (0..1) et mémoire, quand le système les expose.</summary>
public sealed record LoadSample(double? CpuBusy, long? MemoryAvailable, long? MemoryTotal)
{
    /// <summary>Part de la mémoire encore disponible (0..1), si connue.</summary>
    public double? MemoryFree => MemoryAvailable is { } a && MemoryTotal is > 0 ? a / (double)MemoryTotal.Value : null;
}

/// <summary>Source des mesures de charge (remplacée dans les tests).</summary>
public interface ISystemLoadProbe
{
    /// <summary>Mémoire physique totale, si connue.</summary>
    long? TotalMemory { get; }

    /// <summary>Charge depuis l'appel précédent (le premier appel ne connaît pas encore le processeur).</summary>
    LoadSample Sample();
}

/// <summary>
/// Charge lue dans <c>/proc</c> sous Linux (serveur cible) : temps processeur écoulé entre deux
/// mesures (<c>/proc/stat</c>, attente disque comptée comme repos) et <c>MemAvailable</c>
/// (<c>/proc/meminfo</c>). Ailleurs (poste de développement), seule la mémoire vue par .NET est connue.
/// </summary>
public sealed class ProcSystemLoadProbe : ISystemLoadProbe
{
    private (long Busy, long Total)? _lastCpu;

    public long? TotalMemory => OperatingSystem.IsLinux() ? MemInfo("MemTotal") : DotnetMemory();

    public LoadSample Sample()
    {
        if (!OperatingSystem.IsLinux()) return new LoadSample(null, null, DotnetMemory());
        double? cpu = null;
        if (CpuTimes() is { } now)
        {
            if (_lastCpu is { } prev && now.Total > prev.Total)
                cpu = Math.Clamp((now.Busy - prev.Busy) / (double)(now.Total - prev.Total), 0, 1);
            _lastCpu = now;
        }
        return new LoadSample(cpu, MemInfo("MemAvailable"), MemInfo("MemTotal"));
    }

    private static long? DotnetMemory()
    {
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return total > 0 ? total : null;
    }

    /// <summary>Ligne « cpu » de /proc/stat : user nice system idle iowait irq softirq steal…</summary>
    private static (long Busy, long Total)? CpuTimes()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            if (line is null) return null;
            var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
                .Select(v => long.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length < 5) return null;
            var total = values.Sum();
            var idle = values[3] + values[4];
            return (total - idle, total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static long? MemInfo(string key)
    {
        try
        {
            var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal));
            var kb = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
            return kb is not null && long.TryParse(kb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v * 1024 : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Règles du parallélisme adaptatif des extractions : un plafond tiré de la machine
/// (cœurs, mémoire), puis un ajustement pas à pas selon la charge mesurée.
/// </summary>
public static class ParallelismPolicy
{
    /// <summary>Plafond automatique : au-delà, YouTube se méfie quelle que soit la machine.</summary>
    public const int AutoCap = 6;

    /// <summary>Mémoire comptée par extraction (yt-dlp en Python + ffmpeg).</summary>
    public const long MemoryPerExtraction = 350L * 1024 * 1024;

    /// <summary>Mémoire laissée aux services, à SQLite et au système.</summary>
    public const long ReservedMemory = 768L * 1024 * 1024;

    public const double CpuHigh = 0.90;
    public const double CpuLow = 0.70;
    public const double MemoryLow = 0.12;
    public const double MemoryComfortable = 0.25;

    /// <summary>Après une baisse, pas de hausse avant ce nombre de mesures (évite le va-et-vient).</summary>
    public const int CooldownTicks = 3;

    /// <summary>
    /// Plafond pour cette machine : <c>Downloader:MaxParallel</c> s'il est fixé (&gt; 0), sinon
    /// un par cœur, borné par la mémoire et par <see cref="AutoCap"/>.
    /// </summary>
    public static int Ceiling(DownloaderOptions options, int cores, long? totalMemory)
    {
        var min = Math.Max(1, options.MinParallel);
        if (options.MaxParallel > 0) return Math.Max(min, options.MaxParallel);
        var byMemory = totalMemory is { } m ? (int)Math.Max(1, (m - ReservedMemory) / MemoryPerExtraction) : AutoCap;
        return Math.Max(min, Math.Min(Math.Min(Math.Max(1, cores), byMemory), AutoCap));
    }

    /// <summary>Point de départ : prudent (2 au plus), la hausse vient ensuite si la machine suit.</summary>
    public static int Initial(int min, int ceiling) => Math.Clamp(2, min, ceiling);

    /// <summary>
    /// Limite suivante : un cran de moins si le processeur sature ou si la mémoire manque ; un cran
    /// de plus si des extractions attendent (<paramref name="saturated"/>) et que la machine a de la
    /// marge ; sinon inchangée. Une mesure absente ne bloque pas la hausse.
    /// </summary>
    public static int Next(int current, int min, int ceiling, LoadSample sample, bool saturated, bool coolingDown)
    {
        var pressure = sample.CpuBusy >= CpuHigh || sample.MemoryFree < MemoryLow;
        if (pressure) return Math.Max(min, current - 1);
        var headroom = (sample.CpuBusy is null || sample.CpuBusy < CpuLow) &&
                       (sample.MemoryFree is null || sample.MemoryFree > MemoryComfortable);
        if (headroom && saturated && !coolingDown) return Math.Min(ceiling, current + 1);
        return Math.Clamp(current, min, ceiling);
    }
}

/// <summary>État courant du parallélisme (page Diagnostic).</summary>
public sealed record ParallelismStatus(int Limit, int Ceiling, int Active, int Waiting, bool Adaptive, LoadSample? Load);

/// <summary>
/// Plafond commun des extractions simultanées (Musique, Réencoder, Qualité confondus) :
/// chaque type de tâche a sa concurrence, mais yt-dlp et ffmpeg se partagent la machine.
/// Le nombre de places suit les performances du serveur : plafond tiré des cœurs et de la
/// mémoire, puis réévalué toutes les 10 s selon la charge (une place de moins quand le
/// processeur sature ou que la mémoire manque, une de plus quand des extractions attendent
/// et que la machine a de la marge). Une baisse n'interrompt rien : elle retient les suivantes.
/// </summary>
public sealed class ExtractionGate : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly LinkedList<TaskCompletionSource> _waiters = new();
    private readonly ISystemLoadProbe _probe;
    private readonly ILogger<ExtractionGate> _logger;
    private readonly Timer? _timer;
    private int _active;
    private int _limit;
    private int _cooldown;
    private LoadSample? _lastSample;

    public ExtractionGate(IOptions<DownloaderOptions> options, ISystemLoadProbe probe, ILogger<ExtractionGate> logger)
    {
        _probe = probe;
        _logger = logger;
        var o = options.Value;
        Min = Math.Max(1, o.MinParallel);
        Ceiling = ParallelismPolicy.Ceiling(o, Environment.ProcessorCount, probe.TotalMemory);
        Adaptive = o.AdaptiveParallel && Ceiling > Min;
        _limit = Adaptive ? ParallelismPolicy.Initial(Min, Ceiling) : Ceiling;
        logger.LogInformation("Extractions simultanées : {Limit} (plafond {Ceiling}, {Mode})", _limit, Ceiling,
            Adaptive ? "ajusté selon la charge" : "fixe");
        if (Adaptive)
        {
            probe.Sample(); // Référence du temps processeur pour la première mesure.
            _timer = new Timer(_ => Adjust(), null, Interval, Interval);
        }
    }

    public int Min { get; }
    public int Ceiling { get; }
    public bool Adaptive { get; }

    public int Limit
    {
        get { lock (_lock) return _limit; }
    }

    public ParallelismStatus Status()
    {
        lock (_lock) return new ParallelismStatus(_limit, Ceiling, _active, _waiters.Count, Adaptive, _lastSample);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LinkedListNode<TaskCompletionSource> node;
        lock (_lock)
        {
            if (_active < _limit && _waiters.Count == 0)
            {
                _active++;
                return new Releaser(this);
            }
            node = _waiters.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
        await using (ct.Register(() => CancelWaiter(node, ct)))
            await node.Value.Task;
        return new Releaser(this);
    }

    /// <summary>Nouvelle limite (bornée) ; les extractions en attente partent aussitôt s'il y a de la place.</summary>
    public void SetLimit(int limit)
    {
        lock (_lock)
        {
            _limit = Math.Clamp(limit, Min, Ceiling);
            Pump();
        }
    }

    /// <summary>Une mesure de charge et l'ajustement qui en découle (appelé par la minuterie).</summary>
    public void Adjust()
    {
        try
        {
            var sample = _probe.Sample();
            int before, after;
            lock (_lock)
            {
                _lastSample = sample;
                before = _limit;
                var saturated = _waiters.Count > 0 && _active >= _limit;
                after = ParallelismPolicy.Next(before, Min, Ceiling, sample, saturated, _cooldown > 0);
                _cooldown = after < before ? ParallelismPolicy.CooldownTicks : Math.Max(0, _cooldown - 1);
                _limit = after;
                Pump();
            }
            if (after != before)
                _logger.LogInformation("Extractions simultanées : {Before} → {After} (processeur {Cpu}, mémoire libre {Memory})",
                    before, after, Percent(sample.CpuBusy), Percent(sample.MemoryFree));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesure de charge impossible");
        }
    }

    private static string Percent(double? value) => value is { } v ? $"{v:P0}" : "inconnu";

    private void CancelWaiter(LinkedListNode<TaskCompletionSource> node, CancellationToken ct)
    {
        lock (_lock)
        {
            // Déjà servie : la place est prise, l'appelant la rendra en sortant.
            if (node.List is null) return;
            _waiters.Remove(node);
        }
        node.Value.TrySetCanceled(ct);
    }

    private void Release()
    {
        lock (_lock)
        {
            _active = Math.Max(0, _active - 1);
            Pump();
        }
    }

    /// <summary>Sert les attentes dans l'ordre d'arrivée tant qu'il reste des places (verrou tenu).</summary>
    private void Pump()
    {
        while (_active < _limit && _waiters.First is { } first)
        {
            _waiters.RemoveFirst();
            _active++;
            first.Value.TrySetResult();
        }
    }

    public void Dispose() => _timer?.Dispose();

    private sealed class Releaser(ExtractionGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
