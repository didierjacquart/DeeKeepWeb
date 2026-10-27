using System.Buffers.Binary;
using System.Security.Cryptography;
using SkiaSharp;

namespace DeeKeep.Common.Audio;

/// <summary>Type MIME et dimensions d'une image (0 × 0 si elles n'ont pu être lues).</summary>
public sealed record CoverInfo(string Mime, long Width, long Height);

/// <summary>
/// Jaquettes : empreinte (clé de la table <c>covers</c>), type et dimensions,
/// et marquage de l'icône de l'application — portage de <c>db_init.image_info</c>,
/// <c>CoverStore.add</c> et <c>utils.stamp_app_icon</c>/<c>_draw_app_icon_image</c>.
/// </summary>
public static class CoverImage
{
    /// <summary>APP_BADGE_RATIO : côté du badge rapporté au plus petit côté de la jaquette.</summary>
    public const double BadgeRatio = 0.18;
    /// <summary>APP_BADGE_MARGIN : marge depuis les bords haut et droit (même référence).</summary>
    public const double BadgeMargin = 0.04;
    /// <summary>APP_BADGE_MIN_COVER : en deçà, la jaquette est une vignette et n'est pas marquée.</summary>
    public const int BadgeMinCover = 96;
    /// <summary>Qualité JPEG de la jaquette marquée.</summary>
    public const int StampJpegQuality = 92;

    // Signature PNG en octets bruts : un littéral u8 coderait l'octet 0x89 en UTF-8, sur deux octets.
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>SHA-1 hexadécimal minuscule des octets bruts : clé de la table <c>covers</c>.</summary>
    public static string Sha1(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA1.HashData(data));

    /// <summary>
    /// Type et dimensions lus dans l'en-tête seul (<c>SKCodec</c> : l'image n'est pas décodée).
    /// Un format inconnu de Skia retombe sur <see cref="DescribeLikePython"/>. Diffère de
    /// l'original pour WebP (dimensions réelles au lieu de 0 × 0).
    /// </summary>
    public static CoverInfo Describe(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return new CoverInfo("", 0, 0);
        try
        {
            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            var mime = codec?.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Gif => "image/gif",
                SKEncodedImageFormat.Bmp => "image/bmp",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => null,
            };
            if (mime is not null) return new CoverInfo(mime, codec!.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            // En-tête abîmé : la lecture « à la main » de l'original décide.
        }
        return DescribeLikePython(data);
    }

