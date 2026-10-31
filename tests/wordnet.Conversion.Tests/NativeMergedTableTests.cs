using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class NativeMergedTableTests
{
    private const string Grid = "<w:tblGrid><w:gridCol w:w='1800'/><w:gridCol w:w='1800'/><w:gridCol w:w='1800'/></w:tblGrid>";
    private const string Section = "<w:sectPr><w:pgSz w:w='8000' w:h='5000'/><w:pgMar w:left='600' w:right='600' w:top='600' w:bottom='600'/></w:sectPr>";
    private static string P(string text) => TestDocuments.TextParagraph(text);
    private static string Cell(string properties, string content) => "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + content + "</w:tc>";
    private static byte[] Docx(string rows) => TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr>" + Grid + rows + "</w:tbl>" + Section));

    [Fact]
    public void NativeMergedCellsKeepLogicalHindiTextWidthsAndRepeatedHeaders()
    {
        const string text = "हिन्दी क्षि";
        string rows = "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("<w:gridSpan w:val='2'/>", P("Header")) + Cell("", P("Amount")) + "</w:tr>"
            + string.Concat(Enumerable.Range(0, 16).Select(i => "<w:tr>" + Cell("<w:vMerge w:val='restart'/>", P(text))
                + Cell("", P("Item" + i.ToString("D2", CultureInfo.InvariantCulture))) + Cell("", P("100")) + "</w:tr>"
                + "<w:tr>" + Cell("<w:vMerge/>", "<w:p/>") + Cell("", P("Detail")) + Cell("", P("200")) + "</w:tr>"));
        byte[] source = Docx(rows); ConversionOptions options = ExtendedTypographyTests.Options(mode: FidelityMode.BestEffort);
        using var input = new MemoryStream(source); var context = new ConversionContext(options, TestContext.Current.CancellationToken);
        var pages = FixedLayout.Create(DocxReader.Read(input, context), context); Assert.True(pages.Count > 1);
        Assert.Contains(context.Result(0).Diagnostics, item => item.Code == "Typography.MergedTableSpacing");
        Assert.All(pages, page => Assert.Contains(page.Text, item => item.Text == "Header"));
        Assert.Equal(16, pages.SelectMany(page => page.Text).Count(item => item.Text == "हिन्दी"));
        Assert.Equal(16, pages.SelectMany(page => page.Text).Count(item => item.Text == "Detail"));
        for (int i = 0; i < 16; i++) Assert.Single(pages.SelectMany(page => page.Text), item => item.Text == "Item" + i.ToString("D2", CultureInfo.InvariantCulture));
        Assert.All(pages.SelectMany(page => page.Text), item => Assert.NotNull(item.NativeShape));
        foreach (var page in pages)
        {
            var header = page.Text.Single(item => item.Text == "Header"); var amount = page.Text.Single(item => item.Text == "Amount");
            Assert.True(header.X < amount.X);
        }
        Assert.DoesNotContain(pages.SelectMany(page => page.Text), item => item.Text == "ि");
        using var docxInput = new MemoryStream(source); WordDocument document = WordDocument.Load(docxInput, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        using var package = new System.IO.Compression.ZipArchive(new MemoryStream(saved.ToArray()));
        using Stream xml = package.GetEntry("word/document.xml")!.Open(); XNamespace w = TestDocuments.W; XDocument result = XDocument.Load(xml);
        Assert.Single(result.Descendants(w + "gridSpan")); Assert.Equal(32, result.Descendants(w + "vMerge").Count());
        using var previewInput = new MemoryStream(source);
        DocumentPreview preview = new WordConverter().RenderPages(previewInput, DocumentFormat.Docx, options: options, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(preview.Pages.Count > 1); Assert.Contains(preview.Result.Diagnostics, item => item.Code == "Typography.MergedTableSpacing");
        Assert.All(preview.Pages, page => Assert.NotEmpty(page.GetBytes()));
        using var fixedOutput = new MemoryStream();
        ConversionResult pdfResult = document.Save(fixedOutput, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(pdfResult.Diagnostics, item => item.Code == "Typography.MergedTableSpacing"); Assert.True(fixedOutput.Length > 0);
        string? folder = Environment.GetEnvironmentVariable("WORDNET_ROADMAP_EVIDENCE");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); const string name = "native-hindi-merged";
            File.WriteAllBytes(Path.Combine(folder, name + ".docx"), source);
            using var pdf = new MemoryStream(); document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), pdf.ToArray());
        }
    }

    [Theory]
    [InlineData("مرحبا عربي", false)]
    [InlineData("Latin \u2067A\u2069", false)]
    [InlineData("Latin", true)]
    public void RightToLeftMergedNativeTablesRemainExplicitlyRejected(string text, bool forceDirection)
    {
        byte[] source = Docx("<w:tr>" + Cell("<w:gridSpan w:val='2'/>", P(text)) + Cell("", P("A")) + "</w:tr>");
        ConversionOptions options = ExtendedTypographyTests.Options(false);
        if (forceDirection) options.Typography.Direction = TextDirection.RightToLeft;
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(7);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Typography.MergedTableDirection:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Theory]
    [InlineData(PdfAccessibility.Tagged, PdfConformance.Pdf17)]
    [InlineData(PdfAccessibility.PdfUa1, PdfConformance.Pdf17)]
    [InlineData(PdfAccessibility.None, PdfConformance.PdfA2b)]
    public void MergedNativeConformanceProfilesRemainExplicitlyRejected(PdfAccessibility accessibility, PdfConformance conformance)
    {
        byte[] source = Docx("<w:tr>" + Cell("<w:gridSpan w:val='2'/>", P("مرحبا")) + Cell("", P("A")) + "</w:tr>");
        ConversionOptions options = ExtendedTypographyTests.Options(false); options.Pdf.Accessibility = accessibility; options.Pdf.Conformance = conformance;
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(7);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        string expected = conformance == PdfConformance.PdfA2b ? "Typography.Conformance:" : "Typography.MergedTableConformance:";
        Assert.StartsWith(expected, error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Fact]
    public void InvalidNativeMergeLeavesOutputUntouched()
    {
        byte[] source = Docx("<w:tr>" + Cell("<w:vMerge/>", P("Invalid")) + Cell("", P("A")) + Cell("", P("B")) + "</w:tr>");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(7);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, ExtendedTypographyTests.Options(false), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Fact]
    public void StrictMergedNativeOutputRejectsUnqualifiedRowSpacing()
    {
        byte[] source = Docx("<w:tr>" + Cell("<w:gridSpan w:val='2'/>", P("हिन्दी")) + Cell("", P("A")) + "</w:tr>");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(7);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, ExtendedTypographyTests.Options(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Typography.MergedTableSpacing:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Theory]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void OversizedNativeVerticalMergeIsRejectedBeforeOutput(FidelityMode mode)
    {
        string longText = string.Concat(Enumerable.Repeat(P("हिन्दी क्षि"), 60));
        string rows = "<w:tr>" + Cell("<w:vMerge w:val='restart'/>", longText) + Cell("", P("A")) + Cell("", P("B")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:vMerge/>", "<w:p/>") + Cell("", P("C")) + Cell("", P("D")) + "</w:tr>";
        using var input = new MemoryStream(Docx(rows)); using var output = new MemoryStream(); output.WriteByte(7);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, ExtendedTypographyTests.Options(true, mode), TestContext.Current.CancellationToken));
        Assert.StartsWith("Typography.MergedGroupHeight:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7 }, output.ToArray());
    }
}
