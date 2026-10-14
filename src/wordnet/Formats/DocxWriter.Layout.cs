using System;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private static XElement Section(PageSettings settings)
    {
        settings.Validate();
        return new XElement(W + "sectPr",
            new XElement(W + "type", new XAttribute(W + "val", "nextPage")),
            new XElement(W + "pgSz", new XAttribute(W + "w", Twips(settings.Width)), new XAttribute(W + "h", Twips(settings.Height)),
                settings.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar", new XAttribute(W + "top", Twips(settings.Top)), new XAttribute(W + "right", Twips(settings.Right)),
                new XAttribute(W + "bottom", Twips(settings.Bottom)), new XAttribute(W + "left", Twips(settings.Left)),
                new XAttribute(W + "header", 0), new XAttribute(W + "footer", 0), new XAttribute(W + "gutter", 0)));
    }

    private static void ParagraphLayoutProperties(XElement properties, ParagraphLayout layout)
    {
        if (layout.KeepWithNext) properties.Add(new XElement(W + "keepNext"));
        if (layout.KeepTogether) properties.Add(new XElement(W + "keepLines"));
        if (layout.PageBreakBefore) properties.Add(new XElement(W + "pageBreakBefore"));
        var spacing = new XElement(W + "spacing");
        if (layout.Before.HasValue) spacing.Add(new XAttribute(W + "before", Twips(layout.Before.Value)));
        if (layout.After.HasValue) spacing.Add(new XAttribute(W + "after", Twips(layout.After.Value)));
        if (layout.LineSpacing.HasValue)
        {
            spacing.Add(new XAttribute(W + "line", Math.Round(layout.LineSpacing.Value * (layout.MultipleLineSpacing ? 240 : 20))),
                new XAttribute(W + "lineRule", layout.MultipleLineSpacing ? "auto" : layout.ExactLineSpacing ? "exact" : "atLeast"));
        }
        if (spacing.HasAttributes) properties.Add(spacing);
        if (layout.Left != 0 || layout.Right != 0 || layout.FirstLine != 0)
            properties.Add(new XElement(W + "ind", new XAttribute(W + "left", Twips(layout.Left)), new XAttribute(W + "right", Twips(layout.Right)),
                new XAttribute(W + (layout.FirstLine < 0 ? "hanging" : "firstLine"), Twips(Math.Abs(layout.FirstLine)))));
    }
}
