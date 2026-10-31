using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfAccessibleArchiveTests
{
    private static readonly string[] OneCategory = { "A" };
    private static ConversionOptions Options()
    {
        var options = new ConversionOptions(FidelityMode.BestEffort) { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) };
        options.Pdf.Conformance = PdfConformance.PdfA2a; options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        options.Pdf.Language = "en"; options.Pdf.Compress = false; return options;
    }

    [Fact]
    public void CombinedArchiveHasOneCoherentMetadataPacketAndSemanticLinks()
    {
        WordDocument document = PdfAccessibilityTests.Source(); var options = Options();
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        byte[] bytes = output.ToArray(); using PdfDocument pdf = PdfDocument.Open(bytes);
        Assert.Contains("42.50", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains(pdf.GetPage(1).GetHyperlinks(), link => link.Uri == "https://example.test/terms");
        Assert.True(pdf.TryGetXmpMetadata(out var metadata)); XDocument xmp = metadata.GetXDocument();
        XNamespace aid = "http://www.aiim.org/pdfa/ns/id/", ua = "http://www.aiim.org/pdfua/ns/id/", schema = "http://www.aiim.org/pdfa/ns/schema#";
        Assert.Equal("2", Assert.Single(xmp.Descendants().Attributes(aid + "part")).Value);
        Assert.Equal("A", Assert.Single(xmp.Descendants().Attributes(aid + "conformance")).Value);
        Assert.Equal("1", Assert.Single(xmp.Descendants().Attributes(ua + "part")).Value);
        Assert.Equal(ua.NamespaceName, Assert.Single(xmp.Descendants(schema + "namespaceURI")).Value);
        string raw = Encoding.ASCII.GetString(bytes);
        foreach (string token in new[] { "/OutputIntents", "/StructTreeRoot", "/ParentTree", "/OBJR", "/DisplayDocTitle true" })
            Assert.Contains(token, raw, StringComparison.Ordinal);
        using var second = new MemoryStream(); document.Save(second, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, second.ToArray()); TypographyTests.Save("accessible-archive-basic.pdf", bytes);
        using var negative = new MemoryStream(); document.Save(negative, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        TypographyTests.Save("accessible-archive-negative.pdf", negative.ToArray());
    }

    [Theory]
    [InlineData(PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData(PdfVectorSemantics.Decorative)]
    public void ExplicitVectorAndTextBoxSemanticsSurviveCombinedConformance(PdfVectorSemantics semantics)
    {
        bool decorative = semantics == PdfVectorSemantics.Decorative;
        using var input = new MemoryStream(PdfAccessibleVectorTests.Source(decorative ? "" : "Blue trend diagram", change: "box"));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.Title = "Synthetic accessible shapes"; var options = Options(); options.Pdf.VectorSemantics = semantics;
        options.Pdf.TextBoxReadingOrder = PdfTextBoxReadingOrder.AnchorOrder;
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Contains("Box text", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.NotEmpty(pdf.GetPage(1).Paths);
        TypographyTests.Save(decorative ? "accessible-archive-box-decorative.pdf" : "accessible-archive-box.pdf", output.ToArray());
    }

    [Fact]
    public void ChartAndEquationRetainTheirAlternativesInCombinedOutput()
    {
        var options = Options(); WordDocument document = ObjectPdfProfileTests.Document(options);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        string raw = Encoding.ASCII.GetString(output.ToArray());
        Assert.Contains("/S /Figure", raw, StringComparison.Ordinal); Assert.Contains("/S /Formula", raw, StringComparison.Ordinal);
        Assert.Contains("/Alt", raw, StringComparison.Ordinal); TypographyTests.Save("accessible-archive-objects.pdf", output.ToArray());
    }

    [Fact]
    public void DescribedAndDecorativeAlphaImagesRetainTheirSemantics()
    {
        WordDocument document = WordDocument.Create(); document.Title = "Accessible images";
        document.AddParagraph("Image examples", headingLevel: 1, cancellationToken: TestContext.Current.CancellationToken);
        byte[] image = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "color-grid-alpha.png"));
        document.AddImage(new TemplateImage(image, 60, 30, "Red and blue grid"), TestContext.Current.CancellationToken);
        document.AddImage(new TemplateImage(image, 60, 30, "", true), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(2, pdf.GetPage(1).NumberOfImages);
        TypographyTests.Save("accessible-archive-images.pdf", output.ToArray());
    }

    [Theory]
    [InlineData(PdfAccessibility.None)]
    [InlineData(PdfAccessibility.Tagged)]
    public void ArchivalLevelARequiresTheCompleteUaProfile(PdfAccessibility accessibility)
    {
        var options = Options(); options.Pdf.Accessibility = accessibility; using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => PdfAccessibilityTests.Source().Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Pdf.TaggedArchive:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("language")]
    [InlineData("title")]
    [InlineData("a2b")]
    [InlineData("native")]
    public void UnqualifiedCombinationsLeaveCallerOutputUntouched(string failure)
    {
        var options = Options(); WordDocument document = PdfAccessibilityTests.Source();
        if (failure == "language") options.Pdf.Language = null;
        if (failure == "title") document.Title = "";
        if (failure == "a2b") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (failure == "native") { options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; }
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void CancelledCombinedExportDoesNotPublishOutput()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => PdfAccessibilityTests.Source().Save(output, DocumentFormat.Pdf, Options(), cancelled.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void MissingVectorDescriptionAndExhaustedTagBudgetRemainAtomic()
    {
        using var input = new MemoryStream(PdfAccessibleVectorTests.Source(""));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.Title = "Missing vector description"; var options = Options(); options.Pdf.VectorSemantics = PdfVectorSemantics.DescribedAtAnchor;
        using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Pdf.VectorAlternative:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        var limited = new ConversionOptions(maximumExpandedBytes: 4096); limited.Pdf.Conformance = PdfConformance.PdfA2a;
        limited.Pdf.Accessibility = PdfAccessibility.PdfUa1; limited.Pdf.Language = "en";
        Assert.Throws<InvalidDataException>(() => PdfAccessibilityTests.Source().Save(output, DocumentFormat.Pdf, limited, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void MixingPositionedDrawingsAndChartObjectsRequiresQualification()
    {
        using var input = new MemoryStream(PdfAccessibleVectorTests.Source());
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.Title = "Mixed drawing owners";
        document.AddChart(new ChartDefinition(ChartKind.ClusteredColumn, OneCategory, new[] { new ChartSeries("Revenue", "2468AC", new double?[] { 1 }) }),
            description: "One value", cancellationToken: TestContext.Current.CancellationToken);
        var options = Options(); options.Pdf.VectorSemantics = PdfVectorSemantics.DescribedAtAnchor;
        using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Pdf.TaggedArchive:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
