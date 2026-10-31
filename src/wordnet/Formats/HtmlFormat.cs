using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed class HtmlFormat
{
    private readonly ConversionContext context;
    private readonly HtmlOptions options;
    private readonly ListCounter counter = new ListCounter();
    private readonly DocumentModel model;

    private HtmlFormat(DocumentModel model, ConversionContext context)
    {
        this.model = model;
        this.context = context;
        options = (context.Options.Html ?? throw new ArgumentException("HTML options must not be null.")).Snapshot();
        context.PageCount = null;
    }

    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        var writer = new HtmlFormat(model, context);
        if (DocumentReferences.HasFields(model)) _ = DocumentReferences.Refresh(model, context, paginate: false);
        var body = new XElement("body", new XAttribute("style", "margin:0;padding:0;"));
        var content = new XElement("div", new XAttribute("style", "max-width:" + writer.options.MaxContentWidthPx.ToString(CultureInfo.InvariantCulture)
            + "px;margin:0 auto;font-family:'Noto Sans',Arial,sans-serif;font-size:12pt;"));
        if (writer.options.IncludeHeadersAndFooters) writer.Stories(content, model, "header-");
        else if (model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Any(s => s?.Stories.Count > 0)) context.Report("Html.HeadersFootersOmitted", "Headers and footers are omitted by the HTML output policy.");
        writer.Blocks(content, model.Blocks);
        if (model.Notes.Count > 0)
        {
            var notes = new XElement("section", new XAttribute("role", "doc-endnotes"));
            foreach (DocumentNote note in model.Notes)
            {
                var entry = new XElement("div", new XAttribute("id", "note-" + note.Id.Replace(":", "-")), new XAttribute("role", "doc-endnote"),
                    new XElement("a", new XAttribute("href", "#ref-" + note.Id.Replace(":", "-")), new XAttribute("aria-label", "Back to reference " + note.Label), note.Label));
                writer.Blocks(entry, note.Blocks); notes.Add(entry);
            }
            content.Add(notes);
        }
        if (writer.options.IncludeHeadersAndFooters) writer.Stories(content, model, "footer-");
        body.Add(content);
        var document = new XDocument(new XDocumentType("html", null, null, null),
            new XElement("html", new XElement("head", new XElement("meta", new XAttribute("charset", "utf-8")),
                new XElement("title", model.Title)), body));
        // Write directly to the bounded staging stream. No external assets or executable content.
        using var xml = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
            CloseOutput = false,
            CheckCharacters = true,
            Indent = false,
        });
        document.Save(xml);
    }

    private void Blocks(XElement parent, IEnumerable<DocumentBlock> blocks)
    {
        var lists = new List<(string Id, bool Ordered, XElement List, XElement? Item)>();
        foreach (DocumentBlock block in blocks)
        {
            context.CheckCancellation();
            if (block is TableBlock table) { lists.Clear(); parent.Add(Table(table)); continue; }
            var paragraph = (ParagraphBlock)block;
            XElement element = Paragraph(paragraph);
            ListItem? item = paragraph.List;
            if (item == null) { lists.Clear(); parent.Add(element); continue; }
            string label = counter.Label(item);
            int level = item.Level;
            if (level >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: HTML list exceeds the nesting limit.");
            // A nested list must be a child of a list item, never a sibling of one.
            if (level < lists.Count && (lists[level].Id != item.Id || lists[level].Ordered != item.Ordered))
                lists.RemoveRange(level, lists.Count - level);
            if (lists.Count > level + 1) lists.RemoveRange(level + 1, lists.Count - level - 1);
            while (lists.Count <= level)
            {
                var list = new XElement(item.Ordered ? "ol" : "ul", new XAttribute("style", "margin:0;padding-left:24px;"));
                XElement target = parent;
                if (lists.Count > 0)
                {
                    var previous = lists[lists.Count - 1];
                    if (previous.Item == null)
                    {
                        previous.Item = new XElement("li", new XAttribute("style", "list-style-type:none;"));
                        previous.List.Add(previous.Item); lists[lists.Count - 1] = previous;
                    }
                    target = previous.Item;
                }
                target.Add(list); lists.Add((item.Id, item.Ordered, list, null));
            }
            var current = lists[level];
            var li = new XElement("li", element);
            if (item.Ordered) li.SetAttributeValue("value", counter.CurrentValue);
            if (item.Definition != null)
            {
                li.SetAttributeValue("style", "list-style-type:none;");
                element.AddFirst(new XElement("span", new XAttribute("style", "white-space:pre;"), label + (item.Definition[item.Level].Suffix == "nothing" ? "" : " ")));
            }
            current.List.Add(li); current.Item = li; lists[level] = current;
        }
    }

    private void Stories(XElement parent, DocumentModel document, string prefix)
    {
        PageSettings? section = document.Blocks.FirstOrDefault()?.Section ?? document.FinalSection;
        if (section == null || !section.Stories.TryGetValue(prefix + (section.DifferentFirstPage ? "first" : "default"), out List<DocumentBlock>? story)) return;
        var block = new XElement("div", new XAttribute("style", "margin:0;padding:0;"));
        Blocks(block, story); parent.Add(block);
    }

    private XElement Paragraph(ParagraphBlock paragraph)
    {
        string align = paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed ? "justify" : paragraph.Alignment == ParagraphAlignment.Center ? "center" : paragraph.Alignment == ParagraphAlignment.Right ? "right" : "left";
        ParagraphLayout layout = paragraph.Layout;
        if (layout.Tabs.Count > 0) context.Report("Html.TabStopsApproximated", "HTML uses non-breaking spaces for tabs; fixed tab alignment and leaders are approximated.");
        string css = "text-align:" + align + ";white-space:pre-wrap;margin-top:" + Pt(layout.Before ?? 0)
            + ";margin-bottom:" + Pt(layout.After ?? 6) + ";margin-left:" + Pt(layout.Left) + ";margin-right:" + Pt(layout.Right)
            + ";text-indent:" + Pt(layout.FirstLine) + ";";
        if (layout.LineSpacing is double spacing) css += "line-height:" + (layout.MultipleLineSpacing ? Number(spacing) : Pt(spacing)) + ";";
        if (paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed) css += "text-align-last:left;";
        if (paragraph.Alignment == ParagraphAlignment.Distributed) css += "text-justify:inter-character;";
        if (layout.PageBreakBefore || layout.KeepTogether || layout.KeepWithNext)
            context.Report("Html.PaginationIgnored", "Pagination controls are omitted from the reflowable HTML document.");
        var element = new XElement(paragraph.HeadingLevel > 0 ? "h" + paragraph.HeadingLevel.ToString(CultureInfo.InvariantCulture) : "p", new XAttribute("style", css));
        if (paragraph.RightToLeft.HasValue) element.Add(new XAttribute("dir", paragraph.RightToLeft.Value ? "rtl" : "ltr"));
        foreach (TextRun run in paragraph.Runs)
        {
            context.CheckCancellation();
            if (run.Bookmark != null)
            { if ((run.Bookmark.Length == 0 || run.Bookmark[0] != '/')) element.Add(new XElement("span", new XAttribute("id", run.Bookmark), "")); continue; }
            XElement span;
            if (run.Box != null)
            {
                span = new XElement("span", new XAttribute("style", "display:inline-block;position:relative;width:" + Pt(run.Box.Width) + ";height:" + Pt(run.Box.Height) + ";"));
                Blocks(span, run.Box.Blocks);
                foreach (XElement boxParagraph in span.Elements().Where(e => e.Name.LocalName is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"))
                { boxParagraph.Name = "span"; boxParagraph.SetAttributeValue("style", "display:block;" + (string?)boxParagraph.Attribute("style")); }
                context.Loss("Html.DrawingPosition", "Positioned drawings use inline frames in reflowable HTML.");
            }
            else if (run.Note != null)
            {
                string id = run.Note.Id.Replace(":", "-");
                span = new XElement("sup", new XElement("a", new XAttribute("id", "ref-" + id), new XAttribute("href", "#note-" + id), new XAttribute("role", "doc-noteref"), run.Note.Label));
            }
            else if (run.Image is DocumentImage image)
            {
                if (image.Anchor != null) context.Loss("Html.DrawingPosition", "Positioned drawings use inline frames in reflowable HTML.");
                if (!options.EmbedImages)
                {
                    context.Loss("Html.ImageOmitted", "Image embedding is disabled; alternative text is retained.");
                    span = new XElement("span", image.AlternativeText);
                }
                else span = new XElement("img", new XAttribute("src", "data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Bytes)),
                    new XAttribute("alt", image.AlternativeText), new XAttribute("width", Number(image.Width * 96 / 72)),
                    new XAttribute("height", Number(image.Height * 96 / 72)),
                    new XAttribute("style", "display:inline-block;width:" + Pt(image.Width) + ";height:" + Pt(image.Height) + ";border:0;"));
                if (options.EmbedImages && image.Transformed)
                {
                    double a = image.Rotation * Math.PI / 180, c = Math.Round(Math.Cos(a)), s = Math.Round(Math.Sin(a));
                    double xx = c * (image.FlipHorizontal ? -1 : 1), xy = s * (image.FlipHorizontal ? -1 : 1), yx = -s * (image.FlipVertical ? -1 : 1), yy = c * (image.FlipVertical ? -1 : 1);
                    double fw = image.FrameWidth / (1 - image.CropLeft - image.CropRight), fh = image.FrameHeight / (1 - image.CropTop - image.CropBottom);
                    span.SetAttributeValue("width", Number(fw * 96 / 72)); span.SetAttributeValue("height", Number(fh * 96 / 72));
                    span.SetAttributeValue("style", "position:absolute;max-width:none;width:" + Pt(fw) + ";height:" + Pt(fh) + ";left:" + Pt(-fw * image.CropLeft) + ";top:" + Pt(-fh * image.CropTop) + ";");
                    var frame = new XElement("span", new XAttribute("style", "position:absolute;overflow:hidden;width:" + Pt(image.FrameWidth) + ";height:" + Pt(image.FrameHeight)
                        + ";transform-origin:0 0;transform:matrix(" + Number(xx) + "," + Number(xy) + "," + Number(yx) + "," + Number(yy) + ","
                        + Number((image.Width / 2 - xx * image.FrameWidth / 2 - yx * image.FrameHeight / 2) * 96 / 72) + ","
                        + Number((image.Height / 2 - xy * image.FrameWidth / 2 - yy * image.FrameHeight / 2) * 96 / 72) + ");"), span);
                    span = new XElement("span", new XAttribute("style", "display:inline-block;position:relative;width:" + Pt(image.Width) + ";height:" + Pt(image.Height) + ";"), frame);
                }
            }
            else
            {
                string style = "font-size:" + Pt(run.FontSize ?? (paragraph.HeadingLevel > 0 ? 24 - paragraph.HeadingLevel * 2 : 12))
                    + ";font-weight:" + (run.Bold ? "bold" : "normal") + ";font-style:" + (run.Italic ? "italic" : "normal") + ";"
                    + (run.Color == null ? "" : "color:#" + run.Color + ";") + (run.Code ? "font-family:monospace;" : "");
                RunAppearance appearance = run.Appearance;
                if (appearance.FontFamily != null) style += "font-family:" + CssFamily(appearance.FontFamily) + ",Arial,sans-serif;";
                if (appearance.Underline || appearance.Strike) style += "text-decoration:" + (appearance.Underline ? "underline " : "") + (appearance.Strike ? "line-through" : "") + ";";
                if (appearance.Background != null) style += "background-color:#" + appearance.Background + ";";
                if (appearance.VerticalAlignment != null) style += "vertical-align:" + (appearance.VerticalAlignment == "superscript" ? "super" : "sub") + ";";
                span = new XElement("span", new XAttribute("style", style));
                string text;
                if (run.FieldInstruction != null && DocumentFields.Kind(run.FieldInstruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES" or "PAGEREF" or "TOC")
                {
                    context.Report("Html.PageFieldsUsedCachedResult", "Page fields retain their cached text because HTML has no fixed pagination.");
                    text = run.Text;
                }
                else text = DocumentFields.Render(run, model, 1, 1, 1, context);
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) span.Add(new XElement("br"));
                    span.Add(new XText(lines[i].Replace("\t", "\u00a0\u00a0\u00a0\u00a0")));
                }
            }
            if (run.Link != null)
            {
                string? link = (run.Link.Length > 0 && run.Link[0] == '#') ? run.Link : context.SafeLink(run.Link);
                if (link != null) span = new XElement("a", new XAttribute("href", link), span);
            }
            element.Add(span);
        }
        // Explicitly close empty text elements; Outlook must not interpret a self-closing paragraph.
        if (!element.HasElements && element.Value.Length == 0) element.Add(new XText(""));
        return element;
    }

    private XElement Table(TableBlock table)
    {
        string width = table.WidthPercent.HasValue ? Number(table.WidthPercent.Value) + "%" : table.Width.HasValue ? Pt(table.Width.Value) : "100%";
        var element = new XElement("table", new XAttribute("width", table.WidthPercent.HasValue ? Number(table.WidthPercent.Value) + "%" : table.Width.HasValue ? Number(table.Width.Value * 96 / 72) : "100%"),
            new XAttribute("cellspacing", "0"), new XAttribute("cellpadding", "0"),
            new XAttribute("style", "border-collapse:collapse;table-layout:" + (table.AutoFit ? "auto" : "fixed") + ";width:" + width + ";margin-left:" + Pt(table.LeftIndent) + ";"));
        if (table.ColumnWidths.Count > 0) element.Add(new XElement("colgroup", table.ColumnWidths.Select(w => new XElement("col", new XAttribute("style", "width:" + Pt(w) + ";")))));
        var rows = new XElement("tbody");
        for (int r = 0; r < table.Rows.Count; r++)
        {
            context.CheckCancellation();
            var row = new XElement("tr"); int column = 0, cellIndex = 0;
            foreach (TableCell cell in table.Rows[r])
            {
                if (cell.Merge != VerticalMerge.Continue)
                {
                    CellPadding p = cell.Padding ?? new CellPadding();
                    string? background = cell.Background ?? table.Background;
                    string decoration = background == null ? "" : "background-color:#" + background + ";";
                    foreach (var border in table.CellBorders(r, cellIndex))
                        decoration += "border-" + border.Key + ":" + (border.Value.Width == 0 ? "none"
                            : Pt(border.Value.Width) + " " + (border.Value.Style == "single" ? "solid" : border.Value.Style) + " #" + border.Value.Color) + ";";
                    var output = new XElement("td", new XAttribute("style", "vertical-align:" + (cell.VerticalAlignment == "center" ? "middle" : cell.VerticalAlignment)
                        + ";padding:" + Pt(p.Top) + " " + Pt(p.Right) + " " + Pt(p.Bottom) + " " + Pt(p.Left)
                        + ";" + decoration + (cell.WidthPercent.HasValue ? "width:" + Number(cell.WidthPercent.Value) + "%;" : cell.CellWidth.HasValue ? "width:" + Pt(cell.CellWidth.Value) + ";" : "")));
                    if (cell.ColumnSpan > 1) output.SetAttributeValue("colspan", cell.ColumnSpan);
                    if (cell.Merge == VerticalMerge.Start)
                    {
                        int span = 1;
                        for (int next = r + 1; next < table.Rows.Count && CellAt(table.Rows[next], column)?.Merge == VerticalMerge.Continue; next++) span++;
                        if (span > 1) output.SetAttributeValue("rowspan", span);
                    }
                    Blocks(output, cell.Blocks); row.Add(output);
                }
                column += cell.ColumnSpan; cellIndex++;
            }
            rows.Add(row);
        }
        element.Add(rows); return element;
    }

    private static TableCell? CellAt(List<TableCell> row, int column)
    {
        int index = 0;
        foreach (TableCell cell in row) { if (index == column) return cell; index += cell.ColumnSpan; }
        return null;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Pt(double value) => Number(value) + "pt";
    private static string CssFamily(string family)
    {
        // CSS escapes, in addition to XML attribute escaping, prevent declarations in font names.
        var result = new StringBuilder("'");
        foreach (char c in family)
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') result.Append(c);
            else result.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
        }
        return result.Append('\'').ToString();
    }
}
