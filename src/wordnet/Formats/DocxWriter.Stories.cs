using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private readonly Dictionary<List<DocumentBlock>, (string Id, string Kind, string Path)> storyReferences = new Dictionary<List<DocumentBlock>, (string Id, string Kind, string Path)>();

    private void RegisterStories(DocumentModel document)
    {
        foreach (var story in document.Blocks.Select(b => b.Section).Concat(new[] { document.FinalSection }).Where(s => s != null).Distinct().SelectMany(s => s!.Stories))
        {
            if (storyReferences.ContainsKey(story.Value)) continue;
            string number = (storyReferences.Count + 1).ToString(CultureInfo.InvariantCulture);
            string kind = story.Key.StartsWith("header-", StringComparison.Ordinal) ? "header" : "footer";
            string path = kind + number + ".xml", id = "rStory" + number;
            storyReferences.Add(story.Value, (id, kind, path));
            relationships.Add(Relationship(id, kind, path));
        }
    }

    private void WriteStories(DocumentModel document, ZipArchive archive, XElement types)
    {
        foreach (var story in storyReferences)
        {
            int relationshipStart = relationships.Count;
            var root = new XElement(W + (story.Value.Kind == "header" ? "hdr" : "ftr"));
            root.Add(Blocks(story.Key));
            var ownRelationships = relationships.Skip(relationshipStart).ToArray();
            relationships.RemoveRange(relationshipStart, relationships.Count - relationshipStart);
            WritePart(archive, "word/" + story.Value.Path, root);
            if (ownRelationships.Length > 0) WritePart(archive, "word/_rels/" + story.Value.Path + ".rels", new XElement(DocxPackage.Relationships + "Relationships", ownRelationships));
            types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/" + story.Value.Path),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml." + story.Value.Kind + "+xml")));
        }
        bool evenAndOdd = document.Blocks.Select(b => b.Section).Concat(new[] { document.FinalSection }).Any(s => s?.EvenAndOdd == true);
        if (evenAndOdd || document.Theme?.Mapping != null)
        {
            WritePart(archive, "word/settings.xml", new XElement(W + "settings", evenAndOdd ? new XElement(W + "evenAndOddHeaders") : null,
                document.Theme?.Mapping == null ? null : XElement.Parse(document.Theme.Mapping)));
            relationships.Add(Relationship("rSettings", "settings", "settings.xml"));
            types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/settings.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml")));
        }
    }
}
