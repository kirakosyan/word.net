using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;
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
    /// <summary>Appends a bounded editable cross-tab in first-seen key order, with optional totals.</summary>
    /// <remarks>Null or empty keys are rejected. Missing intersections and empty sums are zero.
    /// Counts include null values; sums exclude them. Empty input produces a header-only table.
    /// The table is rejected when its conservative natural widths cannot fit the current body column.</remarks>
    public void AddCrossTab(IEnumerable<CrossTabRecord> records, CrossTabAggregation aggregation = CrossTabAggregation.Sum,
        bool includeTotals = false, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(records, nameof(records));
        if (aggregation is not CrossTabAggregation.Sum and not CrossTabAggregation.Count) throw new ArgumentOutOfRangeException(nameof(aggregation));
        _ = Edit((m, c) =>
        {
            var rows = new List<string>(); var columns = new List<string>();
            var rowKeys = new HashSet<string>(StringComparer.Ordinal); var columnKeys = new HashSet<string>(StringComparer.Ordinal);
            var values = new Dictionary<(string Row, string Column), decimal>(); int inputs = 0; long inputCharacters = 0;
            foreach (CrossTabRecord record in records)
            {
                c.CheckCancellation();
                if (record == null || string.IsNullOrEmpty(record.Row) || string.IsNullOrEmpty(record.Column)) throw new InvalidDataException("Report.CrossTabKey: Row and column keys must be non-empty strings.");
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
                values[key] = checked(previous + (aggregation == CrossTabAggregation.Count ? 1m : record.Value ?? 0m));
            }
            var content = new List<List<string>> { new List<string> { "Row" } };
            content[0].AddRange(columns); if (includeTotals) content[0].Add("Total");
            var totals = new decimal[columns.Count]; decimal grand = 0;
            foreach (string row in rows)
            {
                c.CheckCancellation(); var cells = new List<string> { row }; decimal rowTotal = 0;
                for (int index = 0; index < columns.Count; index++)
                {
                    values.TryGetValue((row, columns[index]), out decimal value); cells.Add(value.ToString(CultureInfo.InvariantCulture));
                    if (includeTotals) { rowTotal = checked(rowTotal + value); totals[index] = checked(totals[index] + value); }
                }
                if (includeTotals) { grand = checked(grand + rowTotal); cells.Add(rowTotal.ToString(CultureInfo.InvariantCulture)); }
                content.Add(cells);
            }
            if (includeTotals)
            {
                var total = new List<string> { "Total" }; total.AddRange(totals.Select(v => v.ToString(CultureInfo.InvariantCulture))); total.Add(grand.ToString(CultureInfo.InvariantCulture)); content.Add(total);
            }
            if ((long)content.Count * content[0].Count > 10000) throw new InvalidDataException("Limit.CrossTabCells: Cross-tab output exceeds the cell bound.");
            long characters = content.Sum(row => row.Sum(value => (long)value.Length));
            if (characters > c.Options.MaximumCharacters || characters * 2 + (long)content.Count * content[0].Count * 128 > c.Options.MaximumExpandedBytes)
                throw new InvalidDataException("Limit.CrossTabCells: Cross-tab output exceeds aggregate resource limits.");
            PageSettings section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section ?? new PageSettings();
            var widths = Enumerable.Range(0, content[0].Count).Select(index => 12d + content.Max(row => row[index].Length) * 22d).ToArray();
            if (widths.Sum() > Enumerable.Range(0, section.Columns).Min(section.ColumnWidth)) throw new NotSupportedException("Report.CrossTabLayout: The cross-tab cannot fit without shrinking or truncation.");
            var table = new TableBlock { Section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section, HasHeaderRow = true, Width = widths.Sum(), AutoFit = false, LayoutExplicit = true };
            table.ColumnWidths.AddRange(widths);
            foreach (List<string> row in content)
            {
                var output = new List<TableCell>();
                foreach (string value in row) { var paragraph = new ParagraphBlock(); paragraph.Runs.Add(new TextRun(value)); output.Add(new TableCell(paragraph)); }
                table.Rows.Add(output);
            }
            m.Blocks.Add(table); return 1;
        }, cancellationToken, objectAware: true);
    }
}
