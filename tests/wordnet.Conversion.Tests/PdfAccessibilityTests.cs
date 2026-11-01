using System;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfAccessibilityTests
{
    private static readonly string[] InvalidLanguages = { "en\n", "en-AA-AA", "en<script>" };
    private static readonly string[][] Headerless = { new[] { "A", "B" }, new[] { "1", "2" } };
    internal static WordDocument Source()
    {
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='2000'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc>" + TestDocuments.TextParagraph("Product") + "</w:tc><w:tc>" + TestDocuments.TextParagraph("Price") + "</w:tc></w:tr><w:tr><w:tc>" + TestDocuments.TextParagraph("Service") + "</w:tc><w:tc>" + TestDocuments.TextParagraph("42.50") + "</w:tc></w:tr></w:tbl>";
        string heading = "<w:p><w:pPr><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>Invoice</w:t></w:r></w:p>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(heading + TestDocuments.TextParagraph("Summary first.") + table + "<w:p><w:hyperlink r:id='link'><w:r><w:t>Terms</w:t></w:r></w:hyperlink></w:p>"), TestDocuments.LinkRelationship("https://example.test/terms")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _); document.Title = "Accessible invoice"; return document;
    }
    [Theory]
    [InlineData(PdfAccessibility.Tagged)]
    [InlineData(PdfAccessibility.PdfUa1)]
    public void SourceSemanticsProduceRealPageAndAnnotationTags(PdfAccessibility mode)
    {
        WordDocument document = Source(); var options = new ConversionOptions(); options.Pdf.Accessibility = mode; options.Pdf.Language = "en-US";
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken); byte[] bytes = output.ToArray();
        using PdfDocument pdf = PdfDocument.Open(bytes); Assert.Contains("Invoice", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("42.50", pdf.GetPage(1).Text, StringComparison.Ordinal); string raw = Encoding.ASCII.GetString(bytes);
        foreach (string name in new[] { "/StructTreeRoot", "/StructParents", "/ParentTree", "/MCID", "/OBJR", "/Scope /Column", "/DisplayDocTitle true" }) Assert.Contains(name, raw, StringComparison.Ordinal);
        TypographyTests.Save(mode == PdfAccessibility.PdfUa1 ? "accessible-ua1.pdf" : "accessible-tagged.pdf", bytes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAuthorSemanticsRejectUaBeforeOutput(bool missingTitle)
    {
        WordDocument document = Source(); if (missingTitle) document.Title = "";
        var options = new ConversionOptions(); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = missingTitle ? "en" : null;
        using var output = new MemoryStream(); output.WriteByte(42); var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)); Assert.StartsWith(missingTitle ? "Pdf.Title" : "Pdf.Language", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        document.Title = "Language controls";
        foreach (string language in InvalidLanguages) { options.Pdf.Language = language; Assert.StartsWith("Pdf.Language", Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray()); }
    }
    [Fact]
    public void TaggingDoesNotChangeOrdinaryPdfDefaults()
    {
        using var output = new MemoryStream(); Source().Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken); Assert.DoesNotContain("/StructTreeRoot", Encoding.ASCII.GetString(output.ToArray()), StringComparison.Ordinal); TypographyTests.Save("accessible-negative.pdf", output.ToArray());
    }
    [Fact]
    public void MultiPageListsImagesAndRepeatedTableHeadersHaveLogicalRoles()
    {
        using var markdown = new MemoryStream(Encoding.UTF8.GetBytes("# Accessibility sample\n\n1. First action\n2. Second action\n\nFinal instruction."));
        WordDocument document = WordDocument.Load(markdown, DocumentFormat.Markdown, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "Reading order qualification";
        byte[] image = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "color-grid.png"));
        document.AddImage(new TemplateImage(image, 60, 30, "Red and blue color grid"), TestContext.Current.CancellationToken); document.AddImage(new TemplateImage(image, 60, 30, "", true), TestContext.Current.CancellationToken);
        var rows = Enumerable.Range(0, 50).Select(i => (System.Collections.Generic.IEnumerable<string>)new[] { i == 0 ? "Column A" : "Record " + i, i == 0 ? "Column B" : "Value " + i }); document.AddTable(rows, true, TestContext.Current.CancellationToken);
        var options = new ConversionOptions(); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en-GB";
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken); using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(pdf.NumberOfPages >= 2);
        string raw = Encoding.ASCII.GetString(output.ToArray()); Assert.Contains("/S /L ", raw, StringComparison.Ordinal); Assert.Contains("/S /LI ", raw, StringComparison.Ordinal); Assert.Contains("/S /Lbl ", raw, StringComparison.Ordinal); Assert.Contains("/S /LBody ", raw, StringComparison.Ordinal); Assert.Contains("/S /Figure ", raw, StringComparison.Ordinal); Assert.Contains("/Alt <", raw, StringComparison.Ordinal);
        TypographyTests.Save("accessible-multipage.pdf", output.ToArray());
        using var docx = new MemoryStream(); document.Save(docx, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); docx.Position = 0; WordDocument reloaded = WordDocument.Load(docx, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); Assert.Contains(reloaded.Paragraphs.SelectMany(p => p.Runs), r => r.Image?.Decorative == true);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingImageAltOrTableHeaderRejectsUa(bool table)
    {
        WordDocument document = WordDocument.Create(); document.Title = "Semantic controls";
        if (table) document.AddTable(Headerless, false, TestContext.Current.CancellationToken);
        else document.AddImage(new TemplateImage(File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "color-grid.png")), 60, 30), TestContext.Current.CancellationToken);
        var options = new ConversionOptions(FidelityMode.BestEffort); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en";
        using var output = new MemoryStream(); var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)); Assert.StartsWith(table ? "Pdf.TableHeaders" : "Pdf.ImageAlt", error.Message, StringComparison.Ordinal); Assert.Equal(0, output.Length);
    }
    [Fact]
    public void TagBufferBudgetFailsBeforeCopyAndStableClockIsDeterministic()
    {
        var options = new ConversionOptions(maximumExpandedBytes: 4096); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en";
        using var output = new MemoryStream(); Assert.Throws<InvalidDataException>(() => Source().Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
        var stable = new ConversionOptions { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) }; stable.Pdf.Accessibility = PdfAccessibility.PdfUa1; stable.Pdf.Language = "en";
        using var first = new MemoryStream(); using var second = new MemoryStream(); Source().Save(first, DocumentFormat.Pdf, stable, TestContext.Current.CancellationToken); Source().Save(second, DocumentFormat.Pdf, stable, TestContext.Current.CancellationToken); Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void SkippedHeadingLevelsAreRejectedAsMissingHierarchy()
    {
        WordDocument document = WordDocument.Create(); document.Title = "Hierarchy control"; document.AddParagraph("Skipped parent", headingLevel: 2, cancellationToken: TestContext.Current.CancellationToken);
        var options = new ConversionOptions(FidelityMode.BestEffort); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en";
        using var output = new MemoryStream(); var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)); Assert.StartsWith("Pdf.HeadingOrder", error.Message, StringComparison.Ordinal); Assert.Equal(0, output.Length);
    }

}
