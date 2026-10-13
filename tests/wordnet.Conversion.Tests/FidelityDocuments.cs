using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace WordNet.Tests;

internal static class FidelityDocuments
{
    internal const string Numbering = "<w:numbering xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:abstractNum w:abstractNumId='5'>"
        + "<w:lvl w:ilvl='0'><w:start w:val='3'/><w:numFmt w:val='decimal'/><w:lvlText w:val='%1.'/></w:lvl>"
        + "<w:lvl w:ilvl='1'><w:start w:val='1'/><w:numFmt w:val='bullet'/><w:lvlText w:val='•'/></w:lvl>"
        + "</w:abstractNum><w:num w:numId='7'><w:abstractNumId w:val='5'/></w:num></w:numbering>";
    internal const string Formatting = "<w:p><w:r><w:rPr><w:b/><w:color w:val='336699'/><w:sz w:val='36'/></w:rPr><w:t>Blue title</w:t></w:r></w:p>";
    internal const string Table = "<w:tbl><w:tblPr><w:tblW w:w='7200' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='4800'/></w:tblGrid><w:tr>"
        + "<w:tc><w:tcPr><w:tcW w:w='2400' w:type='dxa'/><w:tcMar><w:top w:w='100' w:type='dxa'/><w:left w:w='200' w:type='dxa'/><w:bottom w:w='120' w:type='dxa'/><w:right w:w='160' w:type='dxa'/></w:tcMar></w:tcPr><w:p><w:r><w:t>First cell</w:t></w:r></w:p></w:tc>"
        + "<w:tc><w:tcPr><w:tcW w:w='4800' w:type='dxa'/></w:tcPr><w:p><w:r><w:t>Second cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    internal static string ListParagraph(string text, int level = 0) => "<w:p><w:pPr><w:numPr><w:ilvl w:val='" + level + "'/><w:numId w:val='7'/></w:numPr></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    internal static byte[] Docx(string? imageName = null, string? body = null, bool lists = false, string? numbering = null)
    {
        string relationships = (imageName == null ? "" : "<Relationship Id='image' Type='" + TestDocuments.R + "/image' Target='media/" + imageName + "'/>")
            + (lists ? "<Relationship Id='numbering' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>" : "");
        byte[] initial = TestDocuments.Docx(TestDocuments.Body(body ?? (Formatting + ImageParagraph())), relationships);
        using var memory = new MemoryStream(); memory.Write(initial, 0, initial.Length);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            if (imageName != null) Add(zip, "word/media/" + imageName, Fixture(imageName));
            if (lists) Add(zip, "word/numbering.xml", Encoding.UTF8.GetBytes(numbering ?? Numbering));
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            XDocument types;
            using (Stream stream = zip.GetEntry("[Content_Types].xml")!.Open()) types = XDocument.Load(stream);
            if (imageName != null) types.Root!.Add(new XElement(ct + "Default", new XAttribute("Extension", imageName.EndsWith("png", StringComparison.Ordinal) ? "png" : "jpg"), new XAttribute("ContentType", imageName.EndsWith("png", StringComparison.Ordinal) ? "image/png" : "image/jpeg")));
            if (lists) types.Root!.Add(new XElement(ct + "Override", new XAttribute("PartName", "/word/numbering.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml")));
            zip.GetEntry("[Content_Types].xml")!.Delete(); Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(types.ToString()));
        }
        return memory.ToArray();
    }

    internal static string ImageParagraph(long width = 1524000, long height = 762000) => "<w:p><w:r><w:drawing><wp:inline xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'>"
        + "<wp:extent cx='" + width + "' cy='" + height + "'/><wp:docPr id='1' name='Color grid' descr='Red and blue grid'/>"
        + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/picture'>"
        + "<pic:pic xmlns:pic='http://schemas.openxmlformats.org/drawingml/2006/picture'><pic:nvPicPr><pic:cNvPr id='0' name='Grid'/><pic:cNvPicPr/></pic:nvPicPr>"
        + "<pic:blipFill><a:blip r:embed='image'/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
        + "<pic:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='" + width + "' cy='" + height + "'/></a:xfrm><a:prstGeom prst='rect'><a:avLst/></a:prstGeom></pic:spPr>"
        + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";

    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, name));
    internal static void Add(ZipArchive zip, string name, byte[] content)
    {
        using Stream stream = zip.CreateEntry(name).Open(); stream.Write(content, 0, content.Length);
    }
}
