using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private int drawingId = 1000000;

    private static void Anchor(XElement container, DrawingAnchor anchor)
    {
        container.Name = Wp + "anchor";
        container.Add(new XAttribute("distT", Emu(anchor.Distance.Top)), new XAttribute("distB", Emu(anchor.Distance.Bottom)),
            new XAttribute("distL", Emu(anchor.Distance.Left)), new XAttribute("distR", Emu(anchor.Distance.Right)),
            new XAttribute("simplePos", false), new XAttribute("relativeHeight", anchor.RelativeHeight), new XAttribute("behindDoc", anchor.Behind),
            new XAttribute("locked", false), new XAttribute("layoutInCell", true), new XAttribute("allowOverlap", true));
        container.AddFirst(new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement(Wp + "positionH", new XAttribute("relativeFrom", anchor.HorizontalOrigin), anchor.HorizontalAlignment == null
                ? new XElement(Wp + "posOffset", Emu(anchor.X)) : new XElement(Wp + "align", anchor.HorizontalAlignment)),
            new XElement(Wp + "positionV", new XAttribute("relativeFrom", anchor.VerticalOrigin), anchor.VerticalAlignment == null
                ? new XElement(Wp + "posOffset", Emu(anchor.Y)) : new XElement(Wp + "align", anchor.VerticalAlignment)));
        container.Element(Wp + "extent")!.AddAfterSelf(new XElement(Wp + (anchor.Wrap == "square" ? "wrapSquare" : anchor.Wrap == "topAndBottom" ? "wrapTopAndBottom" : "wrapNone"),
            anchor.Wrap == "square" ? new XAttribute("wrapText", "bothSides") : null));
    }

    private XElement BoxDrawing(PositionedBox box)
    {
        XNamespace wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        int id = drawingId++;
        var properties = new XElement(wps + "spPr", new XElement(A + "xfrm", box.Rotation == 0 ? null : new XAttribute("rot", box.Rotation * 60000), new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", Emu(box.Width)), new XAttribute("cy", Emu(box.Height)))),
            new XElement(A + "prstGeom", new XAttribute("prst", box.Geometry ?? "rect"), new XElement(A + "avLst")),
            box.Gradient != null ? Gradient(box.Gradient)
                : box.Fill == null ? new XElement(A + "noFill") : new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", box.Fill))));
        if (box.Border != null) properties.Add(new XElement(A + "ln", new XAttribute("w", Emu(box.Border.Width)),
            new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", box.Border.Color)))));
        var container = new XElement(Wp + "inline", new XElement(Wp + "extent", new XAttribute("cx", Emu(box.Width)), new XAttribute("cy", Emu(box.Height))),
            new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", (box.Geometry == null ? "Text box " : "Shape ") + id.ToString(CultureInfo.InvariantCulture)),
                box.AlternativeText.Length == 0 ? null : new XAttribute("descr", box.AlternativeText),
                box.AlternativeTitle.Length == 0 ? null : new XAttribute("title", box.AlternativeTitle)),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", wps.NamespaceName),
                new XElement(wps + "wsp", new XElement(wps + "cNvSpPr", box.Geometry == null ? new XAttribute("txBox", true) : null), properties,
                    box.Geometry != null ? null : box.StorySequence > 0 ? new XElement(wps + "linkedTxbx", new XAttribute("id", box.StoryId ?? throw new InvalidDataException("Shape.TextBoxChain: Missing story identifier.")), new XAttribute("seq", box.StorySequence))
                        : new XElement(wps + "txbx", box.StoryId == null || box.ImplicitStoryId ? null : new XAttribute("id", box.StoryId.Value), new XElement(W + "txbxContent", box.Blocks.Count == 0 ? new object[] { new XElement(W + "p") } : Blocks(box.Blocks))),
                    box.Geometry != null ? new XElement(wps + "bodyPr") : new XElement(wps + "bodyPr", new XAttribute("lIns", Emu(box.Padding.Left)), new XAttribute("rIns", Emu(box.Padding.Right)),
                        new XAttribute("tIns", Emu(box.Padding.Top)), new XAttribute("bIns", Emu(box.Padding.Bottom)), new XElement(A + (box.ShrinkToFit ? "normAutofit" : "noAutofit")))))));
        Anchor(container, box.Anchor); return new XElement(W + "drawing", container);
    }

    private static string Emu(double points) => Math.Round(points * 12700).ToString(CultureInfo.InvariantCulture);

    private static XElement Gradient(LinearGradient gradient) => new XElement(A + "gradFill",
        new XElement(A + "gsLst", gradient.Stops.Select(stop => new XElement(A + "gs", new XAttribute("pos", stop.Position),
            new XElement(A + (stop.Theme == null ? "srgbClr" : "schemeClr"), new XAttribute("val", stop.Theme ?? stop.Color))))),
        new XElement(A + "lin", new XAttribute("ang", gradient.Angle), new XAttribute("scaled", gradient.Scaled)));
}
