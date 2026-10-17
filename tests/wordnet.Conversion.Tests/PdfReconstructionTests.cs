using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfReconstructionTests
{
    private static readonly XNamespace W = TestDocuments.W;

    [Fact]
    public void IndependentProseRetainsParagraphsLeadingIndentAndMeasuredBaselines()
    {
        byte[] word = Import("prose", out ConversionResult result); Schema(word); XElement root = Part(word);
        XElement[] paragraphs = root.Elements(W + "body").Single().Elements(W + "p").ToArray(); Assert.Equal(2, paragraphs.Length);
        Assert.Equal("First paragraph line one.First paragraph line two.", paragraphs[0].Value); Assert.Equal("Second paragraph starts.", paragraphs[1].Value);
        Assert.Equal("800", (string?)paragraphs[0].Descendants(W + "ind").Single().Attribute(W + "left"));
        Assert.Equal("10000", (string?)root.Descendants(W + "pgSz").Single().Attribute(W + "w"));
        Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.Reconstruction"); Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.ReconstructionProfile");
        byte[] pdf = new WordConverter().ConvertToPdf(word, new ConversionOptions(FidelityMode.BestEffort)); using PdfDocument parsed = PdfDocument.Open(pdf);
        Assert.Equal(500, parsed.GetPage(1).Width); Assert.Equal(700, parsed.GetPage(1).Height);
        Assert.Equal(40, parsed.GetPage(1).Letters[0].StartBaseLine.X, 2); Assert.Equal(640, parsed.GetPage(1).Letters[0].StartBaseLine.Y, 2);
    }

    [Fact]
    public void KnownColumnsOverrideAlternatingContentOrderWithoutDuplicatingText()
    {
        byte[] word = Import("columns", out _, columns: 2); Schema(word); XElement root = Part(word);
        string text = string.Concat(root.Descendants(W + "t").Select(t => t.Value));
        Assert.True(text.IndexOf("Left column line 3.", StringComparison.Ordinal) < text.IndexOf("Right column line 1.", StringComparison.Ordinal));
        int first = text.IndexOf("Left column line 1.", StringComparison.Ordinal); Assert.True(first >= 0 && first == text.LastIndexOf("Left column line 1.", StringComparison.Ordinal));
        Assert.Equal("2", (string?)root.Descendants(W + "cols").Single().Attribute(W + "num"));
        Assert.Single(root.Descendants(W + "br"), b => (string?)b.Attribute(W + "type") == "column");
    }

    [Fact]
    public void CompleteRuledGridBecomesOneEditableTableWithSourceWidths()
    {
        byte[] word = Import("grid", out _); Schema(word); XElement root = Part(word); XElement table = Assert.Single(root.Descendants(W + "tbl"));
        Assert.Equal(2, table.Elements(W + "tr").Count()); Assert.Equal(4, table.Descendants(W + "tc").Count());
        Assert.Equal("ItemTotalService42.50", string.Concat(table.Descendants(W + "t").Select(t => t.Value)));
        Assert.All(table.Descendants(W + "gridCol"), col => Assert.Equal("3000", (string?)col.Attribute(W + "w")));
        Assert.Equal("800", (string?)table.Descendants(W + "tblInd").Single().Attribute(W + "w"));
        Assert.DoesNotContain(root.Element(W + "body")!.Elements(W + "p"), p => InteropText.Contains(p.Value, "Service"));
    }

    [Fact]
    public void SupportedRightAngleImageHasOneEditableAnchorAndCorrectTransform()
    {
        byte[] word = Import("image-transform", out _); Schema(word); XElement root = Part(word);
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XElement anchor = Assert.Single(root.Descendants(wp + "anchor")); Assert.Equal("1016000", (string?)anchor.Element(wp + "extent")?.Attribute("cx")); Assert.Equal("508000", (string?)anchor.Element(wp + "extent")?.Attribute("cy"));
        Assert.Equal("2032000", anchor.Element(wp + "positionH")?.Element(wp + "posOffset")?.Value); Assert.Equal("4064000", anchor.Element(wp + "positionV")?.Element(wp + "posOffset")?.Value);
        Assert.Contains(root.Descendants(a + "xfrm"), x => (string?)x.Attribute("rot") == "16200000");
        using var zip = new ZipArchive(new MemoryStream(word)); Assert.Single(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("borderless")]
    [InlineData("form")]
    [InlineData("rotated")]
    public void AmbiguousFormsBorderlessTablesAndRotationsHaveExplicitFallbacks(string name)
    {
        byte[] word = Import(name, out ConversionResult result); Schema(word); Assert.Empty(Part(word).Descendants(W + "tbl"));
        Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.ReconstructionAmbiguity"); Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.Reconstruction");
    }

    [Fact]
    public void ExistingImportDefaultDoesNotInferTablesAndRemainsCompatible()
    {
        byte[] word = Import("grid", out ConversionResult result, enabled: false); Assert.Empty(Part(word).Descendants(W + "tbl"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Pdf.ReconstructionProfile"); Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.Reconstruction");
    }

    [Fact]
    public void ReconstructionAdmissionAndScannedInputRulesArePreserved()
    {
        using var input = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "prose.pdf")); using var output = new MemoryStream(); output.WriteByte(42);
        var options = new ConversionOptions(FidelityMode.BestEffort) { PdfImport = new PdfImportOptions { ReconstructLayout = true, MaximumElements = 1 } };
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        using var scan = new MemoryStream(ConversionAcceptanceTests.Fixture("scanned.pdf")); options.PdfImport.MaximumElements = 100000;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(scan, output, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken));
    }
    private static byte[] Import(string name, out ConversionResult result, int columns = 1, bool enabled = true)
    {
        using var input = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", name + ".pdf")); using var output = new MemoryStream();
        result = new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort) { PdfImport = new PdfImportOptions { ReconstructLayout = enabled, Columns = columns } }, TestContext.Current.CancellationToken); return output.ToArray();
    }
    private static XElement Part(byte[] bytes) { using var zip = new ZipArchive(new MemoryStream(bytes)); using var stream = zip.GetEntry("word/document.xml")!.Open(); return XElement.Load(stream); }
    private static void Schema(byte[] bytes) { using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(doc)); }
}
