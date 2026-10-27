using System.Text.Json;
using System.Text.RegularExpressions;
using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.MusicBrainz;
using DeeKeep.Common.Data;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.Reference;

/// <summary>
/// Durées de référence d'un titre (<c>reference_duration.py</c>) : Deezer d'abord (cadence
/// 5/s), MusicBrainz en second recours (1/s), réponses mises en cache dans
/// <c>reference_durations</c> — négatives comprises (expirées après 30 jours).
/// L'écran Qualité retient côté client la durée la plus proche de la mesure
/// (<see cref="ReferenceDurations.Closest"/>).
/// </summary>
public sealed partial class ReferenceService(DeezerApi deezer, MusicBrainzApi musicBrainz, ReferenceStore store, ILogger<ReferenceService> logger)
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// <c>_canonical</c> : minuscules, tout ce qui n'est pas [a-z0-9] devient une espace. Les
    /// lettres accentuées sont donc supprimées (« Teló » → « tel »), pour retrouver les clés déjà en base.
    /// </summary>
    public static string Canonical(string? text) =>
        Spaces().Replace(NonAlnum().Replace((text ?? "").ToLowerInvariant(), " "), " ").Trim();

    public static string Key(string? artist, string? title) => Canonical(artist) + "|" + Canonical(title);

    /// <summary><c>_related</c> : l'un des titres commence par l'autre (les deux sens).</summary>
    public static bool Related(string candidate, string wanted) =>
        candidate.Length > 0 && wanted.Length > 0 &&
        (candidate.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(candidate, StringComparison.Ordinal));

    public async Task<ReferenceDurations> LookupAsync(string? artist, string? title, CancellationToken ct)
    {
        artist = (artist ?? "").Trim();
        title = (title ?? "").Trim();
        var key = Key(artist, title);
        if (artist.Length == 0 || title.Length == 0) return new ReferenceDurations(key, [], null, false);
        if (store.Get(key) is { } known) return new ReferenceDurations(key, known.Durations, known.FetchedAt, known.Durations.Count > 0);

        var found = await FromDeezerAsync(artist, title, ct);
        if (found is null) return new ReferenceDurations(key, [], null, false, Concluded: false);
        if (found.Count == 0)
        {
            found = await FromMusicBrainzAsync(artist, title, ct);
            if (found is null) return new ReferenceDurations(key, [], null, false, Concluded: false);
        }
        var at = Math.Round(Clock.UnixNow(), 3);
        var durations = found.Select(d => (double)d).ToList();
        store.Put(key, durations, at);
        return new ReferenceDurations(key, durations, at, durations.Count > 0);
    }

    /// <summary>Null : panne réseau, rien à conclure ni à mettre en cache.</summary>
    private async Task<List<int>?> FromDeezerAsync(string artist, string title, CancellationToken ct)
    {
        try
        {
            var response = await deezer.GetAsync(DeezerApi.Path("search", ("q", $"artist:\"{artist}\" track:\"{title}\""), ("limit", 25)),
                DeezerApi.ShortCache, DeezerApi.MatchTimeout, ct);
            var wanted = Canonical(title);
            List<int> close = [], all = [];
            foreach (var t in response.Data)
            {
                var seconds = (int)(t.Long("duration") ?? 0);
                if (seconds <= 0) continue;
                all.Add(seconds);
                if (Related(Canonical(t.Str("title")), wanted)) close.Add(seconds);
            }
            return close.Count > 0 ? close : all;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException)
        {
            logger.LogDebug("Deezer injoignable pour la référence : {Error}", ex.Message);
            return null;
        }
    }

    private async Task<List<int>?> FromMusicBrainzAsync(string artist, string title, CancellationToken ct)
    {
        try
        {
            MusicBrainzResponse? response = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                response = await musicBrainz.SearchRecordingsAsync($"artist:\"{artist}\" AND recording:\"{title}\"", 25,
                    MusicBrainzApi.ReferenceAgent, DeezerApi.MatchTimeout, ct);
                if (response.Status == 503 && attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2.2), ct);
                    continue;
                }
                break;
            }
            if (response is null || response.Status != 200 || response.Recordings is null) return null;
            var wanted = Canonical(title);
            var found = new List<int>();
            foreach (var r in response.Recordings)
            {
                if (MusicBrainzRecords.Score(r) < 90) continue;
                if (MusicBrainzRecords.LengthMs(r) is not { } ms || ms <= 0) continue;
                if (Related(Canonical(r.Str("title")), wanted)) found.Add(MusicBrainzRecords.RoundSeconds(ms));
            }
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return null;
        }
    }
}
