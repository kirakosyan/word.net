using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;

namespace WordNet.Layout;

// Static, unhinted OpenType MATH 1.0. Offsets are always relative to the
// owning subtable; device tables are admitted but do not affect point geometry.
internal sealed class MathFont
{
    internal sealed class Construction
    {
        internal List<(int Glyph, int Advance)> Variants { get; } = new List<(int, int)>();
        internal List<(int Glyph, int Start, int End, int Advance, bool Extender)> Parts { get; } = new List<(int, int, int, int, bool)>();
        internal int Italic { get; set; }
    }
    private readonly byte[] data;
    private readonly ConversionContext context;
    private readonly int math, limit, glyphCount, loca, glyf, locaStep, hmtx, metrics;
    private readonly Dictionary<int, int> italics = new Dictionary<int, int>();
    private readonly Dictionary<(int Glyph, int Corner), (int[] Heights, int[] Values)> kerns = new Dictionary<(int, int), (int[], int[])>();
    internal Dictionary<int, Construction> Vertical { get; } = new Dictionary<int, Construction>();
    internal Dictionary<int, Construction> Horizontal { get; } = new Dictionary<int, Construction>();
    internal int Units { get; }
    internal int ScriptPercent { get; }
    internal int ScriptScriptPercent { get; }
    internal int DisplayOperatorMinHeight { get; }
    internal int DegreePercent { get; }
    internal int MinOverlap { get; }
    internal int[] Constants { get; } = new int[51];
    private readonly List<(int Start, int End, int Glyph)> mappings = new List<(int, int, int)>();
    private readonly List<(int Start, int End)> claims = new List<(int, int)>();
    internal MathFont(byte[] data, ConversionContext context)
    {
        this.data = data; this.context = context; TrueTypeOutlineGuard.Validate(data);
        var tables = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < Raw16(4); i++)
        { int p = 12 + i * 16; tables.Add(System.Text.Encoding.ASCII.GetString(data, p, 4), (Raw32(p + 8), Raw32(p + 12))); }
        int tableEnd = 12 + Raw16(4) * 16;
        foreach (var table in tables.Values.Where(t => t.Length > 0).OrderBy(t => t.Start))
        { if (table.Start < tableEnd) throw Invalid(); tableEnd = checked(table.Start + table.Length); }
        (int Start, int Length) Table(string name, int minimum)
        { if (!tables.TryGetValue(name, out var value) || value.Length < minimum) throw new NotSupportedException("Equation.Font: A complete static TrueType MATH font is required."); return value; }
        var mt = Table("MATH", 10); math = mt.Start; limit = math + mt.Length;
        if (U16(math) != 1 || U16(math + 2) != 0) throw Invalid();
        int head = Table("head", 54).Start; Units = Raw16(head + 18); if (Units is < 16 or > 16384) throw Invalid();
        int os = Table("OS/2", 10).Start;
        if ((Raw16(os + 8) & 0x202) != 0) throw new NotSupportedException("Equation.Embedding: The supplied math face prohibits outline embedding.");
        glyphCount = Raw16(Table("maxp", 6).Start + 4); loca = Table("loca", 2).Start; glyf = Table("glyf", 0).Start; locaStep = Raw16(head + 50) == 0 ? 2 : 4;
        metrics = Raw16(Table("hhea", 36).Start + 34); var hm = Table("hmtx", 4); hmtx = hm.Start;
        if (metrics < 1 || metrics > glyphCount || hm.Length < metrics * 4 + (glyphCount - metrics) * 2) throw Invalid();
        var cm = Table("cmap", 4); int cmap = cm.Start, records = Raw16(cmap + 2), selected = 0;
        if (records > 256 || 4 + records * 8 > cm.Length) throw Invalid();
        for (int i = 0; i < records; i++)
        {
            int p = cmap + 4 + i * 8, offset = Raw32(p + 4);
            if (offset > cm.Length - 2) throw Invalid();
            if ((Raw16(p) == 0 || Raw16(p) == 3 && Raw16(p + 2) == 10) && Raw16(cmap + offset) == 12) selected = cmap + offset;
        }
        if (selected == 0 || selected > cmap + cm.Length - 16) throw new NotSupportedException("Equation.Cmap: This MATH profile requires a Unicode format-12 character map.");
        int groups = Raw32(selected + 12), length = Raw32(selected + 4);
        if (groups > 20000 || length < 16 || length > cmap + cm.Length - selected || groups * 12L > length - 16) throw Invalid();
        for (int i = 0; i < groups; i++)
        {
            int p = selected + 16 + i * 12, start = Raw32(p), end = Raw32(p + 4), g = Raw32(p + 8);
            if (end < start || end > 0x10ffff || g + (long)end - start >= glyphCount || i > 0 && start <= mappings[i - 1].End) throw Invalid();
            mappings.Add((start, end, g));
        }
        int constants = Offset(math, math + 4, 214); ScriptPercent = S16(constants); ScriptScriptPercent = S16(constants + 2); DisplayOperatorMinHeight = U16(constants + 6);
        Claim(math, 10); Claim(constants, 214);
        if (ScriptPercent is < 10 or > 100 || ScriptScriptPercent is < 10 or > 100) throw Invalid();
        for (int i = 0; i < Constants.Length; i++) Constants[i] = Value(constants, constants + 8 + i * 4);
        DegreePercent = U16(constants + 212); if (DegreePercent > 100 || Constants[1] <= 0 || Constants[34] <= 0 || Constants[47] <= 0) throw Invalid();
        int info = Offset(math, math + 6, 8);
        Claim(info, 8);
        if (U16(info) != 0)
        {
            int sub = Offset(info, info, 4), count = U16(sub + 2); int[] coverage = Coverage(sub, sub, count);
            Claim(sub, 4 + count * 4);
            Need(sub + 4, count * 4); for (int i = 0; i < count; i++) italics.Add(coverage[i], Value(sub, sub + 4 + i * 4));
        }
        if (U16(info + 2) != 0)
        {
            int sub = Offset(info, info + 2, 4), count = U16(sub + 2); _ = Coverage(sub, sub, count); Need(sub + 4, count * 4);
            Claim(sub, 4 + count * 4);
            for (int i = 0; i < count; i++) _ = Value(sub, sub + 4 + i * 4);
        }
        if (U16(info + 4) != 0) _ = Coverage(info, info + 4, null);
        if (U16(info + 6) != 0)
        {
            int sub = Offset(info, info + 6, 4), count = U16(sub + 2); int[] coverage = Coverage(sub, sub, count); Need(sub + 4, count * 8);
            Claim(sub, 4 + count * 8);
            for (int i = 0; i < count; i++) for (int corner = 0; corner < 4; corner++)
            {
                int at = sub + 4 + i * 8 + corner * 2; if (U16(at) == 0) continue;
                int k = Offset(sub, at, 2), n = U16(k); if (n > 1024) throw Invalid(); Need(k + 2, (2 * n + 1) * 4);
                Claim(k, 2 + (2 * n + 1) * 4);
                var heights = new int[n]; var values = new int[n + 1];
                for (int j = 0; j < n; j++) { heights[j] = Value(k, k + 2 + j * 4); if (j > 0 && heights[j] <= heights[j - 1]) throw Invalid(); }
                for (int j = 0; j <= n; j++) values[j] = Value(k, k + 2 + (n + j) * 4);
                kerns.Add((coverage[i], corner), (heights, values));
            }
        }
        int variants = Offset(math, math + 8, 10); MinOverlap = U16(variants); if (MinOverlap > Units * 4) throw Invalid();
        int verticalCount = U16(variants + 6), horizontalCount = U16(variants + 8); Need(variants + 10, (verticalCount + horizontalCount) * 2);
        Claim(variants, 10 + (verticalCount + horizontalCount) * 2);
        ReadVariants(variants, variants + 2, verticalCount, variants + 10, Vertical);
        ReadVariants(variants, variants + 4, horizontalCount, variants + 10 + verticalCount * 2, Horizontal);
        int previousStart = -1, previousEnd = -1;
        foreach (var claim in claims.OrderBy(c => c.Start).ThenBy(c => c.End))
        {
            if (claim.Start == previousStart && claim.End == previousEnd) continue;
            if (claim.Start < previousEnd) throw Invalid(); previousStart = claim.Start; previousEnd = claim.End;
        }
    }
    internal int Advance(int glyph) { Glyph(glyph); return Raw16(hmtx + Math.Min(glyph, metrics - 1) * 4); }
    internal int Character(int scalar)
    {
        int lo = 0, hi = mappings.Count - 1;
        while (lo <= hi) { int mid = lo + (hi - lo) / 2; var r = mappings[mid]; if (scalar < r.Start) hi = mid - 1; else if (scalar > r.End) lo = mid + 1; else return r.Glyph + scalar - r.Start; }
        throw new NotSupportedException("Equation.Glyph: The exact math font does not cover a required character.");
    }
    internal (int Left, int Bottom, int Right, int Top) Bounds(int glyph)
    {
        Glyph(glyph); int Loc(int i) => locaStep == 2 ? Raw16(loca + i * 2) * 2 : Raw32(loca + i * 4);
        int start = Loc(glyph), end = Loc(glyph + 1); if (start == end) return (0, 0, 0, 0);
        return (RawS16(glyf + start + 2), RawS16(glyf + start + 4), RawS16(glyf + start + 6), RawS16(glyf + start + 8));
    }
    internal int Italic(int glyph) => italics.TryGetValue(glyph, out int n) ? n : 0;
    internal int Kern(int glyph, int corner, double height)
    { if (!kerns.TryGetValue((glyph, corner), out var k)) return 0; int i = 0; while (i < k.Heights.Length && height >= k.Heights[i]) i++; return k.Values[i]; }
    private void ReadVariants(int owner, int coverageAt, int count, int offsets, Dictionary<int, Construction> destination)
    {
        if (count == 0) { if (U16(coverageAt) != 0) throw Invalid(); return; }
        int[] covered = Coverage(owner, coverageAt, count);
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation(); int at = Offset(owner, offsets + i * 2, 4), n = U16(at + 2); if (n > 1024) throw Invalid(); Need(at + 4, n * 4);
            Claim(at, 4 + n * 4);
            var result = new Construction(); int previous = 0;
            for (int j = 0; j < n; j++)
            { int glyph = U16(at + 4 + j * 4), advance = U16(at + 6 + j * 4); Glyph(glyph); if (advance < previous || advance == 0) throw Invalid(); previous = advance; result.Variants.Add((glyph, advance)); }
            if (U16(at) != 0)
            {
                int assembly = Offset(at, at, 6); result.Italic = Value(assembly, assembly); int parts = U16(assembly + 4);
                if (parts is < 1 or > 128) throw Invalid(); Need(assembly + 6, parts * 10);
                Claim(assembly, 6 + parts * 10);
                for (int j = 0; j < parts; j++)
                {
                    int p = assembly + 6 + j * 10, glyph = U16(p), start = U16(p + 2), end = U16(p + 4), advance = U16(p + 6), flags = U16(p + 8); Glyph(glyph);
                    if (flags > 1 || advance == 0 || start > Units * 8 || end > Units * 8) throw Invalid(); result.Parts.Add((glyph, start, end, advance, flags == 1));
                }
            }
            destination.Add(covered[i], result);
        }
    }
    private int[] Coverage(int owner, int at, int? expected)
    {
        int p = Offset(owner, at, 4), format = U16(p), count = U16(p + 2); var result = new List<int>();
        if (format == 1) { Claim(p, 4 + count * 2); for (int i = 0; i < count; i++) result.Add(U16(p + 4 + i * 2)); }
        else if (format == 2)
        {
            Need(p + 4, count * 6);
            Claim(p, 4 + count * 6);
            for (int i = 0; i < count; i++)
            { int start = U16(p + 4 + i * 6), end = U16(p + 6 + i * 6); if (end < start || U16(p + 8 + i * 6) != result.Count || end - start + 1 > glyphCount - result.Count) throw Invalid(); for (int g = start; g <= end; g++) result.Add(g); }
        }
        else throw Invalid();
        if (expected.HasValue && result.Count != expected.Value) throw Invalid();
        for (int i = 0; i < result.Count; i++) { Glyph(result[i]); if (i > 0 && result[i] <= result[i - 1]) throw Invalid(); }
        return result.ToArray();
    }
    private int Value(int owner, int p)
    {
        int value = S16(p); if (Math.Abs(value) > Units * 8) throw Invalid();
        if (U16(p + 2) != 0)
        {
            int device = Offset(owner, p + 2, 6), first = U16(device), last = U16(device + 2), format = U16(device + 4);
            if (format is < 1 or > 3 || first > last) throw new NotSupportedException("Equation.MathDevice: Only static OpenType device tables are admitted.");
            int bits = 1 << format; Need(device + 6, ((last - first + 1) * bits + 15) / 16 * 2);
            Claim(device, 6 + ((last - first + 1) * bits + 15) / 16 * 2);
        }
        return value;
    }
    private void Glyph(int glyph) { if (glyph < 0 || glyph >= glyphCount) throw Invalid(); }
    private void Claim(int p, int length)
    { Need(p, length); if (claims.Count >= 20000) throw Invalid(); claims.Add((p, p + length)); }
    private int Offset(int owner, int at, int length) { int value = U16(at); if (value == 0) throw Invalid(); int p = checked(owner + value); Need(p, length); return p; }
    private int U16(int p) { Need(p, 2); return Raw16(p); }
    private int S16(int p) => unchecked((short)U16(p));
    private int RawS16(int p) => unchecked((short)Raw16(p));
    private int Raw16(int p) { if (p < 0 || p > data.Length - 2) throw Invalid(); return data[p] * 256 + data[p + 1]; }
    private int Raw32(int p) { uint n = ((uint)Raw16(p) << 16) | (uint)Raw16(p + 2); if (n > int.MaxValue) throw Invalid(); return (int)n; }
    private void Need(int p, int count) { if (p < math || count < 0 || p > limit - count) throw Invalid(); }
    private static InvalidDataException Invalid() => new InvalidDataException("Equation.MathFont: Invalid bounded OpenType MATH metrics.");
}
