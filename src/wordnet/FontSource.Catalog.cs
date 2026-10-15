using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WordNet;

public sealed partial class FontSource
{
    /// <summary>Reads named static TrueType/CFF faces from an OpenType font or TTC collection, leaving the stream open.</summary>
    /// <param name="source">A TTF, static OTF, or TTC stream of at most 16 MiB.</param>
    /// <returns>Immutable faces with family/style taken from the naming table.</returns>
    public static IReadOnlyList<FontSource> FromStream(Stream source)
    {
        // Reuse the bounded, caller-owned stream reader before inspecting collection offsets.
        byte[] input = new FontSource("catalog", source).data;
        var offsets = new List<int>();
        if (Read32(input, 0) == 0x74746366)
        {
            int count = Read32(input, 8);
            if (count < 1 || count > 64 || count > (input.Length - 12) / 4) throw new InvalidDataException("Font.Invalid: Invalid collection face count.");
            for (int i = 0; i < count; i++) offsets.Add(Read32(input, 12 + i * 4));
        }
        else offsets.Add(0);
        var faces = new List<FontSource>();
        foreach (int offset in offsets)
        {
            byte[] face = Extract(input, offset);
            int name = Table(face, 0x6e616d65, out int nameLength);
            if (nameLength < 6) throw new InvalidDataException("Font.Invalid: Missing naming table.");
            string family = Name(face, name, nameLength, 16) ?? Name(face, name, nameLength, 1)
                ?? throw new InvalidDataException("Font.Invalid: Missing font family name.");
            string style = Name(face, name, nameLength, 17) ?? Name(face, name, nameLength, 2) ?? "Regular";
            bool bold = StyleContains(style, "Bold");
            bool italic = StyleContains(style, "Italic") || StyleContains(style, "Oblique");
            using var stream = new MemoryStream(face, writable: false);
            faces.Add(new FontSource(family, stream, bold, italic));
        }
        return faces.AsReadOnly();
    }

    /// <summary>Reads up to 128 faces from caller-selected font files in one directory, without OS discovery or recursion.</summary>
    /// <param name="path">The explicit font directory.</param>
    /// <returns>Faces ordered deterministically by filename and collection index.</returns>
    public static IReadOnlyList<FontSource> FromDirectory(string path)
    {
        var result = new List<FontSource>();
        foreach (string file in Directory.EnumerateFiles(path).Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
        {
            using var input = File.OpenRead(file);
            foreach (FontSource face in FromStream(input))
            {
                if (result.Count == 128) throw new InvalidDataException("Font.Limit: A font directory exceeds 128 faces.");
                result.Add(face);
            }
        }
        return result.AsReadOnly();
    }

    private static byte[] Extract(byte[] source, int offset)
    {
        Bounds(source, offset, 12);
        int signature = Read32(source, offset);
        if (signature != 0x00010000 && signature != 0x74727565 && signature != 0x4f54544f)
            throw new NotSupportedException("Font.Outline: Expected a static TrueType or CFF OpenType face.");
        int count = Read16(source, offset + 4), header = checked(12 + count * 16);
        Bounds(source, offset, header);
        if (count < 1 || count > 256) throw new InvalidDataException("Font.Invalid: Invalid SFNT table count.");
        int length = header;
        for (int i = 0; i < count; i++)
        {
            int record = offset + 12 + i * 16, dataOffset = Read32(source, record + 8), size = Read32(source, record + 12);
            Bounds(source, dataOffset, size);
            if (size > 16 * 1024 * 1024 - length - 3) throw new InvalidDataException("Font.Limit: Extracted font exceeds 16 MiB.");
            length += (size + 3) & ~3;
        }
        var result = new byte[length]; Buffer.BlockCopy(source, offset, result, 0, header);
        int next = header;
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16, size = Read32(source, offset + record + 12);
            Buffer.BlockCopy(source, Read32(source, offset + record + 8), result, next, size);
            Write32(result, record + 8, next); next += (size + 3) & ~3;
        }
        // TTC tables have absolute collection offsets. Rebuilding a standalone directory
        // also requires a fresh whole-font checksum adjustment in the head table.
        int head = Table(result, 0x68656164, out int headLength);
        if (headLength >= 12)
        {
            Write32(result, head + 8, 0); uint checksum = 0;
            for (int i = 0; i < result.Length; i += 4) checksum = unchecked(checksum + (uint)Read32Unsigned(result, i));
            Write32(result, head + 8, unchecked((int)(0xb1b0afba - checksum)));
        }
        return result;
    }

    private static int Table(byte[] source, int tag, out int length)
    {
        for (int i = 0; i < Read16(source, 4); i++)
        {
            int record = 12 + i * 16;
            if (Read32(source, record) != tag) continue;
            length = Read32(source, record + 12); int offset = Read32(source, record + 8); Bounds(source, offset, length); return offset;
        }
        length = 0; return 0;
    }

    private static string? Name(byte[] data, int offset, int length, int id)
    {
        int count = Read16(data, offset + 2), strings = Read16(data, offset + 4);
        if (count > (length - 6) / 12 || strings > length) throw new InvalidDataException("Font.Invalid: Invalid name records.");
        for (int i = 0; i < count; i++)
        {
            int p = offset + 6 + i * 12;
            if (Read16(data, p + 6) != id || Read16(data, p) is not 0 and not 3) continue;
            int size = Read16(data, p + 8), start = Read16(data, p + 10);
            if (start > length - strings - size || (size & 1) != 0) throw new InvalidDataException("Font.Invalid: Invalid name string.");
            string value = Encoding.BigEndianUnicode.GetString(data, offset + strings + start, size);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static void Bounds(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length) throw new InvalidDataException("Font.Invalid: Invalid font bounds.");
    }
    private static bool StyleContains(string style, string value)
    {
#if NET8_0_OR_GREATER
        return style.Contains(value, StringComparison.OrdinalIgnoreCase);
#else
        return style.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
    private static int Read16(byte[] data, int p) { Bounds(data, p, 2); return (data[p] << 8) | data[p + 1]; }
    private static uint Read32Unsigned(byte[] data, int p)
    { Bounds(data, p, 4); return ((uint)data[p] << 24) | ((uint)data[p + 1] << 16) | ((uint)data[p + 2] << 8) | data[p + 3]; }
    private static int Read32(byte[] data, int p)
    {
        uint value = Read32Unsigned(data, p);
        if (value > int.MaxValue) throw new InvalidDataException("Font.Invalid: Unsupported font offset.");
        return (int)value;
    }
    private static void Write32(byte[] data, int p, int value)
    { data[p] = (byte)(value >> 24); data[p + 1] = (byte)(value >> 16); data[p + 2] = (byte)(value >> 8); data[p + 3] = (byte)value; }
}
