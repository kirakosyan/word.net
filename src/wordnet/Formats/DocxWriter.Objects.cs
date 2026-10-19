using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private readonly List<DocumentObjectData> retainedCharts = new List<DocumentObjectData>();
    private XElement RetainedObject(DocumentObjectData item)
    {
        if (item.Equation != null)
            return item.EquationXml == null ? MathObjectFormat.Write(item.Equation, item.Display, item.FontSize)
                : PackageArchive.ParseXml(System.Text.Encoding.UTF8.GetBytes(item.EquationXml), context).Root!;
        retainedCharts.Add(item); string index = retainedCharts.Count.ToString(CultureInfo.InvariantCulture), id = "rChart" + index;
        relationships.Add(Relationship(id, "chart", "charts/chart" + index + ".xml"));
        return new XElement(W + "r", new XElement(W + "drawing", new XElement(Wp + "inline",
            new XElement(Wp + "extent", new XAttribute("cx", Emu(item.Width)), new XAttribute("cy", Emu(item.Height))),
            new XElement(Wp + "docPr", new XAttribute("id", drawingId++), new XAttribute("name", "Chart " + index), new XAttribute("descr", item.Description)),
            new XElement(Wp + "cNvGraphicFramePr"), new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", ChartObjectFormat.C.NamespaceName),
                new XElement(ChartObjectFormat.C + "chart", new XAttribute(DocxPackage.DocumentRelationships + "id", id)))))));
    }
    private void WriteObjects(ZipArchive archive, XElement types)
    {
        int index = 0;
        foreach (DocumentObjectData item in retainedCharts)
        {
            context.CheckCancellation(); index++; ObjectGraph graph = item.Graph!; string prefix = index.ToString(CultureInfo.InvariantCulture); int partIndex = 0;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ObjectPart part in graph.Parts.Where(p => !p.Name.EndsWith(".rels", StringComparison.Ordinal)))
            {
                string path = part.Name == graph.Root ? "word/charts/chart" + prefix + ".xml" : part.ContentType == ChartObjectFormat.WorkbookType
                    ? "word/embeddings/chart" + prefix + ".xlsx" : "word/charts/chart" + prefix + "_part" + (++partIndex).ToString(CultureInfo.InvariantCulture) + Path.GetExtension(part.Name);
                names.Add(part.Name, path);
            }
            foreach (ObjectPart part in graph.Parts)
            {
                context.CheckCancellation();
                if (part.Name.EndsWith(".rels", StringComparison.Ordinal))
                {
                    string owner = names.Keys.Single(n => DocxPackage.RelationshipPartName(n) == part.Name);
                    XDocument relations = PackageArchive.ParseXml(part.Read(), context);
                    foreach (XElement relation in relations.Root!.Elements())
                    {
                        string target = ChartObjectFormat.Resolve(owner, relation);
                        if (!names.TryGetValue(target, out string? mapped)) throw new InvalidDataException("Object.GraphReference: A retained owner has no admitted target.");
                        relation.SetAttributeValue("Target", "/" + mapped);
                    }
                    WritePart(archive, DocxPackage.RelationshipPartName(names[owner]), relations.Root!);
                }
                else
                {
                    string path = names[part.Name]; PackageArchive.Write(archive, path, part.Read());
                    types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/" + path), new XAttribute("ContentType", part.ContentType)));
                }
            }
        }
    }
    private static void ObjectOutputBudget(DocumentModel document, ConversionContext context)
    {
        if (document.ObjectCount == 0) return;
        long parts = document.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Object?.Chart != null).Sum(r => (long)r.Object!.Graph!.Parts.Count);
        parts += document.Paragraphs.SelectMany(p => p.Runs).LongCount(r => r.Image != null) + document.Notes.Count;
        parts += document.Blocks.Select(b => b.Section).Concat(new[] { document.FinalSection }).Where(s => s != null).Distinct().Sum(s => s!.Stories.Count);
        if (parts + 8 > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Object.PartLimit: Serialized objects and document parts exceed the archive-entry allowance.");
    }
}
