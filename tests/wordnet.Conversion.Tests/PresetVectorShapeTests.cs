using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PresetVectorShapeTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    private static byte[] Source(string geometry, string title = "", string description = "", string adjustment = "", string transform = "", string horizontal = "1270000")
    {
        string drawing = "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='" + Wp + "' distT='0' distB='0' distL='0' distR='0' simplePos='0' relativeHeight='0' behindDoc='0' locked='0' layoutInCell='1' allowOverlap='1'>"
            + "<wp:simplePos x='0' y='0'/><wp:positionH relativeFrom='page'><wp:posOffset>" + horizontal + "</wp:posOffset></wp:positionH>"
            + "<wp:positionV relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionV><wp:extent cx='1778000' cy='1016000'/>"
            + "<wp:wrapNone/><wp:docPr id='1' name='Synthetic shape' title='" + SecurityElement.Escape(title) + "' descr='" + SecurityElement.Escape(description) + "'/>"
            + "<a:graphic xmlns:a='" + A + "'><a:graphicData uri='" + Wps + "'><wps:wsp xmlns:wps='" + Wps + "'><wps:cNvSpPr/><wps:spPr>"
            + "<a:xfrm" + transform + "><a:off x='0' y='0'/><a:ext cx='1778000' cy='1016000'/></a:xfrm>"
            + "<a:prstGeom prst='" + geometry + "'><a:avLst>" + adjustment + "</a:avLst></a:prstGeom><a:solidFill><a:srgbClr val='2468AC'/></a:solidFill>"
            + "<a:ln><a:noFill/></a:ln></wps:spPr><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
        return TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Before") + drawing + TestDocuments.TextParagraph("After")
            + "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='720' w:right='720' w:bottom='720' w:left='720' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"));
    }

    private static byte[] Convert(byte[] input, DocumentFormat format, ConversionOptions? options = null)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        var selected = options ?? new ConversionOptions();
        if (format == DocumentFormat.Docx)
        {
            WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, selected, TestContext.Current.CancellationToken);
            document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken);
        }
        else new WordConverter().Convert(source, output, DocumentFormat.Docx, format, selected, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    [Theory]
    [InlineData("rect", "M 100 100 L 240 100 L 240 180 L 100 180 Z")]
    [InlineData("triangle", "M 170 100 L 240 180 L 100 180 Z")]
    [InlineData("diamond", "M 170 100 L 240 140 L 170 180 L 100 140 Z")]
    public void PresetsRetainEditableGeometryAndRenderVectorPaths(string geometry, string expectedPath)
    {
        byte[] input = Source(geometry); using (var original = WordprocessingDocument.Open(new MemoryStream(input), false))
            Assert.Empty(new OpenXmlValidator().Validate(original, TestContext.Current.CancellationToken));
        byte[] saved = Convert(input, DocumentFormat.Docx);
        using (var word = WordprocessingDocument.Open(new MemoryStream(saved), false)) Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken));
        using (var archive = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read))
        {
            XDocument document = XDocument.Load(archive.GetEntry("word/document.xml")!.Open());
            XNamespace a = A; Assert.Equal(geometry, (string?)document.Descendants(a + "prstGeom").Single().Attribute("prst"));
        }
        using (PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Pdf)))
        {
            var page = pdf.GetPage(1); Assert.Equal(0, page.NumberOfImages);
            Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is UglyToad.PdfPig.Core.PdfRectangle bounds
                && Math.Abs(bounds.Left - 100) < 0.01 && Math.Abs(bounds.Right - 240) < 0.01
                && Math.Abs(bounds.Bottom - 612) < 0.01 && Math.Abs(bounds.Top - 692) < 0.01);
        }
        using var source = new MemoryStream(input);
        PagePreview pagePreview = Assert.Single(new WordConverter().RenderPages(source, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(pagePreview.GetBytes())); XNamespace s = "http://www.w3.org/2000/svg";
        XElement pathElement = Assert.Single(svg.Descendants(s + "path"), path => (string?)path.Attribute("fill") == "#2468AC");
        Assert.Equal(expectedPath, (string?)pathElement.Attribute("d"));
        byte[] xps = Convert(input, DocumentFormat.Xps);
        using var xpsArchive = new ZipArchive(new MemoryStream(xps), ZipArchiveMode.Read);
        string pageXml = new StreamReader(xpsArchive.Entries.Single(entry => entry.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open()).ReadToEnd();
        Assert.Contains("#FF2468AC", pageXml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("triangle", "Annual trend")]
    [InlineData("ellipse", "")]
    public void PresetAlternativeMetadataSurvivesDocxAndItsLossIsExplicit(string geometry, string description)
    {
        byte[] input = Source(geometry, "Diagram", description); byte[] saved = Convert(input, DocumentFormat.Docx);
        using (var archive = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read))
        {
            XNamespace wp = Wp; XElement properties = XDocument.Load(archive.GetEntry("word/document.xml")!.Open()).Descendants(wp + "docPr").Single();
            Assert.Equal("Diagram", (string?)properties.Attribute("title")); Assert.Equal(description, (string?)properties.Attribute("descr") ?? "");
        }
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        var strict = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.PresetAlternative:", strict.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        source.Position = 0; using var best = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, best, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Shape.PresetAlternative"); Assert.NotEmpty(best.ToArray());
    }

    [Theory]
    [InlineData("adjustment")]
    [InlineData("rotation")]
    [InlineData("unknown")]
    [InlineData("title-limit")]
    [InlineData("tagged")]
    [InlineData("hidden")]
    [InlineData("out-of-page")]
    [InlineData("archive")]
    public void UnqualifiedPresetsFailBeforeOutput(string failure)
    {
        byte[] input = failure == "adjustment" ? Source("triangle", adjustment: "<a:gd name='adj' fmla='val 25000'/>")
            : failure == "rotation" ? Source("diamond", transform: " rot='60000'")
            : failure == "unknown" ? Source("hexagon") : failure == "title-limit" ? Source("rect", new string('x', 1025))
            : failure == "out-of-page" ? Source("rect", horizontal: "7366000") : Source("rect");
        if (failure == "hidden")
        {
            using var archive = new ZipArchive(new MemoryStream(input), ZipArchiveMode.Read); using var outputBytes = new MemoryStream();
            using (var changed = new ZipArchive(outputBytes, ZipArchiveMode.Create, leaveOpen: true))
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    using Stream from = entry.Open(); using Stream to = changed.CreateEntry(entry.FullName).Open();
                    if (entry.FullName != "word/document.xml") from.CopyTo(to);
                    else
                    {
                        XDocument xml = XDocument.Load(from); XNamespace wp = Wp; xml.Descendants(wp + "docPr").Single().SetAttributeValue("hidden", "1");
                        xml.Save(to);
                    }
                }
            input = outputBytes.ToArray();
        }
        var options = new ConversionOptions(); if (failure == "tagged") options.Pdf.Accessibility = PdfAccessibility.Tagged;
        if (failure == "archive") options.Pdf.Conformance = PdfConformance.PdfA2b;
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        if (failure == "title-limit") Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        else Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void UnsupportedAdjustmentsAreDiagnosedAndCancellationPreservesOutput()
    {
        byte[] input = Source("triangle", adjustment: "<a:gd name='adj' fmla='val 25000'/>");
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Shape.VectorUnsupported");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Empty(pdf.GetPage(1).Paths);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); source.Position = 0; using var untouched = new MemoryStream(); untouched.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(source, untouched, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(), cancelled.Token));
        Assert.Equal(new byte[] { 42 }, untouched.ToArray());
    }
}
