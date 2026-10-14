using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class XpsFormat
{
    internal static void Write(DocumentModel document, Stream output, ConversionContext context)
    {
        var pages = FixedLayout.Create(document, context);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        XNamespace xps = "http://schemas.microsoft.com/xps/2005/06";
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        Xml("[Content_Types].xml", new XElement(ct + "Types", new[] {
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "ttf"), new XAttribute("ContentType", "application/vnd.ms-opentype")),
            new XElement(ct + "Default", new XAttribute("Extension", "fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml")) }));
        Xml("_rels/.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "r1"), new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation"), new XAttribute("Target", "FixedDocumentSequence.fdseq"))));
        Xml("FixedDocumentSequence.fdseq", new XElement(xps + "FixedDocumentSequence", new XElement(xps + "DocumentReference", new XAttribute("Source", "Documents/1/FixedDocument.fdoc"))));
        Xml("Documents/1/FixedDocument.fdoc", new XElement(xps + "FixedDocument", Enumerable.Range(1, pages.Count).Select(n => new XElement(xps + "PageContent", new XAttribute("Source", "Pages/" + n.ToString(CultureInfo.InvariantCulture) + ".fpage")))));
        for (int i = 0; i < 4; i++) PackageArchive.Write(archive, "Resources/Fonts/" + FixedLayout.FontNames[i], FixedLayout.FontBytes(i));
        for (int p = 0; p < pages.Count; p++)
        {
            context.CheckCancellation();
            string name = (p + 1).ToString(CultureInfo.InvariantCulture);
            Xml("Documents/1/Pages/" + name + ".fpage", new XElement(xps + "FixedPage", new XAttribute("Width", Number(pages[p].Settings.Width)), new XAttribute("Height", Number(pages[p].Settings.Height)), new XAttribute(XNamespace.Xml + "lang", "en-US"),
                pages[p].Text.Where(t => t.Text.Length != 0).Select(t => new XElement(xps + "Glyphs", new XAttribute("FontUri", "/Resources/Fonts/" + FixedLayout.FontNames[t.Font]),
                    new XAttribute("FontRenderingEmSize", Number(t.Size)), new XAttribute("OriginX", Number(t.X)), new XAttribute("OriginY", Number(t.Y)), new XAttribute("Fill", "#FF" + (t.Color ?? "000000")),
                    new XAttribute("UnicodeString", t.Text[0] == '{' ? "{}" + t.Text : t.Text)))));
            Xml("Documents/1/Pages/_rels/" + name + ".fpage.rels", new XElement(rel + "Relationships", Enumerable.Range(0, 4).Select(f => new XElement(rel + "Relationship", new XAttribute("Id", "f" + f.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/required-resource"), new XAttribute("Target", "/Resources/Fonts/" + FixedLayout.FontNames[f])))));
        }
        void Xml(string name, XElement root) => PackageArchive.Write(archive, name, TextFormat.Utf8.GetBytes(root.ToString(SaveOptions.DisableFormatting)));
    }

    private static string Number(double points) => (points * 96 / 72).ToString("0.####", CultureInfo.InvariantCulture);
}
