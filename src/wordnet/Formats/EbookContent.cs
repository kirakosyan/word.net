using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class EbookContent
{
    internal static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";

    internal static byte[] Write(DocumentModel model, ConversionContext context, Func<DocumentImage, string>? imageWriter = null)
    {
        var body = new XElement(Html + "body");
        var lists = new System.Collections.Generic.List<(string Id, XElement Element)>();
        var listCounter = new ListCounter();
        foreach (DocumentBlock block in model.Blocks)
        {
            context.CheckCancellation();
            if (block is ParagraphBlock paragraph)
            {
                CheckWhitespace(paragraph, context);
                XElement content = Paragraph(paragraph, imageWriter);
                if (paragraph.List == null) { lists.Clear(); body.Add(content); }
                else AppendList(body, lists, paragraph.List, content, listCounter.Label(paragraph.List), context);
            }
            else if (block is TableBlock table)
            {
                lists.Clear();
                var element = new XElement(Html + "table");
                if (table.Width.HasValue) element.SetAttributeValue("style", "width:" + Points(table.Width.Value));
                if (table.ColumnWidths.Count > 0) element.Add(new XElement(Html + "colgroup", table.ColumnWidths.Select(w => new XElement(Html + "col", new XAttribute("style", "width:" + Points(w))))));
                foreach (ParagraphBlock cell in table.Rows.SelectMany(r => r).SelectMany(c => c.Paragraphs)) CheckWhitespace(cell, context);
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    var row = new XElement(Html + "tr");
                    int column = 0;
                    foreach (TableCell cell in table.Rows[r])
                    {
                        if (cell.Merge != VerticalMerge.Continue)
                        {
                            XElement output = WriteTableCell(cell, r < table.HeaderRowCount, imageWriter, listCounter, context);
                            if (cell.Merge == VerticalMerge.Start)
                            {
                                int span = 1;
                                for (int next = r + 1; next < table.Rows.Count && CellAt(table.Rows[next], column)?.Merge == VerticalMerge.Continue; next++) span++;
                                if (span > 1) output.SetAttributeValue("rowspan", span);
                            }
                            row.Add(output);
                        }
                        column += cell.ColumnSpan;
                    }
                    element.Add(row);
                }
                body.Add(element);
            }
        }
        var document = new XDocument(new XElement(Html + "html", new XAttribute(XNamespace.Xml + "lang", "en"),
            new XElement(Html + "head", new XElement(Html + "title", "Document")), body));
        return TextFormat.Utf8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    internal static DocumentModel Read(byte[] bytes, ConversionContext context, Func<IElement, DocumentImage?>? imageReader = null)
    {
        string text = TextFormat.Decode(bytes, context);
        // HtmlParser performs no network loading or script execution. Traverse iteratively before recursion.
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(text);
        var queue = new System.Collections.Generic.Stack<(INode Node, int Depth)>();
        queue.Push((document, 0));
        while (queue.Count > 0)
        {
            context.CheckCancellation();
            var item = queue.Pop();
            if (item.Depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Ebook nesting exceeds the limit.");
            foreach (INode child in item.Node.ChildNodes) queue.Push((child, item.Depth + 1));
        }
        if (document.QuerySelector("style,link[rel=stylesheet]") != null) context.Loss("Ebook.Css", "External and embedded CSS styling is not preserved.");
        var result = new DocumentModel();
        IElement body = document.Body ?? throw new InvalidDataException("Ebook.Html: Missing document body.");
        Attributes(body, context);
        ReadBlocks(body, result, context, imageReader);
        return result;
    }

    private static void ReadBlocks(INode parent, DocumentModel model, ConversionContext context, Func<IElement, DocumentImage?>? imageReader = null, ListItem? list = null, IEnumerable<INode>? nodes = null)
    {
        foreach (INode node in nodes ?? parent.ChildNodes)
        {
            context.CheckCancellation();
            if (node is IText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Data)) { var p = new ParagraphBlock { Alignment = parent is IElement element ? Alignment(element, context) : ParagraphAlignment.Left }; p.Runs.Add(new TextRun(Collapse(text.Data))); model.Blocks.Add(p); }
                continue;
            }
            if (node is not IElement e) continue;
            string tag = e.LocalName;
            if (tag is "script" or "style" or "iframe" or "object" or "svg" or "math") { context.Loss("Ebook.ActiveContent", "Active or unsupported embedded content was omitted."); continue; }
            if (tag is "section" or "article" or "div" or "main" or "body") { Attributes(e, context); ReadBlocks(e, model, context, imageReader, list); continue; }
            if (tag == "table")
            {
                Attributes(e, context);
                var table = new TableBlock();
                table.Width = CssLength(e, "width", context);
                foreach (IElement col in e.QuerySelectorAll("colgroup > col"))
                    if (CssLength(col, "width", context) is double colWidth) table.ColumnWidths.Add(colWidth);
                var spans = new Dictionary<int, (int Remaining, int Columns)>();
                foreach (IElement row in e.QuerySelectorAll("tr").Where(r => r.ParentElement == e || r.ParentElement?.ParentElement == e))
                {
                    var cells = new List<TableCell>();
                    int column = 0;
                    foreach (IElement cell in row.Children.Where(c => c.LocalName is "td" or "th"))
                    {
                        Attributes(cell, context);
                        ContinueSpans();
                        int columnSpan = Span(cell, "colspan"), rowSpan = Span(cell, "rowspan");
                        for (int c = column; c < column + columnSpan; c++)
                            if (spans.ContainsKey(c)) throw new InvalidDataException("Ebook.TableMerge: Overlapping cell spans.");
                        var target = new TableCell { CellWidth = CssLength(cell, "width", context), Padding = CssPadding(cell, context), ColumnSpan = columnSpan, Merge = rowSpan > 1 ? VerticalMerge.Start : VerticalMerge.None };
                        if (cell.QuerySelector("p,div,ul,ol,table,h1,h2,h3,h4,h5,h6,pre") != null)
                        {
                            var content = new DocumentModel();
                            ReadBlocks(cell, content, context, imageReader);
                            if (content.Blocks.Any(b => b is TableBlock)) context.Loss("Ebook.NestedTable", "Nested tables are flattened.");
                            target.Paragraphs.AddRange(content.Paragraphs);
                        }
                        else
                        {
                            var p = new ParagraphBlock { Alignment = Alignment(cell, context) };
                            Inline(cell, p, context, false, false, false, null, false, imageReader); Trim(p); target.Paragraphs.Add(p);
                        }
                        if (target.Paragraphs.Count == 0) target.Paragraphs.Add(new ParagraphBlock());
                        cells.Add(target);
                        if (rowSpan > 1) spans.Add(column, (rowSpan - 1, columnSpan));
                        column += columnSpan;
                    }
                    ContinueSpans();
                    if (spans.Keys.Any(c => c > column)) throw new InvalidDataException("Ebook.TableMerge: Missing cell before a row span.");
                    if (row.Children.Any(c => c.LocalName == "th") && table.Rows.Count == table.HeaderRowCount) table.HeaderRowCount++;
                    table.Rows.Add(cells);
                    void ContinueSpans()
                    {
                        while (spans.TryGetValue(column, out var span))
                        {
                            cells.Add(new TableCell(new ParagraphBlock()) { ColumnSpan = span.Columns, Merge = VerticalMerge.Continue });
                            if (span.Remaining == 1) spans.Remove(column); else spans[column] = (span.Remaining - 1, span.Columns);
                            column += span.Columns;
                        }
                    }
                }
                if (spans.Count != 0) throw new InvalidDataException("Ebook.TableMerge: Row span extends beyond the table.");
                model.Blocks.Add(table); continue;
            }
            if (tag is "ul" or "ol")
            {
                int level = list == null ? 0 : list.Level + 1;
                if (level > 8) throw new NotSupportedException("Ebook.ListDepth: Lists support up to nine levels.");
                Attributes(e, context);
                int start = 1;
                if (e.HasAttribute("start") && (!int.TryParse(e.GetAttribute("start"), out start) || start < 0)) throw new InvalidDataException("Ebook.ListStart: Invalid list start.");
                if (e.HasAttribute("reversed") || e.HasAttribute("type")) context.Loss("Ebook.ListStyle", "Reversed and custom list styles use decimal numbering or bullets.");
                ReadBlocks(e, model, context, imageReader, new ListItem { Id = "ebook-" + context.NextListId(), Level = level, Ordered = tag == "ol", Start = start }); continue;
            }
            if (tag == "blockquote") context.Loss("Ebook.Quote", "Block quote indentation is flattened.");
            Attributes(e, context);
            var paragraph = new ParagraphBlock { IsCodeBlock = tag == "pre", HeadingLevel = tag.Length == 2 && tag[0] == 'h' && tag[1] >= '1' && tag[1] <= '6' ? tag[1] - '0' : 0 };
            paragraph.Alignment = Alignment(e, context);
            paragraph.List = tag == "li" ? list?.Copy() : null;
            if (tag == "li" && e.HasAttribute("value")) context.Loss("Ebook.ListValue", "Per-item numbering overrides are not retained.");
            if (!(tag is "p" or "pre" or "li" or "blockquote" || paragraph.HeadingLevel > 0)) context.Loss("Ebook.Block", "Unsupported ebook block formatting was flattened.");
            Inline(e, paragraph, context, false, false, paragraph.IsCodeBlock, null, paragraph.IsCodeBlock, imageReader);
            if (!paragraph.IsCodeBlock) Trim(paragraph);
            model.Blocks.Add(paragraph);
            if (tag == "li") foreach (IElement nested in e.Children.Where(c => c.LocalName is "ul" or "ol"))
                ReadBlocks(e, model, context, imageReader, list, new[] { nested });
        }
    }

    private static int Span(IElement cell, string name)
    {
        if (!cell.HasAttribute(name)) return 1;
        if (!int.TryParse(cell.GetAttribute(name), out int value) || value < 1 || value > 1024) throw new InvalidDataException("Ebook.TableMerge: Cell span must be between one and 1024.");
        return value;
    }

    private static TableCell? CellAt(List<TableCell> row, int column)
    {
        int current = 0;
        foreach (TableCell cell in row) { if (current == column) return cell; current += cell.ColumnSpan; }
        return null;
    }

    private static void Inline(INode node, ParagraphBlock paragraph, ConversionContext context, bool bold, bool italic, bool code, string? link, bool preserve,
        Func<IElement, DocumentImage?>? imageReader = null, double? fontSize = null, string? color = null)
    {
        if (node is IText text) { paragraph.Runs.Add(new TextRun(preserve ? text.Data : Collapse(text.Data), bold, italic, code, link, fontSize, color)); return; }
        if (node is IElement e)
        {
            Attributes(e, context);
            string tag = e.LocalName;
            if (tag is "ul" or "ol") return;
            fontSize = CssLength(e, "font-size", context) ?? fontSize;
            color = CssColor(e, context) ?? color;
            if (tag is "script" or "style" or "iframe" or "object" or "svg" or "math") { context.Loss("Ebook.ActiveContent", "Active or unsupported embedded content was omitted."); return; }
            if (tag == "br") { paragraph.Runs.Add(new TextRun("\n", bold, italic, code, link)); return; }
            if (tag == "img")
            {
                DocumentImage? image = imageReader?.Invoke(e);
                if (image != null) paragraph.Runs.Add(new TextRun("", image: image));
                else { context.Loss("Image.Unsupported", "Unresolved or unsupported ebook images retain alternative text only."); paragraph.Runs.Add(new TextRun(e.GetAttribute("alt") ?? "")); }
                return;
            }
            if (tag == "table") context.Loss("Ebook.NestedTable", "Nested tables are flattened.");
            if (tag == "p")
            {
                ParagraphAlignment alignment = Alignment(e, context);
                if (paragraph.Runs.Count == 0) paragraph.Alignment = alignment;
                else
                {
                    paragraph.Runs.Add(new TextRun("\n"));
                    if (paragraph.Alignment != alignment) context.Loss("Ebook.CellAlignment", "Multiple cell paragraphs use the first paragraph's alignment.");
                }
            }
            if (!(tag is "p" or "pre" or "span" or "b" or "strong" or "i" or "em" or "code" or "a" or "td" or "th" or "li" or "blockquote" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"))
                context.Loss("Ebook.InlineFormatting", "Unsupported inline formatting is flattened.");
            bold |= tag is "b" or "strong";
            string? weight = Css(e, "font-weight");
            if (weight != null) bold = weight is "bold" or "700";
            italic |= tag is "i" or "em";
            code |= tag is "code" or "pre";
            if (tag == "a") link = context.SafeLink(e.GetAttribute("href") ?? "");
        }
        foreach (INode child in node.ChildNodes) Inline(child, paragraph, context, bold, italic, code, link, preserve, imageReader, fontSize, color);
    }

    private static void Attributes(IElement element, ConversionContext context)
    {
        if (element.HasAttribute("class") || (element.GetAttribute("style") ?? "").Split(';').Any(declaration =>
            !string.IsNullOrWhiteSpace(declaration) && !SupportedCss(element, declaration)))
            context.Loss("Ebook.Css", "CSS beyond supported inline alignment, font size, RGB color and table/image geometry is not preserved.");
        if (element.Attributes.Any(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))) context.Loss("Ebook.ActiveContent", "Event handlers are omitted.");
    }

    private static bool IsAlignmentBlock(IElement element) => element.LocalName is "body" or "section" or "article" or "div" or "main" or "p" or "pre" or "li" or "blockquote" or "td" or "th" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    private static bool IsAlignmentDeclaration(string declaration)
    {
        int colon = declaration.IndexOf(':');
        return colon >= 0 && declaration.Substring(0, colon).Trim().Equals("text-align", StringComparison.OrdinalIgnoreCase);
    }

    private static ParagraphAlignment Alignment(IElement element, ConversionContext context)
    {
        for (IElement? current = element; current != null; current = current.ParentElement)
        {
            if (!IsAlignmentBlock(current)) continue;
            string? value = current.GetAttribute("align");
            foreach (string declaration in (current.GetAttribute("style") ?? "").Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (IsAlignmentDeclaration(declaration))
                    value = declaration.Substring(colon + 1);
            }
            if (value == null) continue;
            switch (value.Trim().ToLowerInvariant())
            {
                case "left": case "initial": return ParagraphAlignment.Left;
                case "center": return ParagraphAlignment.Center;
                case "right": return ParagraphAlignment.Right;
                case "inherit": case "unset": continue;
                default:
                    context.Loss("Ebook.Alignment", "Unsupported or direction-dependent ebook alignment becomes left alignment.");
                    return ParagraphAlignment.Left;
            }
        }
        return ParagraphAlignment.Left;
    }

    private static string Collapse(string value)
    {
        var result = new StringBuilder();
        bool space = false;
        foreach (char c in value)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f') { if (!space) result.Append(' '); space = true; }
            else { result.Append(c); space = false; }
        }
        return result.ToString();
    }

    private static void CheckWhitespace(ParagraphBlock paragraph, ConversionContext context)
    {
        string text = paragraph.Text;
        if (!paragraph.IsCodeBlock && (text.Contains("  ") || text.Contains('\t') || text.Length > 0 && (text[0] == ' ' || text[text.Length - 1] == ' ')))
            context.Loss("Ebook.Whitespace", "Reflowable ebook content collapses repeated spaces, tabs and paragraph-edge spaces.");
    }

    private static void Trim(ParagraphBlock p)
    {
        for (int i = 0; i < p.Runs.Count; i++)
        {
            TextRun run = p.Runs[i];
            string value = run.Text;
            if (i == 0) value = value.TrimStart(' ');
            if (i == p.Runs.Count - 1) value = value.TrimEnd(' ');
            if (i > 0 && p.Runs[i - 1].Text.Length > 0 && p.Runs[i - 1].Text[p.Runs[i - 1].Text.Length - 1] == ' ') value = value.TrimStart(' ');
            p.Runs[i] = run.WithText(value);
        }
    }

    private static XElement Paragraph(ParagraphBlock paragraph, Func<DocumentImage, string>? imageWriter)
    {
        var element = new XElement(Html + (paragraph.IsCodeBlock ? "pre" : paragraph.HeadingLevel > 0 ? "h" + paragraph.HeadingLevel : "p"));
        // Make physical left alignment explicit too: HTML table headers default to centered text.
        element.Add(new XAttribute("style", "text-align:" + (paragraph.Alignment == ParagraphAlignment.Center ? "center" : paragraph.Alignment == ParagraphAlignment.Right ? "right" : "left")));
        foreach (TextRun run in paragraph.Runs)
        {
            if (run.Image != null && imageWriter != null)
            {
                element.Add(new XElement(Html + "img", new XAttribute("src", imageWriter(run.Image)), new XAttribute("alt", run.Image.AlternativeText),
                    new XAttribute("style", "width:" + Points(run.Image.Width) + ";height:" + Points(run.Image.Height)))); continue;
            }
            var span = new XElement(Html + "span");
            string style = (run.FontSize.HasValue ? "font-size:" + Points(run.FontSize.Value) + ";" : "") + (run.Color != null ? "color:#" + run.Color : "");
            if (style.Length > 0) span.SetAttributeValue("style", style);
            string[] lines = run.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++) { if (i > 0) span.Add(new XElement(Html + "br")); span.Add(new XText(lines[i])); }
            object content = span;
            if (run.Code && !paragraph.IsCodeBlock) content = new XElement(Html + "code", content);
            if (run.Italic) content = new XElement(Html + "em", content);
            if (run.Bold) content = new XElement(Html + "strong", content);
            if (run.Link != null) content = new XElement(Html + "a", new XAttribute("href", run.Link), content);
            element.Add(content);
        }
        return element;
    }
}
