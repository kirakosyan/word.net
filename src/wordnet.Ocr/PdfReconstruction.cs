using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Ocr;

internal static class PdfReconstruction
{
    private sealed class Line
    {
        internal List<Letter> Letters = new List<Letter>();
        internal double Baseline, Left, Size;
    }
    private sealed class Grid
    {
        internal double[] X = Array.Empty<double>(), Y = Array.Empty<double>();
    }

    internal sealed class FontMetrics
    {
        private readonly ConversionContext context;
        private FontResolver? resolver;
        private FontKerning[]? metrics;
        internal FontMetrics(ConversionContext context) => this.context = context;
        internal (double Ascent, double Descent) Measure(ParagraphBlock paragraph)
        {
            if (resolver == null)
            {
                var fontModel = new DocumentModel(); var styles = new ParagraphBlock(); fontModel.Blocks.Add(styles);
                for (int style = 0; style < 4; style++) styles.Runs.Add(new TextRun("", bold: (style & 1) != 0, italic: (style & 2) != 0));
                resolver = new FontResolver(fontModel, context);
                metrics = resolver.Data.Select(bytes => new FontKerning(bytes, metricsOnly: true)).ToArray();
            }
            double ascent = 0, descent = 0;
            foreach (TextRun run in paragraph.Runs)
            {
                FontKerning face = metrics![resolver.Resolve(run, false)]; double size = run.FontSize ?? 12;
                ascent = Math.Max(ascent, size * face.Ascent / face.UnitsPerEm); descent = Math.Max(descent, size * face.Descent / face.UnitsPerEm);
            }
            return (ascent, descent);
        }
    }
    internal static IReadOnlyList<DocumentBlock>? Read(Page page, PdfImportOptions options, ConversionContext context, FontMetrics fontMetrics)
    {
        context.CheckCancellation();
        Letter[] letters = page.Letters.ToArray(); var images = page.GetImages().ToArray();
        if (letters.Length + images.Length > options.MaximumElements) throw new InvalidDataException("Limit.PdfReconstruction: Per-page elements exceed the reconstruction bound.");
        if (letters.Any(l => Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > .01 || l.EndBaseLine.X < l.StartBaseLine.X || l.Value.Any(c => c >= '\u0590' && c <= '\u08ff')))
        { Ambiguous(context, "Rotated or bidirectional text uses the existing content-order fallback."); return null; }
        var section = new PageSettings { Width = page.Width, Height = page.Height, Left = 0, Right = 0, Top = 0, Bottom = 0, Columns = options.Columns, ColumnGap = 0 };
        section.Validate();
        var segments = new List<(PdfPoint From, PdfPoint To)>(); int elements = letters.Length + images.Length;
        foreach (var path in page.Paths)
            foreach (PdfSubpath subpath in path)
            {
                PdfPoint? start = null, previous = null;
                foreach (var command in subpath.Commands)
                {
                    context.CheckCancellation();
                    if (++elements > options.MaximumElements) throw new InvalidDataException("Limit.PdfReconstruction: Path commands exceed the reconstruction bound.");
                    if (!path.IsStroked || path.IsClipping) continue;
                    if (command is PdfSubpath.Move move) start = previous = move.Location;
                    else if (command is PdfSubpath.Line line) { segments.Add((line.From, line.To)); previous = line.To; }
                    else if (command is PdfSubpath.Close && start.HasValue && previous.HasValue) segments.Add((previous.Value, start.Value));
                    if (segments.Count + letters.Length + images.Length > options.MaximumElements) throw new InvalidDataException("Limit.PdfReconstruction: Path segments exceed the reconstruction bound.");
                }
            }
        Grid? grid = options.DetectRuledTables && options.Columns == 1 ? FindGrid(segments, context) : null;
        var blocks = new List<(int Column, double Top, double Left, double Bottom, DocumentBlock Block)>(); var tableLetters = new HashSet<Letter>();
        if (grid != null)
        {
            TableBlock? table = Table(grid, letters, tableLetters, context, fontMetrics.Measure);
            if (table != null && !images.Any(i => Overlap(i.BoundingBox, grid.X[0], grid.Y[grid.Y.Length - 1], grid.X[grid.X.Length - 1], grid.Y[0])))
                // The layout engine reserves six points after a table; adjacent rows share an edge.
                blocks.Add((0, page.Height - grid.Y[0], grid.X[0], page.Height - grid.Y[grid.Y.Length - 1] + 6, table));
            else { tableLetters.Clear(); Ambiguous(context, "An incomplete/overlapping grid remains prose without inferred editable table structure."); }
        }
        for (int column = 0; column < options.Columns; column++)
        {
            int current = column; double columnLeft = page.Width * column / options.Columns;
            var columnLetters = letters.Where(l => !tableLetters.Contains(l) && Math.Min(options.Columns - 1, (int)Math.Floor(Math.Max(0, l.StartBaseLine.X) * options.Columns / page.Width)) == current).ToArray();
            var lines = Lines(columnLetters); var groups = new List<List<Line>>();
            foreach (Line line in lines)
            {
                if (line.Left < columnLeft - .5 || line.Letters.Max(l => l.EndBaseLine.X) > page.Width * (column + 1) / options.Columns + .5)
                    Ambiguous(context, "Text crossing a configured column boundary retains approximate paragraph geometry.");
                List<Line>? last = groups.LastOrDefault(); Line? previous = last?.LastOrDefault();
                if (previous == null || previous.Baseline - line.Baseline > Math.Max(previous.Size, line.Size) * 1.8 || last!.Count >= 2 && Math.Abs(last[1].Left - line.Left) > line.Size * .75 || Math.Abs(previous.Size - line.Size) > line.Size * .1)
                { last = new List<Line>(); groups.Add(last); }
                last!.Add(line);
            }
            foreach (var group in groups)
            {
                ParagraphBlock paragraph = Paragraph(group, context); Line first = group[0];
                double bodyLeft = group.Count > 1 ? group[1].Left : first.Left;
                paragraph.Layout.Left = Math.Max(0, bodyLeft - columnLeft);
                paragraph.Layout.FirstLine = first.Left - bodyLeft;
                double top = Math.Max(0, page.Height - first.Baseline - fontMetrics.Measure(Paragraph(new[] { first }, context)).Ascent);
                paragraph.Layout.After = 0;
                if (group.Count > 1) { paragraph.Layout.LineSpacing = group.Zip(group.Skip(1), (a, b) => a.Baseline - b.Baseline).Average(); paragraph.Layout.MultipleLineSpacing = false; }
                double bottom = page.Height - group[group.Count - 1].Baseline + fontMetrics.Measure(Paragraph(new[] { group[group.Count - 1] }, context)).Descent;
                blocks.Add((column, top, first.Left, bottom, paragraph));
            }
        }
        bool clip = page.Paths.Any(p => p.IsClipping);
        foreach (IPdfImage image in images)
        {
            context.CheckCancellation();
            var q = image.BoundingBox; PdfPoint a = q.TopLeft, b = q.TopRight, d = q.BottomLeft;
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = d.X - a.X, vy = d.Y - a.Y;
            double width = Math.Sqrt(ux * ux + uy * uy), height = Math.Sqrt(vx * vx + vy * vy);
            if (!(width > 0 && height > 0)) throw new InvalidDataException("Pdf.ImageGeometry: Image frame must have positive dimensions.");
            double angle = -Math.Atan2(uy, ux) * 180 / Math.PI; int rightAngle = (int)Math.Round(angle / 90) * 90;
            bool supported = !clip && Math.Abs(angle - rightAngle) < .01 && Math.Abs(ux * vx + uy * vy) / (width * height) < .001;
            double left = new[] { q.TopLeft.X, q.TopRight.X, q.BottomLeft.X, q.BottomRight.X }.Min(), top = page.Height - new[] { q.TopLeft.Y, q.TopRight.Y, q.BottomLeft.Y, q.BottomRight.Y }.Max();
            if (left < 0 || top < 0 || left + (rightAngle % 180 == 0 ? width : height) > page.Width + .01 || top + (rightAngle % 180 == 0 ? height : width) > page.Height + .01) supported = false;
            context.ReserveImagePixels(image.WidthInSamples, image.HeightInSamples);
            if (image.IsImageMask || image.MaskImage != null) context.Loss("Pdf.ImageMask", "Image stencil/soft masks are not retained by the extraction profile.");
            byte[] bytes = image.RawMemory.Length >= 3 && image.RawMemory.Span[0] == 255 && image.RawMemory.Span[1] == 216 && image.RawMemory.Span[2] == 255 ? image.RawMemory.ToArray()
                : image.TryGetPng(out byte[]? png) ? png : throw new NotSupportedException("Pdf.ImageEncoding: Embedded image cannot be decoded.");
            DocumentImage mapped = DocumentImage.Read(bytes, width, height, "", context, image.WidthInSamples, image.HeightInSamples);
            if (supported)
            {
                mapped.Rotation = (rightAngle % 360 + 360) % 360; mapped.FlipVertical = ux * vy - uy * vx > 0;
                mapped.Anchor = new DrawingAnchor { HorizontalOrigin = "page", VerticalOrigin = "page", X = left, Y = top, Wrap = "none" };
            }
            else context.Loss("Pdf.ReconstructionImageTransform", "Unsupported clipping, shear, rotation or out-of-page image geometry uses an inline image fallback.");
            var paragraph = new ParagraphBlock { Layout = new ParagraphLayout { After = 0 } }; paragraph.Runs.Add(new TextRun("", image: mapped));
            int column = Math.Min(options.Columns - 1, (int)Math.Floor(Math.Max(0, left) * options.Columns / page.Width));
            paragraph.Layout.Left = Math.Max(0, left - page.Width * column / options.Columns);
            blocks.Add((column, Math.Max(0, top), left, Math.Max(0, top) + height, paragraph));
        }
        // Page-relative anchors belong to a flow paragraph without adding another line.
        TextRun[] anchors = blocks.Where(b => b.Block is ParagraphBlock p && p.Runs.Count == 1 && p.Runs[0].Image?.Anchor != null)
            .Select(b => ((ParagraphBlock)b.Block).Runs[0]).ToArray();
        blocks.RemoveAll(b => b.Block is ParagraphBlock p && p.Runs.Count == 1 && p.Runs[0].Image?.Anchor != null);
        var result = new List<DocumentBlock>(); int previousColumn = 0; double previousBottom = 0;
        ParagraphBlock? previousParagraph = null;
        foreach (var block in blocks.OrderBy(b => b.Column).ThenBy(b => b.Top).ThenBy(b => b.Left))
        {
            if (block.Column > previousColumn)
            { var separator = new ParagraphBlock(); separator.Runs.Add(new TextRun(new string('\v', block.Column - previousColumn))); result.Add(separator); previousBottom = 0; previousParagraph = null; }
            double gap = Math.Max(0, block.Top - previousBottom);
            if (block.Block is ParagraphBlock paragraph) paragraph.Layout.Before = gap;
            else if (gap > 0)
            {
                if (previousParagraph != null) previousParagraph.Layout.After = (previousParagraph.Layout.After ?? 0) + gap;
                else result.Add(new ParagraphBlock { Layout = new ParagraphLayout { Before = 0, After = 0, LineSpacing = gap, MultipleLineSpacing = false, ExactLineSpacing = true } });
            }
            result.Add(block.Block); previousColumn = block.Column; previousBottom = Math.Max(previousBottom, block.Bottom);
            previousParagraph = block.Block as ParagraphBlock;
        }
        if (anchors.Length > 0)
        {
            ParagraphBlock? host = result.OfType<ParagraphBlock>().FirstOrDefault(p => p.Runs.Count > 0);
            if (host == null) { host = new ParagraphBlock { Layout = new ParagraphLayout { Before = 0, After = 0 } }; result.Add(host); }
            host.Runs.AddRange(anchors);
        }
        if (result.Count == 0) result.Add(new ParagraphBlock());
        foreach (DocumentBlock block in result) block.Section = section;
        if (grid != null || options.Columns > 1) context.Report("Pdf.ReconstructionFlow", "Editable table/column geometry and reading order are inferred; vertical flow and pagination remain approximate.", DiagnosticSeverity.Warning);
        context.Report("Pdf.ReconstructionProfile", "Digital PDF uses bounded geometric prose grouping, caller-declared columns and complete populated ruled grids; confidence is heuristic, not recovered original structure.");
        return result;
    }

