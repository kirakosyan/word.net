using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private DrawingAnchor ReadAnchor(XElement element)
    {
        var result = new DrawingAnchor { Behind = IsTrue((string?)element.Attribute("behindDoc")) };
        ReadPosition("positionH", true); ReadPosition("positionV", false);
        result.Distance = new CellPadding { Left = Distance("distL"), Right = Distance("distR"), Top = Distance("distT"), Bottom = Distance("distB") };
        if (element.Element(Wp + "wrapSquare") != null) result.Wrap = "square";
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

    private PositionedBox ReadBox(XElement drawing, XElement content)
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
        XElement? shape = drawing.Descendants(wps + "spPr").FirstOrDefault();
        string? geometry = (string?)shape?.Element(A + "prstGeom")?.Attribute("prst");
        if (geometry is not null and not "rect" || shape?.Element(A + "xfrm")?.Attribute("rot") is XAttribute rotation && rotation.Value != "0")
            context.Loss("Shape.GeometryApproximated", "Only unrotated rectangular text boxes are supported.");
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
            if (body.Elements().Any(e => e.Name != A + "noAutofit") || (string?)body.Attribute("vert") is string vertical && vertical != "horz")
                context.Loss("Shape.TextBoxLayout", "Text-box autofit and vertical text are not supported.");
        }
        ReadBlocks(content, box.Blocks); return box;
        double Inset(string name, double fallback) => body?.Attribute(name) == null ? fallback : Integer(body, name, 0, 20116800) / 12700.0;
    }
}
