using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static class EpubFormat
{
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        using var archive = new PackageArchive(input, context);
        if (TextFormat.Utf8.GetString(archive.Read("mimetype")) != "application/epub+zip") throw new InvalidDataException("Epub.Mimetype: Invalid EPUB media type.");
        if (archive.Contains("META-INF/encryption.xml")) throw new NotSupportedException("Epub.Encryption: Encrypted or obfuscated EPUB resources are unsupported.");
        XElement root = archive.ReadXml("META-INF/container.xml").Root ?? throw new InvalidDataException("Epub.Container: Missing container.");
        string package = (string?)root.Element(Container + "rootfiles")?.Elements(Container + "rootfile").FirstOrDefault(e => (string?)e.Attribute("media-type") == "application/oebps-package+xml")?.Attribute("full-path")
            ?? throw new InvalidDataException("Epub.Container: No package document.");
        package = PackageArchive.Resolve("", package);
        XElement opf = archive.ReadXml(package).Root ?? throw new InvalidDataException("Epub.Package: Missing package.");
        if (opf.Name != Opf + "package") throw new InvalidDataException("Epub.Package: Invalid package namespace.");
        var manifest = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var resources = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement item in opf.Element(Opf + "manifest")?.Elements(Opf + "item") ?? Enumerable.Empty<XElement>())
        {
            string id = (string?)item.Attribute("id") ?? throw new InvalidDataException("Epub.Manifest: Missing identifier.");
            if (manifest.ContainsKey(id)) throw new InvalidDataException("Epub.Manifest: Duplicate identifier.");
            manifest.Add(id, item);
            string href = (string?)item.Attribute("href") ?? "";
            if (!Uri.TryCreate(href, UriKind.Absolute, out _) && !href.StartsWith("//", StringComparison.Ordinal))
            {
                string resolved = PackageArchive.Resolve(package, href);
                if (resources.ContainsKey(resolved)) throw new InvalidDataException("Epub.Manifest: Duplicate resource path.");
                resources.Add(resolved, item);
            }
        }
        var model = new DocumentModel();
        int characters = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement reference in opf.Element(Opf + "spine")?.Elements(Opf + "itemref") ?? throw new InvalidDataException("Epub.Spine: Missing reading order."))
        {
            context.CheckCancellation();
            string id = (string?)reference.Attribute("idref") ?? "";
            if (!manifest.TryGetValue(id, out XElement? item) || !seen.Add(id)) throw new InvalidDataException("Epub.Spine: Missing or repeated manifest item.");
            if ((string?)item.Attribute("media-type") != "application/xhtml+xml") { context.Loss("Epub.ContentType", "Non-XHTML spine content is unsupported."); continue; }
            string path = PackageArchive.Resolve(package, (string?)item.Attribute("href") ?? "");
            byte[] bytes = archive.Read(path);
            _ = PackageArchive.ParseXml(bytes, context); // EPUB content must be well-formed, bounded XHTML.
            DocumentModel chapter = EbookContent.Read(bytes, context, image =>
            {
                string target = image.GetAttribute("src") ?? "";
                if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.StartsWith("//", StringComparison.Ordinal))
                { context.Loss("Image.External", "Remote and data-URI ebook images are not fetched."); return null; }
                string resource = PackageArchive.Resolve(path, target);
                if (!resources.TryGetValue(resource, out XElement? asset)) throw new InvalidDataException("Epub.ImageManifest: Image is missing from the manifest.");
                if ((string?)asset.Attribute("media-type") is not "image/png" and not "image/jpeg")
                { context.Loss("Image.Format", "Only PNG and JPEG ebook images are supported."); return null; }
                return DocumentImage.Read(archive.Read(resource), EbookContent.ImageDimension(image, "width", context), EbookContent.ImageDimension(image, "height", context), image.GetAttribute("alt") ?? "", context);
            });
            characters = checked(characters + chapter.Blocks.OfType<ParagraphBlock>().Sum(p => p.Text.Length));
            if (characters > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.Characters: EPUB text exceeds the limit.");
            model.Blocks.AddRange(chapter.Blocks);
        }
        return model;
    }

    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        var images = new List<DocumentImage>();
        byte[] content = EbookContent.Write(model, context, image => { images.Add(image); return "media/image" + images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + image.Extension; });
        PackageArchive.Write(archive, "mimetype", TextFormat.Utf8.GetBytes("application/epub+zip"), CompressionLevel.NoCompression);
        Xml("META-INF/container.xml", new XElement(Container + "container", new XAttribute("version", "1.0"),
            new XElement(Container + "rootfiles", new XElement(Container + "rootfile", new XAttribute("full-path", "EPUB/package.opf"), new XAttribute("media-type", "application/oebps-package+xml")))));
        Xml("EPUB/package.opf", new XElement(Opf + "package", new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "book-id"),
            new XElement(Opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", Dc), new XElement(Dc + "identifier", new XAttribute("id", "book-id"), "urn:wordnet:document"),
                new XElement(Dc + "title", "Document"), new XElement(Dc + "language", "en"), new XElement(Opf + "meta", new XAttribute("property", "dcterms:modified"), "2000-01-01T00:00:00Z")),
            new XElement(Opf + "manifest", Item("content", "content.xhtml", null), Item("nav", "nav.xhtml", "nav"),
                images.Select((image, i) => new XElement(Opf + "item", new XAttribute("id", "image" + (i + 1)), new XAttribute("href", "media/image" + (i + 1) + "." + image.Extension), new XAttribute("media-type", image.MediaType)))),
            new XElement(Opf + "spine", new XElement(Opf + "itemref", new XAttribute("idref", "content")))));
        PackageArchive.Write(archive, "EPUB/content.xhtml", content);
        for (int i = 0; i < images.Count; i++) PackageArchive.Write(archive, "EPUB/media/image" + (i + 1) + "." + images[i].Extension, images[i].Bytes);
        XNamespace h = EbookContent.Html;
        XNamespace epub = "http://www.idpf.org/2007/ops";
        Xml("EPUB/nav.xhtml", new XElement(h + "html", new XElement(h + "head", new XElement(h + "title", "Contents")),
            new XElement(h + "body", new XElement(h + "nav", new XAttribute(epub + "type", "toc"), new XElement(h + "h1", "Contents"),
                new XElement(h + "ol", new XElement(h + "li", new XElement(h + "a", new XAttribute("href", "content.xhtml"), "Document")))))));

        void Xml(string name, XElement element) => PackageArchive.Write(archive, name, TextFormat.Utf8.GetBytes(element.ToString(SaveOptions.DisableFormatting)));
    }

    private static XElement Item(string id, string href, string? properties)
    {
        var item = new XElement(Opf + "item", new XAttribute("id", id), new XAttribute("href", href), new XAttribute("media-type", "application/xhtml+xml"));
        if (properties != null) item.Add(new XAttribute("properties", properties));
        return item;
    }
}
