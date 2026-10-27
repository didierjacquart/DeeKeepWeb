using DeeKeep.Common.Audio;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace DeeKeep.Tests.Audio;

public class CoverImageTests
{
    [Fact]
    public void Sha1_is_lowercase_hex_of_raw_bytes()
    {
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", CoverImage.Sha1("abc"u8));
    }

    [Fact]
    public void Describe_reads_headers_like_image_info()
    {
        var png = File.ReadAllBytes(Path.Combine(AudioTestSupport.Assets(), "cover_200x150.png"));
        Assert.Equal(new CoverInfo("image/png", 200, 150), CoverImage.DescribeLikePython(png));
        Assert.Equal(new CoverInfo("image/png", 200, 150), CoverImage.Describe(png));
        var jpg = File.ReadAllBytes(Path.Combine(AudioTestSupport.Assets(), "cover_200x150_stamped.jpg"));
        Assert.Equal(new CoverInfo("image/jpeg", 200, 150), CoverImage.DescribeLikePython(jpg));
        Assert.Equal(new CoverInfo("image/jpeg", 200, 150), CoverImage.Describe(jpg));
        Assert.Equal(new CoverInfo("application/octet-stream", 0, 0), CoverImage.Describe("not an image"u8));
        Assert.Equal(new CoverInfo("image/webp", 0, 0), CoverImage.DescribeLikePython("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal(new CoverInfo("", 0, 0), CoverImage.Describe([]));
    }

    /// <summary>Toutes les jaquettes de la vraie base : empreinte, type et dimensions identiques à ceux calculés par Python.</summary>
    [Fact]
    public void Matches_existing_covers_table()
    {
        if (!File.Exists(AudioTestSupport.Database)) Assert.Skip("base absente");
        using var db = new SqliteConnection($"Data Source={AudioTestSupport.Database};Mode=ReadOnly");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sha1, mime, width, height, data FROM covers WHERE deleted = 0 AND data IS NOT NULL";
        using var reader = cmd.ExecuteReader();
        var failures = new List<string>();
        var count = 0;
        while (reader.Read())
        {
            count++;
            var data = (byte[])reader["data"];
            var expected = new CoverInfo(reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3));
            if (CoverImage.Sha1(data) != reader.GetString(0)) failures.Add("sha1 " + reader.GetString(0));
            if (CoverImage.DescribeLikePython(data) != expected) failures.Add($"python {reader.GetString(0)} {CoverImage.DescribeLikePython(data)}");
            if (expected.Width > 0 && CoverImage.Describe(data) != expected) failures.Add($"skia {reader.GetString(0)} {CoverImage.Describe(data)}");
        }
        Assert.True(failures.Count == 0, $"{failures.Count}/{count} : " + string.Join("\n", failures.Take(20)));
    }

    /// <summary>Le marquage reproduit celui de Qt (même jaquette marquée par utils.stamp_app_icon), aux écarts de rééchantillonnage et de JPEG près.</summary>
    [Fact]
    public void Stamp_matches_qt_reference()
    {
        var source = File.ReadAllBytes(Path.Combine(AudioTestSupport.Assets(), "cover_200x150.png"));
        var stamped = CoverImage.StampAppIcon(source);
        Assert.Equal(new CoverInfo("image/jpeg", 200, 150), CoverImage.DescribeLikePython(stamped));

        using var mine = SKBitmap.Decode(stamped);
        using var reference = SKBitmap.Decode(Path.Combine(AudioTestSupport.Assets(), "cover_200x150_stamped.jpg"));
        // Badge : côté round(150 × 0,18) = 27, marge round(150 × 0,04) = 6, en haut à droite.
        using var original = SKBitmap.Decode(source);
        var badge = MeanDiff(mine, reference, 167, 6, 27, 27);
        // Hors badge, seul diffère le codeur JPEG : la perte de Skia doit rester du même
        // ordre que celle de libjpeg (Qt) sur cette mire à fortes transitions.
        var lossMine = MeanDiff(mine, original, 0, 40, 200, 110);
        var lossQt = MeanDiff(reference, original, 0, 40, 200, 110);
        Assert.True(badge < 12, $"écart moyen dans le badge : {badge:F2}");
        Assert.True(lossMine < lossQt + 2, $"perte JPEG : {lossMine:F2} contre {lossQt:F2} pour Qt");
        // Le badge est bien là : cyan de l'icône au centre de sa zone.
        var center = mine.GetPixel(167 + 4, 6 + 13);
        Assert.True(center.Green > 180 && center.Blue > 200 && center.Red < 60, center.ToString());
    }

    [Fact]
    public void Stamp_leaves_small_or_unreadable_covers_untouched()
    {
        Assert.Same(Array.Empty<byte>(), CoverImage.StampAppIcon([]));
        var junk = "xyz"u8.ToArray();
        Assert.Same(junk, CoverImage.StampAppIcon(junk));
        using var small = new SKBitmap(90, 120);
        var bytes = SKImage.FromBitmap(small).Encode(SKEncodedImageFormat.Png, 100).ToArray();
        Assert.Same(bytes, CoverImage.StampAppIcon(bytes));
    }

    [Theory]
    [InlineData(128, "icon_drawn_128.png")]
    [InlineData(40, "icon_drawn_40.png")]
    public void Drawn_icon_matches_qt(int size, string reference)
    {
        using var mine = CoverImage.DrawAppIcon(size);
        using var qt = SKBitmap.Decode(Path.Combine(AudioTestSupport.Assets(), reference));
        double total = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var (a, b) = (mine.GetPixel(x, y), qt.GetPixel(x, y));
            // Comparaison en couleurs prémultipliées : un pixel transparent n'a pas de couleur.
            total += Math.Abs(a.Red * a.Alpha - b.Red * b.Alpha) / 255.0 + Math.Abs(a.Green * a.Alpha - b.Green * b.Alpha) / 255.0
                     + Math.Abs(a.Blue * a.Alpha - b.Blue * b.Alpha) / 255.0 + Math.Abs(a.Alpha - b.Alpha);
        }
        var mean = total / (size * size * 4);
        Assert.True(mean < 4, $"écart moyen : {mean:F2}");
    }

    [Fact]
    public void Icon_file_frame_matches_qt()
    {
        using var mine = CoverImage.AppIconImage(36);
        using var qt = SKBitmap.Decode(Path.Combine(AudioTestSupport.Assets(), "icon_file_36.png"));
        Assert.Equal((36, 36), (mine.Width, mine.Height));
        double total = 0;
        for (var y = 0; y < 36; y++)
        for (var x = 0; x < 36; x++)
            total += Math.Abs(mine.GetPixel(x, y).Alpha - qt.GetPixel(x, y).Alpha) + Math.Abs(mine.GetPixel(x, y).Green - qt.GetPixel(x, y).Green);
        Assert.True(total / (36 * 36 * 2) < 12, $"écart moyen : {total / (36 * 36 * 2):F2}");
    }

    private static double MeanDiff(SKBitmap a, SKBitmap b, int left, int top, int width, int height)
    {
        double total = 0;
        for (var y = top; y < top + height; y++)
        for (var x = left; x < left + width; x++)
            total += Math.Abs(a.GetPixel(x, y).Red - b.GetPixel(x, y).Red) + Math.Abs(a.GetPixel(x, y).Green - b.GetPixel(x, y).Green) + Math.Abs(a.GetPixel(x, y).Blue - b.GetPixel(x, y).Blue);
        return total / (width * height * 3);
    }
}
