using System;
using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig.Core;
using WordNet.Internal;

namespace WordNet.Layout;

// OpenType glyf/loca outline decoder. Hinting is deliberately not executed.
// Keep signed deltas and half-unit implied points: Arabic dots exercise both.
internal sealed class TrueTypeOutlines
{
    private readonly byte[] data;
    private readonly int glyf, loca, count, step;
    private readonly ConversionContext context;
    private readonly Dictionary<int, Outline> cache = new Dictionary<int, Outline>();
    private sealed class Outline
    {
        internal List<(double X, double Y, bool On)> Points { get; } = new List<(double, double, bool)>();
        internal List<int> Ends { get; } = new List<int>();
    }
    internal TrueTypeOutlines(byte[] data, ConversionContext context)
    {
        TrueTypeOutlineGuard.Validate(data); this.data = data; this.context = context;
        int Table(string name) { for (int i = 0; i < U16(4); i++) { int p = 12 + i * 16; if (System.Text.Encoding.ASCII.GetString(data, p, 4) == name) return U32(p + 8); } throw new InvalidDataException("Font.Outline: Missing table."); }
        glyf = Table("glyf"); loca = Table("loca"); count = U16(Table("maxp") + 4); step = U16(Table("head") + 50) == 0 ? 2 : 4;
    }
    private int U16(int p) { if (p < 0 || p > data.Length - 2) throw new InvalidDataException("Font.Outline: Truncated glyph data."); return data[p] * 256 + data[p + 1]; }
    private int S16(int p) => unchecked((short)U16(p));
    private int U32(int p) => checked((U16(p) << 16) + U16(p + 2));
    private int Offset(int i) => step == 2 ? U16(loca + i * 2) * 2 : U32(loca + i * 4);
    private Outline Read(int glyph, int depth)
    {
        context.CheckCancellation(); if (glyph < 0 || glyph >= count || depth > 24) throw new InvalidDataException("Font.Outline: Invalid component reference or depth."); if (cache.TryGetValue(glyph, out Outline? cached)) return cached;
        var result = new Outline(); int p = glyf + Offset(glyph), end = glyf + Offset(glyph + 1);
        void Need(int length) { if (length < 0 || p > end - length) throw new InvalidDataException("Font.Outline: Glyph data exceeds its loca extent."); }
        byte Byte() { Need(1); return data[p++]; }
        int Word() { Need(2); int n = U16(p); p += 2; return n; }
        int Signed() => unchecked((short)Word());
        if (p != end)
        {
            Need(10); int contours = Signed(); p += 8;
            if (contours >= 0)
            {
                for (int i = 0; i < contours; i++) { int last = Word(); if (i > 0 && last <= result.Ends[i - 1]) throw new InvalidDataException("Font.Outline: Nonascending contour endpoints."); result.Ends.Add(last); }
                int points = contours == 0 ? 0 : result.Ends[contours - 1] + 1; if (points > 100000) throw new InvalidDataException("Font.Outline: Too many points.");
                int instructions = Word(); Need(instructions); p += instructions;
                var flags = new byte[points];
                for (int i = 0; i < points; i++) { byte flag = Byte(); flags[i] = flag; if ((flag & 8) != 0) { int repeat = Byte(); if (repeat > points - i - 1) throw new InvalidDataException("Font.Outline: Flag repeat exceeds point count."); while (repeat-- > 0) flags[++i] = flag; } }
                var xs = new int[points]; int x = 0, y = 0;
                for (int i = 0; i < points; i++) { int flag = flags[i]; x = checked(x + ((flag & 2) != 0 ? Byte() * ((flag & 16) != 0 ? 1 : -1) : (flag & 16) != 0 ? 0 : Signed())); xs[i] = x; }
                for (int i = 0; i < points; i++) { int flag = flags[i]; y = checked(y + ((flag & 4) != 0 ? Byte() * ((flag & 32) != 0 ? 1 : -1) : (flag & 32) != 0 ? 0 : Signed())); result.Points.Add((xs[i], y, (flag & 1) != 0)); }
            }
            else
            {
                int flags;
                do
                {
                    flags = Word(); int child = Word(); bool xy = (flags & 2) != 0; int first, second;
                    if ((flags & 1) != 0) { first = xy ? Signed() : Word(); second = xy ? Signed() : Word(); }
                    else { first = xy ? unchecked((sbyte)Byte()) : Byte(); second = xy ? unchecked((sbyte)Byte()) : Byte(); }
                    double a = 1, b = 0, c = 0, d = 1;
                    if ((flags & 8) != 0) a = d = Signed() / 16384.0;
                    else if ((flags & 64) != 0) { a = Signed() / 16384.0; d = Signed() / 16384.0; }
                    else if ((flags & 128) != 0) { a = Signed() / 16384.0; b = Signed() / 16384.0; c = Signed() / 16384.0; d = Signed() / 16384.0; }
                    Outline component = Read(child, depth + 1); double dx = first, dy = second;
                    if (xy) { if ((flags & 2048) != 0) { dx = a * first + c * second; dy = b * first + d * second; } if ((flags & 4) != 0) { dx = Math.Round(dx); dy = Math.Round(dy); } }
                    else { if (first >= result.Points.Count || second >= component.Points.Count) throw new InvalidDataException("Font.Outline: Invalid point attachment."); var target = result.Points[first]; var origin = component.Points[second]; dx = target.X - (a * origin.X + c * origin.Y); dy = target.Y - (b * origin.X + d * origin.Y); }
                    int start = result.Points.Count; foreach (var point in component.Points) result.Points.Add((a * point.X + c * point.Y + dx, b * point.X + d * point.Y + dy, point.On));
                    foreach (int last in component.Ends) result.Ends.Add(start + last);
                } while ((flags & 32) != 0);
                if ((flags & 256) != 0) { int instructions = Word(); Need(instructions); p += instructions; }
            }
        }
        context.ReserveOutlinePoints(result.Points.Count); cache.Add(glyph, result); return result;
    }
    internal IReadOnlyList<PdfSubpath> Paths(int glyph)
    {
        Outline outline = Read(glyph, 0); var paths = new List<PdfSubpath>(); int begin = 0;
        foreach (int end in outline.Ends)
        {
            var path = new PdfSubpath(); var first = outline.Points[begin]; var last = outline.Points[end];
            var start = first.On ? first : last.On ? last : ((first.X + last.X) / 2, (first.Y + last.Y) / 2, true);
            path.MoveTo(start.Item1, start.Item2); int index = first.On ? begin + 1 : begin; int limit = first.On || !last.On ? end : end - 1;
            while (index <= limit)
            {
                var point = outline.Points[index++];
                if (point.On) path.LineTo(point.X, point.Y);
                else
                {
                    var next = index <= limit ? outline.Points[index] : (start.Item1, start.Item2, true);
                    if (next.Item3) { path.BezierCurveTo(point.X, point.Y, next.Item1, next.Item2); index++; }
                    else path.BezierCurveTo(point.X, point.Y, (point.X + next.Item1) / 2, (point.Y + next.Item2) / 2);
                }
            }
            path.CloseSubpath(); paths.Add(path); begin = end + 1;
        }
        return paths;
    }
}
