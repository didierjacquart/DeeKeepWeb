using System.Globalization;
using DeeKeep.Common.Data;
using DeeKeep.Common.Text;
using TagLib;
using TagFile = TagLib.File;

namespace DeeKeep.Common.Audio;

/// <summary>Jaquette intégrée : octets bruts, type, dimensions et empreinte (clé de <c>covers</c>).</summary>
public sealed record AudioCover(byte[] Data, string Mime, long Width, long Height, string Sha1);

/// <summary>
/// Tags et caractéristiques d'un fichier audio, dans le vocabulaire de l'original
/// (<c>reencode_scan.read_tags</c> pour les tags, <c>db_init.read_file_metadata</c> pour
/// les caractéristiques et la jaquette), lus avec TagLib#.
/// </summary>
/// <remarks>
/// Lecture par conteneur, comme l'original : trames ID3v2 pour le MP3 (TIT2, TPE1, TALB,
/// TPE2, TRCK, TPOS, TDRC|TYER, TCON, TCOM, TPUB, TCMP, premier COMM non vide), champs
/// Vorbis pour FLAC/OGG/Opus (mêmes noms et replis : <c>albumartist|album artist</c>,
/// <c>tracknumber|track</c>, <c>tracktotal|totaltracks</c>, <c>organization|label|publisher</c>…),
/// atomes iTunes pour le MP4. Écarts : WMA et WAV, que l'original lisait vides faute de
/// correspondance de noms, sont lus par l'interface générique de TagLib ; le MP4 lit en plus
/// l'éditeur et le drapeau compilation. Durée et débit d'un MP3 sont recalculés comme mutagen
/// (<see cref="MpegInfo"/>) ; pour les autres formats ils viennent de TagLib (kb/s entiers).
/// </remarks>
public sealed class AudioTags
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Track { get; set; } = "";
    public string TrackTotal { get; set; } = "";
    public string Disc { get; set; } = "";
    public string DiscTotal { get; set; } = "";
    public string Date { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Compilation { get; set; } = "";

    /// <summary>
    /// Le titre vient-il des tags ? Faux quand <see cref="Title"/> n'est que le nom du
    /// fichier, recopié faute de mieux : un renommage qui l'ignorerait rallongerait le nom
    /// à chaque passe (voir <see cref="NamePattern.MissingRequiredTags(string?, Func{string, string?})"/>).
    /// </summary>
    public bool TitleTagged { get; set; }

    /// <summary>Numéro de piste tel qu'écrit (colonne <c>tracks.track_no</c>) : « 3/12 » en ID3, numéro seul en MP4.</summary>
    public string TrackNo { get; set; } = "";

    /// <summary>Colonne <c>tracks.year</c> : la date telle qu'écrite (« 1995 », « 1995-11-07 »).</summary>
    public string Year => Date;

    /// <summary>Durée en secondes arrondie au millième (comme <c>round(length, 3)</c>), null si inconnue.</summary>
    public double? Duration { get; set; }

    /// <summary>
    /// Débit en kb/s arrondi (<c>read_tags</c> : <c>round(bitrate / 1000)</c>, repli taille × 8 / durée),
    /// 0 si inconnu — celui de la balise <c>%debit%</c>.
    /// </summary>
    public int Bitrate { get; set; }

    /// <summary>Débit en bit/s tel que mutagen le calcule (MP3 : en-tête Xing/VBRI), null si inconnu.</summary>
    public int? BitrateBps { get; set; }

    /// <summary>Colonne <c>tracks.bitrate</c> : <c>bitrate // 1000</c> (troncature, ≠ <see cref="Bitrate"/>).</summary>
    public int? TrackBitrate => BitrateBps / 1000;

    public int? SampleRate { get; set; }
    public int? Channels { get; set; }

    /// <summary>Codec nommé comme le module mutagen de l'original : mp3, flac, mp4, oggvorbis, oggopus, wave, asf, aac, aiff.</summary>
    public string Codec { get; set; } = "";

    /// <summary>Extension en minuscules, sans point (colonne <c>tracks.ext</c>).</summary>
    public string Ext { get; set; } = "";

    /// <summary>Format affiché (« MP3 ») : l'extension en majuscules.</summary>
    public string Format => Ext.ToUpperInvariant();

    /// <summary>Jaquette intégrée (face avant d'abord), null si absente ou non lue.</summary>
    public AudioCover? Cover { get; set; }

    /// <summary>
    /// Valeur par clé du vocabulaire des tags (celui de <c>name_pattern.TOKENS</c> et de
    /// <c>tag_write.FIELDS</c>) : à passer à <see cref="NamePattern.FormatName(string?, Func{string, string?})"/>.
    /// </summary>
    public string? Get(string key) => key switch
    {
        "title" => Title,
        "artist" => Artist,
        "album" => Album,
        "albumartist" => AlbumArtist,
        "track" => Track,
        "tracktotal" => TrackTotal,
        "disc" => Disc,
        "disctotal" => DiscTotal,
        "date" => Date,
        "genre" => Genre,
        "composer" => Composer,
        "publisher" => Publisher,
        "comment" => Comment,
        "compilation" => Compilation,
        "bitrate" => Bitrate > 0 ? Bitrate.ToString(CultureInfo.InvariantCulture) : "",
        "title_tagged" => TitleTagged ? "True" : "",
        "path" => Path,
        "format" => Format,
        _ => null,
    };

    /// <summary>
    /// Lit un fichier. Ne lève jamais : un fichier illisible rend des tags vides, le nom du
    /// fichier servant de titre (<see cref="TitleTagged"/> faux), comme l'original.
    /// <paramref name="withProperties"/> à faux évite de parcourir le flux (rangement de
    /// fichiers : ni durée ni débit) ; <paramref name="withCover"/> à faux ne copie pas la jaquette.
    /// </summary>
    public static AudioTags Read(string path, bool withProperties = true, bool withCover = true)
    {
        var tags = new AudioTags { Path = path, Ext = PyPath.SplitExt(path).Ext.ToLowerInvariant().TrimStart('.') };
        try
        {
            using var file = TagFile.Create(path, withProperties ? ReadStyle.Average : ReadStyle.None);
            ReadFields(file, tags);
            if (withProperties) ReadProperties(file, path, tags);
            if (withCover) tags.Cover = ReadCover(file);
        }
        catch (Exception)
        {
            // Format inconnu, fichier abîmé ou verrouillé : les tags restent vides.
        }
        tags.TitleTagged = tags.Title.Length > 0;
        if (!tags.TitleTagged) tags.Title = PyPath.Stem(path);
        return tags;
    }

    // ------------------------------------------------------------------ champs

    internal static void ReadFields(TagFile file, AudioTags t)
    {
        if (file is TagLib.Mpeg.AudioFile)
        {
            if (file.GetTag(TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3) ReadId3(id3, file.Name, t);
            MergeId3v1(file.Name, t);
        }
        else if (XiphOf(file, create: false) is { } xiph)
            ReadVorbis(xiph, t);
        else if (file.GetTag(TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
            ReadApple(apple, t);
        else
            ReadGeneric(file.Tag, t);
    }

    private static void ReadId3(TagLib.Id3v2.Tag id3, string path, AudioTags t)
    {
        t.Title = Id3Text(id3, "TIT2");
        t.Artist = Id3Text(id3, "TPE1");
        t.Album = Id3Text(id3, "TALB");
        t.AlbumArtist = Id3Text(id3, "TPE2");
        t.TrackNo = Id3Text(id3, "TRCK");
        (t.Track, t.TrackTotal) = SplitNum(t.TrackNo);
        (t.Disc, t.DiscTotal) = SplitNum(Id3Text(id3, "TPOS"));
        t.Date = Id3Date(id3, path);
        t.Genre = Id3Text(id3, "TCON");
        t.Composer = Id3Text(id3, "TCOM");
        t.Publisher = Id3Text(id3, "TPUB");
        t.Compilation = Id3Text(id3, "TCMP");
        // Les commentaires sont indexés par langue : le premier non vide est retenu.
        t.Comment = id3.GetFrames<TagLib.Id3v2.CommentsFrame>().Select(c => c.Text ?? "").FirstOrDefault(text => PyText.Strip(text).Length > 0) ?? "";
    }

    /// <summary>
    /// Fusion de l'ID3v1 comme mutagen (<c>ParseID3v1</c> puis ajout des seules trames absentes
    /// de l'ID3v2) : un MP3 dont l'ID3v2 n'a pas d'année prend celle de l'ID3v1, telle quelle
    /// (« 0000 » compris — 24 lignes de la base). Les champs sont lus bruts (Latin-1, coupés au
    /// premier nul) : TagLib convertirait l'année en nombre et le genre en nom.
    /// </summary>
    private static void MergeId3v1(string path, AudioTags t)
    {
        byte[] tag;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 128) return;
            stream.Seek(-128, SeekOrigin.End);
            tag = new byte[128];
            stream.ReadExactly(tag);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (tag[0] != (byte)'T' || tag[1] != (byte)'A' || tag[2] != (byte)'G') return;

        string Fix(int offset, int length)
        {
            var span = tag.AsSpan(offset, length);
            var nul = span.IndexOf((byte)0);
            return PyText.Strip(System.Text.Encoding.Latin1.GetString(nul >= 0 ? span[..nul] : span));
        }
        var (title, artist, album, year, comment) = (Fix(3, 30), Fix(33, 30), Fix(63, 30), Fix(93, 4), Fix(97, 29));
        int track = tag[126], genre = tag[127];
        if (t.Title.Length == 0) t.Title = title;
        if (t.Artist.Length == 0) t.Artist = artist;
        if (t.Album.Length == 0) t.Album = album;
        if (t.Date.Length == 0) t.Date = year;
        if (t.Comment.Length == 0) t.Comment = comment;
        // Pas de numéro de piste si le commentaire a été complété d'espaces au lieu de nuls (WinAmp).
        if (t.TrackNo.Length == 0 && track != 0 && (track != 32 || tag[125] == 0))
        {
            t.TrackNo = track.ToString(CultureInfo.InvariantCulture);
            (t.Track, t.TrackTotal) = (t.TrackNo, "");
        }
        if (t.Genre.Length == 0 && genre != 255) t.Genre = genre.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Date ID3 telle que mutagen la rend. En ID3v2.3, l'année (TYER), le jour (TDAT « JJMM »)
    /// et l'heure (TIME) sont fusionnés en TDRC par les deux bibliothèques, mais pas de la même
    /// façon : mutagen n'accepte qu'un TDAT de 4 chiffres et écrit « {année}-{TDAT[2:4]}-{TDAT[0:2]} »
    /// (TDAT « 1024 » → « 1995-24-10 », 145 lignes de la base), TagLib fusionne tout TDAT dans
    /// l'autre ordre (« 1995-10-24 », « 2021-11- 5 »). Pour que la colonne <c>tracks.year</c> et
    /// son empreinte restent identiques, les trames brutes d'un tag v2.3 sont relues
    /// (<see cref="Id3v23Dates"/>) et fusionnées comme mutagen.
    /// </summary>
    internal static string Id3Date(TagLib.Id3v2.Tag id3, string path)
    {
        if (id3.Version < 4 && Id3v23Dates.MutagenTimestamp(path) is { } date) return date;
        return Or(Id3Text(id3, "TDRC"), Id3Text(id3, "TYER"));
    }

    /// <summary>Premier texte de la première trame <paramref name="id"/> (non nettoyé, comme <c>_text</c>).</summary>
    internal static string Id3Text(TagLib.Id3v2.Tag id3, string id) =>
        id3.GetFrames<TagLib.Id3v2.TextInformationFrame>(ByteVector.FromString(id, StringType.Latin1))
            .Select(f => f.Text.FirstOrDefault()).FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? "";

    private static void ReadVorbis(TagLib.Ogg.XiphComment x, AudioTags t)
    {
        t.Title = First(x, "title");
        t.Artist = First(x, "artist");
        t.Album = First(x, "album");
        t.AlbumArtist = First(x, "albumartist", "album artist");
        t.TrackNo = First(x, "tracknumber", "track");
        (t.Track, t.TrackTotal) = SplitNum(t.TrackNo);
        t.TrackTotal = Or(t.TrackTotal, First(x, "tracktotal", "totaltracks"));
        (t.Disc, t.DiscTotal) = SplitNum(First(x, "discnumber", "disc"));
        t.DiscTotal = Or(t.DiscTotal, First(x, "disctotal", "totaldiscs"));
        t.Date = First(x, "date", "year", "originaldate");
        t.Genre = First(x, "genre");
        t.Composer = First(x, "composer");
        t.Publisher = First(x, "organization", "label", "publisher");
        t.Comment = First(x, "comment", "description");
        t.Compilation = First(x, "compilation");
    }

    private static void ReadApple(TagLib.Mpeg4.AppleTag a, AudioTags t)
    {
        t.Title = AppleText(a, Box("©nam"));
        t.Artist = AppleText(a, Box("©ART"));
        t.Album = AppleText(a, Box("©alb"));
        t.AlbumArtist = AppleText(a, Box("aART"));
        t.Track = a.Track > 0 ? a.Track.ToString(CultureInfo.InvariantCulture) : "";
        t.TrackTotal = a.Track > 0 && a.TrackCount > 0 ? a.TrackCount.ToString(CultureInfo.InvariantCulture) : "";
        t.TrackNo = t.Track;
        t.Disc = a.Disc > 0 ? a.Disc.ToString(CultureInfo.InvariantCulture) : "";
        t.DiscTotal = a.Disc > 0 && a.DiscCount > 0 ? a.DiscCount.ToString(CultureInfo.InvariantCulture) : "";
        t.Date = AppleText(a, Box("©day"));
        t.Genre = AppleText(a, Box("©gen"));
        t.Composer = AppleText(a, Box("©wrt"));
        t.Comment = Or(AppleText(a, Box("©cmt")), AppleText(a, Box("desc")));
        t.Publisher = PyText.Strip(a.Publisher ?? "");
        t.Compilation = a.IsCompilation ? "1" : "";
    }

    private static void ReadGeneric(Tag g, AudioTags t)
    {
        t.Title = PyText.Strip(g.Title ?? "");
        t.Artist = PyText.Strip(g.FirstPerformer ?? "");
        t.Album = PyText.Strip(g.Album ?? "");
        t.AlbumArtist = PyText.Strip(g.FirstAlbumArtist ?? "");
        t.Track = g.Track > 0 ? g.Track.ToString(CultureInfo.InvariantCulture) : "";
        t.TrackTotal = g.TrackCount > 0 ? g.TrackCount.ToString(CultureInfo.InvariantCulture) : "";
        t.TrackNo = t.TrackTotal.Length > 0 && t.Track.Length > 0 ? $"{t.Track}/{t.TrackTotal}" : t.Track;
        t.Disc = g.Disc > 0 ? g.Disc.ToString(CultureInfo.InvariantCulture) : "";
        t.DiscTotal = g.DiscCount > 0 ? g.DiscCount.ToString(CultureInfo.InvariantCulture) : "";
        t.Date = g.Year > 0 ? g.Year.ToString(CultureInfo.InvariantCulture) : "";
        t.Genre = PyText.Strip(g.FirstGenre ?? "");
        t.Composer = PyText.Strip(g.FirstComposer ?? "");
        t.Publisher = PyText.Strip(g.Publisher ?? "");
        t.Comment = g.Comment ?? "";
    }

    /// <summary>Commentaire Vorbis d'un FLAC ou du premier flux d'un OGG/Opus.</summary>
    internal static TagLib.Ogg.XiphComment? XiphOf(TagFile file, bool create) =>
        file is TagLib.Flac.File or TagLib.Ogg.File
            ? file.GetTag(TagTypes.Xiph, create) switch
            {
                TagLib.Ogg.XiphComment xiph => xiph,
                TagLib.Ogg.GroupedComment grouped => grouped.Comments.FirstOrDefault(),
                _ => null,
            }
            : null;

    /// <summary><c>_first</c> : première valeur non vide (nettoyée) parmi les clés, casse des clés ignorée.</summary>
    private static string First(TagLib.Ogg.XiphComment x, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = x.GetField(key)?.FirstOrDefault();
            if (!string.IsNullOrEmpty(value) && PyText.Strip(value).Length > 0) return PyText.Strip(value);
        }
        return "";
    }

    private static string AppleText(TagLib.Mpeg4.AppleTag a, ByteVector box) => PyText.Strip(a.GetText(box).FirstOrDefault() ?? "");

    /// <summary>Nom d'atome iTunes (« ©nam ») : « © » est l'octet 0xA9 en Latin-1.</summary>
    internal static ByteVector Box(string name) => ByteVector.FromString(name, StringType.Latin1);

    /// <summary><c>_split_num</c> : « 3/12 » → (« 3 », « 12 »).</summary>
    internal static (string Number, string Total) SplitNum(string? value)
    {
        if (string.IsNullOrEmpty(value)) return ("", "");
        var slash = value.IndexOf('/');
        return slash < 0 ? (PyText.Strip(value), "") : (PyText.Strip(value[..slash]), PyText.Strip(value[(slash + 1)..]));
    }

    private static string Or(string first, string second) => first.Length > 0 ? first : second;

    // ------------------------------------------------------------------ caractéristiques

    private static void ReadProperties(TagFile file, string path, AudioTags t)
    {
        var p = file.Properties;
        if (p is null) return;
        var seconds = p.Duration.TotalSeconds;
        t.BitrateBps = p.AudioBitrate > 0 ? p.AudioBitrate * 1000 : null;
        // MP3 : durée et débit recalculés comme mutagen (délai/remplissage LAME retranchés).
        if (file is TagLib.Mpeg.AudioFile && MpegInfo.Read(path, file.InvariantStartPosition) is { } mpeg)
        {
            seconds = mpeg.Length;
            t.BitrateBps = mpeg.BitsPerSecond ?? t.BitrateBps;
        }
        t.Duration = seconds > 0 ? PythonFloat.Round(seconds, 3) : null;
        if (t.BitrateBps > 0)
            t.Bitrate = (int)Math.Round(t.BitrateBps.Value / 1000.0, MidpointRounding.ToEven);
        else if (seconds > 0)
            t.Bitrate = (int)Math.Round(new FileInfo(path).Length * 8 / seconds / 1000, MidpointRounding.ToEven);
        t.SampleRate = p.AudioSampleRate > 0 ? p.AudioSampleRate : null;
        t.Channels = p.AudioChannels > 0 ? p.AudioChannels : null;
        t.Codec = CodecName(file);
    }

    private static string CodecName(TagFile file) => file switch
    {
        TagLib.Mpeg.AudioFile => "mp3",
        TagLib.Flac.File => "flac",
        TagLib.Mpeg4.File => "mp4",
        TagLib.Ogg.File => file.Properties.Codecs.FirstOrDefault()?.GetType().Name switch
        {
            "Opus" => "oggopus",
            "Vorbis" => "oggvorbis",
            "Flac" => "oggflac",
            "Speex" => "oggspeex",
            _ => "ogg",
        },
        TagLib.Riff.File => "wave",
        TagLib.Asf.File => "asf",
        TagLib.Aac.File => "aac",
        TagLib.Aiff.File => "aiff",
        _ => file.GetType().Namespace?.Split('.').LastOrDefault()?.ToLowerInvariant() ?? "",
    };

    // ------------------------------------------------------------------ jaquette

    private static AudioCover? ReadCover(TagFile file)
    {
        var pictures = file.Tag.Pictures;
        if (pictures is not { Length: > 0 }) return null;
        var front = pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? pictures[0];
        var data = front.Data?.Data;
        if (data is not { Length: > 0 }) return null;
        var info = CoverImage.Describe(data);
        return new AudioCover(data, info.Mime, info.Width, info.Height, CoverImage.Sha1(data));
    }
}

/// <summary>Champs à écrire : ceux fournis (comme l'original), ou seulement ceux que le fichier ne renseigne pas.</summary>
public enum TagWriteMode
{
    /// <summary>Les champs fournis remplacent les valeurs présentes (<c>tag_write.write</c>).</summary>
    Replace,

    /// <summary>
    /// Seuls les champs vides du fichier sont écrits — ce que promet l'écran de recherche
    /// des tags (« n'écrit que les champs restés vides »). La jaquette n'est alors posée
    /// que si le fichier n'en porte aucune.
    /// </summary>
    FillEmpty,
}

/// <summary>Issue d'une écriture : succès, message d'erreur, champs effectivement écrits.</summary>
public sealed record TagWriteResult(bool Success, string Message, IReadOnlyList<string> Written);

/// <summary>
/// Écriture des tags — portage de <c>deekeep/audio/tag_write.py</c>. Seuls les champs
/// fournis sont touchés ; les champs jumelés (« 3/12 ») sont fusionnés avec l'existant :
/// écrire le seul total ne doit pas effacer le numéro, ni l'inverse.
/// </summary>
/// <remarks>
/// <para>Par conteneur : MP3 en ID3v2.4, trames neuves en UTF-8 (TIT2 TPE1 TALB TPE2 TCON
/// TDRC TPUB, TRCK/TPOS fusionnés, APIC « Cover » de type face avant) ; un ID3v1 déjà
/// présent est mis à jour, aucun n'est créé (comme mutagen). FLAC/OGG/Opus en champs Vorbis
/// (tracknumber, tracktotal, discnumber, disctotal, organization…). MP4 en atomes iTunes.</para>
/// <para>Défaut corrigé : l'original n'écrivait pour OGG/Opus/MP4/WMA que le jeu restreint de
/// l'interface « easy » de mutagen (ni total de pistes ou de disques, ni éditeur, ni
/// jaquette) et écrasait le total de pistes d'un MP4 en écrivant le numéro. Tous les champs
/// sont désormais écrits quand TagLib sait les porter. Autres écarts : le type MIME de la
/// jaquette est lu dans l'image (l'original annonçait toujours image/jpeg), et seules les
/// faces avant (et images « Cover ») sont remplacées — le dos de pochette est conservé
/// (l'original gardait tout en MP3 mais effaçait tout en FLAC).</para>
/// <para>Écriture sûre : TagLib réécrit le fichier SUR PLACE (en le recopiant entièrement
/// quand le tag grossit au-delà du remplissage) ; une coupure en cours d'écriture peut
/// laisser un fichier tronqué. Avec <c>safe = true</c> (défaut), l'écriture se fait sur une
/// copie voisine qui remplace l'original par un renommage atomique, au prix d'une copie du
/// fichier ; l'original reste intact en cas d'échec.</para>
/// </remarks>
public static class AudioTagWriter
{
    /// <summary>FIELDS : champs écrivables, dans le vocabulaire de <see cref="AudioTags.Get"/>.</summary>
    public static readonly IReadOnlyList<string> Fields =
        ["title", "artist", "album", "albumartist", "genre", "date", "track", "tracktotal", "disc", "disctotal", "publisher"];

    // ID3 (MP3) : une trame par champ simple.
    private static readonly (string Field, string Frame)[] Id3Frames =
    [
        ("title", "TIT2"), ("artist", "TPE1"), ("album", "TALB"), ("albumartist", "TPE2"),
        ("genre", "TCON"), ("date", "TDRC"), ("publisher", "TPUB"),
    ];

    // Champs jumelés dans une seule trame ID3, sous la forme « numéro/total ».
    private static readonly (string Frame, string Number, string Total)[] Id3Pairs =
    [
        ("TRCK", "track", "tracktotal"),
        ("TPOS", "disc", "disctotal"),
    ];

    // Conteneurs à champs nommés : noms Vorbis usuels, ceux que relit AudioTags.
    private static readonly (string Field, string Key)[] VorbisKeys =
    [
        ("title", "TITLE"), ("artist", "ARTIST"), ("album", "ALBUM"), ("albumartist", "ALBUMARTIST"),
        ("genre", "GENRE"), ("date", "DATE"), ("track", "TRACKNUMBER"), ("tracktotal", "TRACKTOTAL"),
        ("disc", "DISCNUMBER"), ("disctotal", "DISCTOTAL"), ("publisher", "ORGANIZATION"),
    ];

    /// <summary><c>clean_values</c> : champs connus et non vides, valeurs nettoyées.</summary>
    public static Dictionary<string, string> CleanValues(IReadOnlyDictionary<string, string?> values)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (!values.TryGetValue(field, out var value) || value is null) continue;
            var text = PyText.Strip(value);
            if (text.Length > 0) cleaned[field] = text;
        }
        return cleaned;
    }

    /// <summary>
    /// Écrit <paramref name="values"/> (clés de <see cref="Fields"/> ; inconnues et vides ignorées)
    /// et la jaquette. Le fichier n'est ouvert que s'il y a quelque chose à écrire. Ne lève pas.
    /// </summary>
    public static TagWriteResult Write(string path, IReadOnlyDictionary<string, string?> values, byte[]? cover = null,
        TagWriteMode mode = TagWriteMode.Replace, bool safe = true)
    {
        var cleaned = CleanValues(values);
        if (cleaned.Count == 0 && cover is not { Length: > 0 }) return new TagWriteResult(true, "", []);
        if (!System.IO.File.Exists(path)) return new TagWriteResult(false, "fichier introuvable", []);

        // La copie garde l'extension : TagLib choisit le format d'après elle.
        var target = safe
            ? System.IO.Path.Join(System.IO.Path.GetDirectoryName(path),
                $".{System.IO.Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}{System.IO.Path.GetExtension(path)}")
            : path;
        try
        {
            if (safe) System.IO.File.Copy(path, target);
            List<string> written;
            using (var file = TagFile.Create(target))
            {
                written = Apply(file, cleaned, cover, mode);
                if (written.Count > 0) file.Save();
            }
            if (safe)
            {
                if (written.Count > 0) System.IO.File.Move(target, path, overwrite: true);
                else System.IO.File.Delete(target);
            }
            return new TagWriteResult(true, "", written);
        }
        catch (Exception ex)
        {
            if (safe) TryDelete(target);
            return new TagWriteResult(false, ex.Message, []);
        }
    }

    private static List<string> Apply(TagFile file, Dictionary<string, string> values, byte[]? cover, TagWriteMode mode)
    {
        if (mode == TagWriteMode.FillEmpty)
        {
            var current = new AudioTags();
            AudioTags.ReadFields(file, current);
            foreach (var field in values.Keys.ToList())
                if (!string.IsNullOrEmpty(current.Get(field))) values.Remove(field);
            if (file.Tag.Pictures is { Length: > 0 }) cover = null;
        }

        var written = new List<string>();
        if (file is TagLib.Mpeg.AudioFile) WriteId3(file, values, written);
        else if (AudioTags.XiphOf(file, create: true) is { } xiph) WriteVorbis(xiph, values, written);
        else if (file is TagLib.Mpeg4.File && file.GetTag(TagTypes.Apple, true) is TagLib.Mpeg4.AppleTag apple) WriteApple(apple, values, written);
        else WriteGeneric(file.Tag, values, written);

        if (cover is { Length: > 0 })
        {
            WriteCover(file, cover);
            written.Add("cover");
        }
        return written;
    }

    // ------------------------------------------------------------------ MP3

    private static void WriteId3(TagFile file, Dictionary<string, string> values, List<string> written)
    {
        // mutagen met à jour un ID3v1 existant mais n'en crée pas ; TagLib en ajouterait
        // un d'office à l'enregistrement d'un MPEG.
        var hadV1 = file.TagTypesOnDisk.HasFlag(TagTypes.Id3v1);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagTypes.Id3v2, true);
        // mutagen réenregistre un tag v2.3 en v2.4 avec SA fusion de TYER/TDAT (voir Id3Date).
        if (id3.Version < 4 && !values.ContainsKey("date"))
        {
            var date = AudioTags.Id3Date(id3, file.Name);
            if (date.Length > 0 && date != AudioTags.Id3Text(id3, "TDRC")) SetUtf8Frame(id3, "TDRC", date);
        }
        id3.Version = 4;

        foreach (var (field, frame) in Id3Frames)
        {
            if (!values.TryGetValue(field, out var value)) continue;
            SetUtf8Frame(id3, frame, value);
            written.Add(field);
        }
        foreach (var (frame, numberField, totalField) in Id3Pairs)
        {
            var text = PairText(AudioTags.Id3Text(id3, frame), values.GetValueOrDefault(numberField), values.GetValueOrDefault(totalField));
            if (text is null) continue;
            SetUtf8Frame(id3, frame, text);
            if (values.ContainsKey(numberField)) written.Add(numberField);
            if (values.ContainsKey(totalField)) written.Add(totalField);
        }

        if (hadV1) id3.CopyTo(file.GetTag(TagTypes.Id3v1, true), true);
        else file.RemoveTags(TagTypes.Id3v1);
    }

    private static void SetUtf8Frame(TagLib.Id3v2.Tag id3, string frame, string value)
    {
        var ident = ByteVector.FromString(frame, StringType.Latin1);
        id3.RemoveFrames(ident);
        id3.AddFrame(new TagLib.Id3v2.TextInformationFrame(ident, StringType.UTF8) { Text = [value] });
    }

    /// <summary>
    /// <c>_pair_text</c> : valeur « numéro/total » fusionnée avec l'ancienne, ou null si rien
    /// à écrire. Un total sans numéro n'a pas de forme ID3 lisible (« /12 ») : on s'abstient.
    /// </summary>
    public static string? PairText(string existing, string? number, string? total)
    {
        if (number is null && total is null) return null;
        var (currentNumber, currentTotal) = AudioTags.SplitNum(existing);
        var n = string.IsNullOrEmpty(number) ? currentNumber : number;
        var t = string.IsNullOrEmpty(total) ? currentTotal : total;
        if (n.Length == 0) return null;
        return t.Length > 0 ? $"{n}/{t}" : n;
    }

    // ------------------------------------------------------------------ autres conteneurs

    private static void WriteVorbis(TagLib.Ogg.XiphComment xiph, Dictionary<string, string> values, List<string> written)
    {
        foreach (var (field, key) in VorbisKeys)
        {
            if (!values.TryGetValue(field, out var value)) continue;
            xiph.SetField(key, value);
            written.Add(field);
        }
    }

    private static void WriteApple(TagLib.Mpeg4.AppleTag apple, Dictionary<string, string> values, List<string> written)
    {
        foreach (var (field, value) in values)
        {
            switch (field)
            {
                case "title": apple.Title = value; break;
                case "artist": apple.Performers = [value]; break;
                case "album": apple.Album = value; break;
                case "albumartist": apple.AlbumArtists = [value]; break;
                case "genre": apple.Genres = [value]; break;
                case "date": apple.SetText(AudioTags.Box("©day"), value); break;   // date telle quelle, pas seulement l'année
                case "publisher": apple.Publisher = value; break;
                // Les atomes trkn/disk portent numéro et total : chacun est écrit sans perdre l'autre.
                case "track" when uint.TryParse(value, out var n): apple.Track = n; break;
                case "tracktotal" when uint.TryParse(value, out var n): apple.TrackCount = n; break;
                case "disc" when uint.TryParse(value, out var n): apple.Disc = n; break;
                case "disctotal" when uint.TryParse(value, out var n): apple.DiscCount = n; break;
                default: continue;   // valeur non numérique : le conteneur ne sait pas la porter
            }
            written.Add(field);
        }
    }

    private static void WriteGeneric(Tag tag, Dictionary<string, string> values, List<string> written)
    {
        foreach (var (field, value) in values)
        {
            switch (field)
            {
                case "title": tag.Title = value; break;
                case "artist": tag.Performers = [value]; break;
                case "album": tag.Album = value; break;
                case "albumartist": tag.AlbumArtists = [value]; break;
                case "genre": tag.Genres = [value]; break;
                case "publisher": tag.Publisher = value; break;
                // L'interface générique ne connaît que l'année.
                case "date" when value.Length >= 4 && uint.TryParse(value[..4], out var year): tag.Year = year; break;
                case "track" when uint.TryParse(value, out var n): tag.Track = n; break;
                case "tracktotal" when uint.TryParse(value, out var n): tag.TrackCount = n; break;
                case "disc" when uint.TryParse(value, out var n): tag.Disc = n; break;
                case "disctotal" when uint.TryParse(value, out var n): tag.DiscCount = n; break;
                default: continue;
            }
            written.Add(field);
        }
    }

    // ------------------------------------------------------------------ jaquette

    private static void WriteCover(TagFile file, byte[] cover)
    {
        var mime = CoverImage.Describe(cover).Mime;
        if (!mime.StartsWith("image/", StringComparison.Ordinal)) mime = "image/jpeg";

        if (file is TagLib.Mpeg.AudioFile)
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagTypes.Id3v2, true);
            foreach (var frame in Pictures(id3).Where(IsReplaced).ToList())
                id3.RemoveFrame(frame);
            // En tête des images : c'est la première face avant que relisent les lecteurs.
            var others = Pictures(id3).ToList();
            foreach (var frame in others) id3.RemoveFrame(frame);
            id3.AddFrame(new TagLib.Id3v2.AttachmentFrame(NewPicture(cover, mime)) { TextEncoding = StringType.UTF8 });
            foreach (var frame in others) id3.AddFrame(frame);
            return;
        }

        // Un seul porteur d'images par conteneur, pour ne pas les dupliquer (FLAC : blocs
        // PICTURE, sans METADATA_BLOCK_PICTURE en plus).
        Tag holder = file switch
        {
            TagLib.Flac.File => file.GetTag(TagTypes.FlacMetadata, true),
            TagLib.Ogg.File => AudioTags.XiphOf(file, create: true)!,
            TagLib.Mpeg4.File => file.GetTag(TagTypes.Apple, true),
            _ => file.Tag,
        };
        var kept = (holder.Pictures ?? []).Where(p => !IsReplaced(p));
        holder.Pictures = [NewPicture(cover, mime), .. kept];
    }

    // Trames APIC (les AttachmentFrame de type « pas une image » sont des GEOB).
    private static IEnumerable<TagLib.Id3v2.AttachmentFrame> Pictures(TagLib.Id3v2.Tag id3) =>
        id3.GetFrames<TagLib.Id3v2.AttachmentFrame>().Where(f => f.Type != PictureType.NotAPicture);

    private static bool IsReplaced(IPicture picture) =>
        picture.Type == PictureType.FrontCover || string.Equals(picture.Description, "Cover", StringComparison.Ordinal);

    private static Picture NewPicture(byte[] cover, string mime) =>
        new(new ByteVector(cover)) { Type = PictureType.FrontCover, MimeType = mime, Description = "Cover" };

    private static void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception)
        {
            // Copie temporaire orpheline : sans conséquence pour l'original.
        }
    }
}
