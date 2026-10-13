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
        if (!numbers.TryGetValue(style.ListId, out XElement? num) || num.Element(W + "abstractNumId") is not XElement reference
            || !abstracts.TryGetValue(Integer(reference, W + "val", 0, int.MaxValue), out XElement? definition))
            throw new InvalidDataException("Docx.Numbering: Unresolved list definition.");
        XElement[] overrides = num.Elements(W + "lvlOverride").Where(e => Integer(e, W + "ilvl", 0, 8) == style.ListLevel).ToArray();
        XElement[] levels = definition.Elements(W + "lvl").Where(e => Integer(e, W + "ilvl", 0, 8) == style.ListLevel).ToArray();
        if (overrides.Length > 1 || levels.Length > 1) throw new InvalidDataException("Docx.Numbering: Duplicate list level.");
        XElement? level = overrides.FirstOrDefault()?.Element(W + "lvl") ?? levels.FirstOrDefault();
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
        XElement? start = overrides.FirstOrDefault()?.Element(W + "startOverride") ?? level.Element(W + "start");
        return new ListItem
        {
            Id = "docx-" + style.ListId.ToString(CultureInfo.InvariantCulture),
            Level = style.ListLevel,
            Ordered = format == "decimal",
            Start = start == null ? 1 : Integer(start, W + "val", 1, int.MaxValue - 1)
        };
    }

    private DocumentImage? ReadDrawing(XElement drawing)
    {
        XElement? inline = drawing.Element(Wp + "inline");
        if (inline == null) { context.Loss("Docx.FloatingImage", "Floating drawings are not imported by the inline image profile."); return null; }
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
        if (picture.Element(Pic + "blipFill")?.Element(A + "srcRect") != null || shape?.Element(A + "xfrm")?.HasAttributes == true
            || shape?.Elements().Any(e => e.Name != A + "xfrm" && e.Name != A + "prstGeom") == true
            || (string?)shape?.Element(A + "prstGeom")?.Attribute("prst") is string geometry && geometry != "rect"
            || blip!.HasElements || inline.Element(Wp + "effectExtent")?.Attributes().Any(a => a.Value != "0") == true)
            context.Loss("Docx.ImageEffects", "Image cropping, transforms and effects are not retained.");
        string path = DocxPackage.ResolvePart(mainPart, relation); knownParts.Add(path);
        return DocumentImage.Read(package.ReadBytes(path), width, height, (string?)inline.Element(Wp + "docPr")?.Attribute("descr") ?? "", context);
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
        if (properties?.Elements().Any(e => e.Name != W + "tblW" && e.Name != W + "tblCellMar" && e.Name != W + "tblLayout") == true)
            context.Loss("Docx.TableFormatting", "Table borders, shading and styles are not retained.");
        if (element.Element(W + "tblGrid")?.Elements().Any(e => e.Name != W + "gridCol") == true)
            context.Loss("Docx.TableGrid", "Table grid revisions are not retained.");
        foreach (XElement column in element.Element(W + "tblGrid")?.Elements(W + "gridCol") ?? Enumerable.Empty<XElement>())
            if (column.Attribute(W + "w") != null) table.ColumnWidths.Add(Integer(column, W + "w", 1, 31680) / 20.0);
    }

    private void ReadCellGeometry(XElement? defaults, XElement? properties, ParagraphBlock cell)
    {
        cell.CellWidth = ReadWidth(properties?.Element(W + "tcW"));
        CellPadding? padding = ReadPadding(defaults, null);
        cell.Padding = ReadPadding(properties?.Element(W + "tcMar"), padding);
        if (properties?.Elements().Any(e => e.Name != W + "tcW" && e.Name != W + "tcMar") == true)
            context.Loss("Docx.TableCell", "Cell merges, borders, shading and vertical alignment are not retained.");
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
