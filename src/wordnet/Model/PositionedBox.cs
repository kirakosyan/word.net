using System.Collections.Generic;

namespace WordNet.Model;

internal sealed class DrawingAnchor
{
    internal string HorizontalOrigin { get; set; } = "margin";
    internal string VerticalOrigin { get; set; } = "paragraph";
    internal string? HorizontalAlignment { get; set; }
    internal string? VerticalAlignment { get; set; }
    internal double X { get; set; }
    internal double Y { get; set; }
    internal string Wrap { get; set; } = "none";
    internal bool Behind { get; set; }
    internal CellPadding Distance { get; set; } = new CellPadding();
}

internal sealed class PositionedBox
{
    internal DrawingAnchor Anchor { get; set; } = new DrawingAnchor();
    internal double Width { get; set; }
    internal double Height { get; set; }
    internal string? Fill { get; set; }
    internal CellBorder? Border { get; set; }
    internal CellPadding Padding { get; set; } = new CellPadding { Left = 7.2, Right = 7.2, Top = 3.6, Bottom = 3.6 };
    internal List<DocumentBlock> Blocks { get; } = new List<DocumentBlock>();
}
