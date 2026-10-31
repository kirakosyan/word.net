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
        double gutter = 0;
        settings.EvenAndOdd = evenAndOdd;
        if (finalSection != null)
            foreach (var story in finalSection.Stories) settings.Stories.Add(story.Key, story.Value);
        foreach (XElement property in element.Elements())
        {
            if (property.Name == W + "pgSz")
            {
                settings.Width = Integer(property, W + "w", 1, 31680) / 20.0;
                settings.Height = Integer(property, W + "h", 1, 31680) / 20.0;
                string orientation = (string?)property.Attribute(W + "orient") ?? "portrait";
                if (orientation is not "portrait" and not "landscape") throw new InvalidDataException("Docx.PageOrientation: Invalid orientation.");
                settings.Landscape = orientation == "landscape";
                if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "w" && a.Name != W + "h" && a.Name != W + "orient" && a.Name != W + "code"))
                    context.Loss("Docx.PageLayout", "Unsupported page-size attributes are not retained.");
            }
            else if (property.Name == W + "pgMar")
            {
                if (property.Attribute(W + "header") != null) settings.HeaderDistance = Integer(property, W + "header", 0, 31680) / 20.0;
                if (property.Attribute(W + "footer") != null) settings.FooterDistance = Integer(property, W + "footer", 0, 31680) / 20.0;
                settings.Left = Margin("left"); settings.Right = Margin("right"); settings.Top = Margin("top"); settings.Bottom = Margin("bottom");
                if (property.Attribute(W + "gutter") != null && (gutter = Integer(property, W + "gutter", 0, 31680) / 20.0) != 0)
                    context.Loss("Docx.Gutter", "Binding gutters use additional left-margin space for explicit columns; other binding behavior is not retained.");
                double Margin(string name)
                {
                    int value = Integer(property, W + name, name is "top" or "bottom" ? -31680 : 0, 31680);
                    if (value < 0) context.Loss("Docx.FixedMargin", "Fixed negative vertical margins use their absolute distance; header overlap behavior is not retained.");
                    return System.Math.Abs(value) / 20.0;
                }
            }
            else if (property.Name == W + "type")
            {
                string kind = (string?)property.Attribute(W + "val") ?? "nextPage";
                if (kind is not "nextPage" and not "continuous" and not "oddPage" and not "evenPage")
                    context.Loss("Docx.SectionBreak", "An unsupported section transition becomes a next-page break.");
                else settings.BreakType = kind;
            }
            else if (property.Name == W + "cols")
            {
                bool explicitWidths = (string?)property.Attribute(W + "equalWidth") is "0" or "false" or "off";
                int count = explicitWidths || property.Attribute(W + "num") == null ? 1 : Integer(property, W + "num", 1, 64);
                if (explicitWidths)
                {
                    XElement[] columns = property.Elements(W + "col").ToArray();
                    if (columns.Length == 0) throw new InvalidDataException("Docx.Columns: Missing explicit column definitions.");
                    if (columns.Length > 3 || property.Elements().Count() != columns.Length)
                        context.Loss("Docx.Columns", "Unsupported explicit column shapes use one body column.");
                    else
                    {
                        settings.Columns = columns.Length;
                        foreach (XElement column in columns)
                            settings.ExplicitColumns.Add(new BodyColumn(Integer(column, W + "w", 1, 31680) / 20.0,
                                column.Attribute(W + "space") == null ? 0 : Integer(column, W + "space", 0, 31680) / 20.0));
                    }
                }
                else if (property.HasElements || count > 3)
                    context.Loss("Docx.Columns", "Only one to three equal-width body columns are supported.");
                else settings.Columns = count;
                if (property.Attribute(W + "space") != null) settings.ColumnGap = Integer(property, W + "space", 0, 31680) / 20.0;
            }
            else if (property.Name == W + "pgNumType")
            {
                if (property.Attribute(W + "start") != null) settings.PageNumberStart = Integer(property, W + "start", 0, int.MaxValue - 1);
                string format = (string?)property.Attribute(W + "fmt") ?? "decimal";
                if (format is "decimal" or "lowerRoman" or "upperRoman" or "lowerLetter" or "upperLetter") settings.PageNumberFormat = format;
                else context.Loss("Docx.PageNumberFormat", "An unsupported page-number format uses decimal labels.");
                if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "start" && a.Name != W + "fmt")) context.Loss("Docx.PageNumberFormat", "Chapter-based page-number prefixes are not retained.");
            }
            else if (property.Name == W + "footnotePr" || property.Name == W + "endnotePr")
            {
                var policy = new NotePolicy();
                if (property.Element(W + "numStart") is XElement start) policy.Start = Integer(start, W + "val", 1, int.MaxValue - 1);
                string format = (string?)property.Element(W + "numFmt")?.Attribute(W + "val") ?? "decimal";
                if (format is "decimal" or "upperRoman" or "lowerRoman" or "upperLetter" or "lowerLetter") policy.Format = format;
                else context.Loss("Notes.Numbering", "An unsupported note number format uses decimal labels.");
                string restart = (string?)property.Element(W + "numRestart")?.Attribute(W + "val") ?? "continuous";
                if (restart is not "continuous" and not "eachSect" and not "eachPage") throw new InvalidDataException("Docx.NoteRestart: Invalid note restart policy.");
                policy.Restart = restart;
                if (property.Elements().Any(e => e.Name != W + "numStart" && e.Name != W + "numFmt" && e.Name != W + "numRestart"))
                    context.Loss("Notes.Properties", "Custom note placement and separator properties use the standard page-bottom profile.");
                if (property.Name == W + "footnotePr") settings.Footnotes = policy; else settings.Endnotes = policy;
            }
            else if (property.Name == W + "titlePg") settings.DifferentFirstPage = IsOn(property);
            else if (property.Name == W + "headerReference" || property.Name == W + "footerReference")
            {
                string type = (string?)property.Attribute(W + "type") ?? "default";
                if (type is not "default" and not "first" and not "even") throw new InvalidDataException("Docx.HeaderFooter: Invalid story type.");
                settings.Stories[(property.Name == W + "headerReference" ? "header-" : "footer-") + type] = ReadStory(property);
            }
            else context.Loss("Docx.PageLayout", "Section features such as headers, footers, columns and page numbering are not retained.");
        }
        if (settings.ExplicitColumns.Count != 0) settings.Left += gutter;
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
            if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "before" && a.Name != W + "after" && a.Name != W + "line" && a.Name != W + "lineRule"))
                context.Loss("Docx.ParagraphSpacing", "Automatic and character-unit paragraph spacing is not retained.");
        }
        else if (property.Name == W + "ind")
        {
            if (property.Attribute(W + "left") != null) layout.Left = Integer(property, W + "left", -31680, 31680) / 20.0;
            if (property.Attribute(W + "right") != null) layout.Right = Integer(property, W + "right", -31680, 31680) / 20.0;
            if (property.Attribute(W + "firstLine") != null) layout.FirstLine = Integer(property, W + "firstLine", 0, 31680) / 20.0;
            if (property.Attribute(W + "hanging") != null) layout.FirstLine = -Integer(property, W + "hanging", 0, 31680) / 20.0;
            if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "left" && a.Name != W + "right" && a.Name != W + "firstLine" && a.Name != W + "hanging"))
                context.Loss("Docx.ParagraphIndent", "Character-unit and direction-dependent indentation is not retained.");
        }
        else if (property.Name == W + "tabs")
        {
            foreach (XElement tab in property.Elements(W + "tab"))
            {
                double position = Integer(tab, W + "pos", -31680, 31680) / 20.0;
                string alignment = (string?)tab.Attribute(W + "val") ?? "left";
                layout.Tabs.RemoveAll(t => t.Position == position);
                if (alignment == "clear") continue;
                if (alignment is not "left" and not "center" and not "right" and not "decimal")
                { context.Loss("Layout.TabAlignment", "Unsupported tab alignment uses a left tab stop."); alignment = "left"; }
                string leader = (string?)tab.Attribute(W + "leader") ?? "none";
                if (leader is not "none" and not "dot" and not "hyphen" and not "underscore")
                { context.Loss("Layout.TabLeader", "Unsupported tab leaders are omitted."); leader = "none"; }
                layout.Tabs.Add(new TabStop { Position = position, Alignment = alignment, Leader = leader });
            }
            layout.Tabs.Sort((a, b) => a.Position.CompareTo(b.Position));
        }
        else if (property.Name == W + "pageBreakBefore") layout.PageBreakBefore = IsOn(property);
        else if (property.Name == W + "keepNext") layout.KeepWithNext = IsOn(property);
        else if (property.Name == W + "keepLines") layout.KeepTogether = IsOn(property);
        else return false;
        return true;
    }
}
