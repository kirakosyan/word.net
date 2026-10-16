using System;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal static class TableSizing
{
    internal static double[] Columns(TableBlock table, double available, Func<TableCell, (double Minimum, double Preferred)> measure, ConversionContext context)
    {
        int count = table.Rows.Max(r => r.Sum(c => c.ColumnSpan));
        bool relative = table.WidthPercent.HasValue || table.Rows.SelectMany(r => r).Any(c => c.WidthPercent.HasValue);
        double total = table.WidthPercent.HasValue ? available * table.WidthPercent.Value / 100
            : table.Width ?? (table.ColumnWidths.Count > 0 ? table.ColumnWidths.Sum() : available);
        var widths = Enumerable.Range(0, count).Select(c => c < table.ColumnWidths.Count ? table.ColumnWidths[c] : total / count).ToArray();
        if (!table.AutoFit && !relative) return widths;
        if (table.AutoFit && !table.Width.HasValue && !table.WidthPercent.HasValue) total = available;
        total = Math.Min(available, total);
        var minima = Enumerable.Repeat(1.0, count).ToArray();
        var preferred = new double[count];
        foreach (var row in table.Rows)
        {
            int column = 0;
            foreach (TableCell cell in row)
            {
                context.CheckCancellation();
                var content = table.AutoFit ? measure(cell) : (Minimum: 1.0, Preferred: 1.0);
                double requested = cell.WidthPercent.HasValue ? total * cell.WidthPercent.Value / 100 : cell.CellWidth ?? content.Preferred;
                for (int c = column; c < column + cell.ColumnSpan; c++)
                {
                    minima[c] = Math.Max(minima[c], content.Minimum / cell.ColumnSpan);
                    preferred[c] = Math.Max(preferred[c], requested / cell.ColumnSpan);
                }
                column += cell.ColumnSpan;
            }
        }
        for (int c = 0; c < count; c++) preferred[c] = Math.Max(minima[c], preferred[c] == 0 ? widths[c] : preferred[c]);
        double minimum = minima.Sum(), desired = preferred.Sum();
        // Long words may wrap at glyph boundaries, as in the existing paragraph engine.
        if (minimum > total) return minima.Select(w => w * total / minimum).ToArray();
        double fraction = desired > minimum ? Math.Min(1, (total - minimum) / (desired - minimum)) : 0;
        for (int c = 0; c < count; c++) widths[c] = minima[c] + (preferred[c] - minima[c]) * fraction;
        double remaining = total - widths.Sum();
        for (int c = 0; c < count; c++) widths[c] += remaining / count;
        return widths;
    }
}
