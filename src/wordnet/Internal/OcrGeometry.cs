using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WordNet.Internal;

// Maps original raster edge coordinates to the selected recognition raster.
internal readonly struct OcrTransform
{
    internal OcrTransform(double a, double b, double c, double d, double x, double y) { A = a; B = b; C = c; D = d; X = x; Y = y; }
    private double A { get; }
    private double B { get; }
    private double C { get; }
    private double D { get; }
    private double X { get; }
    private double Y { get; }
    internal static OcrTransform Identity => new OcrTransform(1, 0, 0, 1, 0, 0);
    internal (double X, double Y) Map(double x, double y) => (A * x + C * y + X, B * x + D * y + Y);
    internal OcrTransform Then(OcrTransform next) => new OcrTransform(next.A * A + next.C * B, next.B * A + next.D * B,
        next.A * C + next.C * D, next.B * C + next.D * D, next.A * X + next.C * Y + next.X, next.B * X + next.D * Y + next.Y);
    internal OcrTransform Inverse()
    {
        double determinant = A * D - B * C;
        if (Math.Abs(determinant) < 1e-12) throw new InvalidDataException("Ocr.Geometry: Recognition transform is singular.");
        return new OcrTransform(D / determinant, -B / determinant, -C / determinant, A / determinant, (C * Y - D * X) / determinant, (B * X - A * Y) / determinant);
    }
    internal static OcrTransform Rotation(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, double degrees)
    {
        double angle = (float)degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        if (Math.Abs(c) < 1e-12) c = 0; if (Math.Abs(s) < 1e-12) s = 0;
        return new OcrTransform(c, s, -s, c, targetWidth / 2.0 - c * sourceWidth / 2.0 + s * sourceHeight / 2.0, targetHeight / 2.0 - s * sourceWidth / 2.0 - c * sourceHeight / 2.0);
    }
}

internal sealed class OcrWordSequence
{
    internal OcrWordSequence(int[] indices, int leftCount) { Indices = indices; LeftCount = leftCount; }
    internal int[] Indices { get; }
    internal int LeftCount { get; }
}

internal static class OcrGeometry
{
    private static readonly char[] Tab = { '\t' };
    private sealed class Word
    {
        internal string Text = "", Line = "", Paragraph = "";
        internal int X, Y, Width, Height;
        internal double? Confidence;
        internal int? Start;
        internal OcrRegion? Region;
    }

    internal static OcrPage Build(string tsv, string text, int sourcePage, int documentStart, double pageWidth, double pageHeight,
        int originalWidth, int originalHeight, int recognizedWidth, int recognizedHeight, OcrTransform transform,
        int meanConfidence, OcrWordSequence? sequence, ConversionContext context)
    {
        context.CheckCancellation(); context.ReserveOcrMetadata(0, checked((long)text.Length * 2 + 128));
        var words = new List<Word>();
        using (var reader = new StringReader(tsv))
        {
            string? row;
            while ((row = reader.ReadLine()) != null)
            {
                context.CheckCancellation(); string[] f = row.Split(Tab, 12);
                if (f[0] != "5") continue;
                if (f.Length != 12) throw new InvalidDataException("Ocr.Geometry: Incomplete native TSV word record.");
                if (string.IsNullOrWhiteSpace(f[11])) continue;
                if (!Int(f[6], out int x) || !Int(f[7], out int y) || !Int(f[8], out int width) || !Int(f[9], out int height)
                    || width <= 0 || height <= 0 || (long)x + width > recognizedWidth || (long)y + height > recognizedHeight
                    || !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence) || double.IsNaN(confidence) || confidence < -1 || confidence > 100)
                    throw new InvalidDataException("Ocr.Geometry: Invalid native TSV word geometry or confidence.");
                if (words.Count >= context.OcrMetadata.MaximumRegions) throw new InvalidDataException("Limit.OcrMetadata: Native word count exceeds the region bound.");
                words.Add(new Word { Text = f[11], Paragraph = f[1] + ":" + f[2] + ":" + f[3], Line = f[1] + ":" + f[2] + ":" + f[3] + ":" + f[4], X = x, Y = y, Width = width, Height = height, Confidence = confidence < 0 ? null : confidence });
            }
        }
        if (sequence != null)
        {
            if (sequence.Indices.Length != words.Count || sequence.LeftCount < 0 || sequence.LeftCount > words.Count) throw new InvalidDataException("Ocr.Geometry: Invalid column word mapping.");
            var used = new bool[words.Count]; var ordered = new List<Word>(words.Count);
            foreach (int index in sequence.Indices)
            {
                if (index < 0 || index >= words.Count || used[index]) throw new InvalidDataException("Ocr.Geometry: Duplicate or invalid column word mapping.");
                used[index] = true; ordered.Add(words[index]);
            }
            words = ordered;
            for (int i = 0; i < words.Count; i++) { string prefix = i < sequence.LeftCount ? "left:" : "right:"; words[i].Line = prefix + words[i].Line; words[i].Paragraph = prefix + words[i].Paragraph; }
        }
        // Index canonical tokens once. Every queue entry is consumed at most once,
        // including mismatches, avoiding repeated full-text scans on noisy input.
        var tokens = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (int offset = 0; offset < text.Length;)
        {
            context.CheckCancellation(); if (char.IsWhiteSpace(text[offset])) { offset++; continue; }
            int start = offset; while (offset < text.Length && !char.IsWhiteSpace(text[offset])) offset++;
            string token = text.Substring(start, offset - start);
            if (!tokens.TryGetValue(token, out Queue<int>? positions)) tokens.Add(token, positions = new Queue<int>());
            positions.Enqueue(start);
        }
        int cursor = 0; var regions = new List<OcrRegion>(); OcrTransform inverse = transform.Inverse();
        foreach (Word word in words)
        {
            context.CheckCancellation();
            if (tokens.TryGetValue(word.Text, out Queue<int>? positions))
            {
                while (positions.Count > 0 && positions.Peek() < cursor) positions.Dequeue();
                if (positions.Count > 0) { word.Start = positions.Dequeue(); cursor = word.Start.Value + word.Text.Length; }
            }
            if (!word.Start.HasValue) context.Report("Ocr.UnmappedRegion", "A native OCR word has no exact ordered span in canonical editable text; its text mapping is unavailable.", DiagnosticSeverity.Warning);
            word.Region = Region(OcrRegionKind.Word, word.X, word.Y, word.X + word.Width, word.Y + word.Height, word.Start, word.Start.HasValue ? word.Text.Length : 0, word.Confidence);
        }
        foreach (var group in words.GroupBy(w => w.Line)) Group(group, OcrRegionKind.Line);
        foreach (var group in words.GroupBy(w => w.Paragraph)) Group(group, OcrRegionKind.Paragraph);
        return new OcrPage(sourcePage, pageWidth, pageHeight, text, documentStart, meanConfidence, regions);

