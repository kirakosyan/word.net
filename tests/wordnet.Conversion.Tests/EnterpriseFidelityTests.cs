using System;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using SkiaSharp;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterpriseFidelityTests
{
    [Fact]
    public void LenientForceSplitsNonSplittingRowsAndMergedGroupsWithoutDroppingText()
    {
        string content = string.Concat(Enumerable.Range(1, 100).Select(i => TestDocuments.TextParagraph("Enterprise row " + i + ".")));
        foreach (bool merged in new[] { false, true })
        {
            string body = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w='6000'/></w:tblGrid><w:tr>"
                + (merged ? "" : "<w:trPr><w:cantSplit/></w:trPr>") + "<w:tc><w:tcPr>"
                + (merged ? "<w:vMerge w:val='restart'/>" : "") + "</w:tcPr>" + content + "</w:tc></w:tr>"
                + (merged ? "<w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc></w:tr>" : "") + "</w:tbl>";
            using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body))); using var output = new MemoryStream();
            ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray());
            Assert.True(pdf.NumberOfPages > 1);
            string text = string.Concat(pdf.GetPages().Select(p => p.Text));
            for (int i = 1; i <= 100; i++) Assert.Contains("Enterprise row " + i + ".", text, StringComparison.Ordinal);
            Assert.Contains(result.Diagnostics, d => d.Code == (merged ? "Table.MergedGroupSplit" : "Table.RowForcedSplit"));
        }
    }

    [Fact]
    public void NamespaceDeclarationsDoNotBecomeImageEffectLosses()
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph());
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var source = new MemoryStream(input); using var rewritten = new MemoryStream();
        DocxWriter.Write(DocxReader.Read(source, context), rewritten, context);
        using var strictInput = new MemoryStream(rewritten.ToArray()); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(strictInput, output, DocumentFormat.Docx, DocumentFormat.Html, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Docx.ImageEffects");
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output.ToArray()));
        Assert.Single(html.QuerySelectorAll("img"));
    }

    [Theory]
    [InlineData("indent")]
    [InlineData("font")]
    [InlineData("cell")]
    public void LenientRetainsTextWhenGeometryCannotFit(string kind)
    {
        string paragraph = "<w:p><w:pPr>" + (kind == "indent" ? "<w:ind w:left='30000'/>" : "")
            + "</w:pPr><w:r><w:rPr>" + (kind == "font" ? "<w:sz w:val='3276'/>" : "")
            + "</w:rPr><w:t>Enterprise geometry</w:t></w:r></w:p>";
        if (kind == "cell") paragraph = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w='20'/></w:tblGrid><w:tr><w:tc><w:tcPr>"
            + "<w:tcW w:type='dxa' w:w='20'/><w:tcMar><w:left w:type='dxa' w:w='200'/><w:right w:type='dxa' w:w='200'/></w:tcMar></w:tcPr>" + paragraph + "</w:tc></w:tr></w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph))); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal("Enterprise geometry", string.Concat(pdf.GetPages().Select(p => p.Text)));
        Assert.Contains(result.Diagnostics, d => d.Code is "Layout.IndentApproximated" or "Layout.TextScaledToFit" or "Table.WidthApproximated");
    }

    [Fact]
    public void BodyPageFieldsFollowParagraphPageBreaks()
    {
        string body = string.Concat(Enumerable.Range(1, 3).Select(i => "<w:p><w:pPr>" + (i > 1 ? "<w:pageBreakBefore/>" : "")
            + "</w:pPr><w:r><w:t>Enterprise page </w:t></w:r><w:fldSimple w:instr='PAGE'><w:r><w:t>99</w:t></w:r></w:fldSimple></w:p>"));
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(TestDocuments.Docx(TestDocuments.Body(body)), new ConversionOptions(FidelityMode.Lenient)));
        Assert.Equal(3, pdf.NumberOfPages);
        for (int i = 1; i <= 3; i++) Assert.Equal("Enterprise page " + i, pdf.GetPage(i).Text);
    }

    [Fact]
    public void HtmlNotesRetainReferenceLinksAndEndText()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "contract-notes.docx"));
        using var source = new MemoryStream(bytes); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Html, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output.ToArray()));
        Assert.Contains("Reference1", html.Body!.TextContent, StringComparison.Ordinal);
        Assert.EndsWith("1Independent contractual note", html.Body.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Notes.PlacedAtEnd");
        Assert.NotNull(html.QuerySelector("a[role=doc-noteref]"));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    public void ManagedLegacyRasterDecodersPreservePixels(string extension)
    {
        string path = Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "logo-" + extension + ".docx");
        using var archive = new System.IO.Compression.ZipArchive(File.OpenRead(path));
        using Stream stream = archive.GetEntry("word/media/logo." + extension)!.Open();
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        byte[] original = buffer.ToArray();
        using SKBitmap expected = SKBitmap.Decode(original);
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        DocumentImage image = DocumentImage.Read(original, 120, 60, "", context);
        using SKBitmap actual = SKBitmap.Decode(image.Bytes);
        Assert.Equal("png", image.Extension);
        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < actual.Height; y++) for (int x = 0; x < actual.Width; x++) Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
        Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Image.Reencoded");
    }

    [Fact]
    public void HtmlRetainsCellDecorationAndNestedStructure()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "table-shading.docx"));
        using var document = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(new WordConverter().ConvertToHtml(input, new ConversionOptions(FidelityMode.BestEffort))));
        string style = Assert.Single(document.QuerySelectorAll("td")).GetAttribute("style")!;
        Assert.Contains("background-color:#DDEEFF", style, StringComparison.Ordinal);
        Assert.Contains("vertical-align:middle", style, StringComparison.Ordinal);
        Assert.Contains("border-bottom:1pt solid #336699", style, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreePartFooterUsesLeftCenterAndRightStops()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "three-part-footer.docx"));
        using PdfDocument document = PdfDocument.Open(new WordConverter().ConvertToPdf(input, new ConversionOptions(FidelityMode.BestEffort)));
        var footer = document.GetPage(1).Letters.Where(l => l.StartBaseLine.Y < 60).ToArray();
        Assert.Equal("Left footerCenter footerRight footer", string.Concat(footer.Select(l => l.Value)));
        Assert.InRange(footer[0].StartBaseLine.X, 71, 73);
        Assert.InRange(footer[11].StartBaseLine.X, 250, 270);
        Assert.InRange(footer.Last().EndBaseLine.X, 521, 523);
    }

    [Fact]
    public void AcceptedRevisionsAndCurrentControlsRetainTextWithExplicitRevisionLoss()
    {
        string markup = "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:ins><w:r><w:t>Accepted content</w:t></w:r></w:ins>"
            + "<w:del><w:r><w:delText>Deleted content</w:delText></w:r></w:del><w:sdt><w:sdtContent><w:r><w:t>Current value</w:t></w:r></w:sdtContent></w:sdt></w:p></w:sdtContent></w:sdt>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(markup))); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Html, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output.ToArray()));
        Assert.Equal("Accepted contentCurrent value", html.Body!.TextContent);
        Assert.Contains(result.Diagnostics, d => d.Code == "Revisions.Accepted" && d.Severity == DiagnosticSeverity.Warning);
    }
}
