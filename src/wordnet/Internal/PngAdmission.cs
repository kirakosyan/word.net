using System;
using System.IO;
using System.IO.Compression;

namespace WordNet.Internal;

// PNG specification scanline sizes, including Adam7 passes. Bound decompression
// before the provider copies inflated IDAT data into an unbounded memory stream.
internal static class PngAdmission
{
    internal static void Validate(byte[] bytes, ConversionContext context)
    {
        if (bytes.Length < 33 || Big(bytes, 8) != 13) throw Invalid();
        int width = Big(bytes, 16), height = Big(bytes, 20), depth = bytes[24], color = bytes[25];
        int samples = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (width <= 0 || height <= 0 || samples == 0 || depth is not 1 and not 2 and not 4 and not 8 and not 16
            || color == 3 && depth == 16 || color is 2 or 4 or 6 && depth < 8
            || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1) throw Invalid();
        long expected = 0;
        if (bytes[28] == 0) Pass(0, 0, 1, 1);
        else
        {
            Pass(0, 0, 8, 8); Pass(4, 0, 8, 8); Pass(0, 4, 4, 8); Pass(2, 0, 4, 4);
            Pass(0, 2, 2, 4); Pass(1, 0, 2, 2); Pass(0, 1, 1, 2);
        }
        if (expected > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: PNG scanlines exceed the expansion limit.");
        using var compressed = new MemoryStream();
        int position = 8; bool ended = false;
        while (position <= bytes.Length - 12)
        {
            context.CheckCancellation();
            int length = Big(bytes, position);
            if (length > bytes.Length - position - 12) throw Invalid();
            bool idat = Tag(position + 4, 'I', 'D', 'A', 'T'), end = Tag(position + 4, 'I', 'E', 'N', 'D');
            if (idat) compressed.Write(bytes, position + 8, length);
            position += length + 12;
            if (end) { if (length != 0) throw Invalid(); ended = true; break; }
        }
        if (!ended || position != bytes.Length || compressed.Length < 6) throw Invalid();
        byte[] data = compressed.GetBuffer(); int size = checked((int)compressed.Length);
        if ((data[0] & 15) != 8 || (data[0] >> 4) > 7 || (data[1] & 32) != 0 || ((data[0] << 8) + data[1]) % 31 != 0) throw Invalid();
        using var input = new MemoryStream(data, 2, size - 6, writable: false);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        var buffer = new byte[8192]; long count = 0; uint a = 1, b = 0; int read;
        while ((read = inflate.Read(buffer, 0, (int)Math.Min(buffer.Length, expected - count + 1))) > 0)
        {
            context.CheckCancellation(); count += read;
            if (count > expected) throw new InvalidDataException("Image.Decode: PNG scanline data exceeds its declared dimensions.");
            for (int i = 0; i < read; i++) { a = (a + buffer[i]) % 65521; b = (b + a) % 65521; }
        }
        uint checksum = ((uint)data[size - 4] << 24) | ((uint)data[size - 3] << 16) | ((uint)data[size - 2] << 8) | data[size - 1];
        if (count != expected || checksum != ((b << 16) | a)) throw Invalid();

        void Pass(int x, int y, int dx, int dy)
        {
            long columns = Math.Max(0, ((long)width - x + dx - 1) / dx), rows = Math.Max(0, ((long)height - y + dy - 1) / dy);
            if (columns > 0 && rows > 0) expected = checked(expected + rows * (1 + (columns * samples * depth + 7) / 8));
        }
        bool Tag(int p, char c0, char c1, char c2, char c3) => bytes[p] == c0 && bytes[p + 1] == c1 && bytes[p + 2] == c2 && bytes[p + 3] == c3;
    }

    private static int Big(byte[] bytes, int p)
    {
        uint value = ((uint)bytes[p] << 24) | ((uint)bytes[p + 1] << 16) | ((uint)bytes[p + 2] << 8) | bytes[p + 3];
        if (value > int.MaxValue) throw Invalid();
        return (int)value;
    }
    private static InvalidDataException Invalid() => new InvalidDataException("Image.Decode: Invalid PNG chunk, scanline or zlib structure.");
}
