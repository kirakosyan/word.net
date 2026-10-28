using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static string LinkedBox(int sequence, int x, int y, int height, string text = "")
    {
        string payload = sequence == 0 ? "<wps:txbx id='42'><w:txbxContent>" + P(text) + "</w:txbxContent></wps:txbx>"
            : "<wps:linkedTxbx id='42' seq='" + sequence.ToString(CultureInfo.InvariantCulture) + "'/>";
        return "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0'>"
            + "<wp:positionH relativeFrom='page'><wp:posOffset>" + (x * 12700).ToString(CultureInfo.InvariantCulture) + "</wp:posOffset></wp:positionH>"
            + "<wp:positionV relativeFrom='page'><wp:posOffset>" + (y * 12700).ToString(CultureInfo.InvariantCulture) + "</wp:posOffset></wp:positionV>"
            + "<wp:extent cx='1778000' cy='" + (height * 12700).ToString(CultureInfo.InvariantCulture) + "'/><wp:wrapNone/><wp:docPr id='"
            + (sequence + 1).ToString(CultureInfo.InvariantCulture) + "' name='Box'/>"
            + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'>"
            + "<wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:prstGeom prst='rect'><a:avLst/></a:prstGeom><a:noFill/></wps:spPr>"
            + payload + "<wps:bodyPr><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
    }

    [Fact]
    public void LinkedBoxesKeepEditableChainAndFlowEachLineOnce()
    {
        string text = string.Join(" ", Enumerable.Repeat("linked", 15));
        byte[] input = Docx(LinkedBox(0, 50, 50, 45, text) + LinkedBox(1, 220, 50, 10) + LinkedBox(2, 50, 150, 80) + Page);
        byte[] docx = Export(input, DocumentFormat.Docx); Schema(docx);
        XNamespace wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        XDocument xml = Part(docx, "word/document.xml");
        Assert.Single(xml.Descendants(wps + "txbx")); Assert.Equal(2, xml.Descendants(wps + "linkedTxbx").Count());
        Assert.Equal("1,2", string.Join(",", xml.Descendants(wps + "linkedTxbx").Select(e => (string?)e.Attribute("seq"))));
        using PdfDocument pdf = PdfDocument.Open(Export(docx, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters.ToArray();
        Assert.Equal(text.Replace(" ", ""), string.Concat(letters.Select(l => l.Value)).Replace(" ", ""));
        Assert.DoesNotContain(letters, l => l.StartBaseLine.X > 220);
        Assert.Contains(letters, l => l.StartBaseLine.Y < 150);
        Assert.Contains(letters, l => l.StartBaseLine.Y > 200);
        XDocument xps = Part(Export(docx, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        Assert.Equal(text.Replace(" ", ""), string.Concat(xps.Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString"))).Replace(" ", ""));
    }

    [Fact]
    public void InvalidLinkedSequencesAndTerminalOverflowFail()
    {
        Assert.Throws<System.IO.InvalidDataException>(() => Export(Docx(LinkedBox(0, 50, 50, 45, "text") + LinkedBox(2, 50, 120, 80) + Page), DocumentFormat.Docx));
        Assert.Throws<System.IO.InvalidDataException>(() => Export(Docx(LinkedBox(1, 50, 50, 45) + Page), DocumentFormat.Docx));
        Assert.Throws<System.IO.InvalidDataException>(() => Export(Docx(LinkedBox(0, 50, 50, 45, "text") + LinkedBox(1, 50, 120, 80) + LinkedBox(1, 200, 120, 80) + Page), DocumentFormat.Docx));
        string rotated = LinkedBox(0, 50, 50, 45, "text").Replace("<a:prstGeom", "<a:xfrm rot='5400000'/><a:prstGeom");
        Assert.Throws<NotSupportedException>(() => Export(Docx(rotated + LinkedBox(1, 50, 120, 80) + Page), DocumentFormat.Docx));
        string autofit = LinkedBox(0, 50, 50, 45, "text").Replace("<a:noAutofit/>", "<a:normAutofit/>");
        Assert.Throws<NotSupportedException>(() => Export(Docx(autofit + LinkedBox(1, 50, 120, 80) + Page), DocumentFormat.Docx));
        string layered = LinkedBox(0, 50, 50, 45, "text").Replace("behindDoc='0'", "behindDoc='0' relativeHeight='1'");
        Assert.Throws<NotSupportedException>(() => Export(Docx(layered + LinkedBox(1, 50, 120, 80) + Page), DocumentFormat.Docx));
        string longText = string.Join(" ", Enumerable.Repeat("linked", 300));
        Assert.Throws<NotSupportedException>(() => Export(Docx(LinkedBox(0, 50, 50, 45, longText) + LinkedBox(1, 50, 120, 45) + Page), DocumentFormat.Pdf));
    }
    [Fact]
    public void DefaultZeroStoryIdentifierLinksAnUnnumberedHead()
    {
        string head = LinkedBox(0, 50, 50, 45, string.Join(" ", Enumerable.Repeat("linked", 12))).Replace("<wps:txbx id='42'>", "<wps:txbx>");
        string continuation = LinkedBox(1, 50, 120, 80).Replace("id='42' seq='1'", "id='0' seq='1'");
        byte[] output = Export(Docx(head + continuation + Page), DocumentFormat.Docx); Schema(output);
        using PdfDocument pdf = PdfDocument.Open(Export(output, DocumentFormat.Pdf));
        Assert.Equal(string.Concat(Enumerable.Repeat("linked", 12)), string.Concat(pdf.GetPage(1).Letters.Select(l => l.Value)).Replace(" ", ""));
    }
}
