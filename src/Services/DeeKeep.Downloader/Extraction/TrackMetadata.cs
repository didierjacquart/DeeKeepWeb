using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Clients;

namespace DeeKeep.Downloader.Extraction;

/// <summary>Titre, artiste, album, tags complémentaires et jaquette d'une piste à extraire.</summary>
public sealed class TrackInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long? DeezerId { get; set; }

    /// <summary>
    /// Tags complémentaires (vocabulaire de <see cref="AudioTagWriter.Fields"/>) : ceux de
    /// l'appelant (le fichier remplacé) font foi, le catalogue ne comble que les vides.
    /// </summary>
    public Dictionary<string, string?> ExtraTags { get; } = new(StringComparer.Ordinal);

    /// <summary>Jaquette d'origine (non marquée) : celle de l'historique ; le fichier reçoit la copie marquée.</summary>
    public byte[]? Cover { get; set; }
    public string? CoverUrl { get; set; }

    /// <summary><c>_tags()</c> : tags complémentaires, puis titre, artiste, album et format.</summary>
    public Dictionary<string, string?> Tags(string format)
    {
        var tags = new Dictionary<string, string?>(ExtraTags, StringComparer.Ordinal)
        {
            ["title"] = Title,
            ["artist"] = Artist,
            ["album"] = Album,
            ["format"] = format.ToUpperInvariant(),
        };
        return tags;
    }

    /// <summary><c>_resolve_extra_tags</c> : ajoute les champs du catalogue restés vides.</summary>
    public void FillFrom(DeezerTrackDetails details)
    {
        foreach (var (field, value) in TrackMetadata.DetailsTags(details))
            if (string.IsNullOrWhiteSpace(ExtraTags.GetValueOrDefault(field))) ExtraTags[field] = value;
    }
}

/// <summary>Métadonnées d'une piste : catalogue Deezer (via le service Catalog), tags d'un fichier, nom de fichier.</summary>
public static class TrackMetadata
{
    /// <summary>Champs complémentaires lus dans un fichier (le titre, l'artiste et l'album sont tenus à part).</summary>
    public static readonly IReadOnlyList<string> ExtraFields =
        ["albumartist", "genre", "date", "track", "tracktotal", "disc", "disctotal", "publisher"];

