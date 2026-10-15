using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Internal;

namespace WordNet.Layout;

/// <summary>Converts used static CFF glyphs to bounded, unhinted TrueType outlines.</summary>
internal static class CffFontConverter
{
    // SFNT/glyf/maxp are independently serialized from the published OpenType tables.
    // Glyph IDs, cmap, advances, names and positioning tables retain their original mapping.
    internal static byte[] Prepare(byte[] input, char[] characters, ConversionContext context)
    {
        if (U32(input, 0) != 0x4f54544f) return input;
        var tables = Tables(input);
        if (!tables.ContainsKey(0x43464620)) throw new NotSupportedException("Font.Outline: Expected static CFF outlines; variable CFF2 fonts require a static instance.");
        var parsed = TrueTypeFontParser.Parse(new TrueTypeDataBytes(input));
        var used = new HashSet<int> { 0 };
        foreach (char character in characters)
            if (parsed.TableRegister.CMapTable?.TryGetGlyphIndex(character, out int glyph) == true) used.Add(glyph);
        byte[] originalMaxp = Required(tables, 0x6d617870, 6), head = Required(tables, 0x68656164, 54);
        int glyphCount = U16(originalMaxp, 4), maximumPoints = 0, maximumContours = 0;
        var locations = new byte[checked((glyphCount + 1) * 4)];
        using var outlines = new MemoryStream();
        int xMin = 0, yMin = 0, xMax = 0, yMax = 0;
        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            context.CheckCancellation();
            Put32(locations, glyph * 4, checked((uint)outlines.Length));
            if (!used.Contains(glyph)) continue;
            if (!parsed.TryGetPath(glyph, out IReadOnlyList<PdfSubpath> paths))
                throw new InvalidDataException("Font.Invalid: A selected CFF glyph has no readable outline.");
            var builder = new GlyphBuilder(context);
            foreach (PdfSubpath path in paths) builder.Add(path);
            byte[] data = builder.Build();
            if (outlines.Length + data.Length > 32 * 1024 * 1024) throw new InvalidDataException("Font.Limit: Converted CFF outlines exceed 32 MiB.");
            outlines.Write(data, 0, data.Length);
            while ((outlines.Length & 3) != 0) outlines.WriteByte(0);
            maximumPoints = Math.Max(maximumPoints, builder.PointCount); maximumContours = Math.Max(maximumContours, builder.ContourCount);
            xMin = Math.Min(xMin, builder.XMin); yMin = Math.Min(yMin, builder.YMin);
            xMax = Math.Max(xMax, builder.XMax); yMax = Math.Max(yMax, builder.YMax);
        }
        Put32(locations, glyphCount * 4, checked((uint)outlines.Length));
        tables.Remove(0x43464620); tables.Remove(0x564f5247); // CFF and vertical-origin data.
        tables[0x676c7966] = outlines.ToArray(); tables[0x6c6f6361] = locations;
        var maxp = new byte[32]; Put32(maxp, 0, 0x00010000); Put16(maxp, 4, glyphCount);
        Put16(maxp, 6, maximumPoints); Put16(maxp, 8, maximumContours); Put16(maxp, 14, 1);
        tables[0x6d617870] = maxp;
        Put32(head, 8, 0); Put16(head, 36, xMin); Put16(head, 38, yMin); Put16(head, 40, xMax); Put16(head, 42, yMax); Put16(head, 50, 1);
        var post = new byte[32];
        if (tables.TryGetValue(0x706f7374, out byte[]? originalPost) && originalPost.Length >= 32) Buffer.BlockCopy(originalPost, 0, post, 0, 32);
        Put32(post, 0, 0x00030000); tables[0x706f7374] = post;
        context.Report("Font.CffOutlinesConverted", "Static CFF outlines use embedded TrueType subsets with at most one font unit of curve/coordinate approximation.");
        return Sfnt(tables);
    }

    private static SortedDictionary<uint, byte[]> Tables(byte[] input)
    {
        int count = U16(input, 4); Bounds(input, 12, count * 16);
        var result = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < count; i++)
        {
            int p = 12 + i * 16; uint tag = U32(input, p);
            uint offset = U32(input, p + 8), length = U32(input, p + 12);
            if (offset > int.MaxValue || length > int.MaxValue) throw Invalid();
            Bounds(input, (int)offset, (int)length);
            if (result.ContainsKey(tag)) throw Invalid();
            var bytes = new byte[length]; Buffer.BlockCopy(input, (int)offset, bytes, 0, bytes.Length); result.Add(tag, bytes);
        }
        return result;
    }

    private static byte[] Required(SortedDictionary<uint, byte[]> tables, uint tag, int size)
    {
        if (!tables.TryGetValue(tag, out byte[]? result) || result.Length < size) throw Invalid();
        return result;
    }

    private static byte[] Sfnt(SortedDictionary<uint, byte[]> tables)
    {
        int length = checked(12 + tables.Count * 16);
        foreach (byte[] bytes in tables.Values) length = checked(length + ((bytes.Length + 3) & ~3));
        if (length > 48 * 1024 * 1024) throw new InvalidDataException("Font.Limit: Converted font exceeds 48 MiB.");
        var output = new byte[length]; Put32(output, 0, 0x00010000); Put16(output, 4, tables.Count);
        int power = 1, selector = 0;
        while (power * 2 <= tables.Count) { power *= 2; selector++; }
        Put16(output, 6, power * 16); Put16(output, 8, selector); Put16(output, 10, tables.Count * 16 - power * 16);
        int record = 12, next = 12 + tables.Count * 16, head = 0;
        foreach (var entry in tables)
        {
            byte[] bytes = entry.Value;
            Put32(output, record, entry.Key); Put32(output, record + 4, Checksum(bytes));
            Put32(output, record + 8, (uint)next); Put32(output, record + 12, (uint)bytes.Length);
            Buffer.BlockCopy(bytes, 0, output, next, bytes.Length);
            if (entry.Key == 0x68656164) head = next;
            record += 16; next += (bytes.Length + 3) & ~3;
        }
        Put32(output, head + 8, unchecked(0xb1b0afba - Checksum(output)));
        return output;
    }

    private static uint Checksum(byte[] bytes)
    {
        uint sum = 0;
        for (int p = 0; p < bytes.Length; p += 4)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++) value = (value << 8) | (p + i < bytes.Length ? bytes[p + i] : 0u);
            sum = unchecked(sum + value);
        }
        return sum;
    }

    private sealed class GlyphBuilder
    {
        private readonly ConversionContext context;
        private readonly List<Point> points = new List<Point>();
        private readonly List<int> endpoints = new List<int>();
        private int begin;
        internal GlyphBuilder(ConversionContext context) => this.context = context;
        internal int PointCount => points.Count;
        internal int ContourCount => endpoints.Count;
        internal int XMin { get; private set; }
        internal int YMin { get; private set; }
        internal int XMax { get; private set; }
        internal int YMax { get; private set; }

        internal void Add(PdfSubpath path)
        {
            foreach (var command in path.Commands)
            {
                if (command is PdfSubpath.Move move) { FinishContour(); AddPoint(move.Location, true); }
                else if (command is PdfSubpath.Line line) AddPoint(line.To, true);
                else if (command is PdfSubpath.CubicBezierCurve cubic)
                    Cubic(cubic.StartPoint, cubic.FirstControlPoint, cubic.SecondControlPoint, cubic.EndPoint, 0);
                else if (command is PdfSubpath.QuadraticBezierCurve quadratic)
                { AddPoint(quadratic.ControlPoint, false); AddPoint(quadratic.EndPoint, true); }
                else if (command is PdfSubpath.Close) FinishContour();
                else throw new InvalidDataException("Font.Invalid: Unrecognized CFF path operation.");
            }
            FinishContour();
        }

        private void AddPoint(PdfPoint p, bool onCurve)
        {
            if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y)
                || p.X < short.MinValue || p.X > short.MaxValue || p.Y < short.MinValue || p.Y > short.MaxValue) throw Invalid();
            if (points.Count >= ushort.MaxValue) throw new InvalidDataException("Font.Limit: A converted glyph exceeds the TrueType point limit.");
            if ((points.Count & 255) == 0) context.CheckCancellation();
            points.Add(new Point((int)Math.Round(p.X, MidpointRounding.AwayFromZero), (int)Math.Round(p.Y, MidpointRounding.AwayFromZero), onCurve));
        }

        private void FinishContour()
        {
            if (points.Count == begin) return;
            if (points.Count > begin + 1 && points[begin].SamePosition(points[points.Count - 1])) points.RemoveAt(points.Count - 1);
            endpoints.Add(points.Count - 1); begin = points.Count;
            if (endpoints.Count > short.MaxValue) throw new InvalidDataException("Font.Limit: A converted glyph exceeds the contour limit.");
        }

        private void Cubic(PdfPoint p0, PdfPoint p1, PdfPoint p2, PdfPoint p3, int depth)
        {
            var q = new PdfPoint((3 * (p1.X + p2.X) - p0.X - p3.X) / 4, (3 * (p1.Y + p2.Y) - p0.Y - p3.Y) / 4);
            double e1x = (p0.X + 2 * q.X) / 3 - p1.X, e1y = (p0.Y + 2 * q.Y) / 3 - p1.Y;
            double e2x = (p3.X + 2 * q.X) / 3 - p2.X, e2y = (p3.Y + 2 * q.Y) / 3 - p2.Y;
            // The convex-hull bound on the cubic control differences is <= 0.25
            // font units. Integer rounding adds at most sqrt(0.5), keeping < 1 unit.
            if (Math.Max(e1x * e1x + e1y * e1y, e2x * e2x + e2y * e2y) <= 0.0625)
            { AddPoint(q, false); AddPoint(p3, true); return; }
            if (depth >= 16) throw new InvalidDataException("Font.Limit: CFF curve subdivision exceeds the bound.");
            PdfPoint a = Mid(p0, p1), b = Mid(p1, p2), c = Mid(p2, p3), d = Mid(a, b), e = Mid(b, c), middle = Mid(d, e);
            Cubic(p0, a, d, middle, depth + 1); Cubic(middle, e, c, p3, depth + 1);
        }

        internal byte[] Build()
        {
            if (points.Count == 0) return Array.Empty<byte>();
            XMin = points.Min(p => p.X); YMin = points.Min(p => p.Y); XMax = points.Max(p => p.X); YMax = points.Max(p => p.Y);
            var bytes = new byte[checked(12 + endpoints.Count * 2 + points.Count * 5)];
            Put16(bytes, 0, endpoints.Count); Put16(bytes, 2, XMin); Put16(bytes, 4, YMin); Put16(bytes, 6, XMax); Put16(bytes, 8, YMax);
            int cursor = 10;
            foreach (int end in endpoints) { Put16(bytes, cursor, end); cursor += 2; }
            cursor += 2; // no hinting instructions
            foreach (Point point in points) bytes[cursor++] = point.OnCurve ? (byte)1 : (byte)0;
            int previous = 0;
            foreach (Point point in points) { Delta(point.X - previous); previous = point.X; }
            previous = 0;
            foreach (Point point in points) { Delta(point.Y - previous); previous = point.Y; }
            return bytes;

            void Delta(int value)
            {
                if (value < short.MinValue || value > short.MaxValue) throw new InvalidDataException("Font.Limit: A glyph delta exceeds the TrueType coordinate range.");
                Put16(bytes, cursor, value); cursor += 2;
            }
        }

        private static PdfPoint Mid(PdfPoint a, PdfPoint b) => new PdfPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        private readonly struct Point
        {
            internal Point(int x, int y, bool onCurve) { X = x; Y = y; OnCurve = onCurve; }
            internal int X { get; }
            internal int Y { get; }
            internal bool OnCurve { get; }
            internal bool SamePosition(Point other) => X == other.X && Y == other.Y;
        }
    }

    private static void Bounds(byte[] bytes, int p, int length) { if (p < 0 || length < 0 || p > bytes.Length - length) throw Invalid(); }
    private static int U16(byte[] bytes, int p) { Bounds(bytes, p, 2); return (bytes[p] << 8) | bytes[p + 1]; }
    private static uint U32(byte[] bytes, int p) { Bounds(bytes, p, 4); return ((uint)bytes[p] << 24) | ((uint)bytes[p + 1] << 16) | ((uint)bytes[p + 2] << 8) | bytes[p + 3]; }
    private static void Put16(byte[] bytes, int p, int value) { bytes[p] = (byte)(value >> 8); bytes[p + 1] = (byte)value; }
    private static void Put32(byte[] bytes, int p, uint value) { bytes[p] = (byte)(value >> 24); bytes[p + 1] = (byte)(value >> 16); bytes[p + 2] = (byte)(value >> 8); bytes[p + 3] = (byte)value; }
    private static InvalidDataException Invalid() => new InvalidDataException("Font.Invalid: Invalid static CFF font or outline coordinates.");
}