        void Group(IEnumerable<Word> source, OcrRegionKind kind)
        {
            Word[] values = source.ToArray(); bool mapped = values.All(w => w.Start.HasValue);
            int start = mapped ? values.Min(w => w.Start!.Value) : 0, end = mapped ? values.Max(w => w.Start!.Value + w.Text.Length) : 0;
            double? confidence = values.Where(w => w.Confidence.HasValue).Select(w => w.Confidence).Average();
            _ = Region(kind, values.Min(w => w.X), values.Min(w => w.Y), values.Max(w => w.X + w.Width), values.Max(w => w.Y + w.Height), mapped ? start : null, end - start, confidence);
        }
        OcrRegion? Region(OcrRegionKind kind, double left, double top, double right, double bottom, int? start, int length, double? confidence)
        {
            var polygon = new List<(double X, double Y)> { inverse.Map(left, top), inverse.Map(right, top), inverse.Map(right, bottom), inverse.Map(left, bottom) };
            bool clipped = polygon.Any(p => p.X < 0 || p.Y < 0 || p.X > originalWidth || p.Y > originalHeight);
            polygon = Clip(polygon, 0, 0, true); polygon = Clip(polygon, 0, originalWidth, false);
            polygon = Clip(polygon, 1, 0, true); polygon = Clip(polygon, 1, originalHeight, false);
            if (polygon.Count < 3)
            { context.Report("Ocr.RegionOutsidePage", "A recognized region outside the original raster is omitted from source geometry.", DiagnosticSeverity.Warning); return null; }
            context.ReserveOcrMetadata(1, 128L + polygon.Count * 32L);
            var region = new OcrRegion(kind, polygon.Select(p => new OcrPoint(Math.Max(0, Math.Min(pageWidth, p.X * pageWidth / originalWidth)), Math.Max(0, Math.Min(pageHeight, p.Y * pageHeight / originalHeight)))), start, length, confidence, clipped);
            regions.Add(region); return region;
        }
    }
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> points, int axis, double boundary, bool minimum)
    {
        var result = new List<(double, double)>(); if (points.Count == 0) return result;
        var previous = points[points.Count - 1]; bool previousInside = Inside(previous);
        foreach (var point in points)
        {
            bool inside = Inside(point);
            if (inside != previousInside)
            {
                double t = (boundary - (axis == 0 ? previous.X : previous.Y)) / ((axis == 0 ? point.X : point.Y) - (axis == 0 ? previous.X : previous.Y));
                result.Add((previous.X + t * (point.X - previous.X), previous.Y + t * (point.Y - previous.Y)));
            }
            if (inside) result.Add(point); previous = point; previousInside = inside;
        }
        return result;
        bool Inside((double X, double Y) p) => minimum ? (axis == 0 ? p.X : p.Y) >= boundary : (axis == 0 ? p.X : p.Y) <= boundary;
    }
    private static bool Int(string value, out int result) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