    /// <summary>
    /// <c>deezer_details</c> : rang et disque, année, genre, artiste d'album, nombre de pistes ;
    /// plus l'éditeur (label), que l'original ignorait alors que le motif <c>%editeur%</c> le réclame.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> DetailsTags(DeezerTrackDetails d)
    {
        if (d.TrackNumber is { } t and > 0) yield return new("track", t.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (d.DiscNumber is { } n and > 0) yield return new("disc", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var year = TagText.Year(d.Year);
        if (year.Length > 0) yield return new("date", year);
        if (!string.IsNullOrWhiteSpace(d.Genre)) yield return new("genre", d.Genre.Trim());
        if (!string.IsNullOrWhiteSpace(d.AlbumArtist)) yield return new("albumartist", d.AlbumArtist.Trim());
        if (d.TrackTotal is { } total and > 0) yield return new("tracktotal", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(d.Label)) yield return new("publisher", d.Label.Trim());
    }

    /// <summary>Tags complémentaires non vides d'un fichier lu.</summary>
    public static IEnumerable<KeyValuePair<string, string>> FileTags(AudioTags tags) =>
        ExtraFields.Select(f => new KeyValuePair<string, string>(f, (tags.Get(f) ?? "").Trim())).Where(kv => kv.Value.Length > 0);

    /// <summary>
    /// <c>library_rules.target_stem</c> sans règles : motif historique « %titre% - %artiste% »,
    /// puis « Titre - Artiste », le titre, l'artiste, et enfin « piste ».
    /// </summary>
    public static string TargetStem(IReadOnlyDictionary<string, string?> tags, string? pattern = null)
    {
        var stem = FileNames.SafeComponent(NamePattern.FormatName(pattern ?? NamePattern.DefaultPattern, tags));
        if (stem.Length > 0) return stem;
        var title = (tags.GetValueOrDefault("title") ?? "").Trim();
        var artist = (tags.GetValueOrDefault("artist") ?? "").Trim();
        foreach (var fallback in new[] { title.Length > 0 && artist.Length > 0 ? $"{title} - {artist}" : "", title, artist })
        {
            stem = FileNames.SafeComponent(fallback);
            if (stem.Length > 0) return stem;
        }
        return "piste";
    }

    /// <summary>Détails Deezer d'une piste, ou null si le catalogue est injoignable ou l'ignore (sans gravité).</summary>
    public static async Task<DeezerTrackDetails?> TryDetailsAsync(CatalogClient catalog, long id, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await catalog.GetTrackDetailsAsync(id, ct);
        }
        catch (ServiceUnavailableException ex)
        {
            logger.LogInformation("Détails Deezer de {Id} indisponibles : {Error}", id, ex.Message);
            return null;
        }
    }

    /// <summary>Octets d'une jaquette (cache d'images du catalogue), le premier lien qui répond.</summary>
    public static async Task<(byte[]? Bytes, string? Url)> TryCoverAsync(CatalogClient catalog, IEnumerable<string?> urls, ILogger logger, CancellationToken ct)
    {
        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
        {
            try
            {
                var bytes = await catalog.GetImageAsync(url!, ct);
                if (bytes is { Length: > 0 }) return (bytes, url);
            }
            catch (Exception ex) when (ex is HttpRequestException or ServiceUnavailableException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogInformation("Jaquette {Url} indisponible : {Error}", url, ex.Message);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// <c>_resolve_deezer_metadata</c> : correspondance Deezer à partir du titre et de l'artiste
    /// (fichier à réencoder sans correspondance, titre abîmé) ; remplace titre, artiste, album et
    /// jaquette si elle aboutit. Toute erreur est ignorée : on extrait alors avec les tags locaux.
    /// </summary>
    public static async Task<DeezerTrack?> TryBestMatchAsync(CatalogClient catalog, string artist, string title, double? duration, ILogger logger,
        CancellationToken ct)
    {
        try
        {
            return await catalog.BestMatchAsync(artist, title, duration, ct);
        }
        catch (ServiceUnavailableException ex)
        {
            logger.LogInformation("Correspondance Deezer indisponible : {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>Applique une correspondance et le détail du catalogue à une piste, puis cherche la jaquette pleine taille.</summary>
    public static async Task ApplyCatalogAsync(TrackInfo info, CatalogClient catalog, DeezerTrack? match, long? deezerId, ILogger logger,
        CancellationToken ct, bool namesFromDetails = false)
    {
        if (match is not null)
        {
            info.DeezerId ??= match.Id;
            if (!string.IsNullOrWhiteSpace(match.Title)) info.Title = match.Title;
            if (!string.IsNullOrWhiteSpace(match.Artist)) info.Artist = match.Artist;
            if (!string.IsNullOrWhiteSpace(match.Album)) info.Album = match.Album!;
        }
        var id = deezerId ?? info.DeezerId;
        DeezerTrackDetails? details = null;
        if (id is { } trackId)
        {
            details = await TryDetailsAsync(catalog, trackId, logger, ct);
            if (details is not null)
            {
                info.DeezerId = trackId;
                info.FillFrom(details);
                // Correspondance connue du seul identifiant (page Réencoder) : ses noms font foi.
                if (match is null && namesFromDetails)
                {
                    if (!string.IsNullOrWhiteSpace(details.Title)) info.Title = details.Title;
                    if (!string.IsNullOrWhiteSpace(details.Artist)) info.Artist = details.Artist;
                    if (!string.IsNullOrWhiteSpace(details.Album)) info.Album = details.Album!;
                }
            }
        }
        // Jaquette pleine taille : l'original intégrait la vignette 250×250 de la recherche.
        var (bytes, url) = await TryCoverAsync(catalog, [details?.CoverUrl, match?.CoverBig, match?.CoverSmall], logger, ct);
        if (bytes is not null)
        {
            info.Cover = bytes;
            info.CoverUrl = url;
        }
    }
}
