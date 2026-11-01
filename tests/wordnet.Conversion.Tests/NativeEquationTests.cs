using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class NativeEquationTests
{
    private static readonly string[] Categories = { "A" };
    private const string Formula = "<m:oMathPara xmlns:m='http://schemas.openxmlformats.org/officeDocument/2006/math'><m:oMath>"
        + "<m:f><m:num><m:r><m:t>x</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>"
        + "</m:oMath></m:oMathPara>";
    private const string InlineFormula = "<m:oMath xmlns:m='http://schemas.openxmlformats.org/officeDocument/2006/math'>"
        + "<m:f><m:num><m:r><m:t>x</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>";

    private static ConversionOptions Options(FidelityMode mode = FidelityMode.BestEffort)
    {
        var options = ExtendedTypographyTests.Options(mode: mode);
        options.Fonts.MathFamily = "STIX Two Math";
        options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf")));
        options.Pdf.Compress = false;
        return options;
    }

    private static byte[] Source(string equation = Formula) => TestDocuments.Docx(TestDocuments.Body(
        TestDocuments.TextParagraph("पहले हिन्दी") + "<w:p>" + equation + "</w:p>" + TestDocuments.TextParagraph("بعد مرحبا")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneEquationUsesMathGlyphsBetweenLogicalNativeParagraphs(bool display)
    {
        byte[] source = Source(display ? Formula : InlineFormula); var options = Options();
        using var input = new MemoryStream(source);
        var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        var model = DocxReader.Read(input, context); FidelityChecks.Destination(model, DocumentFormat.Pdf, context);
        var page = Assert.Single(FixedLayout.Create(model, context));
        Assert.Contains(page.Text, item => item.Text.Contains("हिन्दी") && item.NativeShape != null);
        Assert.Contains(page.Text, item => item.Text.Contains("مرحبا") && item.NativeShape != null);
        var math = page.Text.Where(item => item.NativeShape?.Glyphs.Any(glyph => glyph.Glyph >= 0) == true && item.Text.Length == 0).ToArray();
        Assert.True(math.Length >= 2); Assert.NotEmpty(page.Shapes);
        Assert.Contains(page.Text, item => item.Text == "(x)/(2)" && item.NativeShape != null);
        double before = page.Text.Where(item => item.Text.Contains("हिन्दी")).Max(item => item.Y);
        double after = page.Text.Where(item => item.Text.Contains("مرحبا")).Min(item => item.Y);
        Assert.All(math, item => Assert.InRange(item.Y, before, after));
        byte[] pdf = new WordConverter().ConvertToPdf(source, out ConversionResult result, options);
        Assert.Contains(result.Diagnostics, item => item.Code == "Object.EditableOutputLoss");
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.EquationReferenceApproximation");
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.EquationDirectionalOrder");
        using var parsed = UglyToad.PdfPig.PdfDocument.Open(pdf); Assert.Equal(1, parsed.NumberOfPages);
        string? evidence = Environment.GetEnvironmentVariable("WORDNET_NATIVE_EQUATION_EVIDENCE");
        if (evidence != null) { Directory.CreateDirectory(evidence); File.WriteAllBytes(Path.Combine(evidence, "native-equation.pdf"), pdf); }
    }

    [Theory]
    [InlineData("mixed-inline")]
    [InlineData("mixed")]
    [InlineData("cell")]
    [InlineData("tagged")]
    [InlineData("archive")]
    [InlineData("strict")]
    [InlineData("rtl")]
    [InlineData("notes")]
    [InlineData("old-typography")]
    [InlineData("missing-font")]
    public void UnqualifiedCombinationsLeaveCallerOutputUnchanged(string failure)
    {
        var options = Options(failure == "strict" ? FidelityMode.Strict : FidelityMode.BestEffort); byte[] source = Source();
        if (failure == "mixed-inline") source = Source("<w:r><w:t>text</w:t></w:r>" + InlineFormula);
        if (failure == "mixed") source = Source("<w:r><w:t>text</w:t></w:r>" + Formula);
        if (failure == "cell") source = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tr><w:tc><w:p>" + Formula + "</w:p></w:tc></w:tr></w:tbl>"));
        if (failure == "tagged") options.Pdf.Accessibility = PdfAccessibility.Tagged;
        if (failure == "archive") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (failure == "rtl") options.Typography.Direction = TextDirection.RightToLeft;
        if (failure == "notes") source = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:t>Before</w:t><w:footnoteReference w:id='7'/></w:r></w:p><w:p>" + Formula + "</w:p>"),
            "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new KeyValuePair<string, string>("word/footnotes.xml", "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='7'><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:footnote></w:footnotes>"));
        if (failure == "old-typography") { options.Typography.ExtendedBidi = false; options.Typography.Devanagari = false; }
        if (failure == "missing-font") options.Fonts.MathFamily = null;
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void SvgPreviewUsesLogicalLabelsAndVectorEquationGlyphs()
    {
        using var input = new MemoryStream(Source(InlineFormula));
        var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, options: Options(), cancellationToken: TestContext.Current.CancellationToken);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(Assert.Single(preview.Pages).GetBytes()));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Contains(svg.Descendants(ns + "title"), item => item.Value == "(x)/(2)");
        Assert.Contains(svg.Descendants(ns + "title"), item => item.Value.Contains("हिन्दी"));
        Assert.True(svg.Descendants(ns + "path").Count() > 10);
    }

    [Fact]
    public void RefreshedDirectionalFieldAlsoReportsEquationExtractionDifference()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body("<w:p>" + P1FieldTests.Field("DOCPROPERTY Word") + "</w:p><w:p>" + InlineFormula + "</w:p>"));
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("Word", "مرحبا"), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.FieldDirectionalOrder");
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.EquationDirectionalOrder");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedEquationAndExhaustedGlyphBudgetDoNotPublishOutput(bool malformed)
    {
        string invalid = "<m:oMath xmlns:m='http://schemas.openxmlformats.org/officeDocument/2006/math'><m:f><m:num><m:r><m:t>x</m:t></m:r></m:num></m:f></m:oMath>";
        var options = Options(); if (!malformed) options.Typography.MaximumGlyphs = 2;
        using var input = new MemoryStream(malformed ? Source(invalid) : Source()); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith(malformed ? "Equation.Children:" : "Limit.TypographyGlyphs:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ChartAndCancelledExportStayRejectedWithoutChangingOutput()
    {
        var options = Options(); var document = WordDocument.Create(options);
        document.AddChart(new ChartDefinition(ChartKind.ClusteredColumn, Categories, new[] { new ChartSeries("S", "2468AC", new double?[] { 1 }) }), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var input = new MemoryStream(Source());
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, cancelled.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
