using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

internal static class ControlTestDocuments
{
    internal static readonly XNamespace W = TestDocuments.W;
    internal static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal static readonly XNamespace R = TestDocuments.R;
    internal static readonly XNamespace Pr = TestDocuments.PackageR;
    internal static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    internal static readonly XNamespace Ds = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    internal const string StoreId = "{A67AC88A-A164-4ADE-8889-8826CE44DE6E}";
    internal static string Fixture(string name) => TestDocuments.Fixture(Path.Combine("ContentControls", name));
    internal static string Document(string body) => "<w:document xmlns:w='" + W + "' xmlns:w14='" + W14 + "' xmlns:r='" + R + "' xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006' mc:Ignorable='w14'><w:body>" + body + "</w:body></w:document>";
    internal static string Run(string text) => new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)).ToString(SaveOptions.DisableFormatting);
    internal static string Control(int? id, string type, string text, string metadata = "", bool block = false, string? content = null)
    {
        var wrapper = XElement.Parse("<w:sdt xmlns:w='" + W + "' xmlns:w14='" + W14 + "'><w:sdtPr>" + metadata + (id.HasValue ? "<w:id w:val='" + id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'/>" : "") + type + "</w:sdtPr><w:sdtContent>" + (block ? "<w:p>" + (content ?? Run(text)) + "</w:p>" : content ?? Run(text)) + "</w:sdtContent></w:sdt>");
        return wrapper.ToString(SaveOptions.DisableFormatting);
    }
    internal static string Checkbox(string? checkedValue = "0", string checkedCode = "0058", string font = "Noto Sans") =>
        "<w14:checkbox>" + (checkedValue == null ? "" : "<w14:checked w14:val='" + checkedValue + "'/>") + "<w14:checkedState w14:val='" + checkedCode + "' w14:font='" + font + "'/><w14:uncheckedState w14:val='002D' w14:font='" + font + "'/></w14:checkbox>";
    internal const string Choices = "<w:dropDownList><w:listItem w:displayText='Alpha' w:value='a'/><w:listItem w:displayText='Beta' w:value='b'/></w:dropDownList>";
    internal static string Binding(string path, string? id = StoreId, string? mappings = "xmlns:p='urn:wordnet:controls'") => new XElement(W + "dataBinding",
        id == null ? null : new XAttribute(W + "storeItemID", id), new XAttribute(W + "xpath", path), mappings == null ? null : new XAttribute(W + "prefixMappings", mappings)).ToString(SaveOptions.DisableFormatting);
    internal static byte[] Package(string document) => TestDocuments.Docx(document);
    internal static byte[] Modify(byte[] source, Action<ZipArchive> change)
    {
        using var output = new MemoryStream(); output.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true)) change(zip);
        return output.ToArray();
    }
    internal static void Put(ZipArchive zip, string path, string xml)
    {
        zip.GetEntry(path)?.Delete(); var entry = zip.CreateEntry(path); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open(); byte[] bytes = new UTF8Encoding(false).GetBytes(xml); stream.Write(bytes, 0, bytes.Length);
    }
    internal static XDocument Xml(ZipArchive zip, string name)
    { using Stream input = zip.GetEntry(name)!.Open(); return XDocument.Load(input); }
    internal static XDocument Xml(byte[] bytes, string name) => P1FieldTests.Xml(bytes, name);
    internal static void AddRelationship(ZipArchive zip, string ownerRels, string id, string type, string target, string? mode = null)
    {
        var relations = zip.GetEntry(ownerRels) == null ? new XDocument(new XElement(Pr + "Relationships")) : Xml(zip, ownerRels);
        relations.Root!.Add(new XElement(Pr + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target), mode == null ? null : new XAttribute("TargetMode", mode)));
        Put(zip, ownerRels, relations.ToString());
    }
    internal static void Override(ZipArchive zip, string part, string type)
    {
        var types = Xml(zip, "[Content_Types].xml"); types.Root!.Add(new XElement(Ct + "Override", new XAttribute("PartName", "/" + part), new XAttribute("ContentType", type))); Put(zip, "[Content_Types].xml", types.ToString());
    }
    internal static byte[] WithStore(string body, string? data = null) => Modify(Package(Document(body)), zip =>
    {
        AddRelationship(zip, "word/_rels/document.xml.rels", "data", "customXml", "../customXml/item1.xml");
        AddRelationship(zip, "customXml/_rels/item1.xml.rels", "props", "customXmlProps", "itemProps1.xml");
        Put(zip, "customXml/item1.xml", data ?? Fixture("store.xml"));
        Put(zip, "customXml/itemProps1.xml", new XElement(Ds + "datastoreItem", new XAttribute(Ds + "itemID", StoreId.ToLowerInvariant()), new XElement(Ds + "schemaRefs", new XElement(Ds + "schemaRef", new XAttribute(Ds + "uri", "urn:inert-schema")))).ToString());
        Override(zip, "customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
    });
    internal static byte[] WithGlossary(string body, string placeholder = "Choose") => Modify(Package(Document(body)), zip =>
    {
        AddRelationship(zip, "word/_rels/document.xml.rels", "glossary", "glossaryDocument", "glossary/document.xml");
        var entry = new XElement(W + "docPart", new XElement(W + "docPartPr", new XElement(W + "name", new XAttribute(W + "val", "Prompt")),
            new XElement(W + "types", new XElement(W + "type", new XAttribute(W + "val", "bbPlcHdr"))), new XElement(W + "behaviors", new XElement(W + "behavior", new XAttribute(W + "val", "content")))),
            new XElement(W + "docPartBody", new XElement(W + "p", new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", "808080"))), new XElement(W + "t", placeholder)))));
        Put(zip, "word/glossary/document.xml", new XElement(W + "glossaryDocument", new XElement(W + "docParts", entry)).ToString());
        Override(zip, "word/glossary/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml");
    });
    internal static WordDocument Load(byte[] bytes, ConversionOptions? options = null, ContentControlDateMode mode = ContentControlDateMode.UtcInstant)
    {
        using var source = new MemoryStream(bytes); return WordDocument.LoadWithControls(source, new ContentControlLoadOptions { DateMode = mode }, out _, options, TestContext.Current.CancellationToken);
    }
    internal static WordDocument LoadBody(string body, ConversionOptions? options = null, ContentControlDateMode mode = ContentControlDateMode.UtcInstant) => Load(Package(Document(body)), options, mode);
    internal static byte[] Save(WordDocument document, DocumentFormat format = DocumentFormat.Docx, ConversionOptions? options = null)
    { using var output = new MemoryStream(); document.Save(output, format, options, TestContext.Current.CancellationToken); return output.ToArray(); }
    internal static string Pdf(WordDocument document)
    { using var pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); return string.Concat(pdf.GetPages().Select(p => p.Text)); }
    internal static string Xps(WordDocument document) => string.Concat(Xml(Save(document, DocumentFormat.Xps), "Documents/1/Pages/1.fpage").Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString")));
    internal static void Validate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var package = WordprocessingDocument.Open(input, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2010).Validate(package, TestContext.Current.CancellationToken).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
    }
    internal static string Compact(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
}
