using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordConverter
{
    private static DocumentLayoutSnapshot CapturePreviewLayout(DocumentModel model, List<FixedPage> pages, IReadOnlyList<int> selected,
        LayoutSnapshotOptions policy, ConversionContext context)
    {
        var fragments = new List<LayoutFragment>(); long size = 0; bool graphics = false;
        static bool Plain(ParagraphBlock p) => p.Control == null && p.NestedControl == null
            && !p.TypedControlId.HasValue && p.List == null && p.Runs.All(run => run.Control == null && !run.TypedControlId.HasValue
                && run.FieldInstruction == null && run.Bookmark == null && run.Note == null && run.Image == null && run.Box == null && run.Object == null);
        static bool BodyTable(string path)
        {
            const string prefix = "/document[1]/body[1]/tbl[";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length <= prefix.Length + 1
                || path[path.Length - 1] != ']' || path[prefix.Length] is < '1' or > '9') return false;
            for (int i = prefix.Length + 1; i < path.Length - 1; i++) if (path[i] is < '0' or > '9') return false;
            return true;
        }
        var admitted = new HashSet<ParagraphBlock>(model.Blocks.OfType<ParagraphBlock>().Where(Plain));
        var cells = new Dictionary<ParagraphBlock, SourceLocation>();
        var ambiguous = new HashSet<ParagraphBlock>(); long ownerWork = 0;
        foreach (TableBlock table in model.Blocks.OfType<TableBlock>())
        {
            context.CheckCancellation();
            if (++ownerWork > Math.Min(100000L, context.Options.MaximumExpandedBytes / 128))
                throw new InvalidDataException("Limit.LayoutSnapshot: Cell owner inspection exceeds the snapshot work budget.");
            SourceLocation? tableLocation = table.Location;
            if (tableLocation == null || tableLocation.Part != "word/document.xml" || !BodyTable(tableLocation.Path)
                || table.Control != null || table.NestedControl != null || table.TypedControlId.HasValue
                || table.RowControls.Count != 0 || table.TypedRowControls.Count != 0 || table.RowRevisions.Count != 0
                || table.RowHeights.Count != 0 || table.Rows.Count == 0) continue;
            bool rectangular = true;
            foreach (var row in table.Rows)
            {
                context.CheckCancellation();
                if ((ownerWork = checked(ownerWork + row.Count)) > Math.Min(100000L, context.Options.MaximumExpandedBytes / 128))
                    throw new InvalidDataException("Limit.LayoutSnapshot: Cell owner inspection exceeds the snapshot work budget.");
                if (row.Count != table.Rows[0].Count || row.Any(cell => cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None || cell.TypedControlId.HasValue))
                    rectangular = false;
            }
            if (!rectangular) continue;
            for (int r = 0; r < table.Rows.Count; r++)
                for (int c = 0; c < table.Rows[r].Count; c++)
                {
                    context.CheckCancellation(); TableCell cell = table.Rows[r][c];
                    int count = cell.Content.Count > 0 ? cell.Content.Count : cell.Paragraphs.Count;
                    if ((ownerWork = checked(ownerWork + count)) > Math.Min(100000L, context.Options.MaximumExpandedBytes / 128))
                        throw new InvalidDataException("Limit.LayoutSnapshot: Cell owner inspection exceeds the snapshot work budget.");
                    DocumentBlock[] blocks = cell.Blocks.ToArray();
                    if (blocks.Length == 0 || blocks.Any(block => block is not ParagraphBlock paragraph || !Plain(paragraph))) continue;
                    string cellPath = tableLocation.Path + "/tr[" + (r + 1).ToString(CultureInfo.InvariantCulture)
                        + "]/tc[" + (c + 1).ToString(CultureInfo.InvariantCulture) + "]";
                    for (int p = 0; p < blocks.Length; p++)
                    {
                        var paragraph = (ParagraphBlock)blocks[p]; SourceLocation? location = paragraph.Location;
                        if (location == null || location.Part != tableLocation.Part || location.Path != cellPath + "/p[" + (p + 1).ToString(CultureInfo.InvariantCulture) + "]") continue;
                        if (cells.Remove(paragraph)) { ambiguous.Add(paragraph); admitted.Remove(paragraph); continue; }
                        if (ambiguous.Contains(paragraph)) continue;
                        cells.Add(paragraph, new SourceLocation(tableLocation.Part, cellPath)); admitted.Add(paragraph);
                    }
                }
        }
        using var metrics = new PdfDocumentBuilder();
        var fonts = context.FontData.Select(bytes => metrics.AddTrueTypeFont(bytes)).ToArray();
        var pageMetrics = metrics.AddPage(595, 842);
        foreach (int number in selected)
        {
            context.CheckCancellation(); FixedPage page = pages[number - 1]; graphics |= page.Shapes.Count != 0;
            for (int order = 0; order < page.Text.Count; order++)
            {
                context.CheckCancellation(); PositionedText item = page.Text[order];
                if (fragments.Count >= policy.MaximumFragments) throw new InvalidDataException("Limit.LayoutFragments: Layout snapshot exceeds its fragment limit.");
                SourceLocation? location = item.SourceParagraph?.Location;
                SourceLocation? source = location == null ? null : new SourceLocation(location.Part, location.Path);
                SourceLocation? cellSource = item.SourceParagraph != null && cells.TryGetValue(item.SourceParagraph, out SourceLocation? owner)
                    ? new SourceLocation(owner.Part, owner.Path) : null;
                string? text = policy.IncludeText ? item.Text : null;
                size = checked(size + 128 + 2L * ((source?.Part.Length ?? 0) + (source?.Path.Length ?? 0)
                    + (cellSource?.Part.Length ?? 0) + (cellSource?.Path.Length ?? 0) + (text?.Length ?? 0)));
                if (size > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.LayoutSnapshot: Layout snapshot exceeds the expanded-byte limit.");
                LayoutBounds? bounds = null; string? reason = null;
                if (source == null) reason = "SourceUnavailable";
                else if (!admitted.Contains(item.SourceParagraph!)) reason = "UnqualifiedOwner";
                else if (item.Image != null || item.Box != null || item.Visual != null || item.Field != null || item.Rotation != 0 || item.NativeShape != null || item.IsLabel)
                    reason = "StructuredOrTransformed";
                else if (item.Text.Length == 0 || item.Text.All(char.IsWhiteSpace)) reason = "NoVisibleGlyphs";
                else
                {
                    if (item.Font < 0 || item.Font >= fonts.Length) throw new InvalidDataException("Layout.SnapshotFont: A positioned item has an invalid font index.");
                    var letters = pageMetrics.MeasureText(item.Text, item.Size, new PdfPoint(item.X, page.Settings.Height - item.Y), fonts[item.Font]);
                    double left = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.PositiveInfinity, top = double.NegativeInfinity;
                    foreach (var letter in letters)
                    {
                        context.CheckCancellation();
                        if (string.IsNullOrWhiteSpace(letter.Value)) continue;
                        var box = letter.BoundingBox;
                        if (!Finite(box.Left) || !Finite(box.Right) || !Finite(box.Bottom) || !Finite(box.Top))
                            throw new InvalidDataException("Layout.SnapshotGeometry: Non-finite glyph bounds cannot be reported.");
                        if (box.Right <= box.Left || box.Top <= box.Bottom) continue;
                        left = Math.Min(left, box.Left); right = Math.Max(right, box.Right);
                        bottom = Math.Min(bottom, box.Bottom); top = Math.Max(top, box.Top);
                    }
                    if (double.IsPositiveInfinity(left)) reason = "NoVisibleGlyphs";
                    else
                    {
                        double y = page.Settings.Height - top, width = right - left, height = top - bottom;
                        if (!Finite(y) || !Finite(width) || !Finite(height))
                            throw new InvalidDataException("Layout.SnapshotGeometry: Non-finite glyph bounds cannot be reported.");
                        bounds = new LayoutBounds(left, y, width, height);
                    }
                }
                bool? outsidePage = bounds == null ? null : bounds.X < 0 || bounds.Y < 0
                    || bounds.X + bounds.Width > page.Settings.Width || bounds.Y + bounds.Height > page.Settings.Height;
                fragments.Add(new LayoutFragment(number, order, source, cellSource, bounds, text, reason, outsidePage));
            }
        }
        return new DocumentLayoutSnapshot(fragments, graphics);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
