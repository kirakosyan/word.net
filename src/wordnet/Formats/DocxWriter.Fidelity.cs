using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private readonly List<DocumentImage> images = new List<DocumentImage>();
    private readonly Dictionary<string, int> listIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, ListItem> listLevels = new Dictionary<string, ListItem>(StringComparer.Ordinal);
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private XElement NumberingReference(ListItem item)
    {
        if (!listIds.TryGetValue(item.Id, out int id)) listIds.Add(item.Id, id = listIds.Count + 1);
        listLevels[item.Id + ":" + item.Level.ToString(CultureInfo.InvariantCulture)] = item;
        return new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", item.Level)), new XElement(W + "numId", new XAttribute(W + "val", id)));
    }

    private XElement Drawing(DocumentImage image)
    {
        images.Add(image);
        string id = images.Count.ToString(CultureInfo.InvariantCulture);
        string cx = Math.Round(image.FrameWidth * 12700).ToString(CultureInfo.InvariantCulture), cy = Math.Round(image.FrameHeight * 12700).ToString(CultureInfo.InvariantCulture);
        relationships.Add(Relationship("rImage" + id, "image", "media/image" + id + "." + image.Extension));
        var drawing = new XElement(W + "drawing", new XElement(Wp + "inline",
            new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", "Image " + id), new XAttribute("descr", image.AlternativeText), image.Decorative ? new XElement(A + "extLst", new XElement(A + "ext", new XAttribute("uri", "{C183D7F6-B498-43B3-948B-1728B52AA6E4}"), new XElement(XName.Get("decorative", "http://schemas.microsoft.com/office/drawing/2017/decorative"), new XAttribute("val", "1")))) : null),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", "Image " + id)), new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(DocxPackage.DocumentRelationships + "embed", "rImage" + id)), image.Transformed ? new XElement(A + "srcRect", new XAttribute("l", Math.Round(image.CropLeft * 100000)), new XAttribute("r", Math.Round(image.CropRight * 100000)), new XAttribute("t", Math.Round(image.CropTop * 100000)), new XAttribute("b", Math.Round(image.CropBottom * 100000))) : null,
                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr", new XElement(A + "xfrm", new XAttribute("rot", image.Rotation * 60000), new XAttribute("flipH", image.FlipHorizontal), new XAttribute("flipV", image.FlipVertical), new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
        if (image.Anchor != null) Anchor(drawing.Element(Wp + "inline")!, image.Anchor);
        return drawing;
    }

    private void WriteResources(ZipArchive archive, XElement types)
    {
        foreach (string extension in images.Select(i => i.Extension).Distinct())
            types.Add(new XElement(DocxPackage.ContentTypes + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", extension == "png" ? "image/png" : "image/jpeg")));
        for (int i = 0; i < images.Count; i++)
        {
            context.CheckCancellation();
            ZipArchiveEntry entry = archive.CreateEntry("word/media/image" + (i + 1).ToString(CultureInfo.InvariantCulture) + "." + images[i].Extension);
            entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream stream = entry.Open(); byte[] bytes = images[i].Bytes; stream.Write(bytes, 0, bytes.Length);
        }
        if (listIds.Count == 0) return;
        relationships.Add(Relationship("rNumbering", "numbering", "numbering.xml"));
        types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/numbering.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml")));
        var numbering = new XElement(W + "numbering");
        foreach (var pair in listIds)
        {
            var abstractList = new XElement(W + "abstractNum", new XAttribute(W + "abstractNumId", pair.Value));
            ListItem[] items = listLevels.Values.Where(l => l.Id == pair.Key).OrderBy(l => l.Level).ToArray();
            ListLevel[]? definition = items.Select(l => l.Definition).FirstOrDefault(d => d != null);
            foreach (int index in definition == null ? items.Select(l => l.Level) : Enumerable.Range(0, 9))
            {
                ListItem? item = items.FirstOrDefault(l => l.Level == index);
                ListLevel? level = definition?[index];
                var element = new XElement(W + "lvl", new XAttribute(W + "ilvl", index),
                    new XElement(W + "start", new XAttribute(W + "val", item?.Start ?? level?.Start ?? 1)),
                    new XElement(W + "numFmt", new XAttribute(W + "val", level?.Format ?? (item!.Ordered ? "decimal" : "bullet"))));
                if (level != null && level.RestartAfter != index - 1) element.Add(new XElement(W + "lvlRestart", new XAttribute(W + "val", level.RestartAfter + 1)));
                if (level?.Legal == true) element.Add(new XElement(W + "isLgl"));
                if (level != null) element.Add(new XElement(W + "suff", new XAttribute(W + "val", level.Suffix)));
                element.Add(new XElement(W + "lvlText", new XAttribute(W + "val", level?.Pattern ?? (item!.Ordered ? "%" + (index + 1).ToString(CultureInfo.InvariantCulture) + "." : "•"))));
                if (level != null)
                {
                    var properties = new XElement(W + "pPr"); ParagraphLayoutProperties(properties, level.Layout);
                    if (properties.HasElements) element.Add(properties);
                    XElement? appearance = Run(level.Appearance).Element(W + "rPr"); if (appearance != null) element.Add(appearance);
                }
                abstractList.Add(element);
            }
            numbering.Add(abstractList);
        }
        foreach (int id in listIds.Values) numbering.Add(new XElement(W + "num", new XAttribute(W + "numId", id), new XElement(W + "abstractNumId", new XAttribute(W + "val", id))));
        WritePart(archive, "word/numbering.xml", numbering);
    }
}
