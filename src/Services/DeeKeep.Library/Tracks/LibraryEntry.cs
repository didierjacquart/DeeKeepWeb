using DeeKeep.Common.Data;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Library.Tracks;

/// <summary>Ligne vivante de la table <c>archive</c> (lue, jamais écrite : propriété du service Storage).</summary>
public sealed record ArchiveRow(string Path, string? Sha1, string? Stamp, string? Key, long? Bytes, int? Bitrate, long? SourceBytes);

/// <summary>
/// Un titre de la page Bibliothèque : ligne de <c>tracks</c>, présence sur le disque
/// (stat), état d'archive — l'entrée en mémoire de <c>library_page.reload</c> (spec 02 §5.4).
/// </summary>
public sealed class LibraryEntry
{
    public required string Path { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public string? Year { get; init; }
    public string? TrackNo { get; init; }
    public double? Duration { get; init; }
    public int? Bitrate { get; set; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public string? Codec { get; init; }
    public string? Ext { get; init; }
    public long? FileSize { get; set; }
    public double? FileMtime { get; init; }
    public string? CoverSha1 { get; init; }
    public string? Source { get; init; }
    public bool InHistory { get; init; }
    public int? HistoryPos { get; init; }
    public string? HistoryCoverSha1 { get; init; }
    public double UpdatedAt { get; init; }

    public bool Exists { get; set; }
    /// <summary>Empreinte « date:taille » courante au format de la table <c>archive</c>, "" si absent.</summary>
    public string Stamp { get; set; } = "";
    public ArchiveRow? Archive { get; set; }
    public string LocalPath { get; set; } = "";

    /// <summary><c>_format_of</c> : « MP3 320 », « FLAC », « OPUS 128 ».</summary>
    public string Format
    {
        get
        {
            var codec = (string.IsNullOrWhiteSpace(Codec) ? Ext ?? "" : Codec).Trim().TrimStart('.').ToUpperInvariant();
            var rate = Bitrate ?? 0;
            if (codec.Length > 0 && rate != 0) return $"{codec} {rate}";
            return codec;
        }
    }

    /// <summary><c>state_of</c> (spec 02 §2.9) : absent, à jour, à renvoyer.</summary>
    public string ArchiveState
    {
        get
        {
            if (Archive is null || string.IsNullOrEmpty(Archive.Key)) return ArchiveStates.None;
            if (Stamp.Length > 0 && !string.IsNullOrEmpty(Archive.Stamp) && Archive.Stamp != Stamp) return ArchiveStates.Stale;
            return ArchiveStates.UpToDate;
        }
    }

    /// <summary><c>cloud_order</c> : à jour 0, à renvoyer 1, absent 2.</summary>
    public int CloudOrder => ArchiveState switch { ArchiveStates.UpToDate => 0, ArchiveStates.Stale => 1, _ => 2 };

    /// <summary><c>_selectable</c> : présent, ou case « Titres absents » cochée, ou copie d'archive à jour.</summary>
    public bool Selectable(bool includeMissing) => Exists || includeMissing || ArchiveState == ArchiveStates.UpToDate;

    public static LibraryEntry FromRow(IReadOnlyDictionary<string, object?> row) => new()
    {
        Path = row.Str("path") ?? "",
        Title = row.Str("title"),
        Artist = row.Str("artist"),
        Album = row.Str("album"),
        AlbumArtist = row.Str("album_artist"),
        Genre = row.Str("genre"),
        Year = row.Str("year"),
        TrackNo = row.Str("track_no"),
        Duration = row.Dbl("duration"),
        Bitrate = (int?)row.Long("bitrate"),
        SampleRate = (int?)row.Long("sample_rate"),
        Channels = (int?)row.Long("channels"),
        Codec = row.Str("codec"),
        Ext = row.Str("ext"),
        FileSize = row.Long("file_size"),
        FileMtime = row.Dbl("file_mtime"),
        CoverSha1 = row.Str("cover_sha1"),
        Source = row.Str("source"),
        InHistory = (row.Long("in_history") ?? 0) != 0,
        HistoryPos = (int?)row.Long("history_pos"),
        HistoryCoverSha1 = row.Str("history_cover_sha1"),
        UpdatedAt = row.Dbl("updated_at") ?? 0,
    };

    public TrackDto ToDto() => new(
        Path, Title, Artist, Album, AlbumArtist, Genre, Year, TrackNo, Duration, Bitrate, SampleRate, Channels, Codec, Ext,
        FileSize, FileMtime, CoverSha1 ?? HistoryCoverSha1, Source, InHistory, HistoryPos, Exists, ArchiveState, Archive?.Bytes, UpdatedAt,
        Format, Archive?.Key, Archive?.SourceBytes, Archive?.Bitrate, HistoryCoverSha1, LocalPath);
}
