using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private int sectionStart;
    private int sectionCount;
    private PageSettings? finalSection;

    private void EndSection(XElement element, List<DocumentBlock> blocks, bool paragraphBoundary = false)
    {
        var settings = new PageSettings();
        foreach (XElement property in element.Elements())
        {
            if (property.Name == W + "pgSz")
            {
                settings.Width = Integer(property, W + "w", 1, 31680) / 20.0;
                settings.Height = Integer(property, W + "h", 1, 31680) / 20.0;
                string orientation = (string?)property.Attribute(W + "orient") ?? "portrait";
                if (orientation is not "portrait" and not "landscape") throw new InvalidDataException("Docx.PageOrientation: Invalid orientation.");
                settings.Landscape = orientation == "landscape";
                if (property.Attributes().Any(a => a.Name != W + "w" && a.Name != W + "h" && a.Name != W + "orient" && a.Name != W + "code"))
                    context.Loss("Docx.PageLayout", "Unsupported page-size attributes are not retained.");
            }
            else if (property.Name == W + "pgMar")
            {
                settings.Left = Margin("left"); settings.Right = Margin("right"); settings.Top = Margin("top"); settings.Bottom = Margin("bottom");
                if (property.Attribute(W + "gutter") != null && Integer(property, W + "gutter", 0, 31680) != 0)
                    context.Loss("Docx.Gutter", "Binding gutters are not retained.");
                double Margin(string name) => Integer(property, W + name, 0, 31680) / 20.0;
            }
            else if (property.Name == W + "type")
            {
                string kind = (string?)property.Attribute(W + "val") ?? "nextPage";
                if (kind != "nextPage") context.Loss("Docx.SectionBreak", "Continuous, column and parity section breaks become next-page section breaks.");
            }
            else if (property.Name == W + "cols" && !property.HasElements && ((string?)property.Attribute(W + "num") is null or "1")) { }
            else context.Loss("Docx.PageLayout", "Section features such as headers, footers, columns and page numbering are not retained.");
        }
        settings.Validate();
        if (!paragraphBoundary && !element.HasElements && sectionCount == 0) return;
        if (++sectionCount > context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: Too many sections.");
        for (int i = sectionStart; i < blocks.Count; i++) blocks[i].Section = settings;
        sectionStart = blocks.Count;
        finalSection = settings;
    }

    private bool ReadLayoutProperty(XElement property, ParagraphLayout layout)
    {
        if (property.Name == W + "spacing")
        {
            if (property.Attribute(W + "before") != null) layout.Before = Integer(property, W + "before", 0, 31680) / 20.0;
            if (property.Attribute(W + "after") != null) layout.After = Integer(property, W + "after", 0, 31680) / 20.0;
            if (property.Attribute(W + "line") != null || property.Attribute(W + "lineRule") != null)
            {
                // The numeric amount and its units inherit independently through styles.
                double? amount = layout.LineSpacing * (layout.MultipleLineSpacing ? 240.0 : 20.0);
                if (property.Attribute(W + "line") != null) amount = Integer(property, W + "line", 1, 31680);
                string rule = (string?)property.Attribute(W + "lineRule")
                    ?? (layout.MultipleLineSpacing ? "auto" : layout.ExactLineSpacing ? "exact" : "atLeast");
                if (rule is not "auto" and not "exact" and not "atLeast") throw new InvalidDataException("Docx.LineSpacing: Invalid rule.");
                layout.MultipleLineSpacing = rule == "auto";
                layout.ExactLineSpacing = rule == "exact";
                layout.LineSpacing = amount / (layout.MultipleLineSpacing ? 240.0 : 20.0);
            }
            if (property.Attributes().Any(a => a.Name != W + "before" && a.Name != W + "after" && a.Name != W + "line" && a.Name != W + "lineRule"))
                context.Loss("Docx.ParagraphSpacing", "Automatic and character-unit paragraph spacing is not retained.");
        }
        else if (property.Name == W + "ind")
        {
            if (property.Attribute(W + "left") != null) layout.Left = Integer(property, W + "left", -31680, 31680) / 20.0;
            if (property.Attribute(W + "right") != null) layout.Right = Integer(property, W + "right", -31680, 31680) / 20.0;
            if (property.Attribute(W + "firstLine") != null) layout.FirstLine = Integer(property, W + "firstLine", 0, 31680) / 20.0;
            if (property.Attribute(W + "hanging") != null) layout.FirstLine = -Integer(property, W + "hanging", 0, 31680) / 20.0;
            if (property.Attributes().Any(a => a.Name != W + "left" && a.Name != W + "right" && a.Name != W + "firstLine" && a.Name != W + "hanging"))
                context.Loss("Docx.ParagraphIndent", "Character-unit and direction-dependent indentation is not retained.");
        }
        else if (property.Name == W + "pageBreakBefore") layout.PageBreakBefore = IsOn(property);
        else if (property.Name == W + "keepNext") layout.KeepWithNext = IsOn(property);
        else if (property.Name == W + "keepLines") layout.KeepTogether = IsOn(property);
        else return false;
        return true;
    }
}