    private static List<Line> Lines(IEnumerable<Letter> letters)
    {
        var result = new List<Line>();
        foreach (Letter letter in letters.OrderByDescending(l => l.StartBaseLine.Y).ThenBy(l => l.StartBaseLine.X))
        {
            Line? line = result.LastOrDefault();
            if (line == null || Math.Abs(line.Baseline - letter.StartBaseLine.Y) > Math.Max(1, letter.PointSize * .15))
            { line = new Line { Baseline = letter.StartBaseLine.Y, Left = letter.StartBaseLine.X, Size = letter.PointSize }; result.Add(line); }
            line.Letters.Add(letter); line.Left = Math.Min(line.Left, letter.StartBaseLine.X); line.Size = Math.Max(line.Size, letter.PointSize);
        }
        return result;
    }
    private static ParagraphBlock Paragraph(IEnumerable<Line> lines, ConversionContext context)
    {
        var paragraph = new ParagraphBlock(); bool first = true, bold = false, italic = false; double? size = null;
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length == 0) return;
            paragraph.Runs.Add(new TextRun(text.ToString(), bold: bold, italic: italic, fontSize: size)); text.Clear();
        }
        foreach (Line line in lines)
        {
            if (!first) text.Append('\n'); first = false; double? previous = null;
            foreach (Letter letter in line.Letters.OrderBy(l => l.StartBaseLine.X))
            {
                context.CheckCancellation();
                if (previous.HasValue && letter.StartBaseLine.X - previous.Value > letter.PointSize * .25)
                {
                    if (letter.StartBaseLine.X - previous.Value > letter.PointSize * 3) Ambiguous(context, "Wide within-line gaps may represent a form/borderless table; no cell structure is inferred.");
                    text.Append(' ');
                }
                bool nextBold = NameContains(letter.FontName, "Bold"), nextItalic = NameContains(letter.FontName, "Italic") || NameContains(letter.FontName, "Oblique");
                if (bold != nextBold || italic != nextItalic || size != letter.PointSize) { Flush(); bold = nextBold; italic = nextItalic; size = letter.PointSize; }
                text.Append(letter.Value); previous = letter.EndBaseLine.X;
            }
        }
        Flush();
        return paragraph;
    }
    private static Grid? FindGrid(List<(PdfPoint From, PdfPoint To)> segments, ConversionContext context)
    {
        var candidates = new List<Grid>(); int work = 0;
        foreach (var group in segments.Where(s => Math.Abs(s.From.X - s.To.X) < .1 && Math.Abs(s.From.Y - s.To.Y) >= 10)
            .GroupBy(s => (Math.Round(Math.Min(s.From.Y, s.To.Y), 1), Math.Round(Math.Max(s.From.Y, s.To.Y), 1))))
        {
            double[] xs = group.Select(s => Math.Round(s.From.X, 1)).Distinct().OrderBy(x => x).ToArray(); if (xs.Length < 3) continue;
            work = checked(work + segments.Count); if (work > 2000000) throw new InvalidDataException("Limit.PdfReconstructionWork: Grid search exceeds its bounded work budget.");
            double[] ys = segments.Where(s => Math.Abs(s.From.Y - s.To.Y) < .1 && Math.Abs(Math.Min(s.From.X, s.To.X) - xs[0]) < .2 && Math.Abs(Math.Max(s.From.X, s.To.X) - xs[xs.Length - 1]) < .2)
                .Select(s => Math.Round(s.From.Y, 1)).Where(y => y >= group.Key.Item1 && y <= group.Key.Item2).Distinct().OrderByDescending(y => y).ToArray();
            if (ys.Length >= 3 && Math.Abs(ys[0] - group.Key.Item2) < .2 && Math.Abs(ys[ys.Length - 1] - group.Key.Item1) < .2) candidates.Add(new Grid { X = xs, Y = ys });
            if (candidates.Count > 1) { Ambiguous(context, "Multiple possible grids use prose fallback rather than guessing table boundaries."); return null; }
        }
        return candidates.SingleOrDefault();
    }
    private static TableBlock? Table(Grid grid, Letter[] letters, HashSet<Letter> used, ConversionContext context, Func<ParagraphBlock, (double Ascent, double Descent)> metrics)
    {
        int columns = grid.X.Length - 1, rows = grid.Y.Length - 1; long count = (long)columns * rows;
        if (count > 100000 || count > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.PdfReconstruction: Grid cell count exceeds structural admission.");
        if (count > letters.Length) return null;
        var buckets = new List<Letter>?[(int)count];
        foreach (Letter letter in letters)
        {
            int column = Band(grid.X, letter.StartBaseLine.X, false), row = Band(grid.Y, letter.StartBaseLine.Y, true);
            if (column < 0 || row < 0) continue;
            if (letter.EndBaseLine.X > grid.X[column + 1] + .5) { used.Clear(); return null; }
            (buckets[row * columns + column] ??= new List<Letter>()).Add(letter);
        }
        var table = new TableBlock { LeftIndent = grid.X[0], Width = grid.X[grid.X.Length - 1] - grid.X[0], LayoutExplicit = true };
        table.ColumnWidths.AddRange(grid.X.Zip(grid.X.Skip(1), (a, b) => b - a));
        for (int row = 0; row < grid.Y.Length - 1; row++)
        {
            var cells = new List<TableCell>();
            for (int column = 0; column < grid.X.Length - 1; column++)
            {
                List<Letter>? content = buckets[row * columns + column];
                if (content == null || content.Count == 0) { used.Clear(); return null; }
                used.UnionWith(content); var cell = new TableCell(Paragraph(Lines(content), context)) { CellWidth = grid.X[column + 1] - grid.X[column], Padding = new CellPadding() };
                cell.Paragraphs[0].Layout.Before = Math.Max(0, grid.Y[row] - content.Max(l => l.StartBaseLine.Y) - metrics(cell.Paragraphs[0]).Ascent); cell.Paragraphs[0].Layout.After = 0;
                cells.Add(cell);
            }
            table.Rows.Add(cells); table.RowHeights[row] = (grid.Y[row] - grid.Y[row + 1], false);
        }
        return table;
    }
    private static int Band(double[] edges, double value, bool descending)
    {
        int low = 0, high = edges.Length - 1;
        if (descending ? value > edges[0] || value <= edges[high] : value < edges[0] || value >= edges[high]) return -1;
        while (high - low > 1) { int middle = (low + high) / 2; if (descending ? value <= edges[middle] : value >= edges[middle]) low = middle; else high = middle; }
        return low;
    }
    private static bool Overlap(PdfRectangle box, double left, double bottom, double right, double top) => box.Right > left && box.Left < right && box.Top > bottom && box.Bottom < top;
    private static void Ambiguous(ConversionContext context, string message) => context.Report("Pdf.ReconstructionAmbiguity", message, DiagnosticSeverity.Warning);
    private static bool NameContains(string? value, string fragment)
    {
        if (value == null) return false;
#if NET8_0_OR_GREATER
        return value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
#else
        return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
}
