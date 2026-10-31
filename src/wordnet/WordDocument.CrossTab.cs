using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

/// <summary>Aggregation for an explicitly prepared cross-tab table.</summary>
public enum CrossTabAggregation
{
    /// <summary>Count all records, including records with a null value.</summary>
    Count,
    /// <summary>Sum non-null decimal values with checked arithmetic.</summary>
    Sum,
}

/// <summary>A caller-prepared cross-tab record.</summary>
public sealed class CrossTabRecord
{
    /// <summary>Creates one record with non-empty row and column keys.</summary>
    public CrossTabRecord(string row, string column, decimal? value = null)
    { Row = row ?? throw new ArgumentNullException(nameof(row)); Column = column ?? throw new ArgumentNullException(nameof(column)); Value = value; }
    /// <summary>Gets the row key.</summary>
    public string Row { get; }
    /// <summary>Gets the column key.</summary>
    public string Column { get; }
    /// <summary>Gets the optional numeric value.</summary>
    public decimal? Value { get; }
}

public sealed partial class WordDocument
{
    private static readonly char[] CrossTabLineBreaks = { '\r', '\n', '\t' };
    /// <summary>Appends a bounded editable cross-tab in first-seen key order, with optional totals.</summary>
    /// <remarks>Null, empty and control-character keys are rejected. Missing intersections and empty sums are zero.
    /// Counts include null values; sums exclude them and use the maximum input decimal scale.
    /// Empty input produces a header-only table. Font-measured widths must fit the body column.</remarks>
    public void AddCrossTab(IEnumerable<CrossTabRecord> records, CrossTabAggregation aggregation = CrossTabAggregation.Sum,
        bool includeTotals = false, CancellationToken cancellationToken = default)
        => _ = CrossTabAt(null, records, aggregation, includeTotals, false, cancellationToken);

    /// <summary>Replaces a dedicated body bookmark paragraph with a bounded editable cross-tab.</summary>
    /// <remarks>The bookmark must enclose the entire unprotected plain-text body paragraph; references to it must be resolved first.
    /// Rows retain first-seen order, repeat the header where supported and use the target section's available column width.</remarks>
    public DocumentEditResult InsertCrossTabAtBookmark(string name, IEnumerable<CrossTabRecord> records,
        CrossTabAggregation aggregation = CrossTabAggregation.Sum, bool includeTotals = false, CancellationToken cancellationToken = default)
    {
        ValidateBookmarkName(name);
        return CrossTabAt(name, records, aggregation, includeTotals, false, cancellationToken);
    }

    /// <summary>Replaces a dedicated bookmark paragraph in a fixed-grid body-table cell with a bounded editable cross-tab.</summary>
    /// <remarks>The cell and its table must have unambiguous fixed geometry, all four physical margins explicitly present or inherited in the DOCX/DOTX source,
    /// and no protected owners;
    /// references to the bookmark must be resolved first.
    /// Nested-table headers are retained once and do not repeat across pages.</remarks>
    public DocumentEditResult InsertCrossTabAtCellBookmark(string name, IEnumerable<CrossTabRecord> records,
        CrossTabAggregation aggregation = CrossTabAggregation.Sum, bool includeTotals = false, CancellationToken cancellationToken = default)
    {
        ValidateBookmarkName(name);
        return CrossTabAt(name, records, aggregation, includeTotals, true, cancellationToken);
    }

