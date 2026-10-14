using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class TableValidation
{
    internal static void Validate(TableBlock table, ConversionContext context)
    {
        if (table.Rows.Count == 0 || table.HeaderRowCount < 0 || table.HeaderRowCount > table.Rows.Count)
            throw new InvalidDataException("Table.Rows: Invalid row or repeating-header count.");
        int gridColumns = table.ColumnWidths.Count;
        foreach (List<TableCell> row in table.Rows)
        {
            int columns = 0;
            foreach (TableCell cell in row)
            {
                if (cell.ColumnSpan < 1 || cell.ColumnSpan > 1024 - columns)
                    throw new InvalidDataException("Limit.TableCells: Invalid cell span or too many columns.");
                columns += cell.ColumnSpan;
            }
            gridColumns = Math.Max(gridColumns, columns);
        }
        if (gridColumns > 1024) throw new InvalidDataException("Limit.TableCells: Too many grid columns.");
        if (table.ColumnWidths.Count > 0)
        {
            while (table.ColumnWidths.Count < gridColumns) table.ColumnWidths.Add(0);
            int unknown = table.ColumnWidths.Count(w => w == 0);
            if (unknown > 0)
            {
                if (unknown == gridColumns && !table.Width.HasValue)
                {
                    context.Report("Table.AutomaticGrid", "Automatic table columns use equal widths when fixed geometry is required.");
                    table.ColumnWidths.Clear();
                }
                else
                {
                    context.Loss("Table.GridWidth", "Unspecified grid widths are approximated using the available table width.");
                    double available = table.Width ?? (table.Section == null ? 450 : table.Section.Width - table.Section.Left - table.Section.Right);
                    double remaining = available - table.ColumnWidths.Sum();
                    double width = remaining > 0 ? remaining / unknown : available / gridColumns;
                    for (int i = 0; i < gridColumns; i++) if (table.ColumnWidths[i] == 0) table.ColumnWidths[i] = width;
                }
            }
        }
        var previous = new Dictionary<int, TableCell>();
        int cells = 0;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            context.CheckCancellation();
            if (table.Rows[r].Count == 0) throw new InvalidDataException("Table.Cells: A table row must contain a cell.");
            int missing = gridColumns - table.Rows[r].Sum(cell => cell.ColumnSpan);
            if (missing > 0)
            {
                context.Loss("Table.Grid", "Omitted trailing grid columns are represented by empty cells.");
                table.Rows[r].Add(new TableCell(new ParagraphBlock()) { ColumnSpan = missing, Padding = new CellPadding() });
            }
            var current = new Dictionary<int, TableCell>();
            int column = 0;
            foreach (TableCell cell in table.Rows[r])
            {
                if (++cells > 100000 || column + cell.ColumnSpan > 1024) throw new InvalidDataException("Limit.TableCells: Table exceeds the cell or column limit.");
                if (cell.ColumnSpan < 1 || cell.Paragraphs.Count == 0) throw new InvalidDataException("Table.Cell: Invalid span or empty paragraph collection.");
                if (cell.Merge == VerticalMerge.Continue)
                {
                    if (!previous.TryGetValue(column, out TableCell? above) || above.Merge == VerticalMerge.None || above.ColumnSpan != cell.ColumnSpan)
                        throw new InvalidDataException("Table.Merge: Vertical continuation has no matching cell above.");
                    if (cell.Paragraphs.Any(p => p.Runs.Any(run => run.Image != null || run.Text.Length != 0)))
                        throw new NotSupportedException("Table.MergeContent: Content in a vertical continuation cell cannot be discarded.");
                    if (r == table.HeaderRowCount) throw new NotSupportedException("Table.HeaderMerge: A merge cannot span repeating headers and body rows.");
                }
                current.Add(column, cell); column += cell.ColumnSpan;
            }
            previous = current;
        }
    }
}
