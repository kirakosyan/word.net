using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    private static void InferTableGrid(TableBlock table)
    {
        if (table.Rows.SelectMany(r => r).Any(c => !c.CellWidth.HasValue || c.CellWidth.Value <= 0)) return;
        var edges = new SortedSet<int> { 0 };
        foreach (List<TableCell> row in table.Rows)
        {
            int position = 0;
            foreach (TableCell cell in row) { position += checked((int)Math.Round(cell.CellWidth!.Value * 20)); edges.Add(position); }
        }
        int[] grid = edges.ToArray();
        table.ColumnWidths.Clear();
        for (int i = 1; i < grid.Length; i++) table.ColumnWidths.Add((grid[i] - grid[i - 1]) / 20.0);
        foreach (List<TableCell> row in table.Rows)
        {
            int position = 0, start = 0;
            foreach (TableCell cell in row)
            {
                position += checked((int)Math.Round(cell.CellWidth!.Value * 20));
                int end = Array.BinarySearch(grid, position); cell.ColumnSpan = end - start; start = end;
            }
        }
    }

    private static List<(int End, PageSettings Settings)> ReadSections(byte[] word, byte[] table, string text, ConversionContext context)
    {
        int offset = I32(word, 154 + 6 * 8), length = I32(word, 158 + 6 * 8);
        var result = new List<(int, PageSettings)>();
        if (length == 0) return result;
        Bounds(table, offset, length);
        if (length < 20 || (length - 4) % 16 != 0) throw new InvalidDataException("Doc.Sections: Invalid section table length.");
        int count = (length - 4) / 16;
        if (count > context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: Too many sections.");
        if (I32(table, offset) != 0) throw new InvalidDataException("Doc.Sections: First section must start at zero.");
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation();
            int start = I32(table, offset + i * 4), end = I32(table, offset + (i + 1) * 4);
            if (start < 0 || end <= start || (i < count - 1 && (end > text.Length || text[end - 1] != '\f')) || (i == count - 1 && end < text.Length))
                throw new InvalidDataException("Doc.Sections: Invalid section character range.");
            int sepx = I32(table, offset + (count + 1) * 4 + i * 12 + 2);
            // MS-DOC defaults for LCID 1033. Explicit properties override these values.
            var settings = new PageSettings { Width = 612, Height = 792, Left = 90, Right = 90, Top = 72, Bottom = 72 };
            if (sepx != -1)
            {
                int size = U16(word, sepx);
                foreach (Prl property in ParseProperties(Slice(word, checked(sepx + 2), size)))
                {
                    byte[] operand = property.Operand;
                    switch (property.Code)
                    {
                        case 0xb01f: settings.Width = U16(operand, 0) / 20.0; break;
                        case 0xb020: settings.Height = U16(operand, 0) / 20.0; break;
                        case 0xb021: settings.Left = U16(operand, 0) / 20.0; break;
                        case 0xb022: settings.Right = U16(operand, 0) / 20.0; break;
                        case 0x9023: settings.Top = unchecked((short)U16(operand, 0)) / 20.0; break;
                        case 0x9024: settings.Bottom = unchecked((short)U16(operand, 0)) / 20.0; break;
                        case 0x301d:
                            if (operand[0] > 1) throw new InvalidDataException("Doc.PageOrientation: Invalid orientation.");
                            settings.Landscape = operand[0] == 1; break;
                        case 0x3009:
                            if (operand[0] != 2) context.Loss("Doc.SectionBreak", "Section breaks other than next-page become next-page breaks.");
                            break;
                        case 0xb017: case 0xb018: case 0x900c: case 0x703a: break;
                        case 0x500b:
                        case 0xb025:
                            if (U16(operand, 0) != 0) context.Loss("Doc.SectionLayout", "Multiple columns and binding gutters are not retained.");
                            break;
                        default: context.Loss("Doc.SectionLayout", "Additional section properties are not retained."); break;
                    }
                }
            }
            settings.Validate(); result.Add((end, settings));
        }
        return result;
    }

    private static byte[] SectionProperties(PageSettings settings)
    {
        settings.Validate();
        return Join(Sprm(0x3009, 2), Sprm(0x301d, settings.Landscape ? 1 : 0),
            Dimension(0xb01f, settings.Width), Dimension(0xb020, settings.Height),
            Dimension(0xb021, settings.Left), Dimension(0xb022, settings.Right),
            Dimension(0x9023, settings.Top), Dimension(0x9024, settings.Bottom));
        static byte[] Dimension(int code, double value) => SprmValue(code, checked((int)Math.Round(value * 20)), 2);
    }

    private static byte[] ParagraphProperties(ParagraphLayout layout)
    {
        var result = new List<byte>();
        result.AddRange(Sprm(0x2405, layout.KeepTogether ? 1 : 0));
        result.AddRange(Sprm(0x2406, layout.KeepWithNext ? 1 : 0));
        result.AddRange(Sprm(0x2407, layout.PageBreakBefore ? 1 : 0));
        Add(0xa413, layout.Before); Add(0xa414, layout.After);
        Add(0x840f, layout.Left); Add(0x840e, layout.Right); Add(0x8411, layout.FirstLine);
        if (layout.LineSpacing.HasValue)
        {
            int value = checked((int)Math.Round(layout.LineSpacing.Value * (layout.MultipleLineSpacing ? 240 : 20)));
            if (layout.ExactLineSpacing) value = -value;
            byte[] operand = new byte[6]; W16(operand, 0, 0x6412); W16(operand, 2, value); W16(operand, 4, layout.MultipleLineSpacing ? 1 : 0);
            result.AddRange(operand);
        }
        return result.ToArray();
        void Add(int code, double? value)
        {
            if (value.HasValue) result.AddRange(SprmValue(code, checked((int)Math.Round(value.Value * 20)), 2));
        }
    }
}
