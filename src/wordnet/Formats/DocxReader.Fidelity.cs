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
    private DocxPackage package = null!;
    private string mainPart = "";
    private HashSet<string> knownParts = null!;
    private readonly Dictionary<int, XElement> numbers = new Dictionary<int, XElement>();
    private readonly Dictionary<int, XElement> abstracts = new Dictionary<int, XElement>();
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private static int Integer(XElement e, XName name, int min, int max)
    {
        if (!int.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
            throw new InvalidDataException("Docx.Number: Invalid numeric property.");
        return n;
    }

    private void ReadNumbering()
    {
        XElement[] links = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/numbering").ToArray();
        if (links.Length > 1) throw new InvalidDataException("Docx.Numbering: Duplicate numbering part.");
        if (links.Length == 0) return;
        string path = DocxPackage.ResolvePart(mainPart, links[0]); knownParts.Add(path);
        XElement root = package.ReadXml(path).Root ?? throw new InvalidDataException("Docx.Numbering: Missing root.");
        using var partLocation = context.At(XmlSourceLocation.Create(root, path));
        if (root.Name != W + "numbering") throw new InvalidDataException("Docx.Numbering: Invalid root.");
        foreach (XElement item in root.Elements())
        {
            using var location = context.At(XmlSourceLocation.Create(item, path));
            Dictionary<int, XElement>? dictionary = item.Name == W + "num" ? numbers : item.Name == W + "abstractNum" ? abstracts : null;
            if (dictionary == null) { context.Loss("Docx.NumberingFeature", "Picture bullets and auxiliary numbering metadata are not retained."); continue; }
            int id = Integer(item, W + (item.Name == W + "num" ? "numId" : "abstractNumId"), 0, int.MaxValue);
            if (dictionary.ContainsKey(id)) throw new InvalidDataException("Docx.Numbering: Duplicate numbering identifier.");
            dictionary.Add(id, item);
        }
    }

    private ListItem? ResolveList(TextStyle style)
    {
        if (style.ListId == 0) return null;
        var overrides = new List<XElement>();
        XElement definition = ResolveNumbering(style.ListId, style.ListLevel, overrides);
        XElement[] levels = definition.Elements(W + "lvl").Where(e => Integer(e, W + "ilvl", 0, 8) == style.ListLevel).ToArray();
        if (levels.Length > 1) throw new InvalidDataException("Docx.Numbering: Duplicate list level.");
        XElement? level = overrides.Select(e => e.Element(W + "lvl")).FirstOrDefault(e => e != null) ?? levels.FirstOrDefault();
        if (level == null) throw new InvalidDataException("Docx.Numbering: Missing list level.");
        var result = new ListItem { Id = "docx-" + style.ListId.ToString(CultureInfo.InvariantCulture), Level = style.ListLevel };
        var definitionLevels = new ListLevel[9];
        for (int i = 0; i < 9; i++)
        {
            var levelOverrides = new List<XElement>();
            XElement abstractLevel = ResolveNumbering(style.ListId, i, levelOverrides);
            XElement? source = levelOverrides.Select(e => e.Element(W + "lvl")).FirstOrDefault(e => e != null)
                ?? abstractLevel.Elements(W + "lvl").SingleOrDefault(e => Integer(e, W + "ilvl", 0, 8) == i);
            var parsed = new ListLevel { RestartAfter = i - 1, Pattern = "%" + (i + 1).ToString(CultureInfo.InvariantCulture) + "." };
            definitionLevels[i] = parsed;
            if (source == null) continue;
            parsed.Format = (string?)source.Element(W + "numFmt")?.Attribute(W + "val") ?? "decimal";
            if (parsed.Format is not "decimal" and not "bullet" and not "lowerLetter" and not "upperLetter" and not "lowerRoman" and not "upperRoman")
            { context.Loss("Docx.ListFormat", "An unsupported number format uses decimal numbering."); parsed.Format = "decimal"; }
            parsed.Pattern = (string?)source.Element(W + "lvlText")?.Attribute(W + "val") ?? parsed.Pattern;
            if (parsed.Pattern.Length > 128) throw new InvalidDataException("Limit.ListLabel: A list label exceeds 128 characters.");
            if (parsed.Pattern.Any(c => c >= 0xe000 && c <= 0xf8ff))
            {
                if (parsed.Pattern == "\uf0b7") parsed.Pattern = "•";
                else { context.Loss("Docx.ListLabel", "A private-use bullet without portable glyph semantics uses a round bullet."); parsed.Pattern = "•"; }
            }
            XElement? start = levelOverrides.Select(e => e.Element(W + "startOverride") ?? e.Element(W + "lvl")?.Element(W + "start"))
                .FirstOrDefault(e => e != null) ?? source.Element(W + "start");
            parsed.Start = start == null ? 1 : Integer(start, W + "val", 0, int.MaxValue - 1);
            if (source.Element(W + "lvlRestart") is XElement restart) parsed.RestartAfter = Integer(restart, W + "val", 0, i) - 1;
            parsed.Legal = source.Element(W + "isLgl") is XElement legal && IsOn(legal);
            parsed.Suffix = (string?)source.Element(W + "suff")?.Attribute(W + "val") ?? "tab";
            if (parsed.Suffix is not "tab" and not "space" and not "nothing") throw new InvalidDataException("Docx.ListSuffix: Invalid suffix.");
            var appearance = new TextStyle();
            ReadParagraphProperties(source.Element(W + "pPr"), appearance); ReadRunProperties(source.Element(W + "rPr"), appearance);
            if ((string?)source.Element(W + "lvlJc")?.Attribute(W + "val") is string justification && justification != "left")
                context.Loss("Docx.ListAppearance", "Centered/right-aligned list labels use left label alignment.");
            parsed.Layout = appearance.Layout;
            foreach (XElement property in source.Element(W + "pPr")?.Elements() ?? Enumerable.Empty<XElement>()) parsed.LayoutProperties.Add(property.Name.LocalName);
            parsed.Appearance = new TextRun("", appearance.Bold, appearance.Italic, fontSize: appearance.FontSize, color: appearance.Color, appearance: appearance.Appearance);
            if (source.Elements().Any(e => e.Name != W + "start" && e.Name != W + "numFmt" && e.Name != W + "lvlText" && e.Name != W + "lvlJc"
                && e.Name != W + "pPr" && e.Name != W + "rPr" && e.Name != W + "lvlRestart" && e.Name != W + "isLgl" && e.Name != W + "suff"))
                context.Loss("Docx.ListBehavior", "Unsupported auxiliary numbering behavior is not retained.");
        }
        result.Definition = definitionLevels; result.Start = definitionLevels[style.ListLevel].Start; result.Ordered = definitionLevels[style.ListLevel].Format != "bullet";
        return result;
    }

    private XElement ResolveNumbering(int id, int level, List<XElement> overrides)
    {
        var visited = new HashSet<int>();
        while (true)
        {
            context.CheckCancellation();
            if (!visited.Add(id) || visited.Count > context.Options.MaximumNestingDepth)
                throw new InvalidDataException("Docx.NumberingCycle: Numbering style links cycle or exceed the nesting limit.");
            if (!numbers.TryGetValue(id, out XElement? num) || num.Element(W + "abstractNumId") is not XElement reference
                || !abstracts.TryGetValue(Integer(reference, W + "val", 0, int.MaxValue), out XElement? definition))
                throw new InvalidDataException("Docx.Numbering: Unresolved list definition.");
            XElement[] matching = num.Elements(W + "lvlOverride").Where(e => Integer(e, W + "ilvl", 0, 8) == level).ToArray();
            if (matching.Length > 1) throw new InvalidDataException("Docx.Numbering: Duplicate list override.");
            overrides.AddRange(matching); // Closest numbering instance takes precedence.
            string? link = (string?)definition.Element(W + "numStyleLink")?.Attribute(W + "val");
            if (link == null) return definition;
            if (!styles.TryGetValue(link, out XElement? style) || (string?)style.Attribute(W + "type") != "numbering")
                throw new InvalidDataException("Docx.Numbering: Unresolved numbering style.");
            id = ResolveStyle(link, new HashSet<string>(StringComparer.Ordinal)).ListId;
            if (id == 0) throw new InvalidDataException("Docx.Numbering: Numbering style has no numbering reference.");
        }
    }

    private DocumentImage? ReadDrawing(XElement drawing)
    {
        XElement? inline = drawing.Element(Wp + "inline");
        if (inline == null)
        {
            inline = drawing.Element(Wp + "anchor");
            if (inline == null) { context.Loss("Docx.Drawing", "Unsupported drawings are omitted."); return null; }

        }
        XElement? picture = inline.Element(A + "graphic")?.Element(A + "graphicData")?.Element(Pic + "pic");
        XElement? blip = picture?.Element(Pic + "blipFill")?.Element(A + "blip");
        string? id = (string?)blip?.Attribute(DocxPackage.DocumentRelationships + "embed");
        if (id == null || !relationships.TryGetValue(id, out XElement? relation)
            || (string?)relation.Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/image")
        { context.Loss("Docx.Drawing", "Unsupported or linked drawings are omitted; remote images are never fetched."); return null; }
        if ((string?)relation.Attribute("TargetMode") == "External") { context.Loss("Image.External", "Remote images are not fetched."); return null; }
        XElement extent = inline.Element(Wp + "extent") ?? throw new InvalidDataException("Docx.Image: Missing drawing extent.");
        double width = Integer(extent, "cx", 1, 20116800) / 12700.0;
        double height = Integer(extent, "cy", 1, 20116800) / 12700.0;
        XElement? shape = picture!.Element(Pic + "spPr");
        if (shape?.Elements().Any(e => e.Name != A + "xfrm" && e.Name != A + "prstGeom") == true
            || (string?)shape?.Element(A + "prstGeom")?.Attribute("prst") is string geometry && geometry != "rect"
            || blip!.HasElements || inline.Element(Wp + "effectExtent")?.Attributes().Any(a => a.Value != "0") == true)
            context.Loss("Docx.ImageEffects", "Unsupported image effects and nonrectangular geometry are not retained.");
        string path = DocxPackage.ResolvePart(mainPart, relation); knownParts.Add(path);
        byte[] bytes = package.ReadBytes(path);
        string alt = (string?)inline.Element(Wp + "docPr")?.Attribute("descr") ?? "";
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase))
        {
            context.Loss("Image.UnsupportedFormat", "An unsupported vector image uses a visible placeholder at its declared size.");
            return DocumentImage.Placeholder(width, height, alt, context);
        }
        try
        {
            DocumentImage image = DocumentImage.Read(bytes, width, height, alt, context);
            image.Decorative = inline.Element(Wp + "docPr")?.Descendants(XName.Get("decorative", "http://schemas.microsoft.com/office/drawing/2017/decorative")).Any(e => (string?)e.Attribute("val") is "1" or "true") == true;
            XElement? crop = picture.Element(Pic + "blipFill")?.Element(A + "srcRect");
            double Crop(string side)
            {
                if (crop?.Attribute(side) == null) return 0;
                int value = Integer(crop, side, int.MinValue, 100000);
                if (value < 0) context.Loss("Docx.ImageCropPadding", "Outward image crop padding is omitted; the original image edge is retained.");
                return Math.Max(0, value) / 100000.0;
            }
            image.CropLeft = Crop("l"); image.CropRight = Crop("r"); image.CropTop = Crop("t"); image.CropBottom = Crop("b");
            if (image.CropLeft + image.CropRight >= 1 || image.CropTop + image.CropBottom >= 1)
                throw new InvalidDataException("Docx.ImageCrop: Cropping must retain positive image area.");
            XElement? transform = shape?.Element(A + "xfrm");
            if (transform?.Attribute("rot") != null)
            {
                int rotation = Integer(transform, "rot", -21600000, 21600000);
                if (rotation % 5400000 != 0) context.Loss("Docx.ImageEffects", "Arbitrary-angle image rotation is not retained.");
                else image.Rotation = (rotation / 60000 % 360 + 360) % 360;
            }
            bool Flip(string side)
            {
                string? value = (string?)transform?.Attribute(side);
                if (value is null or "false" or "0" or "off") return false;
                if (IsTrue(value)) return true;
                throw new InvalidDataException("Docx.ImageTransform: Invalid flip value.");
            }
            image.FlipHorizontal = Flip("flipH"); image.FlipVertical = Flip("flipV");
            image.Anchor = inline.Name == Wp + "anchor" ? ReadAnchor(inline) : null;
            return image;
        }
        catch (NotSupportedException)
        {
            context.Loss("Image.UnsupportedFormat", "An unsupported image format uses a visible placeholder at its declared size.");
            return DocumentImage.Placeholder(width, height, alt, context);
        }
    }

    private double? ReadWidth(XElement? element, Action<double>? percentage = null)
    {
        if (element == null || (string?)element.Attribute(W + "type") is "auto" or "nil") return null;
        if ((string?)element.Attribute(W + "type") == "pct" && percentage != null)
        { percentage(Integer(element, W + "w", 1, 5000) / 50.0); return null; }
        if ((string?)element.Attribute(W + "type") != "dxa") { context.Loss("Docx.RelativeWidth", "Percentage table widths become automatic widths."); return null; }
        return Integer(element, W + "w", 1, 31680) / 20.0;
    }

    private void ReadTableGeometry(XElement element, TableBlock table)
    {
        XElement? properties = element.Element(W + "tblPr");
        XElement[] styleReferences = properties?.Elements(W + "tblStyle").ToArray() ?? Array.Empty<XElement>();
        if (styleReferences.Length > 1) throw new InvalidDataException("Docx.TableStyle: Duplicate table style references.");
        if (styleReferences.Length == 1)
        {
            string id = DocxPackage.RequiredAttribute(styleReferences[0], W + "val");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024) throw new InvalidDataException("Docx.TableStyle: Invalid table style identifier.");
            if (styleReferences[0].HasElements || styleReferences[0].Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || styleReferences[0].Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != W + "val"))
                context.Loss("Docx.TableStyleData", "Unsupported table-style reference metadata is not retained.");
            table.Style = ResolveTableStyle(id, new HashSet<string>(StringComparer.Ordinal));
        }
        else if (hasDefaultTableStyle)
            context.Loss("Docx.TableStyleDefault", "Implicit default table styles are not applied or retained.");
        table.Width = ReadWidth(properties?.Element(W + "tblW"), value => table.WidthPercent = value);
        string? layout = (string?)properties?.Element(W + "tblLayout")?.Attribute(W + "type");
        if (layout is not null and not "fixed" and not "autofit") throw new InvalidDataException("Docx.TableLayout: Invalid layout type.");
        table.AutoFit = layout == "autofit";
        table.ManagedImplicitAutoFit = layout == null;
        table.LayoutExplicit = layout != null;
        if (properties?.Element(W + "tblInd") is XElement indent)
        {
            if ((string?)indent.Attribute(W + "type") == "dxa") table.LeftIndent = Integer(indent, W + "w", -31680, 31680) / 20.0;
            else context.Loss("Docx.TableIndent", "Nonabsolute table indentation is not retained.");
        }
        XElement[] borderSets = properties?.Elements(W + "tblBorders").ToArray() ?? Array.Empty<XElement>();
        if (borderSets.Length > 1) throw new InvalidDataException("Docx.TableBorders: Duplicate table border sets.");
        if (borderSets.Length == 1) ReadTableBorders(borderSets[0], table);
        XElement[] shading = properties?.Elements(W + "shd").ToArray() ?? Array.Empty<XElement>();
        if (shading.Length > 1) throw new InvalidDataException("Docx.TableShading: Duplicate table shading properties.");
        if (shading.Length == 1) ReadTableShading(shading[0], table);
        if (table.Style != null && (borderSets.Length != 0 || shading.Length != 0))
            context.Loss("Table.StyleConflict", "Direct table borders or shading combined with a table style are not independently qualified.");
        if (properties?.Elements().Any(e => e.Name != W + "tblStyle" && e.Name != W + "tblW" && e.Name != W + "tblCellMar" && e.Name != W + "tblLayout" && e.Name != W + "tblInd" && e.Name != W + "tblBorders" && e.Name != W + "shd") == true)
            context.Loss("Docx.TableFormatting", "Unsupported table properties such as styles and spacing are not retained.");
        if (element.Element(W + "tblGrid")?.Elements().Any(e => e.Name != W + "gridCol") == true)
            context.Loss("Docx.TableGrid", "Table grid revisions are not retained.");
        foreach (XElement column in element.Element(W + "tblGrid")?.Elements(W + "gridCol") ?? Enumerable.Empty<XElement>())
            table.ColumnWidths.Add(column.Attribute(W + "w") != null ? Integer(column, W + "w", 1, 31680) / 20.0 : 0);
    }

    private TableStyle? ResolveTableStyle(string id, HashSet<string> visiting)
    {
        context.CheckCancellation();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1024) throw new InvalidDataException("Docx.TableStyle: Invalid table style identifier.");
        if (tableStyles.TryGetValue(id, out TableStyle? cached)) return cached;
        if (!styles.TryGetValue(id, out XElement? source) || (string?)source.Attribute(W + "type") != "table")
        { context.Loss("Docx.TableStyleMissing", "A missing or non-table style is not applied or retained."); return null; }
        if (!visiting.Add(id) || visiting.Count > Math.Min(64, context.Options.MaximumNestingDepth))
            throw new InvalidDataException("Docx.TableStyleCycle: Table-style inheritance cycles or exceeds the nesting limit.");
        XElement[] names = source.Elements(W + "name").ToArray(), parents = source.Elements(W + "basedOn").ToArray(), properties = source.Elements(W + "tblPr").ToArray();
        if (names.Length > 1 || parents.Length > 1 || properties.Length > 1) throw new InvalidDataException("Docx.TableStyle: Duplicate table style properties.");
        string name = names.Length == 0 ? id : DocxPackage.RequiredAttribute(names[0], W + "val");
        if (name.Length > 1024) throw new InvalidDataException("Limit.TableStyles: A table style name is too long.");
        if (source.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != W + "type" && attribute.Name != W + "styleId")
            || source.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || source.Elements().Any(child => child.Name != W + "name" && child.Name != W + "basedOn" && child.Name != W + "tblPr")
            || names.Concat(parents).Any(child => child.HasElements || child.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || child.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != W + "val")))
            context.Loss("Docx.TableStyleData", "Unsupported table-style properties or metadata are not retained.");
        TableStyle? parent = parents.Length == 0 ? null : ResolveTableStyle(DocxPackage.RequiredAttribute(parents[0], W + "val"), visiting);
        var appearance = new TableBlock();
        if (properties.Length != 0)
        {
            XElement container = properties[0];
            XElement[] borders = container.Elements(W + "tblBorders").ToArray(), shading = container.Elements(W + "shd").ToArray();
            if (borders.Length > 1 || shading.Length > 1) throw new InvalidDataException("Docx.TableStyle: Duplicate style appearance properties.");
            if (borders.Length != 0) ReadTableBorders(borders[0], appearance);
            if (shading.Length != 0) ReadTableShading(shading[0], appearance);
            if (container.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration)
                || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || container.Elements().Any(child => child.Name != W + "tblBorders" && child.Name != W + "shd"))
                context.Loss("Docx.TableStyleData", "Unsupported whole-table style properties are not retained.");
        }
        visiting.Remove(id);
        tableStyleBytes = checked(tableStyleBytes + ((long)id.Length + name.Length) * 2 + 256);
        if (tableStyles.Count >= 10000 || tableStyleBytes > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Limit.TableStyles: Table-style metadata exceeds the operation allowance.");
        var result = new TableStyle(id, name, parent, appearance.Borders, appearance.Background);
        tableStyles.Add(id, result); return result;
    }

    private void ReadTableShading(XElement shading, TableBlock table)
    {
        string pattern = (string?)shading.Attribute(W + "val") ?? throw new InvalidDataException("Docx.TableShading: Table shading requires a pattern.");
        if (pattern != "clear") context.Loss("Table.ShadingApproximated", "Only clear table shading is retained; other patterns use the background color.");
        if (shading.HasElements || shading.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            context.Loss("Table.ShadingApproximated", "Unsupported table-shading content is not retained.");
        if (shading.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "val" && a.Name != W + "fill" && a.Name != W + "color"
            && a.Name != W + "themeFill" && a.Name != W + "themeFillTint" && a.Name != W + "themeFillShade"
            && a.Name != W + "themeColor" && a.Name != W + "themeTint" && a.Name != W + "themeShade"))
            context.Loss("Table.ShadingApproximated", "Unsupported table-shading attributes are not retained.");
        string? foreground = (string?)shading.Attribute(W + "color");
        if (foreground != null && foreground != "auto")
        {
            _ = ReadRgb(foreground);
            context.Loss("Table.ShadingApproximated", "The foreground color of table shading is not retained.");
        }
        _ = ThemeReference(shading, "themeColor", "themeTint", "themeShade", null);
        table.Background = ThemeReference(shading, "themeFill", "themeFillTint", "themeFillShade", ReadRgb((string?)shading.Attribute(W + "fill")));
        if (table.Background == null) context.Loss("Table.ShadingColor", "Automatic or missing table-shading colors are not retained.");
    }

    private void ReadTableBorders(XElement source, TableBlock table)
    {
        if (source.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            context.Loss("Table.BorderApproximated", "Unsupported table-border set attributes are not retained.");
        if (source.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            context.Loss("Table.BorderApproximated", "Unsupported table-border set content is not retained.");
        foreach (XElement border in source.Elements())
        {
            context.CheckCancellation();
            string side = border.Name.LocalName;
            if (border.Name.Namespace != W || side is not ("top" or "left" or "bottom" or "right" or "insideH" or "insideV"))
            { context.Loss("Table.BorderApproximated", "Direction-dependent and unsupported table borders are omitted."); continue; }
            if (table.Borders.ContainsKey(side)) throw new InvalidDataException("Docx.TableBorders: Duplicate table border side.");
            string style = (string?)border.Attribute(W + "val") ?? throw new InvalidDataException("Docx.TableBorders: A table border requires a style.");
            if (border.HasElements || border.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                context.Loss("Table.BorderApproximated", "Unsupported table-border content is not retained.");
            if (style is not ("single" or "nil" or "none"))
            { context.Loss("Table.BorderApproximated", "An unsupported table border style uses a single line."); style = "single"; }
            if (border.Attribute(W + "space") != null && Integer(border, W + "space", 0, 31) != 0
                || border.Attributes().Any(a => (a.Name == W + "shadow" || a.Name == W + "frame") && a.Value is not ("0" or "false" or "off")))
                context.Loss("Table.BorderApproximated", "Border spacing, shadow and frame effects are not retained.");
            if (border.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "val" && a.Name != W + "sz" && a.Name != W + "color"
                && a.Name != W + "space" && a.Name != W + "shadow" && a.Name != W + "frame"
                && a.Name != W + "themeColor" && a.Name != W + "themeTint" && a.Name != W + "themeShade"))
                context.Loss("Table.BorderApproximated", "Unsupported table-border attributes are not retained.");
            if (style == "single" && ((string?)border.Attribute(W + "color") is null or "auto") && border.Attribute(W + "themeColor") == null)
                context.Loss("Table.BorderColor", "Automatic table-border colors use black.");
            string? color = ThemeReference(border, "themeColor", "themeTint", "themeShade", ReadRgb((string?)border.Attribute(W + "color")));
            double width = border.Attribute(W + "sz") == null ? 0.5 : Integer(border, W + "sz", 0, 255) / 8.0;
            table.Borders.Add(side, new CellBorder { Style = style, Color = color ?? "000000", Width = style is "nil" or "none" ? 0 : width });
        }
    }

    private void ReadCellGeometry(XElement? defaults, XElement? properties, TableCell cell)
    {
        cell.CellWidth = ReadWidth(properties?.Element(W + "tcW"), value => cell.WidthPercent = value);
        CellPadding? padding = ReadPadding(defaults, null);
        cell.Padding = ReadPadding(properties?.Element(W + "tcMar"), padding);
        if (properties?.Element(W + "shd") is XElement shading)
        {
            cell.Background = ThemeReference(shading, "themeFill", "themeFillTint", "themeFillShade", ReadRgb((string?)shading.Attribute(W + "fill")));
            _ = ThemeReference(shading, "themeColor", "themeTint", "themeShade", null);
            if ((string?)shading.Attribute(W + "val") is string pattern && pattern is not "clear" and not "solid" and not "nil")
                context.Loss("Table.ShadingApproximated", "Patterned cell shading uses its solid background color.");
        }
        if (properties?.Element(W + "vAlign") is XElement vertical)
        {
            string value = (string?)vertical.Attribute(W + "val") ?? "top";
            if (value is not "top" and not "center" and not "bottom") throw new InvalidDataException("Docx.TableCell: Invalid vertical alignment.");
            cell.VerticalAlignment = value;
        }
        foreach (XElement border in properties?.Element(W + "tcBorders")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            string side = border.Name.LocalName;
            if (side is "start") side = "left";
            if (side is "end") side = "right";
            if (side is not "left" and not "right" and not "top" and not "bottom") { context.Loss("Table.BorderApproximated", "Diagonal or direction-dependent borders are omitted."); continue; }
            string style = (string?)border.Attribute(W + "val") ?? "single";
            if (style is "nil" or "none")
            {
                _ = ThemeReference(border, "themeColor", "themeTint", "themeShade", null);
                cell.Borders[side] = new CellBorder { Width = 0 }; continue;
            }
            if (style is not "single" and not "double" and not "dashed" and not "dotted")
            { context.Loss("Table.BorderApproximated", "An unsupported border style uses a single line."); style = "single"; }
            cell.Borders[side] = new CellBorder
            {
                Style = style,
                Color = ThemeReference(border, "themeColor", "themeTint", "themeShade", ReadRgb((string?)border.Attribute(W + "color"))) ?? "000000",
                Width = border.Attribute(W + "sz") == null ? 0.5 : Integer(border, W + "sz", 0, 255) / 8.0
            };
        }
        if (properties?.Element(W + "gridSpan") is XElement span) cell.ColumnSpan = Integer(span, W + "val", 1, 1024);
        if (properties?.Element(W + "vMerge") is XElement merge)
            cell.Merge = ((string?)merge.Attribute(W + "val") ?? "continue") switch
            {
                "restart" => VerticalMerge.Start,
                "continue" => VerticalMerge.Continue,
                _ => throw new InvalidDataException("Docx.TableMerge: Invalid merge value.")
            };
        if (properties?.Elements().Any(e => e.Name != W + "tcW" && e.Name != W + "tcMar" && e.Name != W + "gridSpan" && e.Name != W + "vMerge"
            && e.Name != W + "shd" && e.Name != W + "vAlign" && e.Name != W + "tcBorders") == true)
            context.Loss("Docx.TableCell", "Unsupported cell properties are not retained.");
    }

    private static string? ReadRgb(string? value)
    {
        if (value is null or "auto") return null;
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("Docx.Color: Invalid RGB color.");
        return value.ToUpperInvariant();
    }

    private CellPadding? ReadPadding(XElement? source, CellPadding? inherited)
    {
        if (source == null) return inherited;
        CellPadding result = inherited?.Copy() ?? new CellPadding();
        foreach (XElement side in source.Elements())
        {
            if ((string?)side.Attribute(W + "type") != "dxa") { context.Loss("Docx.CellPadding", "Only absolute cell margins are supported."); continue; }
            double value = Integer(side, W + "w", 0, 31680) / 20.0;
            switch (side.Name.LocalName)
            {
                case "top": result.Top = value; break;
                case "bottom": result.Bottom = value; break;
                case "left": case "start": result.Left = value; break;
                case "right": case "end": result.Right = value; break;
                default: context.Loss("Docx.CellPadding", "Unknown cell margin side is omitted."); break;
            }
        }
        return result;
    }
}
