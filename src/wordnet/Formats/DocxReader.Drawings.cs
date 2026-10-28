using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private bool sawLinkedTextBox;
    private DrawingAnchor ReadAnchor(XElement element)
    {
        bool Flag(string name, bool fallback)
        {
            string? value = (string?)element.Attribute(name);
            if (value == null) return fallback;
            if (value is not "true" and not "false" and not "1" and not "0" and not "on" and not "off")
                throw new InvalidDataException("Shape.DrawingOrder: Invalid drawing placement flag.");
            return IsTrue(value);
        }
        var result = new DrawingAnchor { Behind = Flag("behindDoc", false), UnqualifiedLayer = !Flag("allowOverlap", true) || !Flag("layoutInCell", true) };
        if (element.Attribute("relativeHeight") is XAttribute height)
        {
            if (!uint.TryParse(height.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint layer))
                throw new InvalidDataException("Shape.DrawingOrder: Invalid drawing layer.");
            result.RelativeHeight = layer;
        }
        ReadPosition("positionH", true); ReadPosition("positionV", false);
        result.Distance = new CellPadding { Left = Distance("distL"), Right = Distance("distR"), Top = Distance("distT"), Bottom = Distance("distB") };
        if (element.Element(Wp + "wrapSquare") is XElement square)
        {
            result.Wrap = "square";
            if ((string?)square.Attribute("wrapText") is string sides && sides != "bothSides")
                context.Loss("Image.WrapApproximated", "One-sided square wrapping uses both sides of the drawing.");
        }
        if (element.Element(Wp + "wrapTopAndBottom") != null) result.Wrap = "topAndBottom";
        if (element.Element(Wp + "wrapTight") != null || element.Element(Wp + "wrapThrough") != null)
        { context.Loss("Image.WrapApproximated", "Tight/through wrapping uses the rectangular square-wrap profile."); result.Wrap = "square"; }
        if (IsTrue((string?)element.Attribute("simplePos"))) context.Loss("Image.AnchorApproximated", "Simple-position drawing coordinates use the retained page/margin positions.");
        return result;

        double Distance(string name) => element.Attribute(name) == null ? 0 : Integer(element, name, 0, 20116800) / 12700.0;
        void ReadPosition(string name, bool horizontal)
        {
            XElement? source = element.Element(Wp + name);
            if (source == null) return;
            string origin = (string?)source.Attribute("relativeFrom") ?? (horizontal ? "margin" : "paragraph");
            if (horizontal ? origin is not "page" and not "margin" and not "column" : origin is not "page" and not "margin" and not "paragraph")
            { context.Loss("Image.AnchorApproximated", "An unsupported anchor origin uses the text margin."); origin = "margin"; }
            string? alignment = source.Element(Wp + "align")?.Value;
            if (alignment is not null and not "left" and not "right" and not "center" and not "top" and not "bottom")
            { context.Loss("Image.AnchorApproximated", "Inside/outside anchor alignment uses the leading margin."); alignment = null; }
            double offset = 0;
            if (source.Element(Wp + "posOffset") is XElement position)
            {
                if (!int.TryParse(position.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value) || Math.Abs((long)value) > 20116800)
                    throw new InvalidDataException("Docx.AnchorPosition: Anchor offset exceeds the physical-page profile.");
                offset = value / 12700.0;
            }
            if (horizontal) { result.HorizontalOrigin = origin; result.HorizontalAlignment = alignment; result.X = offset; }
            else { result.VerticalOrigin = origin; result.VerticalAlignment = alignment; result.Y = offset; }
        }
    }

    private PositionedBox ReadBox(XElement drawing, XElement? content)
    {
        XNamespace wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        XElement? anchor = drawing.Element(Wp + "anchor");
        XElement extent = (anchor ?? drawing.Element(Wp + "inline"))?.Element(Wp + "extent") ?? throw new InvalidDataException("Docx.TextBox: Missing extent.");
        var box = new PositionedBox
        {
            Width = Integer(extent, "cx", 1, 20116800) / 12700.0,
            Height = Integer(extent, "cy", 1, 20116800) / 12700.0,
            Anchor = anchor == null ? new DrawingAnchor { Wrap = "topAndBottom" } : ReadAnchor(anchor)
        };
        XElement? head = drawing.Descendants(wps + "txbx").FirstOrDefault();
        XElement? continuation = drawing.Descendants(wps + "linkedTxbx").FirstOrDefault();
        if (head != null && continuation != null || content == null && continuation == null && head == null)
            throw new InvalidDataException("Shape.TextBoxChain: Invalid text-box story ownership.");
        if ((continuation != null || head?.Attribute("id") != null) && (drawing.Descendants(wps + "txbx").Skip(1).Any() || drawing.Descendants(wps + "linkedTxbx").Skip(1).Any()
            || head != null && (head.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "id") || head.Elements().Any(e => e.Name != W + "txbxContent"))))
            throw new NotSupportedException("Shape.TextBoxChain: Extended or nested linked-box metadata is unsupported.");
        if (continuation != null)
        {
            sawLinkedTextBox = true;
            if (continuation.HasElements || continuation.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "id" && a.Name != "seq"))
                throw new NotSupportedException("Shape.TextBoxChain: Extended linked-box metadata is unsupported.");
            box.StoryId = Integer(continuation, "id", 0, 65535); box.StorySequence = Integer(continuation, "seq", 1, 65535);
        }
        else if (head != null)
        {
            box.ImplicitStoryId = head.Attribute("id") == null;
            box.StoryId = box.ImplicitStoryId ? 0 : Integer(head, "id", 0, 65535);
        }
        XElement? shape = drawing.Descendants(wps + "spPr").FirstOrDefault();
        string? geometry = (string?)shape?.Element(A + "prstGeom")?.Attribute("prst");
        if (geometry is not null and not "rect") context.Loss("Shape.GeometryApproximated", "Only rectangular text boxes are supported.");
        XElement? transform = shape?.Element(A + "xfrm");
        if (transform?.Attribute("rot") is XAttribute rotation)
        {
            if (!int.TryParse(rotation.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int raw))
                throw new InvalidDataException("Shape.Rotation: Invalid drawing rotation.");
            int normalized = (int)(((long)raw % 21600000 + 21600000) % 21600000);
            if (normalized % 5400000 != 0) context.Loss("Shape.GeometryApproximated", "Only quarter-turn text-box rotation is supported.");
            else box.Rotation = normalized / 60000;
        }
        foreach (string name in new[] { "flipH", "flipV" })
        {
            string? value = (string?)transform?.Attribute(name);
            if (value is not null and not "true" and not "false" and not "1" and not "0" and not "on" and not "off")
                throw new InvalidDataException("Shape.Rotation: Invalid transform flip flag.");
            if (IsTrue(value)) context.Loss("Shape.GeometryApproximated", "Flipped text boxes are outside the supported profile.");
        }
        if (shape?.Elements().Any(e => e.Name != A + "xfrm" && e.Name != A + "prstGeom" && e.Name != A + "solidFill" && e.Name != A + "noFill" && e.Name != A + "ln") == true
            || shape?.Element(A + "solidFill") is XElement fill && fill.Element(A + "srgbClr") == null)
            context.Loss("Shape.Appearance", "Unsupported text-box effects, theme fills and drawing appearance are not retained.");
        box.Fill = ReadRgb((string?)shape?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val"));
        if (shape?.Element(A + "ln") is XElement line && line.Element(A + "noFill") == null)
            box.Border = new CellBorder
            {
                Color = ReadRgb((string?)line.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")) ?? "000000",
                Width = line.Attribute("w") == null ? 0.5 : Integer(line, "w", 0, 20116800) / 12700.0
            };
        XElement? body = drawing.Descendants(wps + "bodyPr").FirstOrDefault();
        if (body != null)
        {
            box.Padding.Left = Inset("lIns", 7.2); box.Padding.Right = Inset("rIns", 7.2); box.Padding.Top = Inset("tIns", 3.6); box.Padding.Bottom = Inset("bIns", 3.6);
            XElement? fit = body.Element(A + "normAutofit");
            if (fit != null)
            {
                if (body.Elements().Count() != 1 || fit.HasElements || fit.Attributes().Any(a => !a.IsNamespaceDeclaration
                    && !(a.Name == "fontScale" && a.Value == "100000") && !(a.Name == "lnSpcReduction" && a.Value == "0")))
                    throw new NotSupportedException("Shape.Autofit: Only the default bounded shrink-to-fit intent is supported.");
                box.ShrinkToFit = true;
            }
            if (body.Elements().Any(e => e.Name != A + "noAutofit" && e.Name != A + "normAutofit") || (string?)body.Attribute("vert") is string vertical && vertical != "horz")
                context.Loss("Shape.TextBoxLayout", "Unsupported text-box autofit and vertical text are not retained.");
        }
        if ((box.Rotation != 0 || box.ShrinkToFit) && box.Anchor.UnqualifiedLayer)
            context.Loss("Shape.DrawingOrder", "The drawing layer or placement is outside the rotated/fit profile.");
        if (content != null) ReadBlocks(content, box.Blocks); return box;
        double Inset(string name, double fallback) => body?.Attribute(name) == null ? fallback : Integer(body, name, 0, 20116800) / 12700.0;
    }

    private PositionedBox? ReadVectorShape(XElement drawing)
    {
        XNamespace wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        XElement[] shapes = drawing.Descendants(wps + "wsp").ToArray();
        if (shapes.Length != 1 || drawing.Element(Wp + "anchor") is not XElement anchor)
            throw new NotSupportedException("Shape.Vector: Only a single anchored vector shape is supported.");
        XElement? properties = shapes[0].Element(wps + "spPr");
        XElement? geometry = properties?.Element(A + "prstGeom"); string? kind = (string?)geometry?.Attribute("prst");
        if (kind is not "line" and not "ellipse" and not "roundRect")
        { context.Loss("Shape.VectorUnsupported", "The drawing geometry is outside the admitted vector profile."); return null; }
        if (properties == null || geometry == null || properties.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || properties.Elements().Any(e => e.Name != A + "xfrm" && e.Name != A + "prstGeom" && e.Name != A + "solidFill" && e.Name != A + "noFill" && e.Name != A + "ln")
            || geometry.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "prst") || geometry.Elements().Any(e => e.Name != A + "avLst" || e.HasElements || e.Attributes().Any(a => !a.IsNamespaceDeclaration))
            || geometry.Elements().Count() > 1 || shapes[0].Elements().Any(e => e.Name != wps + "cNvSpPr" && e.Name != wps + "spPr" && e.Name != wps + "bodyPr")
            || shapes[0].Element(wps + "cNvSpPr") is XElement visual && (visual.Attributes().Any(a => !a.IsNamespaceDeclaration) || visual.HasElements)
            || shapes[0].Element(wps + "bodyPr") is XElement body && (body.Attributes().Any(a => !a.IsNamespaceDeclaration) || body.HasElements)
            || anchor.Element(Wp + "docPr")?.HasElements == true)
            throw new NotSupportedException("Shape.Vector: Effects, adjustments, groups and text are outside the vector profile.");
        XElement extent = anchor.Element(Wp + "extent") ?? throw new InvalidDataException("Shape.Vector: Missing shape extent.");
        int cx = Integer(extent, "cx", 0, 20116800), cy = Integer(extent, "cy", 0, 20116800);
        if (cx == 0 && cy == 0 || kind != "line" && (cx == 0 || cy == 0)) throw new InvalidDataException("Shape.Vector: Degenerate shape extent.");
        XElement? transform = properties.Element(A + "xfrm");
        if (transform != null && (transform.Attributes().Any(a => !a.IsNamespaceDeclaration && !(a.Name == "rot" && a.Value == "0"))
            || transform.Elements().Any(e => e.Name != A + "off" && e.Name != A + "ext")
            || transform.Element(A + "off") is XElement offset && (Integer(offset, "x", 0, 0) != 0 || Integer(offset, "y", 0, 0) != 0)
            || transform.Element(A + "ext") is XElement dimensions && (Integer(dimensions, "cx", 0, 20116800) != cx || Integer(dimensions, "cy", 0, 20116800) != cy)))
            throw new NotSupportedException("Shape.Vector: Transformed vector geometry is unsupported.");
        var box = new PositionedBox
        {
            Geometry = kind,
            Width = cx / 12700.0,
            Height = cy / 12700.0,
            Anchor = ReadAnchor(anchor),
            AlternativeText = (string?)anchor.Element(Wp + "docPr")?.Attribute("descr") ?? ""
        };
        if (box.AlternativeText.Length > 1024) throw new InvalidDataException("Shape.Vector: Alternative text exceeds its bound.");
        if (box.Anchor.UnqualifiedLayer) throw new NotSupportedException("Shape.DrawingOrder: Nonzero vector drawing layers are unsupported.");
        XElement? fill = properties.Element(A + "solidFill");
        if (properties.Element(A + "noFill") is XElement noFill && (noFill.HasElements || noFill.Attributes().Any(a => !a.IsNamespaceDeclaration)))
            throw new NotSupportedException("Shape.Vector: Extended no-fill metadata is unsupported.");
        if (kind != "line" && fill == null && properties.Element(A + "noFill") == null)
            throw new NotSupportedException("Shape.Vector: Explicit fill or no-fill is required.");
        if (fill != null && properties.Element(A + "noFill") != null || kind == "line" && fill != null)
            throw new NotSupportedException("Shape.Vector: Conflicting or unsupported fills.");
        if (fill != null) box.Fill = SolidColor(fill);
        XElement? line = properties.Element(A + "ln");
        if (line != null)
        {
            if (line.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "w") || line.Elements().Any(e => e.Name != A + "solidFill" && e.Name != A + "noFill")
                || line.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1) || line.Element(A + "solidFill") != null && line.Element(A + "noFill") != null
                || line.Element(A + "noFill") is XElement empty && (empty.HasElements || empty.Attributes().Any(a => !a.IsNamespaceDeclaration)))
                throw new NotSupportedException("Shape.Vector: Only solid strokes are supported.");
            if (line.Element(A + "solidFill") is XElement stroke)
            {
                double width = line.Attribute("w") == null ? 0.5 : Integer(line, "w", 1, 20116800) / 12700.0;
                box.Border = new CellBorder { Color = SolidColor(stroke), Width = width };
            }
            else if (line.Element(A + "noFill") == null) throw new NotSupportedException("Shape.Vector: The stroke must specify a solid color or no fill.");
        }
        if (box.Fill == null && box.Border == null) throw new NotSupportedException("Shape.Vector: Invisible or unspecified vector appearance is unsupported.");
        return box;
        static string SolidColor(XElement solid)
        {
            XElement[] children = solid.Elements().ToArray();
            if (solid.Attributes().Any(a => !a.IsNamespaceDeclaration) || children.Length != 1 || children[0].Name != A + "srgbClr" || children[0].HasElements || children[0].Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != "val"))
                throw new NotSupportedException("Shape.Vector: Only opaque RGB colors are supported.");
            return ReadRgb(DocxPackage.RequiredAttribute(children[0], "val")) ?? throw new InvalidDataException("Shape.Vector: Missing RGB color.");
        }
    }
}
