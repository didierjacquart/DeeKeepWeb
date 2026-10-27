using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace DeeKeep.Common.Audio;

/// <summary>
/// Lecture brute des trames de date d'un tag ID3v2.3 (TYER, TDAT, TIME, TDRC) et fusion
/// à la manière de mutagen (<c>ID3.update_to_v24</c>). TagLib fusionne ces trames dès la
/// lecture, autrement que mutagen, et sans garder TDAT : il faut donc les relire soi-même.
/// </summary>
internal static partial class Id3v23Dates
{
    [GeneratedRegex(@"^([0-9]+)\z")]
    private static partial Regex YearRe();

    [GeneratedRegex(@"^([0-9]{2})([0-9]{2})\z")]
    private static partial Regex PairRe();

    /// <summary>TDRC tel que mutagen le rendrait pour ce fichier, ou null (pas un tag v2.3 lisible).</summary>
    public static string? MutagenTimestamp(string path)
    {
        var frames = ReadTextFrames(path);
        if (frames is null) return null;
        // Un TDRC déjà présent l'emporte : mutagen n'ajoute alors pas le sien.
        if (frames.TryGetValue("TDRC", out var tdrc)) return tdrc;
        var y = frames.GetValueOrDefault("TYER", "");
        var d = frames.GetValueOrDefault("TDAT", "");
        var t = frames.GetValueOrDefault("TIME", "");
        var ym = YearRe().Match(y);
        if (!ym.Success) return "";
        var timestamp = ym.Groups[1].Value;
        var dm = PairRe().Match(d);
        if (dm.Success)
        {
            timestamp += $"-{dm.Groups[2].Value}-{dm.Groups[1].Value}";
            var tm = PairRe().Match(t);
            if (tm.Success) timestamp += $"T{tm.Groups[1].Value}:{tm.Groups[2].Value}:00";
        }
        return timestamp;
    }

    /// <summary>Premières valeurs des trames de date d'un tag ID3v2.3, ou null.</summary>
    private static Dictionary<string, string>? ReadTextFrames(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[10];
            if (stream.ReadAtLeast(header, 10, throwOnEndOfStream: false) < 10) return null;
            if (!header[..3].SequenceEqual("ID3"u8) || header[3] != 3) return null;
            var flags = header[5];
            var size = (header[6] & 0x7F) << 21 | (header[7] & 0x7F) << 14 | (header[8] & 0x7F) << 7 | (header[9] & 0x7F);
            var tag = new byte[size];
            size = stream.ReadAtLeast(tag, size, throwOnEndOfStream: false);
            var data = tag.AsSpan(0, size).ToArray();
            if ((flags & 0x80) != 0) data = Resync(data);     // désynchronisation de tout le tag (v2.3)
            var pos = 0;
            if ((flags & 0x40) != 0 && data.Length >= 4) pos = 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(data);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            while (pos + 10 <= data.Length && data[pos] != 0)
            {
                var id = Encoding.Latin1.GetString(data, pos, 4);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
                var frameFlags = data[pos + 9];
                pos += 10;
                if (length < 0 || pos + length > data.Length) break;
                // Trames compressées ou chiffrées : mutagen ne les lirait pas non plus comme texte simple.
                if (id is "TYER" or "TDAT" or "TIME" or "TDRC" && (frameFlags & 0xC0) == 0 && length > 1 && !result.ContainsKey(id))
                    result[id] = DecodeText(data.AsSpan(pos, length));
                pos += length;
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Resync(byte[] data)
    {
        var output = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            output.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00) i++;
        }
        return output.ToArray();
    }

    // Premier texte d'une trame (les valeurs multiples sont séparées par des nuls).
    private static string DecodeText(ReadOnlySpan<byte> frame)
    {
        var body = frame[1..];
        var text = frame[0] switch
        {
            1 => Encoding.Unicode.GetString(body).TrimStart('﻿', '￾'),
            2 => Encoding.BigEndianUnicode.GetString(body),
            3 => Encoding.UTF8.GetString(body),
            _ => Encoding.Latin1.GetString(body),
        };
        if (frame[0] == 1 && body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) text = Encoding.BigEndianUnicode.GetString(body[2..]);
        var nul = text.IndexOf('\0');
        return nul >= 0 ? text[..nul] : text;
    }
}
