using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private static readonly XNamespace W = DocxPackage.Word;
    private readonly ConversionContext context;
    private DocumentModel model = null!;
    private readonly Dictionary<string, int> bookmarkIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<XElement> relationships = new List<XElement>();
    private readonly HashSet<string> usedStyles = new HashSet<string>(StringComparer.Ordinal);

    private DocxWriter(ConversionContext context)
    {
        this.context = context;
    }

    internal static void Write(DocumentModel document, Stream output, ConversionContext context)
    {
        var writer = new DocxWriter(context) { model = document };
        ObjectOutputBudget(document, context);
        if (DocumentReferences.HasFields(document)) _ = DocumentReferences.Refresh(document, context, paginate: false);
        writer.RegisterStories(document);
        var body = new XElement(W + "body");
        PageSettings? previousSection = null;
        foreach (var group in writer.BlockGroups(document.Blocks, includeSections: true))
        {
            context.CheckCancellation();
            DocumentBlock block = group.First;
            PageSettings? section = block.Section;
            if (body.HasElements && !ReferenceEquals(previousSection, section))
            {
                writer.AttachSection(body, previousSection ?? new PageSettings());
            }
            previousSection = group.Last.Section;
            body.Add(group.Content);
        }

        if (!body.HasElements)
        {
            body.Add(new XElement(W + "p"));
        }
        if (document.FinalSection != null && previousSection != null && !ReferenceEquals(previousSection, document.FinalSection))
        {
            writer.AttachSection(body, previousSection);
        }
        if (previousSection != null || document.FinalSection != null)
            body.Add(writer.Section(document.FinalSection ?? previousSection!));

        XNamespace contentTypes = DocxPackage.ContentTypes;
        var types = new XElement(contentTypes + "Types",
            new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", DocxPackage.MainContentType)));

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var rootRelationships = new XElement(DocxPackage.Relationships + "Relationships", Relationship("rDocument", "officeDocument", "word/document.xml"));
        if (document.Title.Length > 0 || document.Author.Length > 0)
        {
            XNamespace core = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            rootRelationships.Add(new XElement(DocxPackage.Relationships + "Relationship", new XAttribute("Id", "rCore"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"), new XAttribute("Target", "docProps/core.xml")));
            types.Add(new XElement(contentTypes + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
            WritePart(archive, "docProps/core.xml", new XElement(core + "coreProperties", new XElement(dc + "title", document.Title), new XElement(dc + "creator", document.Author)));
        }
        writer.WriteCustomProperties(archive, types, rootRelationships);
        WritePart(archive, "_rels/.rels", rootRelationships);
        WritePart(archive, "word/document.xml", new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", DocxPackage.DocumentRelationships.NamespaceName), body));

        writer.WriteStories(document, archive, types);
        writer.WriteReview(archive, types);
        if (writer.usedStyles.Count > 0)
        {
            types.Add(new XElement(contentTypes + "Override", new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")));
            writer.relationships.Add(Relationship("rStyles", "styles", "styles.xml"));
            WritePart(archive, "word/styles.xml", writer.Styles());
        }

        writer.WriteNotes(document, archive, types);
        writer.WriteResources(archive, types);
        writer.WriteObjects(archive, types);
        if (writer.relationships.Count > 0)
        {
            WritePart(archive, "word/_rels/document.xml.rels", new XElement(DocxPackage.Relationships + "Relationships", writer.relationships));
        }

        WritePart(archive, "[Content_Types].xml", types);
        context.CheckCancellation();
    }

    private void AttachSection(XElement body, PageSettings settings)
    {
        XElement? last = body.Elements().LastOrDefault();
        while (last?.Name == W + "sdt") last = last.Element(W + "sdtContent")?.Elements().LastOrDefault();
        if (last == null || last.Name != W + "p") { last = new XElement(W + "p"); body.Add(last); }
        XElement? properties = last.Element(W + "pPr");
        if (properties == null) { properties = new XElement(W + "pPr"); last.AddFirst(properties); }
        properties.Add(Section(settings));
    }

    private XElement Paragraph(ParagraphBlock paragraph)
    {
        var element = new XElement(W + "p");
        var properties = new XElement(W + "pPr");
        string? style = (paragraph.StyleId != null && model.Styles.ContainsKey(paragraph.StyleId) ? paragraph.StyleId : null) ?? (paragraph.IsCodeBlock ? "WordnetCodeBlock"
            : paragraph.HeadingLevel > 0 ? "Heading" + paragraph.HeadingLevel.ToString(CultureInfo.InvariantCulture) : null);
        if (style != null)
        {
            usedStyles.Add(style);
            properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        }

        ParagraphLayout resolvedLayout = paragraph.Layout.Copy();
        resolvedLayout.After ??= paragraph.FallbackAfter;
        ParagraphLayoutProperties(properties, resolvedLayout);
        if (paragraph.List != null)
        {
            XElement? spacing = properties.Elements().FirstOrDefault(e => e.Name == W + "tabs" || e.Name == W + "spacing" || e.Name == W + "ind");
            if (spacing == null) properties.Add(NumberingReference(paragraph.List));
            else spacing.AddBeforeSelf(NumberingReference(paragraph.List));
        }
        if (paragraph.Alignment != ParagraphAlignment.Left)
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", paragraph.Alignment switch
            { ParagraphAlignment.Center => "center", ParagraphAlignment.Right => "right", ParagraphAlignment.Justified => "both", ParagraphAlignment.Distributed => "distribute", _ => "left" })));
        if (paragraph.RightToLeft.HasValue)
        {
            var direction = new XElement(W + "bidi", new XAttribute(W + "val", paragraph.RightToLeft.Value ? "1" : "0"));
            XElement? following = properties.Elements().FirstOrDefault(e => e.Name == W + "spacing" || e.Name == W + "ind" || e.Name == W + "jc");
            if (following == null) properties.Add(direction); else following.AddBeforeSelf(direction);
        }
        if (paragraph.StyleId != null && paragraph.HeadingLevel > 0)
            properties.Add(new XElement(W + "outlineLvl", new XAttribute(W + "val", paragraph.HeadingLevel - 1)));
        XElement? markProperties = paragraph.Mark == null ? null : Run(paragraph.Mark).Element(W + "rPr");
        if (paragraph.StructuralRevision != null) { markProperties ??= new XElement(W + "rPr"); markProperties.AddFirst(Revision(paragraph.StructuralRevision)); }
        if (markProperties != null) properties.Add(markProperties);
        if (properties.HasElements) element.Add(properties);

        TemplateControl? currentControl = null;
        XElement parent = element;
        string? currentRevision = null; XElement? revisionParent = null;
        foreach (TextRun run in paragraph.Runs)
        {
            context.CheckCancellation();
            if (!ReferenceEquals(currentControl, run.Control))
            {
                currentControl = run.Control; parent = element;
                if (currentControl != null)
                {
                    XElement wrapper = Control(currentControl, null); element.Add(wrapper); parent = wrapper.Element(W + "sdtContent")!;
                }
            }
            XElement[] runElements = run.FieldChildren != null ? ComplexField(run, paragraph.FallbackFontSize).ToArray() : new[] { Run(run, paragraph.FallbackFontSize) };
            if (run.Revision != currentRevision)
            {
                currentRevision = run.Revision; revisionParent = null;
                if (currentRevision != null) { revisionParent = Revision(currentRevision); parent.Add(revisionParent); }
            }
            if (revisionParent != null)
            {
                if (model.Review.Revisions[run.Revision!].Deletion) foreach (XElement text in runElements.SelectMany(e => e.Descendants(W + "t"))) text.Name = W + "delText";
                revisionParent.Add(runElements); continue;
            }
            if (run.Link == null)
            {
                parent.Add(runElements);
            }
            else if ((run.Link.Length > 0 && run.Link[0] == '#')) parent.Add(new XElement(W + "hyperlink", new XAttribute(W + "anchor", run.Link.Substring(1)), runElements));
            else
            {
                string id = "rLink" + relationships.Count.ToString(CultureInfo.InvariantCulture);
                XElement relationship = Relationship(id, "hyperlink", run.Link);
                relationship.Add(new XAttribute("TargetMode", "External"));
                relationships.Add(relationship);
                parent.Add(new XElement(W + "hyperlink", new XAttribute(DocxPackage.DocumentRelationships + "id", id), runElements));
            }
        }

        return element;
    }

    private XElement Run(TextRun run, double? fallbackFontSize = null)
    {
        if (run.Object != null) return RetainedObject(run.Object);
        if (run.Comment != null)
        {
            int id = ReviewId(commentIds, run.Comment);
            var marker = new XElement(W + (run.CommentBoundary == 1 ? "commentRangeStart" : run.CommentBoundary == 2 ? "commentRangeEnd" : "commentReference"), new XAttribute(W + "id", id));
            return run.CommentBoundary == 3 ? new XElement(W + "r", marker) : marker;
        }
        if (run.Bookmark != null)
        {
            bool end = (run.Bookmark.Length > 0 && run.Bookmark[0] == '/'); string name = end ? run.Bookmark.Substring(1) : run.Bookmark;
            if (!bookmarkIds.TryGetValue(name, out int id)) bookmarkIds.Add(name, id = bookmarkIds.Count);
            return new XElement(W + (end ? "bookmarkEnd" : "bookmarkStart"), new XAttribute(W + "id", id), end ? null : new XAttribute(W + "name", name));
        }
        if (run.FieldInstruction != null)
        {
            string result = model.FieldResults.TryGetValue(run, out string? calculated) ? calculated : run.Text;
            var field = new XElement(W + "fldSimple", new XAttribute(W + "instr", run.FieldInstruction));
            if (run.FieldCache != null && calculated == null)
            {
                var cached = new ParagraphBlock(); cached.Runs.AddRange(run.FieldCache);
                field.Add(Paragraph(cached).Elements().Where(e => e.Name != W + "pPr").ToArray()); return field;
            }
            if (model.TocTargets.TryGetValue(run, out List<string>? targets))
            {
                string[] lines = result.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) field.Add(new XElement(W + "r", new XElement(W + "br")));
                    XElement content = Run(new TextRun(lines[i], run.Bold, run.Italic, run.Code, fontSize: (run.FontSize ?? fallbackFontSize), color: run.Color, appearance: run.Appearance));
                    field.Add(i < targets.Count ? new XElement(W + "hyperlink", new XAttribute(W + "anchor", targets[i]), content) : content);
                }
            }
            else field.Add(Run(new TextRun(result, run.Bold, run.Italic, run.Code, run.Link, (run.FontSize ?? fallbackFontSize), run.Color, run.Image, run.Appearance)));
            return field;
        }
        var element = new XElement(W + "r");
        var properties = new XElement(W + "rPr");
        RunAppearance appearance = run.Appearance;
        if (run.Code)
        {
            usedStyles.Add("WordnetCode");
            properties.Add(new XElement(W + "rStyle", new XAttribute(W + "val", "WordnetCode")));
        }
        if (appearance.FontFamily != null) properties.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", appearance.FontFamily), new XAttribute(W + "hAnsi", appearance.FontFamily)));

        if (run.Bold)
        {
            properties.Add(new XElement(W + "b"));
        }

        if (run.Italic)
        {
            properties.Add(new XElement(W + "i"));
        }
        if (appearance.Strike) properties.Add(new XElement(W + "strike"));
        if (run.Color != null) properties.Add(new XElement(W + "color", new XAttribute(W + "val", run.Color)));
        if ((run.FontSize ?? fallbackFontSize) is double pointSize) properties.Add(new XElement(W + "sz", new XAttribute(W + "val", checked((int)Math.Round(pointSize * 2)))));
        if (appearance.Underline) properties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (appearance.Background != null) properties.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", appearance.Background)));
        if (appearance.VerticalAlignment != null) properties.Add(new XElement(W + "vertAlign", new XAttribute(W + "val", appearance.VerticalAlignment)));

        if (properties.HasElements)
        {
            element.Add(properties);
        }
        if (run.Box != null) { element.Add(BoxDrawing(run.Box)); return element; }
        if (run.Note != null)
        {
            element.Add(new XElement(W + (run.Note.Endnote ? "endnoteReference" : "footnoteReference"), new XAttribute(W + "id", run.Note.PartId))); return element;
        }
        if (run.Image != null) { element.Add(Drawing(run.Image)); return element; }

        string text;
        try
        {
            _ = XmlConvert.VerifyXmlChars(run.Text.Replace("\f", "\n").Replace("\v", "\n"));
            text = run.Text;
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Text.XmlCharacter: Input contains a character that WordprocessingML cannot represent.", exception);
        }

        int start = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index == text.Length || text[index] == '\n' || text[index] == '\t' || text[index] == '\f' || text[index] == '\v')
            {
                if (index > start)
                {
                    element.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text.Substring(start, index - start)));
                }

                if (index < text.Length)
                {
                    element.Add(new XElement(W + (text[index] == '\t' ? "tab" : "br"), text[index] is '\f' or '\v' ? new XAttribute(W + "type", text[index] == '\f' ? "page" : "column") : null));
                }

                start = index + 1;
            }
        }

        return element;
    }

    private XElement Table(TableBlock table)
    {
        var element = new XElement(W + "tbl", new XElement(W + "tblPr",
            new XElement(W + "tblW", new XAttribute(W + "w", table.WidthPercent.HasValue ? Math.Round(table.WidthPercent.Value * 50).ToString(CultureInfo.InvariantCulture) : table.Width.HasValue ? Twips(table.Width.Value) : "0"), new XAttribute(W + "type", table.WidthPercent.HasValue ? "pct" : table.Width.HasValue ? "dxa" : "auto"))));
        if (table.LeftIndent != 0) element.Element(W + "tblPr")!.Add(new XElement(W + "tblInd", new XAttribute(W + "w", Twips(table.LeftIndent)), new XAttribute(W + "type", "dxa")));
        if (table.LayoutExplicit) element.Element(W + "tblPr")!.Add(new XElement(W + "tblLayout", new XAttribute(W + "type", table.AutoFit ? "autofit" : "fixed")));
        int columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Sum(c => c.ColumnSpan));
        element.Add(new XElement(W + "tblGrid", Enumerable.Range(0, columns).Select(c => new XElement(W + "gridCol",
            c < table.ColumnWidths.Count ? new XAttribute(W + "w", Twips(table.ColumnWidths[c])) : null))));
        TemplateControl? previousControl = null; XElement? rowContent = null;
        for (int index = 0; index < table.Rows.Count; index++)
        {
            context.CheckCancellation();
            var row = new XElement(W + "tr");
            if (index < table.HeaderRowCount || table.NonSplittingRows.Contains(index) || table.RowHeights.ContainsKey(index) || table.RowRevisions.ContainsKey(index))
            {
                row.Add(new XElement(W + "trPr", table.NonSplittingRows.Contains(index) ? new XElement(W + "cantSplit") : null,
                    table.RowHeights.TryGetValue(index, out var height) ? new XElement(W + "trHeight", new XAttribute(W + "val", Twips(height.Height)), new XAttribute(W + "hRule", height.Exact ? "exact" : "atLeast")) : null,
                    index < table.HeaderRowCount ? new XElement(W + "tblHeader") : null,
                    table.RowRevisions.TryGetValue(index, out string? rowRevision) ? Revision(rowRevision) : null));
            }

            foreach (TableCell cell in table.Rows[index])
            {
                var cellProperties = new XElement(W + "tcPr");
                if (cell.WidthPercent.HasValue) cellProperties.Add(new XElement(W + "tcW", new XAttribute(W + "type", "pct"), new XAttribute(W + "w", Math.Round(cell.WidthPercent.Value * 50))));
                else if (cell.CellWidth.HasValue) cellProperties.Add(new XElement(W + "tcW", new XAttribute(W + "type", "dxa"), new XAttribute(W + "w", Twips(cell.CellWidth.Value))));
                if (cell.ColumnSpan > 1) cellProperties.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", cell.ColumnSpan)));
                if (cell.Merge != VerticalMerge.None) cellProperties.Add(new XElement(W + "vMerge", new XAttribute(W + "val", cell.Merge == VerticalMerge.Start ? "restart" : "continue")));
                if (cell.Borders.Count > 0) cellProperties.Add(new XElement(W + "tcBorders", cell.Borders.Select(b => new XElement(W + b.Key,
                    new XAttribute(W + "val", b.Value.Width == 0 ? "nil" : b.Value.Style), new XAttribute(W + "sz", Math.Round(b.Value.Width * 8)), new XAttribute(W + "color", b.Value.Color)))));
                if (cell.Background != null) cellProperties.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", cell.Background)));
                if (cell.Padding != null) cellProperties.Add(new XElement(W + "tcMar",
                    Margin("top", cell.Padding.Top), Margin("left", cell.Padding.Left), Margin("bottom", cell.Padding.Bottom), Margin("right", cell.Padding.Right)));
                if (cell.VerticalAlignment != "top") cellProperties.Add(new XElement(W + "vAlign", new XAttribute(W + "val", cell.VerticalAlignment)));
                row.Add(new XElement(W + "tc", cellProperties.HasElements ? cellProperties : null, Blocks(cell.Blocks)));
            }

            if (table.RowControls.TryGetValue(index, out TemplateControl? control))
            {
                if (ReferenceEquals(previousControl, control)) rowContent!.Add(row);
                else { XElement wrapper = Control(control, row); element.Add(wrapper); rowContent = wrapper.Element(W + "sdtContent"); }
                previousControl = control;
            }
            else { element.Add(row); previousControl = null; rowContent = null; }
        }

        return element;
    }

    private IEnumerable<XElement> Blocks(IEnumerable<DocumentBlock> blocks) => BlockGroups(blocks).Select(g => g.Content);
    private IEnumerable<(DocumentBlock First, DocumentBlock Last, XElement Content)> BlockGroups(IEnumerable<DocumentBlock> blocks, bool includeSections = false)
    {
        DocumentBlock? first = null, last = null; XElement? group = null;
        foreach (DocumentBlock block in blocks)
        {
            if (first != null && (block.Control == null || !ReferenceEquals(first.Control, block.Control)))
            { yield return (first, last!, group!); first = null; group = null; }
            if (first != null && includeSections && !ReferenceEquals(last!.Section, block.Section))
                AttachSection(group!.Element(W + "sdtContent")!, last.Section ?? new PageSettings());
            XElement element = block is ParagraphBlock p ? Paragraph(p) : Table((TableBlock)block);
            if (block.Control == null) { yield return (block, block, element); continue; }
            if (first == null) { first = block; group = Control(block.Control, element); }
            else group!.Element(W + "sdtContent")!.Add(element);
            last = block;
        }
        if (first != null) yield return (first, last!, group!);
    }
    private XElement Control(TemplateControl control, XElement? content)
    {
        if (!control.Executable) context.Loss("Template.ControlStructure", "An unsupported repeat tag is omitted while retaining its cached content.");
        return new XElement(W + "sdt",
            new XElement(W + "sdtPr", control.Executable ? new XElement(W + "tag", new XAttribute(W + "val", control.Tag)) : null), new XElement(W + "sdtContent", content));
    }

    private static string Twips(double value) => Math.Round(value * 20).ToString(CultureInfo.InvariantCulture);
    private static XElement Margin(string side, double value) => new XElement(W + side, new XAttribute(W + "w", Twips(value)), new XAttribute(W + "type", "dxa"));

    private XElement Styles()
    {
        var styles = new XElement(W + "styles");
        foreach (string id in usedStyles.OrderBy(value => value, StringComparer.Ordinal))
        {
            var style = new XElement(W + "style", new XAttribute(W + "type", id == "WordnetCode" ? "character" : "paragraph"),
                new XAttribute(W + "styleId", id), new XElement(W + "name", new XAttribute(W + "val", model.Styles.TryGetValue(id, out string? sourceName) ? sourceName : id)));
            if (id == "WordnetCodeBlock" || id == "WordnetCode")
                style.Add(new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", "Consolas"), new XAttribute(W + "hAnsi", "Consolas"))));
            else if (model.Styles.ContainsKey(id)) { /* Appearance is resolved into direct properties. */ }
            else if (id.StartsWith("Heading", StringComparison.Ordinal))
            {
                int level = id[id.Length - 1] - '0';
                style.Add(new XElement(W + "pPr", new XElement(W + "outlineLvl", new XAttribute(W + "val", level - 1))));
            }
            else
            {
                style.Add(new XElement(W + "rPr", new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", "Consolas"), new XAttribute(W + "hAnsi", "Consolas"))));
            }

            styles.Add(style);
        }

        return styles;
    }

    private static XElement Relationship(string id, string type, string target) =>
        new XElement(DocxPackage.Relationships + "Relationship", new XAttribute("Id", id),
            new XAttribute("Type", DocxPackage.DocumentRelationships.NamespaceName + "/" + type), new XAttribute("Target", target));

    private static void WritePart(ZipArchive archive, string name, XElement root)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open();
        using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = TextFormat.Utf8, CloseOutput = false });
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
    }
}
