using System.Security.Cryptography;
using System.Text.Json;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Text;
using Xunit;

namespace DeeKeep.Tests.Audio;

/// <summary>
/// Lecture et écriture sur un vrai MP3 de la bibliothèque, toujours COPIÉ dans un dossier
/// temporaire. Valeurs attendues : la ligne de la table <c>tracks</c> que mutagen a produite
/// pour ce fichier (D:\Queen\Yeah - Queen.mp3), et ffprobe pour relire ce qui a été écrit.
/// </summary>
public class AudioTagsTests
{
    private static string CopySample(string dir, string name = "yeah.mp3")
    {
        if (!File.Exists(AudioTestSupport.SampleMp3)) Assert.Skip("MP3 d'essai absent");
        var copy = Path.Combine(dir, name);
        File.Copy(AudioTestSupport.SampleMp3, copy);
        return copy;
    }

    [Fact]
    public void Reads_like_mutagen()
    {
        var dir = AudioTestSupport.TempDir();
        try
        {
            var tags = AudioTags.Read(CopySample(dir));
            // tracks : title, artist, album, album_artist, genre, year, track_no, duration, bitrate, sample_rate, channels, codec, ext, cover_sha1
            Assert.Equal("Yeah", tags.Title);
            Assert.True(tags.TitleTagged);
            Assert.Equal("Queen", tags.Artist);
            Assert.Equal("Made in Heaven", tags.Album);
            Assert.Equal("Queen", tags.AlbumArtist);
            Assert.Equal("Rock", tags.Genre);
            Assert.Equal("1995-24-10", tags.Year);
            Assert.Equal("12/13", tags.TrackNo);
            Assert.Equal(("12", "13", "1", "1"), (tags.Track, tags.TrackTotal, tags.Disc, tags.DiscTotal));
            Assert.Equal("Hollywood Records", tags.Publisher);
            Assert.Equal(4.013, tags.Duration!.Value, 0.03);
            Assert.InRange(tags.Bitrate, 127, 128);     // mutagen : 127 (en-tête LAME), TagLib : débit nominal
            Assert.Equal((44100, 2), (tags.SampleRate, tags.Channels));
            Assert.Equal(("mp3", "mp3", "MP3"), (tags.Codec, tags.Ext, tags.Format));
            Assert.NotNull(tags.Cover);
            Assert.Equal("d8af257c89b9ec879e1ff362eea42e070cea895e", tags.Cover.Sha1);   // face avant, pas le dos
            Assert.Equal(("image/jpeg", 499L, 499L), (tags.Cover.Mime, tags.Cover.Width, tags.Cover.Height));
            Assert.Equal("Yeah - Queen", NamePattern.FormatName("%titre% - %artiste%", tags.Get));
            Assert.Equal("12 - Yeah (1995)", NamePattern.FormatName("%piste_00% - %titre% (%annee%)", tags.Get));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// Fidélité à grande échelle : les lignes de <c>tracks</c> (produites par mutagen) des
    /// fichiers de même taille qu'à la collecte sont relues et comparées champ par champ.
    /// </summary>
    [Fact]
    public void Matches_tracks_table_of_existing_database()
    {
        if (!File.Exists(AudioTestSupport.Database)) Assert.Skip("base absente");
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={AudioTestSupport.Database};Mode=ReadOnly");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT path, title, artist, album, album_artist, genre, year, track_no, duration, bitrate, sample_rate,
                   channels, codec, ext, file_size, file_mtime, cover_sha1
            FROM tracks WHERE deleted = 0 AND duration IS NOT NULL ORDER BY path LIMIT 800
            """;
        using var r = cmd.ExecuteReader();
        var failures = new List<string>();
        var compared = 0;
        while (r.Read())
        {
            var path = r.GetString(0);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != r.GetInt64(14)) continue;
            // Même taille : fichier supposé inchangé (les dates ont toutes bougé depuis la collecte).
            compared++;
            var t = AudioTags.Read(path);
            void Check(string field, object? expected, object? actual)
            {
                if (!Equals(expected, actual)) failures.Add($"{field} {path} : base « {expected} », lu « {actual} »");
            }
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            Check("title", S(1), t.Title);
            Check("artist", S(2), t.Artist);
            Check("album", S(3), t.Album);
            Check("album_artist", S(4), t.AlbumArtist);
            Check("genre", S(5), t.Genre);
            Check("year", S(6), t.Year);
            Check("track_no", S(7), t.TrackNo);
            Check("duration", r.GetDouble(8), t.Duration);
            Check("bitrate", r.IsDBNull(9) ? null : r.GetInt32(9), t.TrackBitrate);
            Check("sample_rate", r.IsDBNull(10) ? null : r.GetInt32(10), t.SampleRate);
            Check("channels", r.IsDBNull(11) ? null : r.GetInt32(11), t.Channels);
            Check("codec", S(12), t.Codec);
            Check("ext", S(13), t.Ext);
            Check("cover_sha1", r.IsDBNull(16) ? null : r.GetString(16), t.Cover?.Sha1);
        }
        if (compared == 0) Assert.Skip("aucun fichier inchangé depuis la collecte");
        Assert.True(failures.Count == 0, $"{failures.Count} écarts sur {compared} fichiers :\n" + string.Join("\n", failures.Take(40)));
    }

    [Fact]
    public void Unreadable_file_falls_back_to_file_name()
    {
        var dir = AudioTestSupport.TempDir();
        try
        {
            var path = Path.Combine(dir, "03 - Mr. Brightside.mp3");
            File.WriteAllText(path, "pas un mp3");
            var tags = AudioTags.Read(path);
            Assert.False(tags.TitleTagged);
            Assert.Equal("03 - Mr. Brightside", tags.Title);
            Assert.Equal("", tags.Artist);
            Assert.Equal(["titre"], NamePattern.MissingRequiredTags(NamePattern.DefaultPattern, tags.Get));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Writes_mp3_and_never_touches_the_original()
    {
        if (!File.Exists(AudioTestSupport.SampleMp3)) Assert.Skip("MP3 d'essai absent");
        var originalHash = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(AudioTestSupport.SampleMp3)));
        var dir = AudioTestSupport.TempDir();
        try
        {
            var copy = CopySample(dir);
            var cover = File.ReadAllBytes(Path.Combine(AudioTestSupport.Assets(), "cover_200x150.png"));
            var result = AudioTagWriter.Write(copy, new Dictionary<string, string?>
            {
                ["title"] = "  Ça plane pour moi ",
                ["artist"] = "Plastic Bertrand",
                ["tracktotal"] = "20",          // fusionné avec le numéro 12 existant
                ["publisher"] = "Sire",
                ["unknown"] = "ignoré",
                ["genre"] = "",
            }, cover);
            Assert.True(result.Success, result.Message);
            Assert.Equal(["title", "artist", "publisher", "tracktotal", "cover"], result.Written);
            Assert.Empty(Directory.GetFiles(dir, ".*"));   // copie de travail remplacée, rien ne traîne

            var back = AudioTags.Read(copy);
            Assert.Equal("Ça plane pour moi", back.Title);
            Assert.Equal("Plastic Bertrand", back.Artist);
            Assert.Equal("12/20", back.TrackNo);
            Assert.Equal("Sire", back.Publisher);
            Assert.Equal("Made in Heaven", back.Album);     // non fourni : intact
            Assert.Equal(CoverImage.Sha1(cover), back.Cover!.Sha1);
            Assert.Equal("image/png", back.Cover.Mime);

            // Relecture indépendante : ffprobe voit un ID3v2.4 UTF-8 et pas d'ID3v1 ajouté.
            var ffprobe = AudioTestSupport.Tool("ffprobe");
            if (ffprobe is not null)
            {
                var probe = AudioTestSupport.Run(ffprobe, "-v", "error", "-show_format", "-show_streams", "-of", "json", copy);
                using var json = JsonDocument.Parse(probe.Out);
                var t = json.RootElement.GetProperty("format").GetProperty("tags");
                Assert.Equal("Ça plane pour moi", t.GetProperty("title").GetString());
                Assert.Equal("12/20", t.GetProperty("track").GetString());
                Assert.Equal("Sire", t.GetProperty("publisher").GetString());
                var comments = json.RootElement.GetProperty("streams").EnumerateArray()
                    .Where(s => s.GetProperty("codec_type").GetString() == "video")
                    .Select(s => s.GetProperty("tags").GetProperty("comment").GetString()).ToList();
                Assert.Equal(["Cover (front)", "Cover (back)"], comments);   // nouvelle face avant en tête, dos conservé
            }
            var bytes = File.ReadAllBytes(copy);
            Assert.Equal((byte)4, bytes[3]);                                   // en-tête « ID3 » version 2.4
            Assert.Equal("TAG"u8.ToArray(), bytes[^128..^125]);                // l'ID3v1 présent est mis à jour…
            Assert.Equal("Ça plane pour moi", System.Text.Encoding.Latin1.GetString(bytes[^125..^95]).TrimEnd('\0', ' '));
            Assert.Equal("1995-24-10", back.Date);                             // date v2.3 réécrite comme mutagen

            // … mais aucun n'est créé quand il n'y en avait pas.
            var noV1 = Path.Combine(dir, "nov1.mp3");
            File.WriteAllBytes(noV1, File.ReadAllBytes(AudioTestSupport.SampleMp3)[..^128]);
            Assert.True(AudioTagWriter.Write(noV1, new Dictionary<string, string?> { ["title"] = "x" }).Success);
            Assert.NotEqual("TAG"u8.ToArray(), File.ReadAllBytes(noV1)[^128..^125]);

            // « Remplir les vides » : rien n'est écrasé, la jaquette présente est gardée.
            var fill = AudioTagWriter.Write(copy, new Dictionary<string, string?> { ["title"] = "Autre", ["date"] = "1977" }, cover,
                TagWriteMode.FillEmpty);
            Assert.True(fill.Success, fill.Message);
            Assert.Empty(fill.Written);                    // date déjà renseignée (1995-24-10)
            Assert.Equal("Ça plane pour moi", AudioTags.Read(copy).Title);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
        Assert.Equal(originalHash, Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(AudioTestSupport.SampleMp3))));
    }

    [Fact]
    public void Write_reports_missing_file_and_empty_values()
    {
        Assert.Equal(new TagWriteResult(true, "", []).Success, AudioTagWriter.Write(@"Z:\absent.mp3", new Dictionary<string, string?>()).Success);
        var missing = AudioTagWriter.Write(@"Z:\absent.mp3", new Dictionary<string, string?> { ["title"] = "x" });
        Assert.False(missing.Success);
        Assert.Equal("fichier introuvable", missing.Message);
        Assert.Equal("3/12", AudioTagWriter.PairText("3", null, "12"));
        Assert.Equal("4/12", AudioTagWriter.PairText("3/12", "4", null));
        Assert.Null(AudioTagWriter.PairText("", null, "12"));
        Assert.Null(AudioTagWriter.PairText("3/12", null, null));
    }

    /// <summary>Autres conteneurs (produits par ffmpeg à partir de la copie) : tous les champs, totaux et jaquette compris.</summary>
    [Theory]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "oggvorbis")]
    [InlineData("opus", "oggopus")]
    [InlineData("m4a", "mp4")]
    public void Writes_other_containers(string ext, string codec)
    {
        var ffmpeg = AudioTestSupport.Tool("ffmpeg");
        if (ffmpeg is null) Assert.Skip("ffmpeg absent");
        var dir = AudioTestSupport.TempDir();
        try
        {
            var source = CopySample(dir);
            var target = Path.Combine(dir, "converted." + ext);
            var encoder = ext switch { "ogg" => "libvorbis", "opus" => "libopus", "m4a" => "aac", _ => "flac" };
            var made = AudioTestSupport.Run(ffmpeg, "-v", "error", "-y", "-i", source, "-map", "0:a", "-map_metadata", "-1", "-c:a", encoder, target);
            Assert.True(made.Code == 0, made.Err);

            var cover = File.ReadAllBytes(Path.Combine(AudioTestSupport.Assets(), "cover_200x150_stamped.jpg"));
            var values = new Dictionary<string, string?>
            {
                ["title"] = "Œuvre", ["artist"] = "Ørjan Nilsen", ["album"] = "Album", ["albumartist"] = "Various",
                ["genre"] = "Trance", ["date"] = "2011-05-02", ["track"] = "3", ["tracktotal"] = "12",
                ["disc"] = "1", ["disctotal"] = "2", ["publisher"] = "Armada",
            };
            var result = AudioTagWriter.Write(target, values, cover);
            Assert.True(result.Success, result.Message);

            var back = AudioTags.Read(target);
            Assert.Equal(codec, back.Codec);
            Assert.Equal(("Œuvre", "Ørjan Nilsen", "Album", "Various", "Trance"), (back.Title, back.Artist, back.Album, back.AlbumArtist, back.Genre));
            Assert.Equal(("3", "12", "1", "2"), (back.Track, back.TrackTotal, back.Disc, back.DiscTotal));
            Assert.Equal("2011-05-02", back.Date);
            Assert.Equal("Armada", back.Publisher);
            Assert.Equal(CoverImage.Sha1(cover), back.Cover?.Sha1);

            // Écrire le seul numéro garde le total (l'original l'écrasait en MP4).
            Assert.True(AudioTagWriter.Write(target, new Dictionary<string, string?> { ["track"] = "4" }).Success);
            var again = AudioTags.Read(target);
            Assert.Equal(("4", "12"), (again.Track, again.TrackTotal));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
