using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet.Layout;

/// <summary>Bounded OpenType legacy horizontal kern format 0 pairs, in font units.</summary>
internal sealed partial class FontKerning
{
    private readonly Dictionary<uint, short> pairs = new Dictionary<uint, short>();
    private readonly Gpos? positioning;
    internal double UnitsPerEm { get; } = 1000;
    internal double Ascent { get; private set; } = 1000;
    internal double Descent { get; private set; } = 500;

    internal FontKerning(byte[] font, bool metricsOnly = false)
    {
        if (font.Length < 12) throw new InvalidDataException("Font.Invalid: Truncated SFNT header.");
        int tables = U16(font, 4);
        if (tables > (font.Length - 12) / 16) throw new InvalidDataException("Font.Invalid: Truncated table directory.");
        int start = 0, end = 0, gpos = 0, gposLength = 0, gdef = 0, gdefLength = 0, horizontal = 0, os2 = 0;
        for (int i = 0; i < tables; i++)
        {
            int entry = 12 + i * 16;
            int offset = U32(font, entry + 8), length = U32(font, entry + 12);
            if (offset > font.Length - length) throw new InvalidDataException("Font.Invalid: Table exceeds font data.");
            uint tag = (uint)U32(font, entry);
            if (tag == 0x68656164 && length >= 20) UnitsPerEm = Math.Max(1, U16(font, offset + 18));
            if (tag == 0x68686561 && length >= 10) horizontal = offset;
            if (tag == 0x4f532f32 && length >= 74) os2 = offset;
            if (tag == 0x6b65726e) { start = offset; end = offset + length; }
            if (tag == 0x47504f53) { gpos = offset; gposLength = length; }
            if (tag == 0x47444546) { gdef = offset; gdefLength = length; }
        }
        // OpenType USE_TYPO_METRICS selects sTypoAscender/Descender/LineGap;
        // otherwise use the horizontal header metrics of the actual selected face.
        int metrics = os2 != 0 && (U16(font, os2 + 62) & 128) != 0 ? os2 + 68 : horizontal == 0 ? 0 : horizontal + 4;
        if (metrics != 0)
        {
            int ascent = unchecked((short)U16(font, metrics)), descent = unchecked((short)U16(font, metrics + 2)), gap = unchecked((short)U16(font, metrics + 4));
            if (ascent <= 0 || descent > 0 || gap < 0) throw new InvalidDataException("Font.Metrics: Invalid horizontal line metrics.");
            Ascent = ascent + gap / 2.0; Descent = -descent + gap / 2.0;
        }
        if (metricsOnly) return;
        if (gposLength > 0) positioning = new Gpos(font, gpos, gposLength, gdef, gdefLength);
        if (end - start < 4 || U16(font, start) != 0) return;
        int count = U16(font, start + 2), cursor = start + 4;
        for (int i = 0; i < count; i++)
        {
            if (cursor > end - 6) throw new InvalidDataException("Font.Invalid: Truncated kerning subtable.");
            int length = U16(font, cursor + 2), coverage = U16(font, cursor + 4);
            if (length < 6 || cursor > end - length) throw new InvalidDataException("Font.Invalid: Invalid kerning bounds.");
            if ((coverage >> 8) == 0 && (coverage & 7) == 1)
            {
                if (length < 14) throw new InvalidDataException("Font.Invalid: Truncated kerning pairs.");
                int entries = U16(font, cursor + 6);
                if (entries > (length - 14) / 6) throw new InvalidDataException("Font.Invalid: Truncated kerning pair data.");
                for (int p = 0; p < entries; p++)
                {
                    int pair = cursor + 14 + p * 6;
                    uint key = ((uint)U16(font, pair) << 16) | (uint)U16(font, pair + 2);
                    short value = unchecked((short)U16(font, pair + 4));
                    if ((coverage & 8) == 0 && pairs.TryGetValue(key, out short previous)) value = checked((short)(previous + value));
                    pairs[key] = value;
                }
            }
            cursor += length;
        }
    }

    internal GlyphAdjustment[] Position(int[] glyphs, double size)
    {
        var result = new GlyphAdjustment[glyphs.Length];
        double scale = size / UnitsPerEm;
        if (positioning?.HasKerning == true) positioning.Apply(glyphs, result, scale);
        else for (int i = 1; i < glyphs.Length; i++)
            if (pairs.TryGetValue(((uint)glyphs[i - 1] << 16) | (uint)glyphs[i], out short value)) result[i - 1].AdvanceX += value * scale;
        return result;
    }

    internal double Adjustment(int left, int right, double size)
    {
        GlyphAdjustment[] result = Position(new[] { left, right }, size);
        return result[0].AdvanceX + result[1].AdvanceX;
    }

    private static int U16(byte[] data, int p)
    {
        if (p < 0 || p > data.Length - 2) throw new InvalidDataException("Font.Invalid: Truncated font value.");
        return (data[p] << 8) | data[p + 1];
    }
    private static int U32(byte[] data, int p)
    {
        if (p < 0 || p > data.Length - 4) throw new InvalidDataException("Font.Invalid: Truncated font value.");
        uint value = ((uint)data[p] << 24) | ((uint)data[p + 1] << 16) | ((uint)data[p + 2] << 8) | data[p + 3];
        if (value > int.MaxValue) throw new InvalidDataException("Font.Invalid: SFNT value exceeds supported bounds.");
        return (int)value;
    }
}

internal struct GlyphAdjustment
{
    internal double OffsetX;
    internal double OffsetY;
    internal double AdvanceX;
    internal double AdvanceY;
}
