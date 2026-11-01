using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WordNet.Model;

namespace WordNet.Internal;

// This intentionally admits a small rectangular, unmerged numeric table profile.
// Every formula in an admitted table is staged together, so an invalid dependency
// or arithmetic error cannot leave another formula with a newly refreshed cache.
internal static class TableFields
{
    private const int MaximumRows = 256;
    private const int MaximumColumns = 26;
    private const int MaximumFormulas = 256;
    private const int MaximumReferences = 8192;

    internal static void Refresh(DocumentModel model, ConversionContext context)
    {
        foreach (TableBlock table in model.Tables)
        {
            context.CheckCancellation();
            var formulas = table.Rows.SelectMany(row => row).SelectMany(cell => cell.Blocks.OfType<ParagraphBlock>())
                .SelectMany(paragraph => paragraph.Runs).Where(run => run.FieldInstruction != null && DocumentFields.Kind(run.FieldInstruction) == "=")
                .Distinct().ToArray();
            if (!formulas.Any(run => HasTableReference(run.FieldInstruction!))) continue;
            using var location = context.At(table.Location);
            try
            {
                if (formulas.Length > MaximumFormulas || table.Rows.Count == 0 || table.Rows.Count > MaximumRows
                    || table.RowControls.Count != 0 || table.RowRevisions.Count != 0 || table.Control != null
                    || table.NestedControl != null || table.TypedControlId.HasValue) throw new FormatException();
                int columns = table.Rows[0].Count;
                if (columns == 0 || columns > MaximumColumns || table.Rows.Any(row => row.Count != columns
                    || row.Any(cell => cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None))) throw new FormatException();
                if (table.Rows.SelectMany(row => row).Distinct().Count() != table.Rows.Count * columns) throw new FormatException();
                var formulaSet = new HashSet<TextRun>(formulas);
                var staged = new Dictionary<TextRun, string>();
                var values = new Dictionary<TableCell, decimal>();
                var visiting = new HashSet<TableCell>();
                int work = 0;
                for (int row = 0; row < table.Rows.Count; row++)
                    for (int column = 0; column < columns; column++)
                    {
                        context.CheckCancellation();
                        TableCell cell = table.Rows[row][column];
                        if (cell.Blocks.OfType<ParagraphBlock>().SelectMany(p => p.Runs).Any(formulaSet.Contains))
                            _ = Value(row, column);
                    }
                if (staged.Count != formulas.Length) throw new FormatException();
                foreach (var result in staged) model.FieldResults[result.Key] = result.Value;

                decimal Value(int row, int column)
                {
                    context.CheckCancellation();
                    context.ReserveFieldWork(1);
                    if (++work > MaximumReferences || row < 0 || row >= table.Rows.Count || column < 0 || column >= columns) throw new FormatException();
                    TableCell cell = table.Rows[row][column];
                    if (values.TryGetValue(cell, out decimal known)) return known;
                    if (visiting.Count >= Math.Min(64, context.Options.MaximumNestingDepth) || !visiting.Add(cell)) throw new FormatException();
                    DocumentBlock[] blocks = cell.Blocks.ToArray();
                    if (blocks.Length != 1 || blocks[0] is not ParagraphBlock paragraph || paragraph.Control != null
                        || paragraph.NestedControl != null || paragraph.TypedControlId.HasValue || paragraph.StructuralRevision != null) throw new FormatException();
                    TextRun[] runs = paragraph.Runs.ToArray();
                    if (runs.Any(r => r.Image != null || r.Object != null || r.Box != null || r.Note != null || r.Control != null
                        || r.TypedControlId.HasValue || r.Revision != null)) throw new FormatException();
                    TextRun[] fields = runs.Where(r => r.FieldInstruction != null).ToArray();
                    decimal value;
                    if (fields.Length == 0)
                    {
                        string text = string.Concat(runs.Select(r => r.Text)).Trim();
                        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                            throw new FormatException();
                    }
                    else
                    {
                        if (fields.Length != 1 || DocumentFields.Kind(fields[0].FieldInstruction!) != "="
                            || runs.Any(r => r != fields[0] && !string.IsNullOrWhiteSpace(r.Text))) throw new FormatException();
                        (value, string display) = ScalarFields.EvaluateTable(fields[0], model, context, Resolve);
                        staged.Add(fields[0], display);
                    }
                    visiting.Remove(cell); values.Add(cell, value);
                    return value;

                    decimal Resolve(string operation, string argument)
                    {
                        if (operation == "CELL")
                        {
                            (int r, int c) = Address(argument);
                            return Value(r, c);
                        }
                        int firstRow, lastRow, firstColumn, lastColumn;
                        if (argument.Equals("ABOVE", StringComparison.OrdinalIgnoreCase))
                        { firstRow = 0; lastRow = row - 1; firstColumn = lastColumn = column; }
                        else if (argument.Equals("LEFT", StringComparison.OrdinalIgnoreCase))
                        { firstRow = lastRow = row; firstColumn = 0; lastColumn = column - 1; }
                        else
                        {
                            string[] limits = argument.Split(':');
                            if (limits.Length != 2) throw new FormatException();
                            (firstRow, firstColumn) = Address(limits[0]); (lastRow, lastColumn) = Address(limits[1]);
                            if (firstRow > lastRow || firstColumn > lastColumn) throw new FormatException();
                        }
                        decimal sum = 0;
                        for (int r = firstRow; r <= lastRow; r++)
                            for (int c = firstColumn; c <= lastColumn; c++) sum = checked(sum + Value(r, c));
                        return sum;
                    }
                }
            }
            catch (Exception error) when (error is FormatException or OverflowException or DivideByZeroException)
            {
                context.Report("Field.UnsupportedUsedCachedResult", "A table formula has an unsupported cell, dependency, range or arithmetic result; the table retains all source formula caches.");
                foreach (TextRun formula in formulas) model.FieldResults[formula] = formula.Text;
            }
        }
    }

    private static (int Row, int Column) Address(string address)
    {
        if (address.Length < 2 || address.Length > 4) throw new FormatException();
        char letter = char.ToUpperInvariant(address[0]);
        int row = 0;
        for (int i = 1; i < address.Length; i++)
        {
            if (address[i] < '0' || address[i] > '9') throw new FormatException();
            row = row * 10 + address[i] - '0';
        }
        if (letter < 'A' || letter > 'Z' || row < 1 || row > MaximumRows || address[1] == '0') throw new FormatException();
        return (row - 1, letter - 'A');
    }

    // Any named expression before a formatting switch can be a table reference
    // or unsupported function. Stage the entire table even when that name is
    // invalid, so an unrelated arithmetic formula cannot be refreshed alone.
    private static bool HasTableReference(string instruction) => instruction.Length > 4096 || Regex.IsMatch(instruction,
        @"^[^\\]*\p{L}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
