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

internal static class FixedLayout
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
        var pages = new List<FixedPage> { new FixedPage() };
        var counter = new ListCounter();
        double y = 48;
        foreach (DocumentBlock block in model.Blocks)
        {
            context.CheckCancellation();
            if (block is ParagraphBlock paragraph)
            {
                double size = paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 12;
                foreach (Line line in Lines(paragraph, 48, Width - 96, size, false))
                {
                    NewPageIfNeeded(line.Height); Place(line, y); y += line.Height;
                }
                y += 6;
            }
            else if (block is TableBlock table)
            {
                foreach (List<ParagraphBlock> row in table.Rows)
                {
                    if (row.Count == 0) continue;
                    double left = 48, total = table.Width ?? (table.ColumnWidths.Count == row.Count ? table.ColumnWidths.Sum() : Width - 96);
                    var cells = new List<(List<Line> Lines, CellPadding Padding)>();
                    for (int c = 0; c < row.Count; c++)
                    {
                        ParagraphBlock cell = row[c];
                        double width = cell.CellWidth ?? (c < table.ColumnWidths.Count ? table.ColumnWidths[c] : total / row.Count);
                        CellPadding padding = cell.Padding ?? new CellPadding { Right = 12 };
                        if (left + width > Width - 48 + 0.1 || width <= padding.Left + padding.Right)
                            throw new NotSupportedException("Layout.TableWidth: Table cells do not fit the page or their margins.");
                        cells.Add((Lines(cell, left + padding.Left, width - padding.Left - padding.Right, 11, table.HasHeaderRow && ReferenceEquals(row, table.Rows[0])), padding));
                        left += width;
                    }
                    double height = cells.Max(c => c.Padding.Top + c.Lines.Sum(l => l.Height) + c.Padding.Bottom);
                    NewPageIfNeeded(height);
                    foreach (var cell in cells)
                    {
                        double top = y + cell.Padding.Top;
                        foreach (Line line in cell.Lines) { Place(line, top); top += line.Height; }
                    }
                    y += height + 6;
                }
                y += 6;
            }
        }
        return pages;

        void Place(Line line, double top)
        {
            foreach (PositionedText item in line.Items) pages[pages.Count - 1].Text.Add(item.At(item.X, top + line.Ascent));
        }

        void NewPageIfNeeded(double lineHeight)
        {
            if (lineHeight > Height - 96) throw new NotSupportedException("Layout.ItemHeight: An image, line or table row exceeds one page.");
            if (y + lineHeight <= Height - 48) return;
            if (pages.Count >= context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: Output exceeds the page limit.");
            pages.Add(new FixedPage()); y = 48;
        }

        List<Line> Lines(ParagraphBlock paragraph, double left, double width, double baseSize, bool header)
        {
            var result = new List<Line>();
            List<TextRun> runs = paragraph.Runs;
            if (paragraph.List != null)
            {
                double indent = 18 * paragraph.List.Level; left += indent; width -= indent;
                runs = new List<TextRun> { new TextRun(counter.Label(paragraph.List) + " ") }; runs.AddRange(paragraph.Runs);
            }
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
                int font = ((run.Bold || header || paragraph.HeadingLevel > 0) ? 1 : 0) + (run.Italic ? 2 : 0);
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
                result.Add(line); line = NewLine(); x = left;
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
