using System;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private void WriteNotes(DocumentModel model, ZipArchive archive, XElement types)
    {
        foreach (bool endnote in new[] { false, true })
        {
            DocumentNote[] notes = model.Notes.Where(n => n.Endnote == endnote).ToArray();
            if (notes.Length == 0) continue;
            string kind = endnote ? "endnote" : "footnote", part = kind + "s.xml";
            relationships.Add(Relationship("r" + kind, kind + "s", part));
            types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/" + part),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml." + kind + "s+xml")));
            var root = new XElement(W + (kind + "s"), new XElement(W + kind, new XAttribute(W + "id", -1), new XAttribute(W + "type", "separator"),
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "separator")))));
            root.Add(new XElement(W + kind, new XAttribute(W + "id", -2), new XAttribute(W + "type", "continuationSeparator"),
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "continuationSeparator")))));
            int initialRelations = relationships.Count;
            foreach (DocumentNote note in notes)
            {
                var element = new XElement(W + kind, new XAttribute(W + "id", note.PartId));
                element.Add(Blocks(note.Blocks));
                XElement? first = element.Descendants(W + "p").FirstOrDefault();
                if (first != null)
                {
                    var reference = new XElement(W + "r", new XElement(W + (kind + "Ref")));
                    if (!note.ExplicitReferenceMark) reference.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), " "));
                    if (first.Element(W + "pPr") is XElement properties) properties.AddAfterSelf(reference); else first.AddFirst(reference);
                }
                root.Add(element);
            }
            WritePart(archive, "word/" + part, root);
            // Note images and hyperlinks resolve against their own part. Resources
            // live in word/media, so the same relative targets remain valid.
            if (relationships.Count > initialRelations) WritePart(archive, "word/_rels/" + part + ".rels",
                new XElement(DocxPackage.Relationships + "Relationships", relationships.Skip(initialRelations).Select(r => new XElement(r))));
        }
    }
}
