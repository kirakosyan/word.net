using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static byte[] RotatedBox(int rotation, string? boxText = null, bool autofit = false, int y = 100, int layer = 0, string? boxContent = null, string? imageName = null)
    {
        string shape = "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0' relativeHeight='" + layer.ToString(CultureInfo.InvariantCulture) + "'>"
            + "<wp:positionH relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionH><wp:positionV relativeFrom='page'><wp:posOffset>" + (y * 12700).ToString(CultureInfo.InvariantCulture) + "</wp:posOffset></wp:positionV>"
            + "<wp:extent cx='1778000' cy='1016000'/><wp:wrapSquare wrapText='bothSides'/><wp:docPr id='1' name='Box'/>"
            + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'>"
            + "<wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:xfrm rot='"
            + (rotation * 60000).ToString(CultureInfo.InvariantCulture) + "'><a:off x='0' y='0'/><a:ext cx='1778000' cy='1016000'/></a:xfrm><a:prstGeom prst='rect'><a:avLst/></a:prstGeom>"
            + "<a:solidFill><a:srgbClr val='00FFFF'/></a:solidFill></wps:spPr><wps:txbx><w:txbxContent>" + (boxContent ?? P(boxText ?? "Rotate"))
            + "</w:txbxContent></wps:txbx><wps:bodyPr><a:" + (autofit ? "normAutofit" : "noAutofit") + "/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
        return imageName == null ? Docx(shape + Page) : FidelityDocuments.Docx(imageName, shape + Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void QuarterTurnBoxesPreserveEditableGeometryAndSelectableText(int rotation)
    {
        byte[] input = RotatedBox(rotation), docx = Export(input, DocumentFormat.Docx); Schema(docx);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XElement transform = Part(docx, "word/document.xml").Descendants(a + "xfrm").Single();
        Assert.Equal(rotation * 60000, (int?)transform.Attribute("rot") ?? 0);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters.ToArray();
        Assert.Equal("Rotate", string.Concat(letters.Select(l => l.Value)));
        double left = rotation is 90 or 270 ? 130 : 100, top = rotation is 90 or 270 ? 70 : 100;
        double width = rotation is 90 or 270 ? 80 : 140, height = rotation is 90 or 270 ? 140 : 80;
        Assert.All(letters, letter =>
        {
            Assert.InRange(letter.StartBaseLine.X, left, left + width);
            Assert.InRange(letter.StartBaseLine.Y, 300 - top - height, 300 - top);
        });
        double dx = letters[letters.Length - 1].StartBaseLine.X - letters[0].StartBaseLine.X;
        double dy = letters[letters.Length - 1].StartBaseLine.Y - letters[0].StartBaseLine.Y;
        if (rotation == 0) { Assert.True(dx > 0); Assert.InRange(Math.Abs(dy), 0, 0.01); }
        if (rotation == 90) { Assert.True(dy < 0); Assert.InRange(Math.Abs(dx), 0, 0.01); }
        if (rotation == 180) { Assert.True(dx < 0); Assert.InRange(Math.Abs(dy), 0, 0.01); }
        if (rotation == 270) { Assert.True(dy > 0); Assert.InRange(Math.Abs(dx), 0, 0.01); }
        XDocument xps = Part(Export(input, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        var glyph = xps.Descendants().Single(e => e.Name.LocalName == "Glyphs" && (string?)e.Attribute("UnicodeString") == "Rotate");
        Assert.Equal(rotation != 0, glyph.Parent?.Name.LocalName == "Canvas");
        using var source = new MemoryStream(input);
        PagePreview preview = Assert.Single(new WordConverter().RenderPages(source, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken).Pages);
        string svg = Encoding.UTF8.GetString(preview.GetBytes());
        if (rotation != 0) Assert.Contains("rotate(" + rotation.ToString(CultureInfo.InvariantCulture) + " ", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void ExplicitAutofitKeepsAllTextWithinTheFrameAndEnforcesMinimumSize(int rotation)
    {
        string text = string.Join(" ", Enumerable.Repeat("sample", 24));
        byte[] input = RotatedBox(rotation, text, true), docx = Export(input, DocumentFormat.Docx); Schema(docx);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Assert.Single(Part(docx, "word/document.xml").Descendants(a + "normAutofit"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters.ToArray();
        Assert.Equal(text.Replace(" ", ""), string.Concat(letters.Select(l => l.Value)).Replace(" ", ""));
        Assert.All(letters, letter => Assert.InRange(letter.FontSize, 6, 11));
        Assert.Contains(letters, letter => letter.FontSize < 10.9);
        XDocument xps = Part(Export(input, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        var glyphs = xps.Descendants().Where(e => e.Name.LocalName == "Glyphs").ToArray();
        Assert.Equal(text.Replace(" ", ""), string.Concat(glyphs.Select(e => (string?)e.Attribute("UnicodeString"))).Replace(" ", ""));
        Assert.All(glyphs, glyph => Assert.InRange(double.Parse((string)glyph.Attribute("FontRenderingEmSize")!, CultureInfo.InvariantCulture), 8, 14.6667));
        double left = rotation == 90 ? 130 : 100, top = rotation == 90 ? 70 : 100;
        double width = rotation == 90 ? 80 : 140, height = rotation == 90 ? 140 : 80;
        Assert.All(letters, letter => { Assert.InRange(letter.StartBaseLine.X, left, left + width); Assert.InRange(letter.StartBaseLine.Y, 300 - top - height, 300 - top); });
        Assert.Throws<NotSupportedException>(() => Export(RotatedBox(rotation, string.Join(" ", Enumerable.Repeat("sample", 300)), true), DocumentFormat.Pdf));
    }
    [Fact]
    public void WordLayerValueIsRetainedForRotatedAndFittedBoxes()
    {
        foreach (byte[] input in new[] { RotatedBox(90, layer: 251659264), RotatedBox(0, "fit text", true, layer: 251659264) })
        {
            byte[] output = Export(input, DocumentFormat.Docx); Schema(output);
            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            Assert.Equal("251659264", (string?)Part(output, "word/document.xml").Descendants(wp + "anchor").Single().Attribute("relativeHeight"));
            using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
            Assert.NotEmpty(pdf.GetPage(1).Letters);
        }
    }
    [Fact]
    public void UnsupportedRotationBoundsAndConformanceAreRejected()
    {
        var generalError = Assert.Throws<NotSupportedException>(() => ExportRotated(RotatedBox(45, y: 0), DocumentFormat.Pdf));
        Assert.StartsWith("Layout.AnchorBounds:", generalError.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => Export(RotatedBox(90, y: 0), DocumentFormat.Pdf));
        using var source = new MemoryStream(RotatedBox(90)); using var output = new MemoryStream();
        var options = new ConversionOptions(); options.Pdf.Conformance = PdfConformance.PdfA2b;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }
}
