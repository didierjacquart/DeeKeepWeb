using System.Diagnostics;

namespace DeeKeep.Catalog.Http;

/// <summary>
/// Cadence d'un service distant, globale au processus : deux appels sont espacés d'au
/// moins <see cref="Interval"/> — portage de <c>_throttle</c> (horloge monotone, attente
/// prise avant l'appel plutôt que subie sous forme de refus). Les appelants attendent
/// leur tour dans l'ordre d'arrivée ; l'annulation libère la place.
/// </summary>
public sealed class IntervalGate(TimeSpan interval)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _last = long.MinValue;

    public TimeSpan Interval { get; } = interval;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_last != long.MinValue)
            {
                var wait = Interval - Stopwatch.GetElapsedTime(_last);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
            _last = Stopwatch.GetTimestamp();
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Cadences partagées par tout le service (recherche, durées de référence, tags).</summary>
public sealed class CatalogGates(Microsoft.Extensions.Options.IOptions<CatalogOptions> options)
{
    public IntervalGate Deezer { get; } = new(TimeSpan.FromMilliseconds(Math.Max(0, options.Value.DeezerIntervalMs)));
    public IntervalGate MusicBrainz { get; } = new(TimeSpan.FromMilliseconds(Math.Max(0, options.Value.MusicBrainzIntervalMs)));
    public IntervalGate AcoustId { get; } = new(TimeSpan.FromMilliseconds(Math.Max(0, options.Value.AcoustIdIntervalMs)));
}
