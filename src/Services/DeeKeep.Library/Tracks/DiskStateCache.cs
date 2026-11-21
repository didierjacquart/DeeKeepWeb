using System.Collections.Concurrent;
using DeeKeep.Common.Audio;

namespace DeeKeep.Library.Tracks;

/// <summary>Présence et empreinte d'un fichier au moment du dernier <c>stat</c>.</summary>
public readonly record struct DiskState(bool Exists, long Size, long MtimeNs)
{
    public string ArchiveStamp => Exists ? Stamp.Archive(MtimeNs, Size) : "";

    public double MtimeSeconds => MtimeNs / 1e9;
}

/// <summary>
/// Cache des <c>stat</c> de la bibliothèque. L'original relisait l'état de chaque fichier à
/// chaque affichage ; côté serveur, chaque requête le ferait pour des milliers de fichiers,
/// sur un partage réseau où chaque <c>stat</c> coûte un aller-retour (une vingtaine de
/// millisecondes en Wi-Fi : plus de 30 s pour 5 500 fichiers, 4 à la fois). Toute écriture
/// connue (enregistrement, suppression, collecte, restauration) invalide l'état aussitôt.
/// <para>
/// <see cref="GetMany"/> (liste de la bibliothèque) ne fait attendre que les fichiers jamais
/// vus : un état expiré est rendu tel quel et relu en arrière-plan, un seul rafraîchissement
/// à la fois. Les <c>stat</c> passent par des fils dédiés (pas le pool, qui ne grossit que
/// lentement quand ses fils bloquent sur le réseau).
/// </para>
/// </summary>
public sealed class DiskStateCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

    /// <summary><c>stat</c> simultanés : la latence du réseau, pas le processeur, fait la durée.</summary>
    public const int StatParallelism = 32;

    private readonly ConcurrentDictionary<string, (DiskState State, long At)> _states = new(StringComparer.Ordinal);
    private int _refreshing;

    /// <summary>Durée de validité d'un état (au-delà : relu en arrière-plan par <see cref="GetMany"/>).</summary>
    public TimeSpan Ttl { get; init; } = DefaultTtl;

    /// <summary>Rafraîchissement d'arrière-plan en cours, pour les tests.</summary>
    public Task Refresh { get; private set; } = Task.CompletedTask;

    private bool Fresh(long at, long now) => now - at < Ttl.TotalMilliseconds;

    public DiskState Get(string localPath)
    {
        var now = Environment.TickCount64;
        if (_states.TryGetValue(localPath, out var cached) && Fresh(cached.At, now)) return cached.State;
        var state = Stat(localPath);
        _states[localPath] = (state, now);
        return state;
    }

    /// <summary>
    /// États de nombreux fichiers : connus (même expirés, relus alors en arrière-plan) ou,
    /// pour les fichiers jamais vus ou invalidés, lus maintenant en parallèle.
    /// </summary>
    public Dictionary<string, DiskState> GetMany(IReadOnlyCollection<string> localPaths)
    {
        var now = Environment.TickCount64;
        var result = new Dictionary<string, DiskState>(localPaths.Count, StringComparer.Ordinal);
        var unknown = new List<string>();
        var stale = new List<KeyValuePair<string, (DiskState, long)>>();
        foreach (var path in localPaths)
        {
            if (result.ContainsKey(path)) continue;
            if (_states.TryGetValue(path, out var cached))
            {
                result[path] = cached.State;
                if (!Fresh(cached.At, now)) stale.Add(new(path, cached));
            }
            else
            {
                result[path] = default;
                unknown.Add(path);
            }
        }

        if (unknown.Count > 0)
        {
            var states = StatAll(unknown);
            var at = Environment.TickCount64;
            for (var i = 0; i < unknown.Count; i++)
            {
                _states[unknown[i]] = (states[i], at);
                result[unknown[i]] = states[i];
            }
        }
        if (stale.Count > 0) RefreshInBackground(stale);
        return result;
    }

    private void RefreshInBackground(List<KeyValuePair<string, (DiskState, long)>> stale)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        Refresh = Task.Run(() =>
        {
            try
            {
                var states = StatAll(stale.Select(s => s.Key).ToList());
                var at = Environment.TickCount64;
                // Seulement si l'entrée n'a pas changé entre-temps : une invalidation (écriture
                // connue) pendant la relecture ne doit pas être recouverte par un état plus ancien.
                for (var i = 0; i < stale.Count; i++) _states.TryUpdate(stale[i].Key, (states[i], at), stale[i].Value);
            }
            finally
            {
                Volatile.Write(ref _refreshing, 0);
            }
        });
    }

    private static DiskState[] StatAll(IReadOnlyList<string> paths)
    {
        var states = new DiskState[paths.Count];
        var workers = Math.Min(StatParallelism, paths.Count);
        if (workers <= 1)
        {
            for (var i = 0; i < paths.Count; i++) states[i] = Stat(paths[i]);
            return states;
        }
        var next = -1;
        var threads = Enumerable.Range(0, workers).Select(_ => new Thread(() =>
        {
            int i;
            while ((i = Interlocked.Increment(ref next)) < paths.Count) states[i] = Stat(paths[i]);
        }) { IsBackground = true, Name = "deekeep-stat" }).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        return states;
    }

    public void Invalidate(IEnumerable<string> localPaths)
    {
        foreach (var path in localPaths) _states.TryRemove(path, out _);
    }

    public void InvalidateAll() => _states.Clear();

    public static DiskState Stat(string localPath)
    {
        try
        {
            var info = new FileInfo(localPath);
            return info.Exists ? new DiskState(true, info.Length, Stamp.MtimeNs(info)) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }
}
