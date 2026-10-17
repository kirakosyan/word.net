using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet.Layout;

// Admit the composite graph before PdfPig expands glyph paths, including unused glyphs.
internal static class TrueTypeOutlineGuard
{
    internal static void Validate(byte[] bytes)
    {
        int U16(int p) { Check(p, 2); return bytes[p] * 256 + bytes[p + 1]; }
        int I16(int p) => unchecked((short)U16(p));
        int U32(int p) { Check(p, 4); uint n = ((uint)bytes[p] << 24) | ((uint)bytes[p + 1] << 16) | ((uint)bytes[p + 2] << 8) | bytes[p + 3]; if (n > int.MaxValue) throw new InvalidDataException("Font.Outline: Table offset exceeds bounds."); return (int)n; }
        void Check(int p, int length) { if (p < 0 || length < 0 || p > bytes.Length - length) throw new InvalidDataException("Font.Outline: Truncated font table."); }
        int count = U16(4); if (count > 256) throw new InvalidDataException("Font.Outline: Too many font tables.");
        var tables = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        for (int t = 0; t < count; t++)
        {
            int p = 12 + t * 16; Check(p, 16); string name = System.Text.Encoding.ASCII.GetString(bytes, p, 4); int start = U32(p + 8), length = U32(p + 12); Check(start, length);
            if (tables.ContainsKey(name)) throw new InvalidDataException("Font.Outline: Duplicate font table."); tables.Add(name, (start, length));
        }
        if (!tables.TryGetValue("head", out var head) || head.Length < 54 || !tables.TryGetValue("maxp", out var maxp) || maxp.Length < 6 || !tables.TryGetValue("loca", out var loca) || !tables.TryGetValue("glyf", out var glyf)) throw new NotSupportedException("Font.Outline: Outlined previews and native typography require static TrueType outlines.");
        if (tables.ContainsKey("fvar")) throw new NotSupportedException("Font.Outline: Variable fonts require a static caller-selected instance.");
        int glyphCount = U16(maxp.Start + 4), format = I16(head.Start + 50); if (format is not 0 and not 1) throw new InvalidDataException("Font.Outline: Invalid loca format.");
        int step = format == 0 ? 2 : 4; if ((glyphCount + 1) * step > loca.Length) throw new InvalidDataException("Font.Outline: Incomplete glyph offsets.");
        var offsets = new int[glyphCount + 1];
        for (int i = 0; i <= glyphCount; i++) { offsets[i] = format == 0 ? U16(loca.Start + i * 2) * 2 : U32(loca.Start + i * 4); if (offsets[i] > glyf.Length || i > 0 && offsets[i] < offsets[i - 1]) throw new InvalidDataException("Font.Outline: Invalid glyph offset."); }
        var children = new List<int>[glyphCount]; var points = new int[glyphCount]; int edges = 0;
        for (int i = 0; i < glyphCount; i++)
        {
            children[i] = new List<int>(); int p = glyf.Start + offsets[i], end = glyf.Start + offsets[i + 1]; if (p == end) continue;
            if (end - p < 10) throw new InvalidDataException("Font.Outline: Incomplete glyph header."); int contours = I16(p); p += 10;
            if (contours >= 0) { if (contours * 2 > end - p) throw new InvalidDataException("Font.Outline: Incomplete contour endpoints."); points[i] = contours == 0 ? 0 : U16(p + (contours - 1) * 2) + 1; continue; }
            if (contours != -1) throw new InvalidDataException("Font.Outline: Invalid composite contour count.");
            int flags;
            do
            {
                if (p > end - 4 || ++edges > 1000000) throw new InvalidDataException("Font.Outline: Composite graph exceeds bounds."); flags = U16(p); int child = U16(p + 2); if (child >= glyphCount) throw new InvalidDataException("Font.Outline: Invalid component glyph."); children[i].Add(child); p += 4 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 64) != 0 ? 4 : (flags & 128) != 0 ? 8 : 0);
                if (p > end) throw new InvalidDataException("Font.Outline: Incomplete component transform.");
            } while ((flags & 32) != 0);
        }
        var state = new byte[glyphCount]; var heights = new int[glyphCount];
        int Visit(int glyph, int depth)
        {
            if (depth > 24 || state[glyph] == 1) throw new InvalidDataException("Font.Outline: Cyclic or excessive composite depth."); if (state[glyph] == 2) return points[glyph]; state[glyph] = 1;
            foreach (int child in children[glyph]) { heights[glyph] = Math.Max(heights[glyph], heights[child] + 1); points[glyph] = checked(points[glyph] + Visit(child, depth + 1)); if (points[glyph] > 100000) throw new InvalidDataException("Font.Outline: Expanded glyph exceeds point budget."); }
            foreach (int child in children[glyph]) heights[glyph] = Math.Max(heights[glyph], heights[child] + 1);
            if (heights[glyph] > 24) throw new InvalidDataException("Font.Outline: Excessive composite expansion depth.");
            state[glyph] = 2; return points[glyph];
        }
        for (int i = 0; i < glyphCount; i++) _ = Visit(i, 0);
    }
}
