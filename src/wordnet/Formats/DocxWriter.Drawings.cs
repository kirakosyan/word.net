using System;
using System.Globalization;
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
            new XAttribute("simplePos", false), new XAttribute("relativeHeight", 0), new XAttribute("behindDoc", anchor.Behind),
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
        var properties = new XElement(wps + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", Emu(box.Width)), new XAttribute("cy", Emu(box.Height)))),
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")),
            box.Fill == null ? new XElement(A + "noFill") : new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", box.Fill))));
        if (box.Border != null) properties.Add(new XElement(A + "ln", new XAttribute("w", Emu(box.Border.Width)),
            new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", box.Border.Color)))));
        var container = new XElement(Wp + "inline", new XElement(Wp + "extent", new XAttribute("cx", Emu(box.Width)), new XAttribute("cy", Emu(box.Height))),
            new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", "Text box " + id.ToString(CultureInfo.InvariantCulture))),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", wps.NamespaceName),
                new XElement(wps + "wsp", new XElement(wps + "cNvSpPr", new XAttribute("txBox", true)), properties,
                    new XElement(wps + "txbx", new XElement(W + "txbxContent", box.Blocks.Select(Block))),
                    new XElement(wps + "bodyPr", new XAttribute("lIns", Emu(box.Padding.Left)), new XAttribute("rIns", Emu(box.Padding.Right)),
                        new XAttribute("tIns", Emu(box.Padding.Top)), new XAttribute("bIns", Emu(box.Padding.Bottom)), new XElement(A + "noAutofit"))))));
        Anchor(container, box.Anchor); return new XElement(W + "drawing", container);
    }

    private static string Emu(double points) => Math.Round(points * 12700).ToString(CultureInfo.InvariantCulture);
}
