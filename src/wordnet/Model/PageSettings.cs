using System;
using System.IO;

namespace WordNet.Model;

internal sealed class PageSettings
{
    internal double Width { get; set; } = 595.276;
    internal double Height { get; set; } = 841.89;
    internal double Left { get; set; } = 48;
    internal double Right { get; set; } = 48;
    internal double Top { get; set; } = 48;
    internal double Bottom { get; set; } = 48;
    internal bool Landscape { get; set; }
    internal PageSettings Copy() => (PageSettings)MemberwiseClone();

    internal void Validate()
    {
        if (!Dimension(Width) || !Dimension(Height) || !Margin(Left) || !Margin(Right)
            || !Margin(Top) || !Margin(Bottom) || Left + Right >= Width || Top + Bottom >= Height)
            throw new InvalidDataException("Layout.PageGeometry: Page dimensions and margins must leave positive content space within the 22-inch profile.");
    }

    private static bool Dimension(double value) => value > 0 && value <= 1584 && !double.IsNaN(value);
    private static bool Margin(double value) => value >= 0 && value <= 1584 && !double.IsNaN(value);
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
    internal bool HasValues => Before.HasValue || After.HasValue || Left != 0 || Right != 0 || FirstLine != 0
        || LineSpacing.HasValue || PageBreakBefore || KeepTogether || KeepWithNext;
    internal ParagraphLayout Copy() => (ParagraphLayout)MemberwiseClone();
}
