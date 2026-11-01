using System.IO.Compression;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private void WriteTheme(DocumentModel document, ZipArchive archive, XElement types)
    {
        if (document.Theme == null) return;
        WritePart(archive, "word/theme/theme1.xml", XElement.Parse(document.Theme.Xml));
        relationships.Add(Relationship("rTheme", "theme", "theme/theme1.xml"));
        types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/theme/theme1.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")));
    }
}
