using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private static readonly XNamespace W = DocxPackage.Word;
    private readonly ConversionContext context;
    private readonly Dictionary<string, XElement> relationships;
    private readonly Dictionary<string, XElement> styles = new Dictionary<string, XElement>(StringComparer.Ordinal);
    private string? defaultStyle;
    private ParagraphAlignment defaultAlignment;
    private TextStyle defaultRun = new TextStyle();

    private DocxReader(ConversionContext context, Dictionary<string, XElement> relationships)
    {
        this.context = context;
        this.relationships = relationships;
    }

    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        using var package = new DocxPackage(input, context);
        var mainRelations = package.ReadRelationships("").Values
            .Where(relation => (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/officeDocument").ToArray();
        if (mainRelations.Length != 1)
        {
            throw new InvalidDataException("Docx.MainPart: Exactly one document relationship is required.");
        }

        string main = DocxPackage.ResolvePart("", mainRelations[0]);
        XDocument types = package.ReadXml("[Content_Types].xml");
        if (types.Root?.Name != DocxPackage.ContentTypes + "Types"
            || !types.Root.Elements(DocxPackage.ContentTypes + "Override").Any(item =>
                (string?)item.Attribute("PartName") == "/" + main && (string?)item.Attribute("ContentType") == DocxPackage.MainContentType))
        {
            throw new InvalidDataException("Docx.ContentType: A non-macro DOCX main document content type is required.");
        }

        var reader = new DocxReader(context, package.ReadRelationships(main));
        var knownParts = new HashSet<string>(StringComparer.Ordinal)
        {
            "[Content_Types].xml", "_rels/.rels", main, DocxPackage.RelationshipPartName(main),
        };
        XElement[] styleRelations = reader.relationships.Values.Where(relation =>
            (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/styles").ToArray();
        if (styleRelations.Length > 1)
        {
            throw new InvalidDataException("Docx.Styles: Multiple style parts are not valid.");
        }

        if (styleRelations.Length == 1)
        {
            string stylePart = DocxPackage.ResolvePart(main, styleRelations[0]);
            reader.ReadStyles(package.ReadXml(stylePart));
            knownParts.Add(stylePart);
        }

        reader.package = package;
        reader.mainPart = main;
        reader.knownParts = knownParts;
        reader.ReadNumbering();

        XDocument source = package.ReadXml(main);
        if (source.Root?.Name != W + "document")
        {
            throw new NotSupportedException("Docx.Namespace: Only Transitional WordprocessingML is supported in this development version.");
        }

        XElement[] bodies = source.Root.Elements(W + "body").ToArray();
        if (bodies.Length != 1)
        {
            throw new InvalidDataException("Docx.Body: Exactly one document body is required.");
        }

        var document = new DocumentModel();
        reader.ReadBlocks(bodies[0], document.Blocks);
        if (package.PartNames.Any(name => !knownParts.Contains(name)))
            context.Loss("Docx.AdditionalParts", "Additional parts such as metadata or headers are not preserved in this conversion profile.");
        return document;
    }

    private void ReadStyles(XDocument source)
    {
        XElement root = source.Root ?? throw new InvalidDataException("Docx.Styles: Missing style root.");
        if (root.Name != W + "styles")
        {
            throw new InvalidDataException("Docx.Styles: Invalid style namespace.");
        }

        var defaults = new TextStyle();
        ReadParagraphProperties(root.Element(W + "docDefaults")?.Element(W + "pPrDefault")?.Element(W + "pPr"), defaults);
        defaultAlignment = defaults.Alignment;
        ReadRunProperties(root.Element(W + "docDefaults")?.Element(W + "rPrDefault")?.Element(W + "rPr"), defaultRun);

        foreach (XElement style in root.Elements(W + "style"))
        {
            string id = DocxPackage.RequiredAttribute(style, W + "styleId");
            if (styles.ContainsKey(id))
            {
                throw new InvalidDataException("Docx.Styles: Duplicate style identifier.");
            }

            styles.Add(id, style);
            if ((string?)style.Attribute(W + "type") == "paragraph" && IsTrue((string?)style.Attribute(W + "default")))
            {
                defaultStyle = id;
            }
        }
    }

    private void ReadBlocks(XElement container, List<DocumentBlock> blocks)
    {
        foreach (XElement element in container.Elements())
        {
            context.CheckCancellation();
            if (element.Name == W + "p")
            {
                blocks.Add(ReadParagraph(element));
            }
            else if (element.Name == W + "tbl")
            {
                blocks.Add(ReadTable(element));
            }
            else if (element.Name == W + "sectPr")
            {
                if (element.HasElements)
                {
                    context.Loss("Docx.PageLayout", "Section geometry, headers and page layout are not preserved in text/Markdown.");
                }
            }
            else
            {
                context.Loss("Docx.Block", "Unsupported document block structure is flattened where supported content is available.");
                if (element.Name != W + "del")
                {
                    ReadBlocks(element, blocks);
                }
            }
        }
    }

    private ParagraphBlock ReadParagraph(XElement element)
    {
        var style = ResolveStyle(defaultStyle, new HashSet<string>(StringComparer.Ordinal));
        XElement? properties = element.Element(W + "pPr");
        string? styleId = (string?)properties?.Element(W + "pStyle")?.Attribute(W + "val");
        if (styleId != null)
        {
            style = ResolveStyle(styleId, new HashSet<string>(StringComparer.Ordinal));
        }

        ReadParagraphProperties(properties, style);
        var paragraph = new ParagraphBlock { HeadingLevel = style.Heading, IsCodeBlock = style.CodeBlock, Alignment = style.Alignment, List = ResolveList(style) };
        ReadInlines(element, paragraph.Runs, style, null);
        return paragraph;
    }

    private void ReadInlines(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        foreach (XElement child in element.Elements())
        {
            context.CheckCancellation();
            if (child.Name == W + "pPr" || child.Name == W + "rPr")
            {
                continue;
            }

            if (child.Name == W + "r")
            {
                TextStyle style = inherited.Copy();
                ReadRunProperties(child.Element(W + "rPr"), style);
                ReadInlines(child, runs, style, link);
            }
            else if (child.Name == W + "t")
            {
                runs.Add(new TextRun(child.Value, inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color));
            }
            else if (child.Name == W + "tab")
            {
                runs.Add(new TextRun("\t", inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color));
            }
            else if (child.Name == W + "br" || child.Name == W + "cr")
            {
                string? type = (string?)child.Attribute(W + "type");
                if (type != null && type != "textWrapping")
                {
                    context.Loss("Docx.Break", "Page and column breaks become line breaks.");
                }

                runs.Add(new TextRun("\n", inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color));
            }
            else if (child.Name == W + "drawing")
            {
                DocumentImage? image = ReadDrawing(child);
                if (image != null) runs.Add(new TextRun("", link: link, image: image));
            }
            else if (child.Name == W + "hyperlink")
            {
                string? id = (string?)child.Attribute(DocxPackage.DocumentRelationships + "id");
                string? target = null;
                if (id != null && relationships.TryGetValue(id, out XElement? relation)
                    && (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/hyperlink"
                    && (string?)relation.Attribute("TargetMode") == "External")
                {
                    target = context.SafeLink((string?)relation.Attribute("Target"));
                }
                else
                {
                    context.Loss("Docx.Hyperlink", "An internal or unresolved hyperlink becomes plain text.");
                }

                ReadInlines(child, runs, inherited, target);
            }
            else
            {
                context.Loss("Docx.Inline", "Unsupported inline content or annotations are flattened where text is available.");
                if (child.Name != W + "del" && child.Name != W + "delText" && child.Name != W + "instrText")
                {
                    ReadInlines(child, runs, inherited, link);
                }
            }
        }
    }

    private TableBlock ReadTable(XElement element)
    {
        var table = new TableBlock();
        ReadTableGeometry(element, table);
        foreach (XElement child in element.Elements())
        {
            context.CheckCancellation();
            if (child.Name == W + "tblPr" || child.Name == W + "tblGrid")
            {
                continue;
            }

            if (child.Name != W + "tr")
            {
                context.Loss("Docx.TableStructure", "Unsupported table structure is omitted.");
                continue;
            }

            var cells = new List<ParagraphBlock>();
            foreach (XElement rowChild in child.Elements())
            {
                if (rowChild.Name == W + "trPr")
                {
                    XElement? header = rowChild.Element(W + "tblHeader");
                    table.HasHeaderRow |= table.Rows.Count == 0 && header != null && IsOn(header);
                    if (rowChild.Elements().Any(property => property.Name != W + "tblHeader"))
                    {
                        context.Loss("Docx.TableFormatting", "Table row formatting is not retained.");
                    }

                    continue;
                }

                if (rowChild.Name != W + "tc")
                {
                    context.Loss("Docx.TableStructure", "Unsupported table-row content is omitted.");
                    continue;
                }

                var cell = new ParagraphBlock();
                ReadCellGeometry(element.Element(W + "tblPr")?.Element(W + "tblCellMar"), rowChild.Element(W + "tcPr"), cell);
                bool firstParagraph = true;
                foreach (XElement content in rowChild.Elements())
                {
                    if (content.Name == W + "tcPr") continue;
                    if (content.Name != W + "p")
                    {
                        context.Loss("Docx.TableCell", "Cell merges, cell formatting and nested tables are not supported.");
                        continue;
                    }

                    if (!firstParagraph)
                    {
                        cell.Runs.Add(new TextRun("\n"));
                        context.Loss("Docx.CellParagraphs", "Multiple table-cell paragraphs become line breaks.");
                    }

                    ParagraphBlock paragraph = ReadParagraph(content);
                    if (firstParagraph) { cell.Alignment = paragraph.Alignment; cell.List = paragraph.List; }
                    else if (paragraph.List != null) context.Loss("Docx.CellList", "Only the first cell paragraph's list membership is retained.");
                    if (paragraph.HeadingLevel != 0 || paragraph.IsCodeBlock)
                    {
                        context.Loss("Docx.CellParagraphFormatting", "Heading and block-code formatting inside table cells is flattened.");
                    }

                    cell.Runs.AddRange(paragraph.Runs);
                    firstParagraph = false;
                }

                cells.Add(cell);
            }

            table.Rows.Add(cells);
        }

        return table;
    }

    private TextStyle ResolveStyle(string? id, HashSet<string> visiting, bool characterStyle = false)
    {
        if (id == null)
        {
            TextStyle initial = characterStyle ? new TextStyle() : defaultRun.Copy(); initial.Alignment = defaultAlignment; return initial;
        }

        if (!visiting.Add(id) || visiting.Count > context.Options.MaximumNestingDepth)
        {
            throw new InvalidDataException("Docx.StyleCycle: Styles contain a cycle or exceed the nesting limit.");
        }

        if (!styles.TryGetValue(id, out XElement? element))
        {
            context.Loss("Docx.MissingStyle", "A referenced style is missing; default text formatting is used.");
            TextStyle initial = characterStyle ? new TextStyle() : defaultRun.Copy(); initial.Alignment = defaultAlignment; return initial;
        }

        TextStyle style = ResolveStyle((string?)element.Element(W + "basedOn")?.Attribute(W + "val"), visiting, characterStyle);
        style.Code |= id == "WordnetCode";
        style.CodeBlock |= id == "WordnetCodeBlock";
        ReadParagraphProperties(element.Element(W + "pPr"), style);
        ReadRunProperties(element.Element(W + "rPr"), style, knownCodeStyle: style.Code || style.CodeBlock, visiting: visiting);
        return style;
    }

    private void ReadParagraphProperties(XElement? properties, TextStyle style)
    {
        if (properties == null)
        {
            return;
        }

        foreach (XElement property in properties.Elements())
        {
            if (property.Name == W + "outlineLvl")
            {
                if (!int.TryParse((string?)property.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                    || level < 0 || level > 9)
                {
                    throw new InvalidDataException("Docx.Outline: Invalid outline level.");
                }

                if (level > 5 && level != 9)
                {
                    context.Loss("Docx.HeadingLevel", "Heading levels greater than six are not supported in Markdown.");
                }

                style.Heading = level < 6 ? level + 1 : 0;
            }
            else if (property.Name == W + "jc")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                switch (value)
                {
                    case "left": style.Alignment = ParagraphAlignment.Left; break;
                    case "center": style.Alignment = ParagraphAlignment.Center; break;
                    case "right": style.Alignment = ParagraphAlignment.Right; break;
                    case "both":
                    case "distribute":
                    case "mediumKashida":
                    case "highKashida":
                    case "lowKashida":
                    case "thaiDistribute":
                    case "numTab":
                    case "start":
                    case "end":
                        context.Loss("Docx.Alignment", "Justified, distributed and direction-dependent alignment becomes left alignment.");
                        style.Alignment = ParagraphAlignment.Left; break;
                    default: throw new InvalidDataException("Docx.Alignment: Invalid paragraph justification value.");
                }
            }
            else if (property.Name == W + "numPr")
            {
                if (property.Element(W + "numId") is XElement num) style.ListId = Integer(num, W + "val", 0, int.MaxValue);
                if (property.Element(W + "ilvl") is XElement lvl) style.ListLevel = Integer(lvl, W + "val", 0, 8);
                if (property.Elements().Any(e => e.Name != W + "numId" && e.Name != W + "ilvl")) context.Loss("Docx.ListRevision", "Numbering revisions are not retained.");
            }
            else if (property.Name != W + "pStyle")
            {
                context.Loss("Docx.ParagraphFormatting", "Paragraph formatting such as numbering, spacing or page controls is not retained.");
            }
        }
    }

    private void ReadRunProperties(XElement? properties, TextStyle style, bool knownCodeStyle = false, HashSet<string>? visiting = null)
    {
        if (properties == null)
        {
            return;
        }

        foreach (XElement property in properties.Elements())
        {
            if (property.Name == W + "b")
            {
                // Style-level emphasis is a toggle; direct run formatting is absolute.
                style.Bold = visiting == null ? IsOn(property) : style.Bold ^ IsOn(property);
            }
            else if (property.Name == W + "i")
            {
                style.Italic = visiting == null ? IsOn(property) : style.Italic ^ IsOn(property);
            }
            else if (property.Name == W + "rStyle")
            {
                TextStyle character = ResolveStyle((string?)property.Attribute(W + "val"), visiting ?? new HashSet<string>(StringComparer.Ordinal), characterStyle: true);
                style.Code |= character.Code;
                style.Bold ^= character.Bold;
                style.Italic ^= character.Italic;
                style.FontSize = character.FontSize ?? style.FontSize;
                if (character.HasColor) { style.Color = character.Color; style.HasColor = true; }
            }
            else if (property.Name == W + "sz") style.FontSize = Integer(property, W + "val", 2, 3276) / 2.0;
            else if (property.Name == W + "color")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                if (value != "auto" && (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                    throw new InvalidDataException("Docx.Color: Expected six RGB hexadecimal digits or auto.");
                style.Color = value == "auto" ? null : value.ToUpperInvariant();
                style.HasColor = true;
                if (property.Attributes().Any(a => a.Name != W + "val")) context.Loss("Docx.ThemeColor", "Theme color uses its stored RGB fallback.");
            }
            else if (!(knownCodeStyle && property.Name == W + "rFonts"))
            {
                context.Loss("Docx.RunFormatting", "Run formatting beyond bold, italic and recognized code styles is not retained.");
            }
        }
    }

    private static bool IsOn(XElement element)
    {
        string? value = (string?)element.Attribute(W + "val");
        if (value == null || IsTrue(value))
        {
            return true;
        }

        if (value == "false" || value == "0" || value == "off")
        {
            return false;
        }

        throw new InvalidDataException("Docx.Boolean: Invalid on/off property value.");
    }

    private static bool IsTrue(string? value) => value == "true" || value == "1" || value == "on";

    private sealed class TextStyle
    {
        internal bool Bold { get; set; }
        internal bool Italic { get; set; }
        internal bool Code { get; set; }
        internal bool CodeBlock { get; set; }
        internal int Heading { get; set; }
        internal ParagraphAlignment Alignment { get; set; }
        internal double? FontSize { get; set; }
        internal string? Color { get; set; }
        internal bool HasColor { get; set; }
        internal int ListId { get; set; }
        internal int ListLevel { get; set; }

        internal TextStyle Copy() => (TextStyle)MemberwiseClone();
    }
}
