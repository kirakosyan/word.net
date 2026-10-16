using System.Collections.Generic;
using System.Linq;

namespace WordNet.Model;

internal sealed class DocumentModel
{
    internal string Title { get; set; } = "";
    internal string Author { get; set; } = "";
    internal List<DocumentBlock> Blocks { get; } = new List<DocumentBlock>();
    internal PageSettings? FinalSection { get; set; }
    internal IEnumerable<ParagraphBlock> Paragraphs => AllBlocks.SelectMany(block => block is ParagraphBlock p ? new[] { p } : ((TableBlock)block).Rows.SelectMany(row => row).SelectMany(cell => cell.Paragraphs));
    internal IEnumerable<DocumentBlock> AllBlocks => Blocks.Concat(Blocks.Select(b => b.Section).Concat(new[] { FinalSection }).Where(s => s != null).Distinct()
        .SelectMany(s => s!.Stories.Values).Distinct().SelectMany(story => story));
    internal int CharacterCount => Paragraphs.Sum(p => p.Text.Length);
}

internal abstract class DocumentBlock
{
    internal SourceLocation? Location { get; set; }
    internal PageSettings? Section { get; set; }
}

internal enum ParagraphAlignment { Left, Center, Right, Justified, Distributed }

internal sealed class ParagraphBlock : DocumentBlock
{
    internal List<TextRun> Runs { get; } = new List<TextRun>();

    internal int HeadingLevel { get; set; }

    internal bool IsCodeBlock { get; set; }

    internal ParagraphAlignment Alignment { get; set; }
    internal ListItem? List { get; set; }
    internal ParagraphLayout Layout { get; set; } = new ParagraphLayout();

    internal string Text => string.Concat(Runs.ConvertAll(run => run.Text));
}

internal sealed class TableBlock : DocumentBlock
{
    internal int HeaderRowCount { get; set; }
    internal bool HasHeaderRow { get => HeaderRowCount > 0; set => HeaderRowCount = value ? System.Math.Max(1, HeaderRowCount) : 0; }
    internal double? Width { get; set; }
    internal double LeftIndent { get; set; }
    internal List<double> ColumnWidths { get; } = new List<double>();
    internal Dictionary<int, double> RowLefts { get; } = new Dictionary<int, double>();

    internal double[] ColumnEdges()
    {
        var edges = new double[ColumnWidths.Count + 1];
        for (int i = 0; i < ColumnWidths.Count; i++) edges[i + 1] = edges[i] + ColumnWidths[i];
        return edges;
    }

    internal List<List<TableCell>> Rows { get; } = new List<List<TableCell>>();
    internal HashSet<int> NonSplittingRows { get; } = new HashSet<int>();
}

internal enum VerticalMerge { None, Start, Continue }

internal sealed class TableCell
{
    internal List<DocumentBlock> Content { get; } = new List<DocumentBlock>();
    internal IEnumerable<DocumentBlock> Blocks => Content.Count > 0 ? Content : Paragraphs;
    internal TableCell() { }
    internal TableCell(ParagraphBlock paragraph) => Paragraphs.Add(paragraph);
    internal List<ParagraphBlock> Paragraphs { get; } = new List<ParagraphBlock>();
    internal double? CellWidth { get; set; }
    internal CellPadding? Padding { get; set; }
    internal int ColumnSpan { get; set; } = 1;
    internal VerticalMerge Merge { get; set; }
    internal string VerticalAlignment { get; set; } = "top";
    internal string? Background { get; set; }
    internal Dictionary<string, CellBorder> Borders { get; } = new Dictionary<string, CellBorder>();
    internal string Text => string.Join("\n", Paragraphs.Select(p => p.Text));
}

internal sealed class CellBorder
{
    internal string Style { get; set; } = "single";
    internal string Color { get; set; } = "000000";
    internal double Width { get; set; } = 0.5;
}

internal sealed class TextRun
{
    internal TextRun(string text, bool bold = false, bool italic = false, bool code = false, string? link = null,
        double? fontSize = null, string? color = null, DocumentImage? image = null, RunAppearance? appearance = null, string? fieldInstruction = null)
    {
        Text = text;
        Bold = bold;
        Italic = italic;
        Code = code;
        Link = link;
        FontSize = fontSize;
        Color = color;
        Image = image;
        Appearance = appearance ?? new RunAppearance();
        FieldInstruction = fieldInstruction;
    }

    internal string Text { get; }
    internal bool Bold { get; }
    internal bool Italic { get; }
    internal bool Code { get; }
    internal string? Link { get; }
    internal double? FontSize { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal RunAppearance Appearance { get; }
    internal string? FieldInstruction { get; }
    internal TextRun WithText(string text) => new TextRun(text, Bold, Italic, Code, Link, FontSize, Color, Image, Appearance, FieldInstruction);
}

internal sealed class RunAppearance
{
    internal string? FontFamily { get; set; }
    internal bool Underline { get; set; }
    internal bool Strike { get; set; }
    internal string? Background { get; set; }
    internal string? VerticalAlignment { get; set; }
    internal bool HasValues => FontFamily != null || Underline || Strike || Background != null || VerticalAlignment != null;
    internal RunAppearance Copy() => (RunAppearance)MemberwiseClone();
}

internal sealed class CellPadding
{
    internal double Top { get; set; }
    internal double Right { get; set; }
    internal double Bottom { get; set; }
    internal double Left { get; set; }
    internal CellPadding Copy() => (CellPadding)MemberwiseClone();
}

internal sealed class ListItem
{
    internal string Id { get; set; } = "";
    internal int Level { get; set; }
    internal int Start { get; set; } = 1;
    internal bool Ordered { get; set; }
    internal ListItem Copy() => (ListItem)MemberwiseClone();
}

internal sealed class ListCounter
{
    private readonly Dictionary<string, int?[]> counters = new Dictionary<string, int?[]>();
    internal string Label(ListItem item)
    {
        if (!counters.TryGetValue(item.Id, out int?[]? values)) counters.Add(item.Id, values = new int?[9]);
        int value = values[item.Level].HasValue ? checked(values[item.Level]!.Value + 1) : item.Start;
        values[item.Level] = value;
        for (int i = item.Level + 1; i < 9; i++) values[i] = null;
        return item.Ordered ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : "•";
    }
}
