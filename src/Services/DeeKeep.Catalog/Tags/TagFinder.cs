using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.MusicBrainz;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;

namespace DeeKeep.Catalog.Tags;

/// <summary>Services interrogés par <see cref="TagFinder"/> (remplaçables dans les tests).</summary>
public interface ITagSources
{
    Task<IReadOnlyList<TagCandidate>> SearchDeezerAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<TagCandidate>> SearchMusicBrainzAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyDictionary<string, string>> DeezerDetailsAsync(string trackId, string albumId, CancellationToken ct);
    Task<IReadOnlyList<TagCandidate>> FingerprintAsync(string localPath, int duration, CancellationToken ct);
    Task<int?> EffectiveDurationAsync(string localPath, int declared, CancellationToken ct);
}

/// <summary>Services réels : Deezer, MusicBrainz, AcoustID, ffmpeg.</summary>
public sealed class TagSources(DeezerCatalog deezer, MusicBrainzApi musicBrainz, AcoustIdService acoustId, AudioProbe probe) : ITagSources
{
    public Task<IReadOnlyList<TagCandidate>> SearchDeezerAsync(string query, int limit, CancellationToken ct) =>
        deezer.SearchForTagsAsync(query, limit, ct);

    /// <summary>
    /// <c>search_musicbrainz</c> : 503 (quota) → une seule relance après 1,05 s ; tout autre
    /// échec → liste vide.
    /// </summary>
    public async Task<IReadOnlyList<TagCandidate>> SearchMusicBrainzAsync(string query, int limit, CancellationToken ct)
    {
        try
        {
            var response = await musicBrainz.SearchRecordingsAsync(query, limit, MusicBrainzApi.TagsAgent, TimeSpan.FromSeconds(15), ct);
            if (response.Status == 503)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.05), ct);
                response = await musicBrainz.SearchRecordingsAsync(query, limit, MusicBrainzApi.TagsAgent, TimeSpan.FromSeconds(15), ct);
            }
            if (response.Status != 200 || response.Recordings is null) return [];
            return response.Recordings.Select(MusicBrainzRecords.ToCandidate).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return [];
        }
    }

    public Task<IReadOnlyDictionary<string, string>> DeezerDetailsAsync(string trackId, string albumId, CancellationToken ct) =>
        deezer.DetailsForTagsAsync(trackId, albumId, ct);

    public Task<IReadOnlyList<TagCandidate>> FingerprintAsync(string localPath, int duration, CancellationToken ct) =>
        acoustId.LookupAsync(localPath, duration, ct);

    public Task<int?> EffectiveDurationAsync(string localPath, int declared, CancellationToken ct) =>
        probe.EffectiveDurationAsync(localPath, declared, ct);
}

/// <summary>
/// <c>find_tags</c> / <c>_propose</c> : services interrogés dans l'ordre (Deezer, puis
/// MusicBrainz et l'empreinte seulement si un tag indispensable manque), seconde passe avec
/// la durée mesurée hors silence final quand rien de sûr n'a été trouvé.
/// </summary>
public sealed class TagFinder(ITagSources sources)
{
    public const string SilenceNote = " (durée mesurée hors silence final)";

    /// <param name="entry">Tags actuels du fichier.</param>
    /// <param name="localPath">Chemin lisible par le service (empreinte, silence final) ; null si inaccessible.</param>
    /// <param name="duration">Durée de l'en-tête arrondie à la seconde (0 si inconnue).</param>
    public async Task<TagFound?> FindAsync(TagEntry entry, string? localPath, int duration, bool useAcoustId, bool extras, CancellationToken ct)
    {
        var absent = TagMatcher.MissingFields(entry);
        var wanted = extras && TagMatcher.NeedsExtras(entry) ? TagMatcher.MissingExtras(entry) : [];
        if (absent.Count == 0 && wanted.Count == 0) return null;

        var source = TagMatcher.TitleSource(entry);
        var proposal = await ProposeAsync(entry, localPath, absent, wanted, duration, source, useAcoustId, "", ct);
        if (proposal is { Sure: true }) return proposal;
        // Seuls des tags complémentaires manquaient : ni la remesure ni les autres services n'y changeraient rien.
        if (absent.Count == 0) return proposal;

        if (localPath is not null)
        {
            var useful = await sources.EffectiveDurationAsync(localPath, duration, ct);
            if (useful is { } u && Math.Abs(u - duration) > TagMatcher.DurationExact)
            {
                var retried = await ProposeAsync(entry, localPath, absent, wanted, u, source, useAcoustId, SilenceNote, ct);
                if (retried is not null && (proposal is null || retried.Score > proposal.Score)) return retried;
            }
        }
        return proposal;
    }