    private DocumentEditResult CrossTabAt(string? bookmark, IEnumerable<CrossTabRecord> records, CrossTabAggregation aggregation,
        bool includeTotals, bool cellDestination, CancellationToken cancellationToken)
    {
        Guard.NotNull(records, nameof(records));
        if (aggregation is not CrossTabAggregation.Sum and not CrossTabAggregation.Count) throw new ArgumentOutOfRangeException(nameof(aggregation));
        return Edit((m, c) =>
        {
            int destinationIndex = -1; ParagraphBlock? placeholder = null; TableBlock? ownerTable = null; TableCell? ownerCell = null;
            int ownerRow = -1, ownerColumn = -1;
            if (bookmark != null)
            {
                ValidateRanges(m);
                long ownerWork = 0;
                if (cellDestination)
                {
                    foreach (TableBlock candidateTable in m.Blocks.OfType<TableBlock>())
                        for (int row = 0; row < candidateTable.Rows.Count; row++)
                            for (int column = 0; column < candidateTable.Rows[row].Count; column++)
                            {
                                ReserveOwnerWork();
                                TableCell cell = candidateTable.Rows[row][column]; int index = 0;
                                foreach (DocumentBlock block in cell.Blocks)
                                {
                                    ReserveOwnerWork();
                                    if (block is ParagraphBlock candidate && candidate.Runs.Any(run => run.Bookmark == bookmark))
                                    {
                                        if (placeholder != null) throw new InvalidDataException("Report.CrossTabPlaceholder: A unique cell bookmark is required.");
                                        placeholder = candidate; destinationIndex = index; ownerTable = candidateTable; ownerCell = cell; ownerRow = row; ownerColumn = column;
                                    }
                                    index++;
                                }
                            }
                }
                else for (int i = 0; i < m.Blocks.Count; i++)
                {
                    c.CheckCancellation();
                    if (m.Blocks[i] is not ParagraphBlock candidate || !candidate.Runs.Any(run => run.Bookmark == bookmark)) continue;
                    if (placeholder != null) throw new InvalidDataException("Report.CrossTabPlaceholder: A unique body bookmark is required.");
                    placeholder = candidate; destinationIndex = i;
                }
                if (placeholder == null || placeholder.Control != null || placeholder.NestedControl != null || placeholder.TypedControlId.HasValue
                    || placeholder.List != null || placeholder.HeadingLevel != 0 || placeholder.IsCodeBlock
                    || placeholder.Layout.PageBreakBefore || placeholder.Layout.KeepTogether || placeholder.Layout.KeepWithNext)
                    throw new InvalidDataException("Report.CrossTabPlaceholder: A unique unwrapped bookmark paragraph in the selected owner is required.");
                if (cellDestination && (ownerTable == null || ownerCell == null || !AdmittedCrossTabCell(ownerTable, ownerRow, ownerColumn, ReserveOwnerWork)))
                    throw new NotSupportedException("Report.CrossTabCell: The destination requires an unprotected fixed-grid body-table cell.");
                int start = placeholder.Runs.FindIndex(run => run.Bookmark == bookmark), end = placeholder.Runs.FindIndex(run => run.Bookmark == "/" + bookmark);
                if (start != 0 || end != placeholder.Runs.Count - 1 || end <= start || placeholder.Runs.Any(run => run.TypedControlId.HasValue)
                    || placeholder.Runs.Skip(1).Take(end - 1).Any(run => run.Bookmark != null || run.Control != null || run.Note != null || run.Image != null
                        || run.FieldInstruction != null || run.Link != null || run.Box != null || run.Object != null))
                    throw new NotSupportedException("Report.CrossTabPlaceholder: The bookmark must own the complete plain-text paragraph.");
                var open = new Stack<string>(); long boundaryWork = 0;
                foreach (TextRun run in m.BodyParagraphs.SelectMany(p => p.Runs))
                {
                    c.CheckCancellation(); if (++boundaryWork > Math.Max(4096L, c.Options.MaximumCharacters * 64L)) throw new InvalidDataException("Limit.FragmentWork: Bookmark traversal exceeds the work bound.");
                    if (run.Bookmark is not string marker) continue;
                    if (marker == bookmark && open.Count != 0) throw new NotSupportedException("Report.CrossTabPlaceholder: The placeholder cannot be enclosed by another bookmark range.");
                    if (Starts(marker, '/'))
                    {
                        if (open.Count == 0 || open.Pop() != marker.Substring(1)) throw new NotSupportedException("Report.CrossTabPlaceholder: Crossing bookmark ranges are unsupported.");
                    }
                    else
                    {
                        if (open.Count >= c.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Bookmark nesting exceeds the configured bound.");
                        open.Push(marker);
                    }
                }
                if (open.Count != 0) throw new InvalidDataException("Report.CrossTabPlaceholder: An incomplete body bookmark range was found.");
                var pending = new Stack<TextRun>(m.Paragraphs.SelectMany(p => p.Runs)); long work = 0;
                while (pending.Count != 0)
                {
                    c.CheckCancellation(); if (++work > Math.Max(4096L, c.Options.MaximumCharacters * 64L)) throw new InvalidDataException("Limit.FragmentWork: Reference traversal exceeds the work bound.");
                    TextRun run = pending.Pop();
                    if (run.Link != null && Starts(run.Link, '#') && string.Equals(run.Link.Substring(1), bookmark, StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Report.CrossTabReference: Resolve links to the placeholder before replacement.");
                    if (run.FieldInstruction != null)
                    {
                        Match reference = Regex.Match(run.FieldInstruction, "^\\s*(REF|PAGEREF)\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                        if (reference.Success && string.Equals(reference.Groups[2].Success ? reference.Groups[2].Value : reference.Groups[3].Value, bookmark, StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException("Report.CrossTabReference: Resolve fields referring to the placeholder before replacement.");
                    }
                    if (run.FieldChildren != null) foreach (TextRun child in run.FieldChildren.Values) pending.Push(child);
                    if (run.FieldCache != null) foreach (TextRun child in run.FieldCache) pending.Push(child);
                }

                void ReserveOwnerWork()
                {
                    c.CheckCancellation();
                    if (++ownerWork > Math.Max(4096L, c.Options.MaximumCharacters * 64L))
                        throw new InvalidDataException("Limit.FragmentWork: Owner traversal exceeds the work bound.");
                }
            }
            var rows = new List<string>(); var columns = new List<string>();
            var rowKeys = new HashSet<string>(StringComparer.Ordinal); var columnKeys = new HashSet<string>(StringComparer.Ordinal);
            var values = new Dictionary<(string Row, string Column), decimal>(); int inputs = 0, scale = 0; long inputCharacters = 0;
            foreach (CrossTabRecord record in records)
            {
                c.CheckCancellation();
                if (record == null || string.IsNullOrEmpty(record.Row) || string.IsNullOrEmpty(record.Column)) throw new InvalidDataException("Report.CrossTabKey: Row and column keys must be non-empty strings.");
                if (record.Row.Any(char.IsControl) || record.Column.Any(char.IsControl)) throw new InvalidDataException("Report.CrossTabKey: Row and column keys cannot contain control characters.");
                if (includeTotals && (record.Row == "Total" || record.Column == "Total")) throw new InvalidDataException("Report.CrossTabKey: The Total label is reserved when totals are enabled.");
                CheckText(record.Row, c); CheckText(record.Column, c);
                inputCharacters = checked(inputCharacters + record.Row.Length + record.Column.Length);
                if (++inputs > 100000 || inputCharacters > c.Options.MaximumCharacters || inputCharacters * 2 > c.Options.MaximumExpandedBytes)
                    throw new InvalidDataException("Limit.CrossTabInput: Cross-tab input exceeds the aggregate bound.");
                if (rowKeys.Add(record.Row)) rows.Add(record.Row);
                if (columnKeys.Add(record.Column)) columns.Add(record.Column);
                if (columns.Count > 64 || rows.Count > 10000 || (long)(rows.Count + 1) * (columns.Count + 1 + (includeTotals ? 1 : 0)) > 10000)
                    throw new InvalidDataException("Limit.CrossTabCells: Cross-tab dimensions exceed the cell bound.");
                var key = (record.Row, record.Column); values.TryGetValue(key, out decimal previous);
                if (aggregation == CrossTabAggregation.Sum && record.Value.HasValue) scale = Math.Max(scale, (decimal.GetBits(record.Value.Value)[3] >> 16) & 0xff);
                values[key] = checked(previous + (aggregation == CrossTabAggregation.Count ? 1m : record.Value ?? 0m));
            }
            string numberFormat = aggregation == CrossTabAggregation.Count ? "0" : "F" + scale.ToString(CultureInfo.InvariantCulture);
            var content = new List<List<string>> { new List<string> { "Row" } };
            content[0].AddRange(columns); if (includeTotals) content[0].Add("Total");
            var totals = new decimal[columns.Count]; decimal grand = 0;
            foreach (string row in rows)
            {
                c.CheckCancellation(); var cells = new List<string> { row }; decimal rowTotal = 0;
                for (int index = 0; index < columns.Count; index++)
                {
                    values.TryGetValue((row, columns[index]), out decimal value); cells.Add(value.ToString(numberFormat, CultureInfo.InvariantCulture));
                    if (includeTotals) { rowTotal = checked(rowTotal + value); totals[index] = checked(totals[index] + value); }
                }
                if (includeTotals) { grand = checked(grand + rowTotal); cells.Add(rowTotal.ToString(numberFormat, CultureInfo.InvariantCulture)); }
                content.Add(cells);
            }
            if (includeTotals)
            {
                var total = new List<string> { "Total" }; total.AddRange(totals.Select(v => v.ToString(numberFormat, CultureInfo.InvariantCulture))); total.Add(grand.ToString(numberFormat, CultureInfo.InvariantCulture)); content.Add(total);
            }
            if ((long)content.Count * content[0].Count > 10000) throw new InvalidDataException("Limit.CrossTabCells: Cross-tab output exceeds the cell bound.");
            long characters = content.Sum(row => row.Sum(value => (long)value.Length));
            if (characters > c.Options.MaximumCharacters || characters * 2 + (long)content.Count * content[0].Count * 128 > c.Options.MaximumExpandedBytes)
                throw new InvalidDataException("Limit.CrossTabCells: Cross-tab output exceeds aggregate resource limits.");
            PageSettings? tableSection = placeholder == null ? m.FinalSection ?? m.Blocks.LastOrDefault()?.Section : cellDestination ? ownerTable!.Section : placeholder.Section;
            PageSettings section = tableSection ?? new PageSettings();
            double available = Enumerable.Range(0, section.Columns).Min(section.ColumnWidth);
            if (cellDestination)
            {
                double gridWidth = ownerTable!.ColumnWidths.Sum();
                if (ownerTable.LeftIndent < 0 || gridWidth + ownerTable.LeftIndent > available + 0.01)
                    throw new NotSupportedException("Report.CrossTabCell: The fixed outer table must fit the containing section column.");
                CellPadding padding = ownerCell!.Padding!;
                available = ownerTable.ColumnWidths[ownerColumn] - padding.Left - padding.Right;
                if (available <= 0) throw new NotSupportedException("Report.CrossTabCell: Cell margins leave no usable width.");
            }
            TableBlock table = BuildSimpleTable(content, cellDestination ? null : tableSection, !cellDestination, c);
            if (cellDestination)
                foreach (TableCell cell in table.Rows.SelectMany(row => row)) cell.Padding = new CellPadding { Right = 12, SourceSides = 15 };
            if (placeholder != null) table.Location = placeholder.Location;
            ApplyDefaultTableStyle(m, table);
            PageSettings? destinationSection = table.Section; table.Section = null;
            var preview = new DocumentModel(); preview.Blocks.Add(table); var resolver = new FontResolver(preview, c);
            using var measure = new PdfDocumentBuilder(); var fonts = resolver.Data.Select(bytes => measure.AddTrueTypeFont(bytes)).ToArray(); var page = measure.AddPage(595, 842);
            var maps = resolver.Data.Select(bytes => TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable).ToArray();
            var kerning = resolver.Data.Select(bytes => new FontKerning(bytes)).ToArray();
            var cache = new Dictionary<string, double>(StringComparer.Ordinal);
            double Width(string value)
            {
                if (cache.TryGetValue(value, out double known)) return known;
                int font = resolver.Resolve(new TextRun(value), false); double max = 0;
                foreach (string line in value.Split(CrossTabLineBreaks))
                {
                    var letters = page.MeasureText(line, 11, new PdfPoint(0, 0), fonts[font]);
                    var glyphs = new int[line.Length];
                    for (int index = 0; index < glyphs.Length; index++) if (maps[font] != null) _ = maps[font]!.TryGetGlyphIndex(line[index], out glyphs[index]);
                    double adjustment = kerning[font].Position(glyphs, 11).Sum(glyph => glyph.AdvanceX);
                    if (letters.Count != 0) max = Math.Max(max, letters[letters.Count - 1].EndBaseLine.X + adjustment);
                }
                cache[value] = max; return max;
            }
            var widths = Enumerable.Range(0, content[0].Count).Select(index => 14d + content.Max(row => Width(row[index])) * 1.1d).ToArray();
            if (widths.Sum() > available) throw new NotSupportedException("Report.CrossTabLayout: The cross-tab cannot fit without shrinking or truncation.");
            table.Section = destinationSection; table.Width = widths.Sum(); table.AutoFit = false; table.LayoutExplicit = true;
            table.ColumnWidths.AddRange(widths);
            if (placeholder == null) m.Blocks.Add(table);
            else if (!cellDestination) { m.Blocks[destinationIndex] = table; ValidateRanges(m); }
            else
            {
                if (ownerCell!.Content.Count == 0) ownerCell.Content.AddRange(ownerCell.Paragraphs);
                ownerCell.Content[destinationIndex] = table;
                if (ownerCell.Content[ownerCell.Content.Count - 1] is not ParagraphBlock) ownerCell.Content.Add(new ParagraphBlock());
                ownerCell.Paragraphs.Clear(); ownerCell.Paragraphs.AddRange(DocumentModel.CellParagraphs(ownerCell.Content));
                ValidateRanges(m);
            }
            return 1;
        }, cancellationToken, objectAware: true);
    }

    private static bool AdmittedCrossTabCell(TableBlock table, int row, int column, Action reserveWork)
    {
        if (table.Control != null || table.NestedControl != null || table.TypedControlId.HasValue || table.RowControls.Count != 0
            || table.TypedRowControls.Count != 0 || table.RowRevisions.Count != 0 || table.HeaderRowCount > row || table.NonSplittingRows.Contains(row)
            || table.RowHeights.TryGetValue(row, out var height) && height.Exact || table.AutoFit || table.ManagedImplicitAutoFit || !table.LayoutExplicit
            || table.WidthPercent.HasValue || table.ColumnWidths.Count == 0 || column < 0 || column >= table.ColumnWidths.Count
            || table.Rows[row][column].Padding?.SourceSides != 15 || table.Rows[row][column].Padding!.SourceAmbiguous
            || table.ColumnWidths.Any(width => double.IsNaN(width) || double.IsInfinity(width) || width <= 0)) return false;
        foreach (List<TableCell> cells in table.Rows)
        {
            reserveWork();
            if (cells.Count != table.ColumnWidths.Count) return false;
            for (int index = 0; index < cells.Count; index++)
            {
                reserveWork();
                TableCell cell = cells[index];
                if (cell.TypedControlId.HasValue || cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None || cell.WidthPercent.HasValue
                    || cell.CellWidth.HasValue && Math.Abs(cell.CellWidth.Value - table.ColumnWidths[index]) > 0.01
                    || cell.Blocks.Any(block => block is TableBlock)) return false;
            }
        }
        return true;
    }
}
