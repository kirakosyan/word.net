using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Rendering.Skia;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static byte[] VectorShape(string geometry, string extra = "", bool highLayer = false)
    {
        string fill = geometry == "line" ? "<a:noFill/>" : "<a:solidFill><a:srgbClr val='00FF00'/></a:solidFill>";
        string drawing = "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0'" + (highLayer ? " relativeHeight='1'" : "") + ">"
            + "<wp:positionH relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionH><wp:positionV relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionV>"
            + "<wp:extent cx='1778000' cy='1016000'/><wp:wrapNone/><wp:docPr id='1' name='Vector' descr='Accessible description'/>"
            + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'>"
            + "<wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr/><wps:spPr>"
            + "<a:prstGeom prst='" + geometry + "'><a:avLst/></a:prstGeom>" + fill + "<a:ln w='25400'><a:solidFill><a:srgbClr val='FF0000'/></a:solidFill></a:ln>"
            + extra + "</wps:spPr><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
        return Docx(P("Body") + drawing + Page);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("ellipse")]
    [InlineData("roundRect")]
    public void VectorShapeRetainsEditableMarkupAndManagedVectorGeometry(string geometry)
    {
        byte[] input = VectorShape(geometry), docx = Export(input, DocumentFormat.Docx); Schema(docx);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main", wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        Assert.Equal(geometry, (string?)Part(docx, "word/document.xml").Descendants(a + "prstGeom").Single().Attribute("prst"));
        Assert.Equal("Accessible description", (string?)Part(docx, "word/document.xml").Descendants(wp + "docPr").Single().Attribute("descr"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf), SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
        var page = pdf.GetPage(1); Assert.Equal("Body", page.Text); Assert.Equal(0, page.NumberOfImages);
        Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is PdfRectangle bounds && Math.Abs(bounds.Left - 100) < 0.01
            && Math.Abs(bounds.Right - 240) < 0.01 && Math.Abs(bounds.Bottom - 120) < 0.01 && Math.Abs(bounds.Top - 200) < 0.01);
        if (geometry != "line") Assert.Contains(page.Operations, op => op is AppendDualControlPointBezierCurve);
        using SKBitmap raster = pdf.GetPageAsSKBitmap(1, 1, SKColors.White);
        if (geometry != "line") { Assert.Equal(SKColors.Lime, raster.GetPixel(170, 140)); Assert.Equal(SKColors.White, raster.GetPixel(101, 101)); }
        XDocument xps = Part(Export(input, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        Assert.Contains(xps.Descendants().Where(e => e.Name.LocalName == "Path").Attributes("Data"), a => a.Value.Contains(geometry == "line" ? "L" : "C"));
        string start = geometry == "line" ? "M 133.3333,133.3333" : geometry == "ellipse" ? "M 226.6667,133.3333" : "M 151.1111,133.3333";
        Assert.Contains(xps.Descendants().Where(e => e.Name.LocalName == "Path").Attributes("Data"), attribute => attribute.Value.StartsWith(start, StringComparison.Ordinal));
        using var source = new MemoryStream(input); PagePreview preview = Assert.Single(new WordConverter().RenderPages(source, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(preview.GetBytes())); XNamespace ns = "http://www.w3.org/2000/svg";
        XElement vector = Assert.Single(svg.Descendants(ns + "path"), p => (string?)p.Attribute("stroke") == "#FF0000");
        string svgStart = geometry == "line" ? "M 100 100" : geometry == "ellipse" ? "M 170 100" : "M 113.333333 100";
        Assert.StartsWith(svgStart, (string)vector.Attribute("d")!);
    }

    [Fact]
    public void UnsupportedVectorEffectsAreReportedInLenientConversion()
    {
        using var source = new MemoryStream(VectorShape("ellipse", "<a:effectLst/>"));
        using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.VectorUnsupported");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal("Body", pdf.GetPage(1).Text);
    }
    [Fact]
    public void OverlappingExplicitLayersAreRejectedForManagedRendering()
    {
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XElement first = Part(VectorShape("ellipse", highLayer: true), "word/document.xml").Descendants(W + "p")
            .Single(p => p.Descendants(wp + "anchor").Any());
        XElement second = new XElement(first);
        second.Descendants(wp + "docPr").Single().SetAttributeValue("id", "2");
        second.Descendants(wp + "anchor").Single().SetAttributeValue("relativeHeight", "2");
        byte[] input = Docx(first.ToString(SaveOptions.DisableFormatting) + second.ToString(SaveOptions.DisableFormatting) + Page);
        Assert.Throws<NotSupportedException>(() => Export(input, DocumentFormat.Pdf));
    }
    [Fact]
    public void VectorEffectsAndUnqualifiedProfilesAreRejected()
    {
        Assert.Throws<NotSupportedException>(() => Export(VectorShape("ellipse", "<a:effectLst/>"), DocumentFormat.Docx));
        byte[] layered = Export(VectorShape("ellipse", highLayer: true), DocumentFormat.Docx); Schema(layered);
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        Assert.Equal("1", (string?)Part(layered, "word/document.xml").Descendants(wp + "anchor").Single().Attribute("relativeHeight"));
        using PdfDocument layeredPdf = PdfDocument.Open(Export(VectorShape("ellipse", highLayer: true), DocumentFormat.Pdf));
        Assert.Equal("Body", layeredPdf.GetPage(1).Text);
        using var source = new MemoryStream(VectorShape("ellipse")); using var output = new MemoryStream();
        var options = new ConversionOptions(); options.Pdf.Conformance = PdfConformance.PdfA2b;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }
}
