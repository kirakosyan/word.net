using System;

namespace WordNet.Internal;

// RFC 1320 MD4 identifiers required by OfficeArt. Never used for authentication or security.
internal static class ImageIdentifier
{
    internal static byte[] Compute(byte[] input)
    {
        int length = checked((input.Length + 9 + 63) / 64 * 64);
        byte[] data = new byte[length]; Array.Copy(input, data, input.Length); data[input.Length] = 128;
        ulong bits = (ulong)input.Length * 8;
        for (int i = 0; i < 8; i++) data[length - 8 + i] = (byte)(bits >> (8 * i));
        uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
        uint[] x = new uint[16]; int[] shifts = { 3, 7, 11, 19, 3, 5, 9, 13, 3, 9, 11, 15 };
        int[] order = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };
        unchecked
        {
            for (int block = 0; block < length; block += 64)
            {
                for (int i = 0; i < 16; i++) { int p = block + i * 4; x[i] = (uint)(data[p] | data[p + 1] << 8 | data[p + 2] << 16 | data[p + 3] << 24); }
                uint aa = a, bb = b, cc = c, dd = d;
                for (int round = 0; round < 3; round++)
                    for (int step = 0; step < 16; step++)
                    {
                        int k = round == 0 ? step : round == 1 ? step / 4 + step % 4 * 4 : order[step];
                        uint f = round == 0 ? (b & c) | (~b & d) : round == 1 ? (b & c) | (b & d) | (c & d) : b ^ c ^ d;
                        uint value = a + f + x[k] + (round == 0 ? 0U : round == 1 ? 0x5a827999U : 0x6ed9eba1U);
                        int shift = shifts[round * 4 + step % 4]; value = (value << shift) | (value >> (32 - shift));
                        a = d; d = c; c = b; b = value;
                    }
                a += aa; b += bb; c += cc; d += dd;
            }
        }
        byte[] output = new byte[16]; uint[] words = { a, b, c, d };
        for (int i = 0; i < 16; i++) output[i] = (byte)(words[i / 4] >> (8 * (i % 4)));
        return output;
    }
}
