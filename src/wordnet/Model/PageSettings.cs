using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet.Model;

internal sealed class PageSettings
{
    internal Dictionary<string, List<DocumentBlock>> Stories { get; private set; } = new Dictionary<string, List<DocumentBlock>>(StringComparer.Ordinal);
    internal bool DifferentFirstPage { get; set; }
    internal bool EvenAndOdd { get; set; }
    internal double HeaderDistance { get; set; } = 36;
    internal double FooterDistance { get; set; } = 36;
    internal double Width { get; set; } = 595.276;
    internal double Height { get; set; } = 841.89;
    internal double Left { get; set; } = 48;
    internal double Right { get; set; } = 48;
    internal double Top { get; set; } = 48;
    internal double Bottom { get; set; } = 48;
    internal bool Landscape { get; set; }
    internal string BreakType { get; set; } = "nextPage";
    internal int Columns { get; set; } = 1;
    internal double ColumnGap { get; set; } = 36;
    internal List<BodyColumn> ExplicitColumns { get; private set; } = new List<BodyColumn>();
    internal int? PageNumberStart { get; set; }
    internal string PageNumberFormat { get; set; } = "decimal";
    internal NotePolicy Footnotes { get; set; } = new NotePolicy();
    internal NotePolicy Endnotes { get; set; } = new NotePolicy();
    internal PageSettings Copy()
    {
        var copy = (PageSettings)MemberwiseClone(); copy.ExplicitColumns = new List<BodyColumn>(ExplicitColumns); return copy;
    }
    internal double ColumnWidth(int index) => ExplicitColumns.Count == 0
        ? (Width - Left - Right - (Columns - 1) * ColumnGap) / Columns : ExplicitColumns[index].Width;
    internal double ColumnLeft(int index)
    {
        double left = Left;
        for (int i = 0; i < index; i++) left += ColumnWidth(i) + (ExplicitColumns.Count == 0 ? ColumnGap : ExplicitColumns[i].Gap);
        return left;
    }
    internal PageSettings EditingCopy()
    {
        var copy = Copy(); copy.Stories = new Dictionary<string, List<DocumentBlock>>(StringComparer.Ordinal); return copy;
    }

    internal void Validate()
    {
        if (Columns < 1 || Columns > 3 || ColumnGap < 0 || double.IsNaN(ColumnGap) || double.IsInfinity(ColumnGap)
            || ExplicitColumns.Count == 0 && (Width - Left - Right - (Columns - 1) * ColumnGap) / Columns <= 0)
            throw new InvalidDataException("Layout.Columns: Columns must leave positive text width.");
        if (!Dimension(Width) || !Dimension(Height) || !Margin(Left) || !Margin(Right)
            || !Margin(Top) || !Margin(Bottom) || Left + Right >= Width || Top + Bottom >= Height)
            throw new InvalidDataException("Layout.PageGeometry: Page dimensions and margins must leave positive content space within the 22-inch profile.");
        if (ExplicitColumns.Count != 0)
        {
            if (ExplicitColumns.Count != Columns) throw new InvalidDataException("Layout.Columns: Explicit column count does not match geometry.");
            double total = 0;
            for (int i = 0; i < ExplicitColumns.Count; i++)
            {
                BodyColumn column = ExplicitColumns[i];
                if (!Dimension(column.Width) || !Margin(column.Gap)) throw new InvalidDataException("Layout.Columns: Invalid explicit width or spacing.");
                total += column.Width + (i + 1 < Columns ? column.Gap : 0);
            }
            if (System.Math.Abs(total - (Width - Left - Right)) > 0.1)
                throw new InvalidDataException("Layout.Columns: Explicit columns must fill the body area within two twips.");
        }
    }

    private static bool Dimension(double value) => value > 0 && value <= 1584 && !double.IsNaN(value);
    private static bool Margin(double value) => value >= 0 && value <= 1584 && !double.IsNaN(value);
}

internal sealed class BodyColumn
{
    internal BodyColumn(double width, double gap) { Width = width; Gap = gap; }
    internal double Width { get; }
    internal double Gap { get; }
}

internal sealed class ParagraphLayout
{
    internal double? Before { get; set; }
    internal double? After { get; set; }
    internal double Left { get; set; }
    internal double Right { get; set; }
    internal double FirstLine { get; set; }
    internal double? LineSpacing { get; set; }
    internal bool MultipleLineSpacing { get; set; } = true;
    internal bool ExactLineSpacing { get; set; }
    internal bool PageBreakBefore { get; set; }
    internal bool KeepTogether { get; set; }
    internal bool KeepWithNext { get; set; }
    internal List<TabStop> Tabs { get; private set; } = new List<TabStop>();
    internal bool HasValues => Before.HasValue || After.HasValue || Left != 0 || Right != 0 || FirstLine != 0
        || LineSpacing.HasValue || PageBreakBefore || KeepTogether || KeepWithNext;
    internal ParagraphLayout Copy()
    {
        var copy = (ParagraphLayout)MemberwiseClone(); copy.Tabs = new List<TabStop>(Tabs); return copy;
    }
}

internal sealed class TabStop
{
    internal double Position { get; set; }
    internal string Alignment { get; set; } = "left";
    internal string Leader { get; set; } = "none";
}

internal sealed class NotePolicy
{
    internal string Format { get; set; } = "decimal";
    internal int Start { get; set; } = 1;
    internal string Restart { get; set; } = "continuous";
}
