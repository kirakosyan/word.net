using System.Collections.Generic;
using System.Linq;

namespace WordNet.Model;

internal sealed class DocumentModel
{
    internal List<DocumentBlock> Blocks { get; } = new List<DocumentBlock>();
    internal int CharacterCount => Blocks.Sum(block => block is ParagraphBlock p ? p.Text.Length : ((TableBlock)block).Rows.Sum(row => row.Sum(cell => cell.Text.Length)));
}

internal abstract class DocumentBlock;

internal enum ParagraphAlignment { Left, Center, Right }

internal sealed class ParagraphBlock : DocumentBlock
{
    internal List<TextRun> Runs { get; } = new List<TextRun>();

    internal int HeadingLevel { get; set; }

    internal bool IsCodeBlock { get; set; }

    internal ParagraphAlignment Alignment { get; set; }
    internal ListItem? List { get; set; }
    // Cells currently contain one paragraph; geometry applies only inside a table.
    internal double? CellWidth { get; set; }
    internal CellPadding? Padding { get; set; }

    internal string Text => string.Concat(Runs.ConvertAll(run => run.Text));
}

internal sealed class TableBlock : DocumentBlock
{
    internal bool HasHeaderRow { get; set; }
    internal double? Width { get; set; }
    internal List<double> ColumnWidths { get; } = new List<double>();

    internal List<List<ParagraphBlock>> Rows { get; } = new List<List<ParagraphBlock>>();
}

internal sealed class TextRun
{
    internal TextRun(string text, bool bold = false, bool italic = false, bool code = false, string? link = null,
        double? fontSize = null, string? color = null, DocumentImage? image = null)
    {
        Text = text;
        Bold = bold;
        Italic = italic;
        Code = code;
        Link = link;
        FontSize = fontSize;
        Color = color;
        Image = image;
    }

    internal string Text { get; }
    internal bool Bold { get; }
    internal bool Italic { get; }
    internal bool Code { get; }
    internal string? Link { get; }
    internal double? FontSize { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal TextRun WithText(string text) => new TextRun(text, Bold, Italic, Code, Link, FontSize, Color, Image);
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
    private readonly Dictionary<string, int[]> counters = new Dictionary<string, int[]>();
    internal string Label(ListItem item)
    {
        if (!counters.TryGetValue(item.Id, out int[]? values)) counters.Add(item.Id, values = new int[9]);
        values[item.Level] = values[item.Level] == 0 ? item.Start : checked(values[item.Level] + 1);
        for (int i = item.Level + 1; i < 9; i++) values[i] = 0;
        return item.Ordered ? values[item.Level].ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : "•";
    }
}
