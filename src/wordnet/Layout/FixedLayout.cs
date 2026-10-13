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
    internal List<PositionedText> Text { get; } = new List<PositionedText>();
}

internal sealed class PositionedText
{
    internal PositionedText(string text, double x, double y, double size, int font, string? color = null, DocumentImage? image = null)
    { Text = text; X = x; Y = y; Size = size; Font = font; Color = color; Image = image; }
    internal string Text { get; }
    internal double X { get; }
    internal double Y { get; }
    internal double Size { get; }
    internal int Font { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal PositionedText At(double x, double y) => new PositionedText(Text, x, y, Size, Font, Color, Image);
}

internal static partial class FixedLayout
{
    internal const double Width = 595.276;
    internal const double Height = 841.89;
    internal static readonly string[] FontNames = { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf", "NotoSans-Italic.ttf", "NotoSans-BoldItalic.ttf" };
    private sealed class Line
    {
        internal List<PositionedText> Items { get; } = new List<PositionedText>();
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

    internal static byte[] FontBytes(int index)
    {
        using Stream stream = typeof(FixedLayout).GetTypeInfo().Assembly.GetManifestResourceStream("WordNet.Resources.Fonts." + FontNames[index])
            ?? throw new InvalidOperationException("Embedded font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }

    internal static List<FixedPage> Create(DocumentModel model, ConversionContext context)
    {
        using var measure = new PdfDocumentBuilder();
        var data = Enumerable.Range(0, 4).Select(FontBytes).ToArray();
        var fonts = data.Select(bytes => measure.AddTrueTypeFont(bytes)).ToArray();
        var maps = data.Select(bytes => TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable).ToArray();
        PdfPageBuilder metrics = measure.AddPage(Width, Height);
        PageSettings settings = model.Blocks.FirstOrDefault()?.Section ?? model.FinalSection ?? new PageSettings();
        settings.Validate();
        var pages = new List<FixedPage> { new FixedPage { Settings = settings } };
        var counter = new ListCounter();
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
                settings = block.Section ?? new PageSettings(); settings.Validate();
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
                foreach (Line line in lines)
                {
                    NewPageIfNeeded(line.Height); Place(line, y); y += line.Height;
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
            settings = model.FinalSection; settings.Validate(); AddPage();
        }
        return pages;

        void RenderTable(TableBlock table)
        {
            var rows = new List<RowLayout>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                context.CheckCancellation();
                List<TableCell> source = table.Rows[r];
                var row = new RowLayout(); rows.Add(row);
                int columns = source.Sum(c => c.ColumnSpan), column = 0;
                double left = settings.Left, total = table.Width ?? (table.ColumnWidths.Count > 0 ? table.ColumnWidths.Sum() : settings.Width - settings.Left - settings.Right);
                foreach (TableCell cell in source)
                {
                    double width = cell.CellWidth ?? (column + cell.ColumnSpan <= table.ColumnWidths.Count
                        ? table.ColumnWidths.Skip(column).Take(cell.ColumnSpan).Sum() : total * cell.ColumnSpan / columns);
                    CellPadding padding = cell.Padding ?? new CellPadding { Right = 12 };
                    if (left + width > settings.Width - settings.Right + 0.1 || width <= padding.Left + padding.Right)
                        throw new NotSupportedException("Layout.TableWidth: Table cells do not fit the page or their margins.");
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
            if (headerHeight >= pageHeight) throw new NotSupportedException("Layout.TableHeader: Repeating headers leave no space for body rows.");
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
                        throw new NotSupportedException("Layout.TableGroupHeight: A non-splitting row or vertically merged group exceeds a page.");
                    SplitRow(rows[r], r >= table.HeaderRowCount);
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
                        throw new NotSupportedException("Layout.TableLineHeight: A cell line and its padding cannot fit below repeating headers.");
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
            foreach (PositionedText item in line.Items) pages[pages.Count - 1].Text.Add(item.At(item.X, top + line.Ascent));
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
            pages.Add(new FixedPage { Settings = settings }); y = settings.Top;
        }

        List<Line> Lines(ParagraphBlock paragraph, double left, double width, double baseSize, bool header)
        {
            var result = new List<Line>();
            left += paragraph.Layout.Left;
            width -= paragraph.Layout.Left + paragraph.Layout.Right;
            List<TextRun> runs = paragraph.Runs;
            if (paragraph.List != null)
            {
                double indent = 18 * paragraph.List.Level; left += indent; width -= indent;
                runs = new List<TextRun> { new TextRun(counter.Label(paragraph.List) + " ") }; runs.AddRange(paragraph.Runs);
            }
            double continuationLeft = left, continuationWidth = width;
            left += paragraph.Layout.FirstLine;
            width -= paragraph.Layout.FirstLine;
            if (left < 0 || continuationLeft < 0 || continuationWidth <= 0 || left + width > settings.Width || continuationLeft + continuationWidth > settings.Width)
                throw new NotSupportedException("Layout.Indent: Paragraph indentation leaves no usable content space or extends beyond the page.");
            if (width <= 0) throw new NotSupportedException("Layout.ColumnWidth: No space remains for paragraph content.");
            double x = left;
            Line line = NewLine();
            if (paragraph.IsCodeBlock || runs.Any(r => r.Code)) context.Loss("Layout.CodeFont", "Code content uses the embedded proportional font.");
            foreach (TextRun run in runs)
            {
                context.CheckCancellation();
                if (run.Link != null) context.Loss("Layout.Hyperlink", "Fixed-page output retains hyperlink text without an interactive target.");
                if (run.Image is DocumentImage image)
                {
                    if (image.Width > width) throw new NotSupportedException("Layout.ImageWidth: Image exceeds the content width; dimensions are never silently reduced.");
                    if (x > left && x + image.Width > left + width) Finish();
                    line.Items.Add(new PositionedText("", x, 0, 0, 0, image: image)); x += image.Width;
                    line.Ascent = Math.Max(line.Ascent, image.Height); continue;
                }
                if (run.Text.Any(c => c >= '\u0590' && c <= '\u0dff' || char.IsSurrogate(c)))
                    throw new NotSupportedException("Layout.Shaping: This page-layout profile does not support complex-script shaping or supplementary characters.");
                int font = ((run.Bold || paragraph.HeadingLevel > 0) ? 1 : 0) + (run.Italic ? 2 : 0);
                double size = run.FontSize ?? baseSize;
                foreach (char character in run.Text)
                    if (character is not '\n' and not '\t' && (maps[font] == null || !maps[font]!.TryGetGlyphIndex(character, out int glyph) || glyph == 0))
                        throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.");
                if (run.Text.Contains('\t')) context.Loss("Layout.Tabs", "Tab stops are approximated by four spaces in fixed-page output.");
                var token = new StringBuilder();
                foreach (char c in run.Text.Replace("\t", "    "))
                {
                    if (c == '\n') { Flush(); Finish(); }
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
                    if (advance > width) throw new NotSupportedException("Layout.ColumnWidth: A glyph cannot fit within the column.");
                    if (x > left && x + advance > left + width) Finish();
                    line.Items.Add(new PositionedText(value, x, 0, size, font, run.Color)); x += advance;
                    line.Ascent = Math.Max(line.Ascent, size); line.Descent = Math.Max(line.Descent, size * 0.5);
                }
            }
            Finish();
            return result;

            Line NewLine() => new Line { Ascent = baseSize, Descent = baseSize * 0.5 };
            void Finish()
            {
                if (paragraph.Alignment != ParagraphAlignment.Left)
                {
                    PositionedText? last = line.Items.LastOrDefault(i => i.Image != null || i.Text.TrimEnd(' ').Length > 0);
                    double end = last == null ? left : last.X + (last.Image?.Width ?? Measure(last.Text.TrimEnd(' '), last.Font, last.Size));
                    double shift = Math.Max(0, left + width - end) / (paragraph.Alignment == ParagraphAlignment.Center ? 2 : 1);
                    for (int i = 0; i < line.Items.Count; i++) line.Items[i] = line.Items[i].At(line.Items[i].X + shift, 0);
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

        double Measure(string value, int font, double size)
        {
            context.CheckCancellation();
            try { var letters = metrics.MeasureText(value, size, new PdfPoint(0, 0), fonts[font]); return letters.Count == 0 ? 0 : letters[letters.Count - 1].EndBaseLine.X; }
            catch (InvalidOperationException error) { throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.", error); }
        }
    }
}
