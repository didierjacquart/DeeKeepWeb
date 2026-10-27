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
/// chaque affichage ; côté serveur, chaque requête le ferait pour 5 000 fichiers. Un état
/// reste valable quelques secondes, et toute écriture connue (enregistrement, suppression,
/// collecte, restauration) l'invalide aussitôt.
/// </summary>
public sealed class DiskStateCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, (DiskState State, long At)> _states = new(StringComparer.Ordinal);

    public DiskState Get(string localPath)
    {
        var now = Environment.TickCount64;
        if (_states.TryGetValue(localPath, out var cached) && now - cached.At < Ttl.TotalMilliseconds) return cached.State;
        var state = Stat(localPath);
        _states[localPath] = (state, now);
        return state;
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
