using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DeeKeep.Common.Data;

/// <summary>
/// BLAKE2b (RFC 7693), sans clé, longueur de condensé paramétrable.
/// .NET n'en fournit pas : l'empreinte de ligne de la base existante
/// (<c>hashlib.blake2b(digest_size=8)</c> côté Python) l'exige.
/// </summary>
public sealed class Blake2b
{
    private static readonly ulong[] IV =
    [
        0x6a09e667f3bcc908UL, 0xbb67ae8584caa73bUL, 0x3c6ef372fe94f82bUL, 0xa54ff53a5f1d36f1UL,
        0x510e527fade682d1UL, 0x9b05688c2b3e6c1fUL, 0x1f83d9abfb41bd6bUL, 0x5be0cd19137e2179UL,
    ];

    private static readonly byte[,] Sigma =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
        { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
        { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 },
        { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
        { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 },
        { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
        { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 },
        { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
        { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 },
        { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 },
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
        { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
    };

    private readonly ulong[] _h = new ulong[8];
    private readonly byte[] _buffer = new byte[128];
    private readonly ulong[] _m = new ulong[16];
    private readonly ulong[] _v = new ulong[16];
    private readonly int _outLength;
    private int _bufferLength;
    private ulong _t0, _t1;

    public Blake2b(int outLength = 64)
    {
        if (outLength is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(outLength));
        _outLength = outLength;
        Array.Copy(IV, _h, 8);
        _h[0] ^= 0x01010000UL ^ (ulong)outLength;
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        while (data.Length > 0)
        {
            if (_bufferLength == 128)
            {
                Increment(128);
                Compress(_buffer, last: false);
                _bufferLength = 0;
            }
            var take = Math.Min(128 - _bufferLength, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_bufferLength));
            _bufferLength += take;
            data = data[take..];
        }
    }

    public byte[] Final()
    {
        Increment((ulong)_bufferLength);
        Array.Clear(_buffer, _bufferLength, 128 - _bufferLength);
        Compress(_buffer, last: true);
        var full = new byte[64];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64LittleEndian(full.AsSpan(i * 8), _h[i]);
        return full[.._outLength];
    }

    private void Increment(ulong n)
    {
        _t0 += n;
        if (_t0 < n) _t1++;
    }

    private void Compress(byte[] block, bool last)
    {
        for (var i = 0; i < 16; i++) _m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(i * 8));
        for (var i = 0; i < 8; i++)
        {
            _v[i] = _h[i];
            _v[i + 8] = IV[i];
        }
        _v[12] ^= _t0;
        _v[13] ^= _t1;
        if (last) _v[14] = ~_v[14];

        for (var r = 0; r < 12; r++)
        {
            G(0, 4, 8, 12, _m[Sigma[r, 0]], _m[Sigma[r, 1]]);
            G(1, 5, 9, 13, _m[Sigma[r, 2]], _m[Sigma[r, 3]]);
            G(2, 6, 10, 14, _m[Sigma[r, 4]], _m[Sigma[r, 5]]);
            G(3, 7, 11, 15, _m[Sigma[r, 6]], _m[Sigma[r, 7]]);
            G(0, 5, 10, 15, _m[Sigma[r, 8]], _m[Sigma[r, 9]]);
            G(1, 6, 11, 12, _m[Sigma[r, 10]], _m[Sigma[r, 11]]);
            G(2, 7, 8, 13, _m[Sigma[r, 12]], _m[Sigma[r, 13]]);
            G(3, 4, 9, 14, _m[Sigma[r, 14]], _m[Sigma[r, 15]]);
        }
        for (var i = 0; i < 8; i++) _h[i] ^= _v[i] ^ _v[i + 8];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void G(int a, int b, int c, int d, ulong x, ulong y)
    {
        var v = _v;
        v[a] = v[a] + v[b] + x;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 63);
    }
}
