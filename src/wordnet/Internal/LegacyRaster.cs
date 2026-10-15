using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WordNet.Internal;

internal static class LegacyRaster
{
    internal static byte[] Bmp(byte[] data, int width, int height)
    {
        int header = Little32(data, 14), offset = Little32(data, 10), bits = Little16(data, 28);
        if (header < 40 || header > data.Length - 14 || Little32(data, 30) != 0 || bits is not 8 and not 24 and not 32 || Little16(data, 26) != 1)
            throw new NotSupportedException("Image.UnsupportedFormat: Only uncompressed 8/24/32-bit BMP images are supported.");
        int stride = checked(((width * bits + 31) / 32) * 4);
        if (offset < 14 + header || (long)offset + (long)stride * height > data.Length) throw Invalid();
        var pixels = new byte[checked(width * height * 4)];
        int colors = bits == 8 ? (Little32(data, 46) == 0 ? 256 : Little32(data, 46)) : 0;
        if (colors < 0 || colors > 256 || bits == 8 && 14L + header + colors * 4L > offset) throw Invalid();
        bool topDown = Little32(data, 22) < 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int source = offset + (topDown ? y : height - y - 1) * stride + x * (bits / 8), target = (y * width + x) * 4;
                if (bits == 8) { int index = data[source]; if (index >= colors) throw Invalid(); source = 14 + header + index * 4; }
                pixels[target] = data[source + 2]; pixels[target + 1] = data[source + 1]; pixels[target + 2] = data[source]; pixels[target + 3] = 255;
            }
        return Png(pixels, width, height);
    }

    internal static byte[] Gif(byte[] data, int width, int height)
    {
        if (data.Length < 13 || data[data.Length - 1] != 0x3B) throw Invalid();
        int cursor = 13, packed = data[10], transparent = -1;
        byte[]? global = (packed & 128) != 0 ? Palette(data, ref cursor, 2 << (packed & 7)) : null;
        while (cursor < data.Length)
        {
            int marker = data[cursor++];
            if (marker == 0x21)
            {
                if (cursor >= data.Length) throw Invalid();
                int kind = data[cursor++];
                byte[] extension = Blocks(data, ref cursor);
                if (kind == 0xF9)
                {
                    if (extension.Length != 4) throw Invalid();
                    transparent = (extension[0] & 1) != 0 ? extension[3] : -1;
                }
                continue;
            }
            if (marker != 0x2C || cursor > data.Length - 9) throw Invalid();
            int left = Little16(data, cursor), top = Little16(data, cursor + 2), w = Little16(data, cursor + 4), h = Little16(data, cursor + 6);
            packed = data[cursor + 8]; cursor += 9;
            if (w <= 0 || h <= 0 || left + w > width || top + h > height) throw Invalid();
            byte[] palette = (packed & 128) != 0 ? Palette(data, ref cursor, 2 << (packed & 7)) : global ?? throw Invalid();
            if (cursor >= data.Length) throw Invalid();
            int minimum = data[cursor++]; byte[] compressed = Blocks(data, ref cursor);
            byte[] indices = Lzw(compressed, minimum, checked(w * h));
            var pixels = new byte[checked(width * height * 4)];
            int sourceRow = 0;
            if ((packed & 64) == 0) for (int y = 0; y < h; y++) Row(y);
            else for (int pass = 0; pass < 4; pass++)
                for (int y = pass == 0 ? 0 : pass == 1 ? 4 : pass == 2 ? 2 : 1; y < h; y += pass < 2 ? 8 : pass == 2 ? 4 : 2) Row(y);
            return Png(pixels, width, height);

            void Row(int row)
            {
                for (int x = 0; x < w; x++)
                {
                    int color = indices[sourceRow * w + x], destination = ((top + row) * width + left + x) * 4;
                    if (color * 3 + 2 >= palette.Length) throw Invalid();
                    pixels[destination] = palette[color * 3]; pixels[destination + 1] = palette[color * 3 + 1]; pixels[destination + 2] = palette[color * 3 + 2];
                    pixels[destination + 3] = color == transparent ? (byte)0 : (byte)255;
                }
                sourceRow++;
            }
        }
        throw Invalid();
    }

    private static byte[] Lzw(byte[] input, int minimum, int length)
    {
        if (minimum < 2 || minimum > 8) throw Invalid();
        int clear = 1 << minimum, end = clear + 1, available = end + 1, size = minimum + 1, bit = 0, previous = -1, output = 0;
        var prefixes = new int[4096]; var suffixes = new byte[4096]; var stack = new byte[4097]; var result = new byte[length];
        for (int i = 0; i < clear; i++) suffixes[i] = (byte)i;
        byte first = 0;
        while (bit + size <= input.Length * 8)
        {
            int code = 0;
            for (int i = 0; i < size; i++) code |= ((input[(bit + i) >> 3] >> ((bit + i) & 7)) & 1) << i;
            bit += size;
            if (code == clear) { available = end + 1; size = minimum + 1; previous = -1; continue; }
            if (code == end) { if (output != length) throw Invalid(); return result; }
            if (code > available || code == available && previous < 0) throw Invalid();
            int current = code, count = 0;
            if (current == available) { stack[count++] = first; current = previous; }
            while (current >= clear)
            {
                if (current >= available || count >= 4096) throw Invalid();
                stack[count++] = suffixes[current]; current = prefixes[current];
            }
            first = (byte)current; stack[count++] = first;
            while (count > 0) { if (output >= length) throw Invalid(); result[output++] = stack[--count]; }
            if (previous >= 0 && available < 4096)
            {
                prefixes[available] = previous; suffixes[available++] = first;
                if (available == 1 << size && size < 12) size++;
            }
            previous = code;
        }
        throw Invalid();
    }

    private static byte[] Palette(byte[] bytes, ref int cursor, int colors)
    {
        int length = checked(colors * 3);
        if (cursor > bytes.Length - length) throw Invalid();
        var result = new byte[length]; Buffer.BlockCopy(bytes, cursor, result, 0, length); cursor += length; return result;
    }

    private static byte[] Blocks(byte[] bytes, ref int cursor)
    {
        using var result = new MemoryStream();
        while (cursor < bytes.Length)
        {
            int length = bytes[cursor++];
            if (length == 0) return result.ToArray();
            if (cursor > bytes.Length - length) throw Invalid();
            result.Write(bytes, cursor, length); cursor += length;
        }
        throw Invalid();
    }

    internal static byte[] Png(byte[] rgba, int width, int height)
    {
        using var output = new MemoryStream();
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 }; output.Write(signature, 0, signature.Length);
        var header = new byte[13]; Put32(header, 0, (uint)width); Put32(header, 4, (uint)height); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream(); compressed.WriteByte(0x78); compressed.WriteByte(0x9C);
        uint a = 1, b = 0;
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                deflate.WriteByte(0); b = (b + a) % 65521;
                deflate.Write(rgba, y * width * 4, width * 4);
                for (int i = y * width * 4; i < (y + 1) * width * 4; i++) { a = (a + rgba[i]) % 65521; b = (b + a) % 65521; }
            }
        }
        var checksum = new byte[4]; Put32(checksum, 0, (b << 16) | a); compressed.Write(checksum, 0, 4);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();

        void Chunk(string name, byte[] data)
        {
            var sizeBytes = new byte[4]; Put32(sizeBytes, 0, (uint)data.Length); output.Write(sizeBytes, 0, 4);
            byte[] type = Encoding.ASCII.GetBytes(name); output.Write(type, 0, 4); output.Write(data, 0, data.Length);
            uint crc = uint.MaxValue;
            foreach (byte value in type) Update(value);
            foreach (byte value in data) Update(value);
            Put32(sizeBytes, 0, ~crc); output.Write(sizeBytes, 0, 4);
            void Update(byte value) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320U : crc >> 1; }
        }
    }

    internal static int Little16(byte[] data, int at) { if (at < 0 || at > data.Length - 2) throw Invalid(); return data[at] | (data[at + 1] << 8); }
    internal static int Little32(byte[] data, int at) { if (at < 0 || at > data.Length - 4) throw Invalid(); return data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24); }
    private static void Put32(byte[] data, int at, uint value) { data[at] = (byte)(value >> 24); data[at + 1] = (byte)(value >> 16); data[at + 2] = (byte)(value >> 8); data[at + 3] = (byte)value; }
    private static InvalidDataException Invalid() => new InvalidDataException("Image.Decode: Malformed or truncated raster data.");
}
