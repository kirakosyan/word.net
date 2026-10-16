using System;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private XElement Section(PageSettings settings)
    {
        settings.Validate();
        var section = new XElement(W + "sectPr",
            new XElement(W + "type", new XAttribute(W + "val", settings.BreakType)),
            new XElement(W + "pgSz", new XAttribute(W + "w", Twips(settings.Width)), new XAttribute(W + "h", Twips(settings.Height)),
                settings.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar", new XAttribute(W + "top", Twips(settings.Top)), new XAttribute(W + "right", Twips(settings.Right)),
                new XAttribute(W + "bottom", Twips(settings.Bottom)), new XAttribute(W + "left", Twips(settings.Left)),
                new XAttribute(W + "header", Twips(settings.HeaderDistance)), new XAttribute(W + "footer", Twips(settings.FooterDistance)), new XAttribute(W + "gutter", 0)));
        NoteProperties("endnotePr", settings.Endnotes); NoteProperties("footnotePr", settings.Footnotes);
        if (settings.PageNumberStart.HasValue || settings.PageNumberFormat != "decimal") section.Add(new XElement(W + "pgNumType", new XAttribute(W + "fmt", settings.PageNumberFormat),
            settings.PageNumberStart.HasValue ? new XAttribute(W + "start", settings.PageNumberStart.Value) : null));
        if (settings.Columns != 1) section.Add(new XElement(W + "cols", new XAttribute(W + "num", settings.Columns), new XAttribute(W + "space", Twips(settings.ColumnGap)), new XAttribute(W + "equalWidth", true)));
        if (settings.DifferentFirstPage) section.Add(new XElement(W + "titlePg"));
        section.AddFirst(settings.Stories.OrderBy(p => p.Key.StartsWith("header-", StringComparison.Ordinal) ? 0 : 1).Select(story =>
            new XElement(W + (story.Key.StartsWith("header-", StringComparison.Ordinal) ? "headerReference" : "footerReference"),
                new XAttribute(W + "type", story.Key.Substring(7)), new XAttribute(DocxPackage.DocumentRelationships + "id", storyReferences[story.Value].Id))));
        return section;
        void NoteProperties(string kind, NotePolicy policy)
        {
            if (policy.Format == "decimal" && policy.Start == 1 && policy.Restart == "continuous") return;
            section.AddFirst(new XElement(W + kind, new XElement(W + "numFmt", new XAttribute(W + "val", policy.Format)),
                new XElement(W + "numStart", new XAttribute(W + "val", policy.Start)), new XElement(W + "numRestart", new XAttribute(W + "val", policy.Restart))));
        }
    }

    private static void ParagraphLayoutProperties(XElement properties, ParagraphLayout layout)
    {
        if (layout.KeepWithNext) properties.Add(new XElement(W + "keepNext"));
        if (layout.KeepTogether) properties.Add(new XElement(W + "keepLines"));
        if (layout.PageBreakBefore) properties.Add(new XElement(W + "pageBreakBefore"));
        if (layout.Tabs.Count > 0) properties.Add(new XElement(W + "tabs", layout.Tabs.Select(t => new XElement(W + "tab",
            new XAttribute(W + "val", t.Alignment), new XAttribute(W + "pos", Twips(t.Position)), new XAttribute(W + "leader", t.Leader)))));
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