    /// <summary>
    /// <c>db_init.image_info</c> à l'identique : signatures PNG, GIF, BMP, WebP (sans
    /// dimensions) et JPEG (parcours des marqueurs jusqu'à un SOF). Une image d'un format
    /// inconnu n'est pas rejetée : « application/octet-stream », 0 × 0. C'est ce qu'il faut
    /// pour écrire une ligne de <c>covers</c> identique à celle de l'application desktop.
    /// </summary>
    public static CoverInfo DescribeLikePython(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return new CoverInfo("", 0, 0);
        if (data.StartsWith(PngSignature) && data.Length >= 24)
            return new CoverInfo("image/png", BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..]));
        if (data.StartsWith("GIF8"u8) && data.Length >= 10)
            return new CoverInfo("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        if (data.StartsWith("BM"u8) && data.Length >= 26)
            return new CoverInfo("image/bmp", BinaryPrimitives.ReadUInt32LittleEndian(data[18..]), BinaryPrimitives.ReadUInt32LittleEndian(data[22..]));
        if (data.StartsWith("RIFF"u8) && data.Length >= 12 && data[8..12].SequenceEqual("WEBP"u8))
            return new CoverInfo("image/webp", 0, 0);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
        {
            // On saute de marqueur en marqueur jusqu'à un en-tête de trame (SOF0..SOF15,
            // hors DHT/JPG/DAC), qui porte les dimensions réelles.
            int index = 2, end = data.Length;
            while (index + 9 < end)
            {
                if (data[index] != 0xFF) { index++; continue; }
                var marker = data[index + 1];
                if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) { index += 2; continue; }
                var length = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 2)..]);
                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                    return new CoverInfo("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(data[(index + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(index + 5)..]));
                if (length <= 0) break;
                index += 2 + length;
            }
            return new CoverInfo("image/jpeg", 0, 0);
        }
        return new CoverInfo("application/octet-stream", 0, 0);
    }

    /// <summary>
    /// <c>stamp_app_icon</c> : jaquette signée de l'icône de l'application en haut à
    /// droite, réencodée en JPEG qualité 92. Le badge mesure 18 % du plus petit côté
    /// (16 px au moins), à 4 % des bords. Rend les octets d'origine inchangés si la
    /// jaquette est vide, illisible ou plus petite que 96 px : marquer n'est jamais une
    /// raison de perdre une pochette.
    /// </summary>
    public static byte[] StampAppIcon(byte[] cover, double ratio = BadgeRatio, double margin = BadgeMargin)
    {
        if (cover is not { Length: > 0 }) return cover;
        try
        {
            using var decoded = SKBitmap.Decode(cover);
            if (decoded is null) return cover;
            var side = Math.Min(decoded.Width, decoded.Height);
            if (side < BadgeMinCover) return cover;

            var badge = Math.Max(16, (int)Math.Round(side * ratio, MidpointRounding.ToEven));
            var gap = Math.Max(1, (int)Math.Round(side * margin, MidpointRounding.ToEven));
            using var icon = AppIconImage(badge);

            // Surface opaque : le JPEG ignore la transparence, une PNG transparente ne doit pas virer au noir.
            using var canvasBitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(canvasBitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawImage(SKImage.FromBitmap(decoded), 0, 0, new SKSamplingOptions());
                canvas.DrawImage(SKImage.FromBitmap(icon), decoded.Width - icon.Width - gap, gap, new SKSamplingOptions());
            }
            // 4:4:4 (couleur non sous-échantillonnée) : ce que produisait Qt à cette qualité (vérifié sur sa sortie).
            using var pixmap = canvasBitmap.PeekPixels();
            using var encoded = pixmap.Encode(new SKJpegEncoderOptions(StampJpegQuality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore));
            return encoded?.ToArray() ?? cover;
        }
        catch (Exception)
        {
            return cover;
        }
    }

    /// <summary>
    /// <c>app_icon_image</c> : icône carrée de côté <paramref name="size"/>, fond transparent.
    /// Tirée de l'icon.ico embarqué (la plus petite résolution qui couvre la taille demandée,
    /// à défaut la plus grande — réduire préserve les détails, agrandir les invente),
    /// sinon dessinée (<see cref="DrawAppIcon"/>).
    /// </summary>
    public static SKBitmap AppIconImage(int size)
    {
        var frames = IconFrames.Value;
        if (frames.Count == 0) return DrawAppIcon(size);
        var covering = frames.Where(f => Math.Min(f.Width, f.Height) >= size).ToList();
        var best = covering.Count > 0 ? covering.MinBy(f => f.Width)! : frames.MaxBy(f => f.Width)!;
        if (best.Width == size && best.Height == size) return best.Copy();
        // Réduction/agrandissement lissés (bilinéaire + mipmaps), comme le lissage de Qt.
        return best.Resize(new SKSizeI(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? DrawAppIcon(size);
    }

    // Résolutions de l'icon.ico embarqué (copie de celui de l'application desktop).
    private static readonly Lazy<IReadOnlyList<SKBitmap>> IconFrames = new(LoadIconFrames);

    private static IReadOnlyList<SKBitmap> LoadIconFrames()
    {
        var frames = new List<SKBitmap>();
        try
        {
            using var stream = typeof(CoverImage).Assembly.GetManifestResourceStream("DeeKeep.Common.Audio.icon.ico");
            if (stream is null) return frames;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var ico = buffer.ToArray();
            int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var entry = ico.AsSpan(6 + 16 * i, 16);
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
                try
                {
                    // Les résolutions de l'icône sont des PNG ; une image BMP serait ignorée.
                    if (SKBitmap.Decode(ico.AsSpan(offset, size)) is { } frame) frames.Add(frame);
                }
                catch (Exception)
                {
                    // Résolution illisible : les autres suffisent.
                }
            }
        }
        catch (Exception)
        {
            // Ressource absente ou abîmée : l'icône sera dessinée.
        }
        return frames;
    }

    /// <summary>
    /// <c>_draw_app_icon_image</c> : l'icône vectorielle (croche sombre sur carré arrondi
    /// cyan), définie dans un repère 128 × 128 et rendue avec anticrénelage (8 × 8
    /// échantillons par pixel). Repli quand l'icon.ico est indisponible.
    /// </summary>
    public static SKBitmap DrawAppIcon(int size = 128)
    {
        const int n = 8;
        var cyan = new SKColor(0x00, 0xE5, 0xFF);
        var dark = new SKColor(0x00, 0x36, 0x3D);
        var flag = FlagPolygon();
        var image = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var scale = 128.0 / size;
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                int inDark = 0, inCyan = 0;
                for (var sy = 0; sy < n; sy++)
                {
                    for (var sx = 0; sx < n; sx++)
                    {
                        var x = (px + (sx + 0.5) / n) * scale;
                        var y = (py + (sy + 0.5) / n) * scale;
                        if (InNote(x, y, flag)) inDark++;
                        else if (InRoundedRect(x, y, 0, 0, 128, 128, 30)) inCyan++;
                    }
                }
                var covered = inDark + inCyan;
                if (covered == 0) continue;
                image.SetPixel(px, py, new SKColor(
                    (byte)Math.Round((inDark * dark.Red + inCyan * cyan.Red) / (double)covered),
                    (byte)Math.Round((inDark * dark.Green + inCyan * cyan.Green) / (double)covered),
                    (byte)Math.Round((inDark * dark.Blue + inCyan * cyan.Blue) / (double)covered),
                    (byte)Math.Round(255.0 * covered / (n * n))));
            }
        }
        return image;
    }

    // Croche : hampe (rectangle arrondi), crochet (deux courbes de Bézier, remplissage
    // pair-impair comme QPainterPath) et tête (ellipse inclinée de -20° centrée en 62,84).
    private static bool InNote(double x, double y, (double X, double Y)[] flag)
    {
        const double stemX = 78, stemW = 9, stemTop = 32, stemBottom = 90;
        if (InRoundedRect(x, y, stemX, stemTop, stemW, stemBottom - stemTop, 4)) return true;
        if (InPolygon(x, y, flag)) return true;
        var (u, v) = (x - 62, y - 84);
        var angle = -20 * Math.PI / 180;
        var (lx, ly) = (Math.Cos(angle) * u + Math.Sin(angle) * v, -Math.Sin(angle) * u + Math.Cos(angle) * v);
        return lx * lx / 400 + ly * ly / 196 <= 1;
    }

    private static (double X, double Y)[] FlagPolygon()
    {
        const double stemX = 78, stemW = 9, stemTop = 32;
        var points = new List<(double X, double Y)> { (stemX + stemW, stemTop) };
        AddCubic(points, (stemX + stemW, stemTop), (stemX + 34, stemTop + 8), (stemX + 32, stemTop + 30), (stemX + 8, stemTop + 34));
        AddCubic(points, (stemX + 8, stemTop + 34), (stemX + 26, stemTop + 24), (stemX + 26, stemTop + 12), (stemX + stemW, stemTop + 14));
        return points.ToArray();
    }

    private static void AddCubic(List<(double X, double Y)> points, (double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3)
    {
        const int steps = 48;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var mt = 1 - t;
            points.Add((mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X,
                        mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y));
        }
    }

    private static bool InPolygon(double x, double y, (double X, double Y)[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > y) != (poly[j].Y > y)
                && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static bool InRoundedRect(double x, double y, double left, double top, double width, double height, double radius)
    {
        if (x < left || x > left + width || y < top || y > top + height) return false;
        var cx = Math.Clamp(x, left + radius, left + width - radius);
        var cy = Math.Clamp(y, top + radius, top + height - radius);
        var (dx, dy) = (x - cx, y - cy);
        return dx * dx + dy * dy <= radius * radius;
    }
}