    private async Task<TagFound?> ProposeAsync(TagEntry entry, string? localPath, IReadOnlyList<string> absent, IReadOnlyList<string> extras,
        int duration, string source, bool useAcoustId, string note, CancellationToken ct)
    {
        var attempts = new List<(string Name, Func<Task<TagMatch?>> Run)>
        {
            ("Deezer", () => TagMatcher.BestFromTextAsync(entry, duration, source, TagMatcher.DeezerQueries(entry), sources.SearchDeezerAsync, ct)),
        };
        if (absent.Count > 0)
        {
            attempts.Add(("MusicBrainz", () => TagMatcher.BestFromTextAsync(entry, duration, source, TagMatcher.MusicBrainzQueries(entry),
                sources.SearchMusicBrainzAsync, ct)));
            if (useAcoustId && localPath is not null)
                attempts.Add(("AcoustID", async () => TagMatcher.BestFromFingerprint(await sources.FingerprintAsync(localPath, duration, ct), duration)));
        }

        foreach (var (name, run) in attempts)
        {
            var match = await run();
            if (match is null || match.Score < TagMatcher.MinScore) continue;
            IReadOnlyDictionary<string, string>? details = null;
            if (extras.Count > 0 && name == "Deezer")
            {
                // Vérifie d'abord que la piste comble les champs bloquants, avant deux appels de plus.
                if (TagMatcher.Assemble(entry, match, name, absent, [], null, duration, note) is null && absent.Count > 0) continue;
                details = await sources.DeezerDetailsAsync(match.Candidate.Id, match.Candidate.AlbumId, ct);
            }
            var found = TagMatcher.Assemble(entry, match, name, absent, extras, details, duration, note);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Durée de l'en-tête arrondie à la seconde (<c>local_duration</c>) : 0 si inconnue.</summary>
    public static int LocalDuration(double? seconds) => seconds is { } s and > 0 ? (int)Math.Round(s, MidpointRounding.ToEven) : 0;

    /// <summary>Entrée d'analyse à partir des tags lus sur le disque.</summary>
    public static TagEntry FromTags(string path, AudioTags tags) => new(
        path, tags.Title, tags.TitleTagged, tags.Artist, tags.Album, tags.AlbumArtist, tags.Genre, tags.Date, tags.Track, tags.TrackTotal, tags.Disc);

    /// <summary>Entrée d'analyse à partir de la demande (fichier inaccessible au service).</summary>
    public static TagEntry FromRequest(DeeKeep.Contracts.Catalog.TagLookupFile file)
    {
        var title = file.Title ?? "";
        var tagged = file.TitleTagged ?? title.Length > 0;
        // « 3/12 » : le numéro et le total sont deux tags distincts.
        var trackNo = file.TrackNo ?? "";
        var slash = trackNo.IndexOf('/');
        var track = slash >= 0 ? trackNo[..slash] : trackNo;
        var total = file.TrackTotal ?? (slash >= 0 ? trackNo[(slash + 1)..] : "");
        return new TagEntry(file.Path, tagged ? title : PyPath.Stem(file.Path), tagged, file.Artist ?? "", file.Album ?? "", file.AlbumArtist ?? "",
            file.Genre ?? "", file.Year ?? "", PyText.Strip(track), PyText.Strip(total), file.Disc ?? "");
    }
}
