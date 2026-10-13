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
        string cx = Math.Round(image.Width * 12700).ToString(CultureInfo.InvariantCulture), cy = Math.Round(image.Height * 12700).ToString(CultureInfo.InvariantCulture);
        relationships.Add(Relationship("rImage" + id, "image", "media/image" + id + "." + image.Extension));
        return new XElement(W + "drawing", new XElement(Wp + "inline",
            new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", "Image " + id), new XAttribute("descr", image.AlternativeText)),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", "Image " + id)), new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(DocxPackage.DocumentRelationships + "embed", "rImage" + id)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
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
            foreach (ListItem level in listLevels.Values.Where(l => l.Id == pair.Key).OrderBy(l => l.Level))
                abstractList.Add(new XElement(W + "lvl", new XAttribute(W + "ilvl", level.Level),
                    new XElement(W + "start", new XAttribute(W + "val", level.Start)),
                    new XElement(W + "numFmt", new XAttribute(W + "val", level.Ordered ? "decimal" : "bullet")),
                    new XElement(W + "lvlText", new XAttribute(W + "val", level.Ordered ? "%" + (level.Level + 1).ToString(CultureInfo.InvariantCulture) + "." : "•"))));
            numbering.Add(abstractList);
        }
        foreach (int id in listIds.Values) numbering.Add(new XElement(W + "num", new XAttribute(W + "numId", id), new XElement(W + "abstractNumId", new XAttribute(W + "val", id))));
        WritePart(archive, "word/numbering.xml", numbering);
    }
}
