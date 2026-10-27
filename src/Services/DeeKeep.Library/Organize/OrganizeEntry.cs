using System.Globalization;
using System.Text.Json.Serialization;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;

namespace DeeKeep.Library.Organize;

/// <summary>
/// Fichier analysé : l'« entrée » de <c>LocalScanThread._read_one</c> (spec 03 §5.1).
/// <see cref="Path"/> est le chemin LOCAL du serveur. Le débit n'est lu qu'à la
/// demande (doublons, balise %debit%) : 0 tant qu'il ne l'a pas été.
/// </summary>
public sealed class OrganizeEntry
{
    public int Index { get; set; }
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string Title { get; set; } = "";
    public bool TitleTagged { get; set; }
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Compilation { get; set; } = "";
    public string Track { get; set; } = "";
    public string TrackTotal { get; set; } = "";
    public string Disc { get; set; } = "";
    public string DiscTotal { get; set; } = "";
    public string Date { get; set; } = "";

    /// <summary>Débit en kb/s ; null tant qu'il n'a pas été lu.</summary>
    [JsonIgnore] public int? Bitrate { get; set; }
    [JsonIgnore] public long Size { get; set; }
    [JsonIgnore] public long MtimeTicks { get; set; }

    /// <summary>Valeur par clé, dans le vocabulaire de <c>name_pattern</c> et <c>tag_write</c>.</summary>
    public string? Get(string key) => key switch
    {
        "path" => Path,
        "format" => Format,
        "title" => Title,
        "title_tagged" => TitleTagged ? "True" : "",
        "artist" => Artist,
        "album" => Album,
        "albumartist" => AlbumArtist,
        "genre" => Genre,
        "composer" => Composer,
        "publisher" => Publisher,
        "comment" => Comment,
        "compilation" => Compilation,
        "track" => Track,
        "tracktotal" => TrackTotal,
        "disc" => Disc,
        "disctotal" => DiscTotal,
        "date" => Date,
        "bitrate" => Bitrate is > 0 ? Bitrate.Value.ToString(CultureInfo.InvariantCulture) : "",
        _ => null,
    };

    /// <summary>EXTRA_TRIGGER_FIELDS : champs dont l'absence déclenche une recherche de tags complémentaires.</summary>
    public static readonly IReadOnlyList<string> ExtraTriggerFields = ["albumartist", "genre", "date", "track"];

    /// <summary><c>needs_extras</c> : il manque l'artiste d'album, le genre, l'année ou le numéro de piste.</summary>
    public bool NeedsExtras => ExtraTriggerFields.Any(f => PyText.Strip(Get(f) ?? "").Length == 0);

    /// <summary><c>missing_fields</c> : tags indispensables absents (titre seulement s'il ne vient pas des tags).</summary>
    public IReadOnlyList<string> MissingCore()
    {
        var missing = new List<string>();
        if (!TitleTagged) missing.Add("title");
        if (PyText.Strip(Artist).Length == 0) missing.Add("artist");
        if (PyText.Strip(Album).Length == 0) missing.Add("album");
        return missing;
    }

    /// <summary>Entrée lue sur le disque (tags seulement : ni durée ni débit, comme l'original).</summary>
    public static OrganizeEntry FromTags(AudioTags t) => new()
    {
        Path = t.Path,
        Format = t.Format,
        Title = t.Title,
        TitleTagged = t.TitleTagged,
        Artist = t.Artist,
        Album = t.Album,
        AlbumArtist = t.AlbumArtist,
        Genre = t.Genre,
        Composer = t.Composer,
        Publisher = t.Publisher,
        Comment = t.Comment,
        Compilation = t.Compilation,
        Track = t.Track,
        TrackTotal = t.TrackTotal,
        Disc = t.Disc,
        DiscTotal = t.DiscTotal,
        Date = t.Date,
        Bitrate = t.Bitrate > 0 ? t.Bitrate : null,
    };
}
