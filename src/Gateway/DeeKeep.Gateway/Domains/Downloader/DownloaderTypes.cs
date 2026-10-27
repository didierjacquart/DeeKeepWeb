using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;
using DeeKeep.Contracts.Downloader;

namespace DeeKeep.Gateway.Domains.Downloader;

/// <summary>Piste Deezer à extraire (telle que la renvoie la recherche).</summary>
public sealed record DownloadTrackInput(
    long Id, string Title, string Artist, string? Album, double Duration, long? ArtistId = null, long? AlbumId = null,
    string? PreviewUrl = null, string? CoverSmall = null, string? CoverBig = null, string? Link = null, int? TrackPosition = null, int? DiskNumber = null)
{
    public DeezerTrack ToTrack() => new(Id, Title, Artist, ArtistId, Album, AlbumId, Duration, null, PreviewUrl, CoverSmall, CoverBig, Link,
        false, TrackPosition, DiskNumber);
}

/// <summary>Fichier local à convertir sur place, avec sa correspondance Deezer éventuelle.</summary>
public sealed record ConvertItemInput(string LocalPath, long? DeezerId = null);

/// <summary>Titre abîmé à retélécharger (tags du fichier).</summary>
public sealed record RedownloadItemInput(string DbPath, string Artist, string Title, string? Album = null, double? Duration = null);

/// <summary>Format et qualité d'export (Paramètres : <c>file_format</c>, <c>quality</c>) ; omis = valeurs des Paramètres.</summary>
public sealed record ExportOptionsInput(string Format, string Quality);
