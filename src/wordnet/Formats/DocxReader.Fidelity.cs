using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
        if (root.Name != W + "numbering") throw new InvalidDataException("Docx.Numbering: Invalid root.");
        foreach (XElement item in root.Elements())
        {
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
        string format = (string?)level.Element(W + "numFmt")?.Attribute(W + "val") ?? "decimal";
        if (format is not "decimal" and not "bullet") { context.Loss("Docx.ListFormat", "Unsupported number formats become decimal numbering."); format = "decimal"; }
        string pattern = (string?)level.Element(W + "lvlText")?.Attribute(W + "val") ?? "";
        string expected = "%" + (style.ListLevel + 1).ToString(CultureInfo.InvariantCulture) + ".";
        if (format == "decimal" && pattern != expected || format == "bullet" && pattern is not "•" and not "\uf0b7")
            context.Loss("Docx.ListLabel", "Custom list labels become decimal numbers or round bullets.");
        if (level.Elements().Any(e => e.Name != W + "start" && e.Name != W + "numFmt" && e.Name != W + "lvlText"
            && e.Name != W + "lvlJc" && e.Name != W + "pPr" && e.Name != W + "rPr"))
            context.Loss("Docx.ListBehavior", "Custom list restart, suffix or legal numbering behavior is not retained.");
        if (level.Element(W + "pPr") != null || level.Element(W + "rPr") != null)
            context.Loss("Docx.ListAppearance", "List label styling and indentation use the standard list profile.");
        XElement? start = overrides.Select(e => e.Element(W + "startOverride") ?? e.Element(W + "lvl")?.Element(W + "start"))
            .FirstOrDefault(e => e != null) ?? level.Element(W + "start");
        return new ListItem
        {
            Id = "docx-" + style.ListId.ToString(CultureInfo.InvariantCulture),
            Level = style.ListLevel,
            Ordered = format == "decimal",
            Start = start == null ? 1 : Integer(start, W + "val", 0, int.MaxValue - 1)
        };
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
            context.Loss("Image.AnchorApproximated", "An anchored image is placed inline in reading order; wrapping and positioning are approximated.");
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
        if (picture.Element(Pic + "blipFill")?.Element(A + "srcRect") != null || shape?.Element(A + "xfrm")?.Attributes().Any(a => !a.IsNamespaceDeclaration) == true
            || shape?.Elements().Any(e => e.Name != A + "xfrm" && e.Name != A + "prstGeom") == true
            || (string?)shape?.Element(A + "prstGeom")?.Attribute("prst") is string geometry && geometry != "rect"
            || blip!.HasElements || inline.Element(Wp + "effectExtent")?.Attributes().Any(a => a.Value != "0") == true)
            context.Loss("Docx.ImageEffects", "Image cropping, transforms and effects are not retained.");
        string path = DocxPackage.ResolvePart(mainPart, relation); knownParts.Add(path);
        byte[] bytes = package.ReadBytes(path);
        string alt = (string?)inline.Element(Wp + "docPr")?.Attribute("descr") ?? "";
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase))
        {
            context.Loss("Image.UnsupportedFormat", "An unsupported vector image uses a visible placeholder at its declared size.");
            return DocumentImage.Placeholder(width, height, alt, context);
        }
        try { return DocumentImage.Read(bytes, width, height, alt, context); }
        catch (NotSupportedException)
        {
            context.Loss("Image.UnsupportedFormat", "An unsupported image format uses a visible placeholder at its declared size.");
            return DocumentImage.Placeholder(width, height, alt, context);
        }
    }

    private double? ReadWidth(XElement? element)
    {
        if (element == null || (string?)element.Attribute(W + "type") is "auto" or "nil") return null;
        if ((string?)element.Attribute(W + "type") != "dxa") { context.Loss("Docx.RelativeWidth", "Percentage table widths become automatic widths."); return null; }
        return Integer(element, W + "w", 1, 31680) / 20.0;
    }

    private void ReadTableGeometry(XElement element, TableBlock table)
    {
        XElement? properties = element.Element(W + "tblPr");
        table.Width = ReadWidth(properties?.Element(W + "tblW"));
        if (properties?.Element(W + "tblInd") is XElement indent)
        {
            if ((string?)indent.Attribute(W + "type") == "dxa") table.LeftIndent = Integer(indent, W + "w", -31680, 31680) / 20.0;
            else context.Loss("Docx.TableIndent", "Nonabsolute table indentation is not retained.");
        }
        if (properties?.Elements().Any(e => e.Name != W + "tblW" && e.Name != W + "tblCellMar" && e.Name != W + "tblLayout" && e.Name != W + "tblInd") == true)
            context.Loss("Docx.TableFormatting", "Table borders, shading and styles are not retained.");
        if (element.Element(W + "tblGrid")?.Elements().Any(e => e.Name != W + "gridCol") == true)
            context.Loss("Docx.TableGrid", "Table grid revisions are not retained.");
        foreach (XElement column in element.Element(W + "tblGrid")?.Elements(W + "gridCol") ?? Enumerable.Empty<XElement>())
            table.ColumnWidths.Add(column.Attribute(W + "w") != null ? Integer(column, W + "w", 1, 31680) / 20.0 : 0);
    }

    private void ReadCellGeometry(XElement? defaults, XElement? properties, TableCell cell)
    {
        cell.CellWidth = ReadWidth(properties?.Element(W + "tcW"));
        CellPadding? padding = ReadPadding(defaults, null);
        cell.Padding = ReadPadding(properties?.Element(W + "tcMar"), padding);
        if (properties?.Element(W + "shd") is XElement shading)
        {
            cell.Background = ReadRgb((string?)shading.Attribute(W + "fill"));
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
            if (style is "nil" or "none") { cell.Borders[side] = new CellBorder { Width = 0 }; continue; }
            if (style is not "single" and not "double" and not "dashed" and not "dotted")
            { context.Loss("Table.BorderApproximated", "An unsupported border style uses a single line."); style = "single"; }
            cell.Borders[side] = new CellBorder
            {
                Style = style,
                Color = ReadRgb((string?)border.Attribute(W + "color")) ?? "000000",
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
