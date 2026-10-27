using System.Buffers.Binary;

namespace DeeKeep.Common.Audio;

/// <summary>
/// Durée et débit d'un MP3 calculés comme mutagen (<c>mutagen.mp3.MPEGInfo</c>), à partir
/// de l'en-tête Xing/Info (et de l'extension LAME) ou VBRI de la première trame.
/// </summary>
/// <remarks>
/// TagLib compte les trames sans retrancher le délai et le remplissage de l'encodeur
/// (« Yeah » de Queen : 4,092 s contre 4,013 s chez mutagen et ffprobe) et lit le débit
/// nominal de la trame : les colonnes <c>tracks.duration</c> et <c>tracks.bitrate</c> de la
/// base, produites par mutagen, ne seraient pas reproduites. Sans en-tête Xing/VBRI
/// (débit constant), la durée est estimée comme mutagen : (taille − début de la 1re trame) × 8 / débit.
/// </remarks>
internal static class MpegInfo
{
    private static readonly int[,] SampleRates = { { 11025, 12000, 8000 }, { 0, 0, 0 }, { 22050, 24000, 16000 }, { 44100, 48000, 32000 } };

    // Débits en kb/s : [MPEG1 L1, L2, L3, MPEG2/2.5 L1, L2/L3].
    private static readonly int[][] Bitrates =
    [
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
    ];

    /// <summary>(durée en secondes, débit en bit/s) façon mutagen, ou null.</summary>
    public static (double Length, int? BitsPerSecond)? Read(string path, long audioStart)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = Math.Max(0, audioStart);
            var buffer = new byte[65536 + 256];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            for (var i = 0; i + 4 <= read; i++)
            {
                if (TryFrame(buffer.AsSpan(i, read - i), stream.Length - (stream.Position - read + i), out var result)) return result;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static bool TryFrame(ReadOnlySpan<byte> f, long contentSize, out (double, int?)? result)
    {
        result = null;
        if (f.Length < 4 || f[0] != 0xFF || (f[1] & 0xE0) != 0xE0) return false;
        var versionBits = (f[1] >> 3) & 3;           // 3 : MPEG1, 2 : MPEG2, 0 : MPEG2.5
        var layerBits = (f[1] >> 1) & 3;             // 3 : couche I, 2 : II, 1 : III
        var bitrateIndex = f[2] >> 4;
        var rateIndex = (f[2] >> 2) & 3;
        if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || rateIndex == 3) return false;

        var mpeg1 = versionBits == 3;
        var layer = 4 - layerBits;
        var sampleRate = SampleRates[versionBits, rateIndex];
        var padding = (f[2] >> 1) & 1;
        var mono = f[3] >> 6 == 3;
        var table = mpeg1 ? layer - 1 : layer == 1 ? 3 : 4;
        var bitrate = Bitrates[table][bitrateIndex] * 1000;
        var samplesPerFrame = layer == 1 ? 384 : layer == 2 || mpeg1 ? 1152 : 576;
        var frameLength = layer == 1
            ? (12 * bitrate / sampleRate + padding) * 4
            : samplesPerFrame / 8 * bitrate / sampleRate + padding;

        if (layer == 3)
        {
            var xingOffset = mpeg1 ? (mono ? 21 : 36) : (mono ? 13 : 21);
            if (f.Length >= xingOffset + 8 && (f.Slice(xingOffset, 4).SequenceEqual("Xing"u8) || f.Slice(xingOffset, 4).SequenceEqual("Info"u8)))
            {
                var x = f[xingOffset..];
                var flags = BinaryPrimitives.ReadUInt32BigEndian(x[4..]);
                var p = 8;
                long frames = -1, bytes = -1;
                if ((flags & 1) != 0 && x.Length >= p + 4) { frames = BinaryPrimitives.ReadUInt32BigEndian(x[p..]); p += 4; }
                if ((flags & 2) != 0 && x.Length >= p + 4) { bytes = BinaryPrimitives.ReadUInt32BigEndian(x[p..]); p += 4; }
                if ((flags & 4) != 0) p += 100;
                if ((flags & 8) != 0) p += 4;
                if (frames < 0) { result = Estimate(contentSize, bitrate); return true; }

                var samples = samplesPerFrame * frames;
                int? bps = null;
                // La première trame (celle de l'en-tête) compte dans les octets mais pas dans les trames.
                if (bytes >= 0 && samples > 0)
                    bps = (int)Math.Round(Math.Max(0, bytes - frameLength) * 8.0 * sampleRate / samples, MidpointRounding.ToEven);
                if (x.Length >= p + 24 && (x.Slice(p, 4).SequenceEqual("LAME"u8) || x.Slice(p, 5).SequenceEqual("L3.99"u8)))
                {
                    var lame = x[p..];
                    var delay = (lame[21] << 4) | (lame[22] >> 4);
                    var paddingEnd = ((lame[22] & 0x0F) << 8) | lame[23];
                    samples -= delay + paddingEnd;
                }
                result = (Math.Max(0, samples) / (double)sampleRate, bps);
                return true;
            }
        }
        // En-tête VBRI (Fraunhofer), toujours 32 octets après l'en-tête de trame.
        if (f.Length >= 36 + 18 && f.Slice(36, 4).SequenceEqual("VBRI"u8))
        {
            var bytes = BinaryPrimitives.ReadUInt32BigEndian(f[(36 + 10)..]);
            var frames = BinaryPrimitives.ReadUInt32BigEndian(f[(36 + 14)..]);
            var samples = (long)samplesPerFrame * frames;
            if (samples <= 0) { result = Estimate(contentSize, bitrate); return true; }
            result = (samples / (double)sampleRate, (int)Math.Round(bytes * 8.0 * sampleRate / samples, MidpointRounding.ToEven));
            return true;
        }
        // Débit constant sans en-tête : mutagen estime la durée d'après la taille, de la
        // première trame à la fin du fichier (ID3v1 compris).
        result = Estimate(contentSize, bitrate);
        return true;
    }

    private static (double, int?) Estimate(long contentSize, int bitrate) => (8.0 * contentSize / bitrate, bitrate);
}
