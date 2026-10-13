using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace WordNet.Tests;

internal static class TestDocuments
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal const string PackageR = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal static string FixtureDirectory => AppContext.GetData("wordnet.TestFixtureDirectory") as string ?? Path.Combine(AppContext.BaseDirectory, "Fixtures");
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureDirectory, name));

    internal static byte[] Docx(string document, string? relationships = null, string? styles = null,
        string? rootRelationships = null, KeyValuePair<string, string>? extra = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + (styles == null ? "" : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>")
                + "</Types>");
            Add(zip, "_rels/.rels", rootRelationships ?? "<Relationships xmlns=\"" + PackageR + "\"><Relationship Id=\"main\" Type=\"" + R + "/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml", document);
            if (relationships != null || styles != null)
            {
                Add(zip, "word/_rels/document.xml.rels", "<Relationships xmlns=\"" + PackageR + "\">" + relationships
                    + (styles == null ? "" : "<Relationship Id=\"styles\" Type=\"" + R + "/styles\" Target=\"styles.xml\"/>") + "</Relationships>");
            }

            if (styles != null)
            {
                Add(zip, "word/styles.xml", styles);
            }

            if (extra.HasValue)
            {
                Add(zip, extra.Value.Key, extra.Value.Value);
            }
        }

        return output.ToArray();
    }

    internal static string Body(string body) => "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\"><w:body>" + body + "</w:body></w:document>";

    internal static string TextParagraph(string text) => new XElement(XName.Get("p", W),
        new XElement(XName.Get("r", W), new XElement(XName.Get("t", W), text))).ToString();

    internal static string LinkRelationship(string target) => new XElement(XName.Get("Relationship", PackageR),
        new XAttribute("Id", "link"), new XAttribute("Type", R + "/hyperlink"),
        new XAttribute("Target", target), new XAttribute("TargetMode", "External")).ToString();

    private static void Add(ZipArchive archive, string name, string content)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
