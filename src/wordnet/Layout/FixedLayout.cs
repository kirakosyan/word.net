using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class FixedPage
{
    internal PageSettings Settings { get; set; } = new PageSettings();
    internal PageSettings SourceSettings { get; set; } = new PageSettings();
    internal List<PositionedText> Text { get; } = new List<PositionedText>();
    internal List<FixedHeading> Headings { get; } = new List<FixedHeading>();
}

internal sealed class FixedHeading
{
    internal FixedHeading(string title, int level, double x, double y) { Title = title; Level = level; X = x; Y = y; }
    internal string Title { get; }
    internal int Level { get; }
    internal double X { get; }
    internal double Y { get; }
}

internal sealed class PositionedText
{
    internal PositionedText(string text, double x, double y, double size, int font, string? color = null, DocumentImage? image = null, string? link = null, TextRun? field = null)
    { Text = text; X = x; Y = y; Size = size; Font = font; Color = color; Image = image; Link = link; Field = field; }
    internal string Text { get; }
    internal double X { get; }
    internal double Y { get; }
    internal double Size { get; }
    internal int Font { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal string? Link { get; }
    internal TextRun? Field { get; }
    internal PositionedText At(double x, double y) => new PositionedText(Text, x, y, Size, Font, Color, Image, Link, Field);
    internal PositionedText WithText(string text) => new PositionedText(text, X, Y, Size, Font, Color, Image, Link, Field);
}

internal static partial class FixedLayout
{
    internal const double Width = 595.276;
    internal const double Height = 841.89;
    internal static readonly string[] FontNames = { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf", "NotoSans-Italic.ttf", "NotoSans-BoldItalic.ttf" };
    private sealed class Line
    {
        internal List<PositionedText> Items { get; } = new List<PositionedText>();
        internal double Origin { get; set; }
        internal double Ascent { get; set; }
        internal double Descent { get; set; }
        internal double Height => Ascent + Descent;
    }

    private sealed class CellLayout
    {
        internal List<Line> Lines { get; } = new List<Line>();
        internal CellPadding Padding { get; set; } = new CellPadding();
        internal int Column { get; set; }
        internal int RowSpan { get; set; } = 1;
        internal VerticalMerge Merge { get; set; }
        internal double Height => Padding.Top + Lines.Sum(l => l.Height) + Padding.Bottom;
    }

    private sealed class RowLayout
    {
        internal List<CellLayout> Cells { get; } = new List<CellLayout>();
        internal double Height { get; set; }
    }

    private sealed class MeasuredText
    {
        internal double Width { get; set; }
        internal double[] X { get; set; } = Array.Empty<double>();
        internal double[] Y { get; set; } = Array.Empty<double>();
        internal double[] OriginalX { get; set; } = Array.Empty<double>();
    }

    internal static byte[] FontBytes(int index)
    {
        using Stream stream = typeof(FixedLayout).GetTypeInfo().Assembly.GetManifestResourceStream("WordNet.Resources.Fonts." + FontNames[index])
            ?? throw new InvalidOperationException("Embedded font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }

    internal static List<FixedPage> Create(DocumentModel model, ConversionContext context)
    {
        List<FixedPage> pages = CreateCore(model, context, 1, null);
        if (DocumentFields.NeedsPages(model))
        {
            int expected = pages.Count;
            var sections = Counts(pages);
            pages = CreateCore(model, context, expected, sections);
            var current = Counts(pages);
            if (pages.Count != expected || current.Count != sections.Count || current.Any(p => !sections.TryGetValue(p.Key, out int count) || count != p.Value))
                pages = CreateCore(model, context, pages.Count, current);
        }
        return pages;

        static Dictionary<PageSettings, int> Counts(List<FixedPage> result) => result.GroupBy(p => p.SourceSettings).ToDictionary(g => g.Key, g => g.Count());
    }

    private static List<FixedPage> CreateCore(DocumentModel model, ConversionContext context, int totalPages, Dictionary<PageSettings, int>? sectionTotals)
    {
        using var measure = new PdfDocumentBuilder();
        var resolver = new FontResolver(model, context);
        var data = resolver.Data.ToArray();
        context.FontData = data;
        var fonts = data.Select(bytes => measure.AddTrueTypeFont(bytes)).ToArray();
        var maps = data.Select(bytes => TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable).ToArray();
        var kerning = data.Select(bytes => new FontKerning(bytes)).ToArray();
        // Per-call, bounded token measurements; repeated template runs avoid repeated parsing.
        var measurements = new Dictionary<(string Text, int Font, double Size), MeasuredText>();
        int cachedCharacters = 0;
        PdfPageBuilder metrics = measure.AddPage(Width, Height);
        PageSettings sourceSettings = model.Blocks.FirstOrDefault()?.Section ?? model.FinalSection ?? new PageSettings();
        PageSettings settings = sourceSettings;
        settings.Validate();
        var pages = new List<FixedPage> { new FixedPage { Settings = settings, SourceSettings = sourceSettings } };
        var counter = new ListCounter();
        int renderPage = 0, renderSectionPages = totalPages;
        settings = ReserveStories(sourceSettings); pages[0].Settings = settings;
        double y = settings.Top;
        PageSettings? previousSection = model.Blocks.FirstOrDefault()?.Section;
        var prepared = new Dictionary<ParagraphBlock, List<Line>>();
        for (int blockIndex = 0; blockIndex < model.Blocks.Count; blockIndex++)
        {
            DocumentBlock block = model.Blocks[blockIndex];
            context.CheckCancellation();
            if (!ReferenceEquals(previousSection, block.Section))
            {
                previousSection = block.Section;
                sourceSettings = block.Section ?? new PageSettings(); sourceSettings.Validate();
                settings = ReserveStories(sourceSettings);
                AddPage();
            }
            if (block is ParagraphBlock paragraph)
            {
                List<Line> lines = Prepare(paragraph);
                if (paragraph.Layout.PageBreakBefore && y > settings.Top) AddPage();
                double before = paragraph.Layout.Before ?? 0;
                double totalHeight = before + lines.Sum(l => l.Height);
                if (paragraph.Layout.KeepWithNext)
                {
                    int index = blockIndex + 1;
                    double after = paragraph.Layout.After ?? 6;
                    if (index < model.Blocks.Count && model.Blocks[index] is TableBlock)
                        context.Loss("Layout.KeepWithTable", "Keeping a paragraph with the following table is not supported by this layout profile.");
                    while (index < model.Blocks.Count && model.Blocks[index] is ParagraphBlock following
                        && ReferenceEquals(block.Section, following.Section) && !following.Layout.PageBreakBefore)
                    {
                        List<Line> nextLines = Prepare(following);
                        totalHeight += (following.Layout.Before ?? 0) + after
                            + (following.Layout.KeepTogether || following.Layout.KeepWithNext ? nextLines.Sum(l => l.Height) : nextLines[0].Height);
                        // Once the group exceeds a page, no later member can make it fit.
                        // Bound the lookahead instead of rescanning arbitrarily long keep chains.
                        if (!following.Layout.KeepWithNext || totalHeight > settings.Height - settings.Top - settings.Bottom) break;
                        after = following.Layout.After ?? 6;
                        index++;
                    }
                }
                if ((paragraph.Layout.KeepTogether || paragraph.Layout.KeepWithNext) && totalHeight <= settings.Height - settings.Top - settings.Bottom)
                    NewPageIfNeeded(totalHeight);
                y += before;
                bool firstLine = true;
                foreach (Line original in lines)
                {
                    Line line = FitLine(original, settings.Height - settings.Top - settings.Bottom);
                    NewPageIfNeeded(line.Height);
                    if (firstLine && paragraph.HeadingLevel > 0)
                    {
                        string title = string.Concat(paragraph.Runs.Select(r => DocumentFields.Render(r, model, pages.Count, totalPages, totalPages, context)));
                        pages[pages.Count - 1].Headings.Add(new FixedHeading(title, paragraph.HeadingLevel, settings.Left, settings.Height - y));
                    }
                    firstLine = false;
                    Place(line, y); y += line.Height;
                }
                y += paragraph.Layout.After ?? 6;
            }
            else if (block is TableBlock table)
            {
                RenderTable(table);
                y += 6;
            }
        }
        if (model.FinalSection != null && !ReferenceEquals(previousSection, model.FinalSection) && model.Blocks.Count > 0)
        {
            sourceSettings = model.FinalSection; sourceSettings.Validate(); settings = ReserveStories(sourceSettings); AddPage();
        }
        var sectionPages = pages.GroupBy(p => p.SourceSettings).ToDictionary(g => g.Key, g => g.Count());
        var sectionIndices = new Dictionary<PageSettings, int>();
        for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            FixedPage page = pages[pageIndex]; sourceSettings = page.SourceSettings; settings = page.Settings;
            int sectionIndex = sectionIndices.TryGetValue(sourceSettings, out int previous) ? previous + 1 : 1;
            sectionIndices[sourceSettings] = sectionIndex;
            renderPage = pageIndex + 1; renderSectionPages = sectionPages[sourceSettings];
            AddStory("header-", sourceSettings.HeaderDistance, atEnd: false);
            AddStory("footer-", sourceSettings.Height - sourceSettings.FooterDistance, atEnd: true);
            void AddStory(string prefix, double position, bool atEnd)
            {
                string variant = sourceSettings.DifferentFirstPage && sectionIndex == 1 ? "first"
                    : sourceSettings.EvenAndOdd && (pageIndex + 1) % 2 == 0 ? "even" : "default";
                if (!sourceSettings.Stories.TryGetValue(prefix + variant, out List<DocumentBlock>? story)) return;
                List<Line> lines = StoryLines(story);
                double top = atEnd ? position - lines.Sum(l => l.Height) : position;
                if (context.Options.FidelityMode == FidelityMode.Lenient)
                {
                    double constrained = Math.Max(0, Math.Min(top, settings.Height - lines.Sum(l => l.Height)));
                    if (constrained != top) context.Report("Layout.StoryPositionApproximated", "A header/footer outside page bounds is moved onto the page.", DiagnosticSeverity.Warning);
                    top = constrained;
                }
                var items = new List<PositionedText>();
                foreach (Line line in lines)
                {
                    foreach (PositionedText item in line.Items) items.Add(item.At(item.X, top + line.Ascent + item.Y));
                    top += line.Height;
                }
                if (atEnd) page.Text.AddRange(items); else page.Text.InsertRange(0, items);
            }
        }
        context.PageCount = pages.Count;
        return pages;

        PageSettings ReserveStories(PageSettings source)
        {
            settings = source;
            double header = 0, footer = 0;
            foreach (var story in source.Stories)
            {
                double height = StoryLines(story.Value).Sum(l => l.Height);
                if (story.Key.StartsWith("header-", StringComparison.Ordinal)) header = Math.Max(header, height);
                else footer = Math.Max(footer, height);
            }
            PageSettings reserved = source.Copy();
            if (header > 0) reserved.Top = Math.Max(source.Top, source.HeaderDistance + header + 6);
            if (footer > 0) reserved.Bottom = Math.Max(source.Bottom, source.FooterDistance + footer + 6);
            if (context.Options.FidelityMode == FidelityMode.Lenient && reserved.Top + reserved.Bottom >= reserved.Height)
            {
                reserved.Top = Math.Min(reserved.Top, reserved.Height * 0.45);
                reserved.Bottom = Math.Min(reserved.Bottom, reserved.Height * 0.45);
                context.Report("Layout.StoryAreaConstrained", "Oversized header/footer reservation is constrained to retain printable body space.", DiagnosticSeverity.Warning);
            }
            reserved.Validate(); return reserved;
        }

        List<Line> StoryLines(List<DocumentBlock> story)
        {
            ListCounter bodyCounter = counter; counter = new ListCounter();
            var result = new List<Line>();
            foreach (DocumentBlock block in story)
            {
                if (block is ParagraphBlock paragraph) AddParagraph(paragraph, settings.Left, settings.Width - settings.Left - settings.Right, result);
                else if (block is TableBlock table)
                {
                    foreach (List<TableCell> row in table.Rows)
                    {
                        var cells = new List<List<Line>>(); double left = settings.Left + table.LeftIndent;
                        int columns = row.Sum(c => c.ColumnSpan), column = 0;
                        foreach (TableCell cell in row)
                        {
                            double width = cell.CellWidth ?? (column < table.ColumnWidths.Count ? table.ColumnWidths.Skip(column).Take(cell.ColumnSpan).Sum()
                                : (table.Width ?? settings.Width - settings.Left - settings.Right) * cell.ColumnSpan / columns);
                            CellPadding padding = cell.Padding ?? new CellPadding();
                            var lines = new List<Line>();
                            foreach (ParagraphBlock p in cell.Paragraphs) AddParagraph(p, left + padding.Left, width - padding.Left - padding.Right, lines);
                            if (lines.Count > 0) { lines[0].Ascent += padding.Top; lines[lines.Count - 1].Descent += padding.Bottom; }
                            cells.Add(lines); left += width; column += cell.ColumnSpan;
                        }
                        int count = cells.Count == 0 ? 0 : cells.Max(c => c.Count);
                        for (int i = 0; i < count; i++)
                        {
                            var line = new Line { Origin = settings.Left };
                            foreach (var cell in cells.Where(c => c.Count > i))
                            { line.Items.AddRange(cell[i].Items); line.Ascent = Math.Max(line.Ascent, cell[i].Ascent); line.Descent = Math.Max(line.Descent, cell[i].Descent); }
                            result.Add(line);
                        }
                    }
                }
            }
            if (context.Options.FidelityMode == FidelityMode.Lenient)
            {
                double height = result.Sum(l => l.Height), maximum = settings.Height * 0.35;
                if (height > maximum)
                {
                    context.Report("Layout.StoryScaledToFit", "An oversized header/footer is proportionally scaled to retain body space.", DiagnosticSeverity.Warning);
                    double factor = maximum / height;
                    result = result.Select(l => ScaleLine(l, factor)).ToList();
                }
            }
            counter = bodyCounter; return result;
        }

        void AddParagraph(ParagraphBlock paragraph, double left, double width, List<Line> target)
        {
            List<Line> lines = Lines(paragraph, left, width, 12, false);
            if (lines.Count > 0) { lines[0].Ascent += paragraph.Layout.Before ?? 0; lines[lines.Count - 1].Descent += paragraph.Layout.After ?? 6; }
            target.AddRange(lines);
        }

        void RenderTable(TableBlock table)
        {
            var rows = new List<RowLayout>();
            double[] edges = table.ColumnEdges();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                context.CheckCancellation();
                List<TableCell> source = table.Rows[r];
                var row = new RowLayout(); rows.Add(row);
                int columns = source.Sum(c => c.ColumnSpan), column = 0;
                double left = settings.Left + table.LeftIndent, total = table.Width ?? (table.ColumnWidths.Count > 0 ? edges[edges.Length - 1] : settings.Width - settings.Left - settings.Right);
                foreach (TableCell cell in source)
                {
                    double width = cell.CellWidth ?? (column + cell.ColumnSpan <= table.ColumnWidths.Count
                        ? edges[column + cell.ColumnSpan] - edges[column] : total * cell.ColumnSpan / columns);
                    CellPadding padding = cell.Padding ?? new CellPadding { Right = 12 };
                    if (left < -0.1 || left + width > settings.Width + 0.1 || width <= padding.Left + padding.Right)
                    {
                        if (context.Options.FidelityMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableWidth: Table cells do not fit the page or their margins.");
                        context.Report("Table.WidthApproximated", "Cell width and padding are constrained to printable space.", DiagnosticSeverity.Warning);
                        left = Math.Max(settings.Left, Math.Min(left, settings.Width - settings.Right - 1));
                        width = Math.Max(1, Math.Min(width, settings.Width - settings.Right - left));
                        padding = new CellPadding();
                    }
                    var preparedCell = new CellLayout { Padding = padding, Column = column, Merge = cell.Merge };
                    if (cell.Merge != VerticalMerge.Continue)
                    {
                        for (int p = 0; p < cell.Paragraphs.Count; p++)
                        {
                            ParagraphBlock paragraph = cell.Paragraphs[p];
                            if (paragraph.Layout.PageBreakBefore || paragraph.Layout.KeepTogether || paragraph.Layout.KeepWithNext)
                                context.Loss("Layout.CellPageControls", "Paragraph page controls inside table cells use the table row's pagination policy.");
                            if (paragraph.Layout.Before is double before && before > 0) preparedCell.Lines.Add(new Line { Descent = before });
                            preparedCell.Lines.AddRange(Lines(paragraph, left + padding.Left, width - padding.Left - padding.Right,
                                paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 11, false));
                            double after = paragraph.Layout.After ?? (p + 1 < cell.Paragraphs.Count ? 6 : 0);
                            if (after > 0) preparedCell.Lines.Add(new Line { Descent = after });
                        }
                    }
                    row.Cells.Add(preparedCell);
                    left += width; column += cell.ColumnSpan;
                }
            }
            for (int r = 0; r < rows.Count; r++)
            {
                foreach (CellLayout cell in rows[r].Cells)
                    if (cell.Merge == VerticalMerge.Start)
                        while (r + cell.RowSpan < rows.Count && rows[r + cell.RowSpan].Cells.Any(c => c.Column == cell.Column && c.Merge == VerticalMerge.Continue)) cell.RowSpan++;
                rows[r].Height = rows[r].Cells.Where(c => c.RowSpan == 1).Select(c => c.Height).DefaultIfEmpty(0).Max();
            }
            for (int r = 0; r < rows.Count; r++)
                foreach (CellLayout cell in rows[r].Cells.Where(c => c.RowSpan > 1))
                {
                    double available = rows.Skip(r).Take(cell.RowSpan).Sum(row => row.Height + 6) - 6;
                    rows[r + cell.RowSpan - 1].Height += Math.Max(0, cell.Height - available);
                }
            double headerHeight = rows.Take(table.HeaderRowCount).Sum(row => row.Height + 6);
            double pageHeight = settings.Height - settings.Top - settings.Bottom;
            if (headerHeight >= pageHeight)
            {
                if (context.Options.FidelityMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableHeader: Repeating headers leave no space for body rows.");
                context.Report("Table.HeaderRepeatOmitted", "An oversized table header is retained once without repetition.", DiagnosticSeverity.Warning);
                table.HeaderRowCount = 0; headerHeight = 0;
            }
            if (table.HeaderRowCount > 0 && table.HeaderRowCount < rows.Count)
            {
                int body = table.HeaderRowCount, end = body + 1;
                for (int member = body; member < end; member++)
                    foreach (CellLayout cell in rows[member].Cells) end = Math.Max(end, member + cell.RowSpan);
                double bodyHeight = rows.Skip(body).Take(end - body).Sum(row => row.Height + 6) - 6;
                if (bodyHeight > pageHeight - headerHeight && end == body + 1 && !table.NonSplittingRows.Contains(body))
                    bodyHeight = rows[body].Cells.Select(cell => cell.Padding.Top + (cell.Lines.FirstOrDefault()?.Height ?? 0) + cell.Padding.Bottom).DefaultIfEmpty(0).Max();
                if (headerHeight + bodyHeight <= pageHeight) NewPageIfNeeded(headerHeight + bodyHeight);
            }
            for (int r = 0; r < rows.Count;)
            {
                context.CheckCancellation();
                int end = r == 0 ? Math.Max(1, table.HeaderRowCount) : r + 1;
                for (int member = r; member < end; member++)
                    foreach (CellLayout cell in rows[member].Cells) end = Math.Max(end, member + cell.RowSpan);
                double height = rows.Skip(r).Take(end - r).Sum(row => row.Height + 6) - 6;
                double capacity = pageHeight - (r >= table.HeaderRowCount ? headerHeight : 0);
                if (height > capacity)
                {
                    if (end - r > 1 || table.NonSplittingRows.Contains(r) || r < table.HeaderRowCount)
                    {
                        if (context.Options.FidelityMode != FidelityMode.Lenient)
                            throw new NotSupportedException("Layout.TableGroupHeight: A non-splitting row or vertically merged group exceeds a page.");
                        context.Report(end - r > 1 ? "Table.MergedGroupSplit" : "Table.RowForcedSplit", "An oversized row/group is split at line boundaries to retain its content.", DiagnosticSeverity.Warning);
                        for (int member = r; member < end; member++) SplitRow(rows[member], r >= table.HeaderRowCount);
                    }
                    else SplitRow(rows[r], r >= table.HeaderRowCount);
                }
                else
                {
                    if (y + height > settings.Height - settings.Bottom) TablePage(r >= table.HeaderRowCount);
                    for (int member = r; member < end; member++) PlaceRow(rows[member]);
                }
                r = end;
            }

            void PlaceRow(RowLayout row)
            {
                foreach (CellLayout cell in row.Cells)
                {
                    double top = y + cell.Padding.Top;
                    foreach (Line line in cell.Lines) { Place(line, top); top += line.Height; }
                }
                y += row.Height + 6;
            }

            void TablePage(bool repeat)
            {
                AddPage();
                if (repeat) foreach (RowLayout header in rows.Take(table.HeaderRowCount)) PlaceRow(header);
            }

            void SplitRow(RowLayout row, bool repeat)
            {
                int[] offsets = new int[row.Cells.Count];
                while (row.Cells.Where((cell, index) => offsets[index] < cell.Lines.Count).Any())
                {
                    context.CheckCancellation();
                    double used = 0;
                    bool progress = false;
                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        CellLayout cell = row.Cells[c];
                        double top = y + cell.Padding.Top;
                        while (offsets[c] < cell.Lines.Count && top + cell.Lines[offsets[c]].Height + cell.Padding.Bottom <= settings.Height - settings.Bottom)
                        {
                            Line line = cell.Lines[offsets[c]++]; Place(line, top); top += line.Height; progress = true;
                        }
                        used = Math.Max(used, top - y + cell.Padding.Bottom);
                    }
                    if (!progress && y <= settings.Top + (repeat ? headerHeight : 0) + 0.001)
                    {
                        if (context.Options.FidelityMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableLineHeight: A cell line and its padding cannot fit below repeating headers.");
                        context.Report("Table.LineScaledToFit", "Unusable cell padding is omitted and an oversized line is scaled to fit printable space.", DiagnosticSeverity.Warning);
                        used = settings.Height - settings.Bottom - y;
                        for (int c = 0; c < row.Cells.Count; c++)
                            if (offsets[c] < row.Cells[c].Lines.Count) Place(FitLine(row.Cells[c].Lines[offsets[c]++], used), y);
                    }
                    y += used + 6;
                    if (row.Cells.Where((cell, index) => offsets[index] < cell.Lines.Count).Any()) TablePage(repeat);
                }
            }
        }

        List<Line> Prepare(ParagraphBlock paragraph)
        {
            if (!prepared.TryGetValue(paragraph, out List<Line>? lines))
            {
                lines = Lines(paragraph, settings.Left, settings.Width - settings.Left - settings.Right,
                    paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 12, false);
                prepared.Add(paragraph, lines);
            }
            return lines;
        }

        void Place(Line line, double top)
        {
            foreach (PositionedText item in line.Items)
            {
                PositionedText placed = item.At(item.X, top + line.Ascent + item.Y);
                if (item.Field != null)
                    placed = placed.WithText(DocumentFields.Render(item.Field, model, pages.Count, totalPages,
                        sectionTotals != null && sectionTotals.TryGetValue(sourceSettings, out int count) ? count : totalPages, context));
                pages[pages.Count - 1].Text.Add(placed);
            }
        }

        Line FitLine(Line line, double maximum)
        {
            if (line.Height <= maximum || context.Options.FidelityMode != FidelityMode.Lenient) return line;
            context.Report("Layout.TextScaledToFit", "An oversized line is reduced to fit printable space.", DiagnosticSeverity.Warning);
            return ScaleLine(line, maximum / line.Height);
        }

        Line ScaleLine(Line line, double factor)
        {
            var scaled = new Line { Origin = line.Origin, Ascent = line.Ascent * factor, Descent = line.Descent * factor };
            foreach (PositionedText item in line.Items)
                scaled.Items.Add(new PositionedText(item.Text, line.Origin + (item.X - line.Origin) * factor, item.Y * factor,
                    item.Size * factor, item.Font, item.Color, item.Image?.Scaled(factor), item.Link, item.Field));
            return scaled;
        }

        void NewPageIfNeeded(double lineHeight)
        {
            if (lineHeight > settings.Height - settings.Top - settings.Bottom) throw new NotSupportedException("Layout.ItemHeight: An image, line or table row exceeds one page.");
            if (y + lineHeight <= settings.Height - settings.Bottom) return;
            AddPage();
        }

        void AddPage()
        {
            if (pages.Count >= context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: Output exceeds the page limit.");
            pages.Add(new FixedPage { Settings = settings, SourceSettings = sourceSettings }); y = settings.Top;
        }

        List<Line> Lines(ParagraphBlock paragraph, double left, double width, double baseSize, bool header)
        {
            var result = new List<Line>();
            double tabOrigin = left;
            left += paragraph.Layout.Left;
            width -= paragraph.Layout.Left + paragraph.Layout.Right;
            List<TextRun> runs = RunCoalescer.Combine(paragraph.Runs);
            if (paragraph.List != null)
            {
                double indent = 18 * paragraph.List.Level; left += indent; width -= indent;
                List<TextRun> content = runs;
                runs = new List<TextRun> { new TextRun(counter.Label(paragraph.List) + " ") }; runs.AddRange(content);
            }
            double continuationLeft = left, continuationWidth = width;
            left += paragraph.Layout.FirstLine;
            width -= paragraph.Layout.FirstLine;
            if (left < 0 || continuationLeft < 0 || width <= 0 || continuationWidth <= 0 || left + width > settings.Width || continuationLeft + continuationWidth > settings.Width)
            {
                if (context.Options.FidelityMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.Indent: Paragraph indentation leaves no usable content space or extends beyond the page.");
                context.Report("Layout.IndentApproximated", "Unusable indentation is constrained to printable space.", DiagnosticSeverity.Warning);
                left = continuationLeft = Math.Max(settings.Left, Math.Min(left, settings.Width - settings.Right - 12));
                width = continuationWidth = Math.Max(1, settings.Width - settings.Right - left);
            }
            if (width <= 0) throw new NotSupportedException("Layout.ColumnWidth: No space remains for paragraph content.");
            var tabText = new Queue<string>(string.Concat(runs.Select(r => DocumentFields.Render(r, model,
                renderPage == 0 ? pages.Count : renderPage, totalPages, renderSectionPages, context))).Split('\t').Skip(1).Select(t => t.Split('\n')[0]));
            double x = left;
            Line line = NewLine();
            if (paragraph.IsCodeBlock || runs.Any(r => r.Code)) context.Loss("Layout.CodeFont", "Code content uses the embedded proportional font.");
            foreach (TextRun run in runs)
            {
                context.CheckCancellation();
                if (run.Link != null && context.Destination == DocumentFormat.Xps) context.Loss("Layout.Hyperlink", "This XPS profile retains hyperlink text without an interactive target.");
                if (run.Image is DocumentImage image)
                {
                    if (context.Options.FidelityMode == FidelityMode.Lenient)
                    {
                        double scale = Math.Min(1, Math.Min(width / image.Width, (settings.Height - settings.Top - settings.Bottom) / image.Height));
                        if (scale < 1) { image = image.Scaled(scale); context.Report("Image.ScaledToFit", "An oversized image is proportionally scaled to fit printable space.", DiagnosticSeverity.Warning); }
                    }
                    if (image.Width > width) throw new NotSupportedException("Layout.ImageWidth: Image exceeds the content width; dimensions are never silently reduced.");
                    if (x > left && x + image.Width > left + width) Finish(false);
                    line.Items.Add(new PositionedText("", x, 0, 0, 0, image: image, link: run.Link)); x += image.Width;
                    line.Ascent = Math.Max(line.Ascent, image.Height); continue;
                }
                if (run.Text.Any(c => c >= '\u0590' && c <= '\u0dff' || char.IsSurrogate(c)))
                    context.Loss("Layout.Shaping", "Unsupported complex-script shaping and supplementary characters use visible fallback characters.");
                int font = resolver.Resolve(run, paragraph.HeadingLevel > 0);
                double size = run.FontSize ?? baseSize;
                if (context.Options.FidelityMode == FidelityMode.Lenient && size * 1.5 > settings.Height - settings.Top - settings.Bottom)
                {
                    size = (settings.Height - settings.Top - settings.Bottom) / 1.5;
                    context.Report("Layout.TextScaledToFit", "Oversized text is reduced to fit printable space.", DiagnosticSeverity.Warning);
                }
                string runText = DocumentFields.Render(run, model, renderPage == 0 ? pages.Count : renderPage, totalPages, renderSectionPages, context);
                if (context.Options.FidelityMode != FidelityMode.Strict)
                {
                    var safe = new StringBuilder(runText.Length);
                    foreach (char c in runText)
                    {
                        if (c is '\n' or '\t' || !(c >= '\u0590' && c <= '\u0dff') && !char.IsSurrogate(c) && maps[font] != null && maps[font]!.TryGetGlyphIndex(c, out int glyph) && glyph != 0) safe.Append(c);
                        else { safe.Append('?'); context.Report("Font.MissingGlyph", "An unsupported glyph or shaping requirement uses a visible fallback character.", DiagnosticSeverity.Warning); }
                    }
                    runText = safe.ToString();
                }
                foreach (char character in runText)
                    if (character is not '\n' and not '\t' && (maps[font] == null || !maps[font]!.TryGetGlyphIndex(character, out int glyph) || glyph == 0))
                        throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.");
                var token = new StringBuilder();
                foreach (char c in runText)
                {
                    if (c == '\t')
                    {
                        Flush();
                        string following = tabText.Count > 0 ? tabText.Dequeue() : "";
                        TabStop? stop = paragraph.Layout.Tabs.FirstOrDefault(t => tabOrigin + t.Position > x + 0.01);
                        double position = stop == null ? tabOrigin + (Math.Floor((x - tabOrigin) / 36) + 1) * 36 : tabOrigin + stop.Position;
                        double advance = Measure(following, font, size);
                        if (stop?.Alignment == "center") position -= advance / 2;
                        else if (stop?.Alignment == "right") position -= advance;
                        else if (stop?.Alignment == "decimal") position -= Measure(following.Split('.')[0], font, size);
                        position = Math.Min(left + width, Math.Max(x, position));
                        if (stop != null && stop.Leader != "none")
                        {
                            string leader = stop.Leader == "dot" ? "." : stop.Leader == "hyphen" ? "-" : "_";
                            double leaderWidth = Measure(leader, font, size);
                            int count = leaderWidth > 0 ? (int)Math.Min(10000, (position - x) / leaderWidth) : 0;
                            if (count > 0) line.Items.Add(new PositionedText(new string(leader[0], count), x, 0, size, font, run.Color));
                        }
                        x = position;
                    }
                    else if (c == '\n') { Flush(); Finish(); }
                    else { token.Append(c); if (c == ' ') Flush(); }
                }
                Flush();

                void Flush()
                {
                    if (token.Length == 0) return;
                    string value = token.ToString(); token.Clear(); double advance = Measure(value, font, size);
                    if (advance <= width) Add(value, advance);
                    else foreach (char c in value) Add(c.ToString(), Measure(c.ToString(), font, size));
                }
                void Add(string value, double advance)
                {
                    if (advance > width)
                    {
                        if (context.Options.FidelityMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.ColumnWidth: A glyph cannot fit within the column.");
                        size *= width / advance; advance = width;
                        context.Report("Layout.TextScaledToFit", "Oversized text is reduced to fit printable space.", DiagnosticSeverity.Warning);
                    }
                    if (x > left && x + advance > left + width) Finish(false);
                    MeasuredText shaped = Shape(value, font, size);
                    int begin = 0;
                    for (int i = 1; i < value.Length; i++)
                    {
                        if (Math.Abs((shaped.X[i] - shaped.OriginalX[i]) - (shaped.X[begin] - shaped.OriginalX[begin])) < 0.000001
                            && Math.Abs(shaped.Y[i] - shaped.Y[begin]) < 0.000001) continue;
                        line.Items.Add(new PositionedText(value.Substring(begin, i - begin), x + shaped.X[begin], shaped.Y[begin], size, font, run.Color, link: run.Link));
                        begin = i;
                    }
                    string kind = run.FieldInstruction == null ? "" : DocumentFields.Kind(run.FieldInstruction);
                    TextRun? pageField = begin == 0 && value == runText && kind is "PAGE" or "NUMPAGES" or "SECTIONPAGES" ? run : null;
                    line.Items.Add(new PositionedText(value.Substring(begin), x + shaped.X[begin], shaped.Y[begin], size, font, run.Color, link: run.Link, field: pageField)); x += advance;
                    line.Ascent = Math.Max(line.Ascent, size); line.Descent = Math.Max(line.Descent, size * 0.5);
                }
            }
            Finish();
            return result;

            Line NewLine() => new Line { Origin = tabOrigin, Ascent = baseSize, Descent = baseSize * 0.5 };
            void Finish(bool lastLine = true)
            {
                if (paragraph.Alignment is ParagraphAlignment.Center or ParagraphAlignment.Right)
                {
                    PositionedText? last = line.Items.LastOrDefault(i => i.Image != null || i.Text.TrimEnd(' ').Length > 0);
                    double end = last == null ? left : last.X + (last.Image?.Width ?? Measure(last.Text.TrimEnd(' '), last.Font, last.Size));
                    double shift = Math.Max(0, left + width - end) / (paragraph.Alignment == ParagraphAlignment.Center ? 2 : 1);
                    for (int i = 0; i < line.Items.Count; i++) line.Items[i] = line.Items[i].At(line.Items[i].X + shift, line.Items[i].Y);
                }
                else if (!lastLine && paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed)
                {
                    if (paragraph.Alignment == ParagraphAlignment.Distributed)
                    {
                        var characters = new List<PositionedText>();
                        foreach (PositionedText item in line.Items)
                        {
                            if (item.Image != null) { characters.Add(item); continue; }
                            double position = item.X;
                            foreach (char c in item.Text)
                            { characters.Add(new PositionedText(c.ToString(), position, item.Y, item.Size, item.Font, item.Color, link: item.Link)); position += Measure(c.ToString(), item.Font, item.Size); }
                        }
                        line.Items.Clear(); line.Items.AddRange(characters);
                    }
                    PositionedText? last = line.Items.LastOrDefault(i => i.Image != null || i.Text.TrimEnd(' ').Length > 0);
                    if (last != null)
                    {
                        double end = last.X + (last.Image?.Width ?? Measure(last.Text.TrimEnd(' '), last.Font, last.Size));
                        int lastIndex = line.Items.IndexOf(last);
                        bool distributed = paragraph.Alignment == ParagraphAlignment.Distributed;
                        int gaps = distributed ? lastIndex : line.Items.Take(lastIndex).Sum(item => item.Text.Count(c => c == ' '));
                        if (gaps > 0)
                        {
                            double extra = Math.Max(0, left + width - end) / gaps, offset = 0;
                            for (int i = 0; i < line.Items.Count; i++)
                            {
                                PositionedText item = line.Items[i]; line.Items[i] = item.At(item.X + offset, item.Y);
                                if (i < lastIndex) offset += (distributed ? 1 : item.Text.Count(c => c == ' ')) * extra;
                            }
                        }
                    }
                }
                if (paragraph.Layout.LineSpacing is double spacing)
                {
                    double requested = paragraph.Layout.MultipleLineSpacing ? line.Height * spacing : spacing;
                    if (requested < line.Height && (paragraph.Layout.ExactLineSpacing || paragraph.Layout.MultipleLineSpacing))
                        context.Loss("Layout.LineSpacing", "Line spacing smaller than content height is expanded to avoid clipping or overlap.");
                    line.Descent += Math.Max(0, requested - line.Height);
                }
                result.Add(line); line = NewLine(); left = continuationLeft; width = continuationWidth; x = left;
            }
        }

        double Measure(string value, int font, double size) => Shape(value, font, size).Width;

        MeasuredText Shape(string value, int font, double size)
        {
            context.CheckCancellation();
            var key = (value, font, size);
            if (measurements.TryGetValue(key, out MeasuredText? cached)) return cached;
            try
            {
                var letters = metrics.MeasureText(value, size, new PdfPoint(0, 0), fonts[font]);
                var glyphs = new int[value.Length];
                for (int i = 0; i < glyphs.Length; i++)
                    if (maps[font] != null) _ = maps[font]!.TryGetGlyphIndex(value[i], out glyphs[i]);
                GlyphAdjustment[] adjusted = kerning[font].Position(glyphs, size);
                var result = new MeasuredText { X = new double[value.Length], Y = new double[value.Length], OriginalX = new double[value.Length] };
                double advanceX = 0, advanceY = 0;
                for (int i = 0; i < letters.Count; i++)
                {
                    result.OriginalX[i] = letters[i].StartBaseLine.X;
                    result.X[i] = letters[i].StartBaseLine.X + advanceX + adjusted[i].OffsetX;
                    result.Y[i] = -advanceY - adjusted[i].OffsetY;
                    advanceX += adjusted[i].AdvanceX; advanceY += adjusted[i].AdvanceY;
                }
                result.Width = Math.Max(0, (letters.Count == 0 ? 0 : letters[letters.Count - 1].EndBaseLine.X) + advanceX);
                if (measurements.Count < 2048 && value.Length <= 131072 - cachedCharacters)
                { measurements.Add(key, result); cachedCharacters += value.Length; }
                return result;
            }
            catch (InvalidOperationException error) { throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.", error); }
        }
    }
}
