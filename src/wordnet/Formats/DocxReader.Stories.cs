using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Dictionary<string, List<DocumentBlock>> storyCache = new Dictionary<string, List<DocumentBlock>>(StringComparer.Ordinal);

    private List<DocumentBlock> ReadStory(XElement reference)
    {
        string id = DocxPackage.RequiredAttribute(reference, DocxPackage.DocumentRelationships + "id");
        string kind = reference.Name == W + "headerReference" ? "header" : "footer";
        if (!relationships.TryGetValue(id, out XElement? relation)
            || (string?)relation.Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/" + kind)
            throw new InvalidDataException("Docx.HeaderFooter: A header/footer relationship is missing or has the wrong type.");
        string path = DocxPackage.ResolvePart(mainPart, relation);
        if (storyCache.TryGetValue(path, out List<DocumentBlock>? cached)) return cached;
        knownParts.Add(path); knownParts.Add(DocxPackage.RelationshipPartName(path));
        XElement root = package.ReadXml(path).Root ?? throw new InvalidDataException("Docx.HeaderFooter: Missing story root.");
        if (root.Name != W + (kind == "header" ? "hdr" : "ftr")) throw new InvalidDataException("Docx.HeaderFooter: Invalid story root.");
        var reader = new DocxReader(context, package.ReadRelationships(path))
        {
            package = package,
            mainPart = path,
            knownParts = knownParts,
            defaultStyle = defaultStyle,
            defaultAlignment = defaultAlignment,
            defaultLayout = defaultLayout.Copy(),
            defaultRun = defaultRun.Copy(),
        };
        foreach (var pair in styles) reader.styles.Add(pair.Key, pair.Value);
        foreach (var pair in numbers) reader.numbers.Add(pair.Key, pair.Value);
        foreach (var pair in abstracts) reader.abstracts.Add(pair.Key, pair.Value);
        var blocks = new List<DocumentBlock>(); reader.ReadBlocks(root, blocks);
        if (reader.fields.Count > 0) throw new InvalidDataException("Docx.Field: Unclosed header/footer field.");
        storyCache.Add(path, blocks); return blocks;
    }
}
