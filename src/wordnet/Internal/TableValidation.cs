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
        var previous = new Dictionary<int, TableCell>();
        int cells = 0;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            context.CheckCancellation();
            if (table.Rows[r].Count == 0) throw new InvalidDataException("Table.Cells: A table row must contain a cell.");
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
            if (table.ColumnWidths.Count > 0 && column != table.ColumnWidths.Count)
                throw new NotSupportedException("Table.Grid: Rows with omitted grid columns or inconsistent grid spans are not supported.");
            previous = current;
        }
    }
}
