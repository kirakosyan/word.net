using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private bool noteReferenceMark;
    private readonly Dictionary<string, DocumentNote> notes = new Dictionary<string, DocumentNote>(StringComparer.Ordinal);
    private readonly HashSet<string> noteIds = new HashSet<string>(StringComparer.Ordinal);

    private void ReadNotes()
    {
        foreach (string kind in new[] { "footnotes", "endnotes" })
        {
            XElement[] links = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/" + kind).ToArray();
            if (links.Length > 1) throw new InvalidDataException("Docx.Notes: Duplicate note part.");
            if (links.Length == 0) continue;
            string path = DocxPackage.ResolvePart(mainPart, links[0]); knownParts.Add(path); knownParts.Add(DocxPackage.RelationshipPartName(path));
            XElement root = package.ReadXml(path).Root ?? throw new InvalidDataException("Docx.Notes: Missing note root.");
            using var partLocation = context.At(XmlSourceLocation.Create(root, path));
            if (root.Name != W + kind) throw new InvalidDataException("Docx.Notes: Invalid note root.");
            string singular = kind.Substring(0, kind.Length - 1);
            foreach (XElement note in root.Elements(W + singular))
            {
                using var noteLocation = context.At(XmlSourceLocation.Create(note, path));
                int id = Integer(note, W + "id", int.MinValue, int.MaxValue);
                if (id < 0 || (string?)note.Attribute(W + "type") is "separator" or "continuationSeparator")
                {
                    if (note.Descendants().Any(e => e.Name == W + "t" || e.Name == W + "drawing" || e.Name == W + "tbl" || e.Name == W + "rPr"))
                        context.Loss("Notes.Separator", "Custom note separator content and appearance use a standard horizontal rule.");
                    continue;
                }
                string label = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!noteIds.Add(kind + ":" + label)) throw new InvalidDataException("Docx.Notes: Duplicate note identifier.");

                var reader = new DocxReader(context, package.ReadRelationships(path))
                { package = package, mainPart = path, knownParts = knownParts, defaultAlignment = defaultAlignment, defaultRun = defaultRun.Copy(), defaultLayout = defaultLayout.Copy(), defaultStyle = defaultStyle };
                foreach (var pair in styles) reader.styles.Add(pair.Key, pair.Value);
                foreach (var pair in numbers) reader.numbers.Add(pair.Key, pair.Value);
                foreach (var pair in abstracts) reader.abstracts.Add(pair.Key, pair.Value);
                var blocks = new List<DocumentBlock>(); reader.ReadBlocks(note, blocks);
                sawLinkedTextBox |= reader.sawLinkedTextBox;
                if (reader.fields.Count > 0) throw new InvalidDataException("Docx.Field: Unclosed note field.");
                XElement? firstParagraph = note.Elements(W + "p").FirstOrDefault();
                if (note.Descendants(W + (kind == "footnotes" ? "footnoteRef" : "endnoteRef")).Any(mark => !ReferenceEquals(mark.Ancestors(W + "p").FirstOrDefault(), firstParagraph)))
                    context.Loss("Notes.ReferenceMark", "A note reference mark outside the first ordinary paragraph uses a generated leading label.");
                foreach (ParagraphBlock paragraph in DocumentModel.Expand(blocks).OfType<ParagraphBlock>()) paragraph.FallbackFontSize = null;
                var semantic = new DocumentNote { ExplicitReferenceMark = reader.noteReferenceMark, Id = kind + ":" + label, PartId = id, Endnote = kind == "endnotes" };
                semantic.Blocks.AddRange(blocks); notes.Add(semantic.Id, semantic);
            }
        }
    }
}
