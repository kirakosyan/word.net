using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PreviewTests
{
    private const string Geometry = "<w:sectPr><w:pgSz w:w='6000' w:h='4000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/><w:pgNumType w:fmt='lowerRoman' w:start='3'/></w:sectPr>";
    private static byte[] Source() => TestDocuments.Docx(TestDocuments.Body(Page("First", false) + Page("Middle", true) + Page("Last", true) + Geometry));
    private static string Page(string name, bool pageBreak) => "<w:p>" + (pageBreak ? "<w:r><w:br w:type='page'/></w:r>" : "") + "<w:r><w:t xml:space='preserve'>" + name + " </w:t></w:r><w:fldSimple w:instr='PAGE'><w:r><w:t>0</w:t></w:r></w:fldSimple><w:r><w:t>/</w:t></w:r><w:fldSimple w:instr='NUMPAGES'><w:r><w:t>0</w:t></w:r></w:fldSimple></w:p>";

    [Fact]
    public void SelectedPdfKeepsSourceFieldsAndRemapsPhysicalPages()
    {
        var options = new ConversionOptions(); options.Pdf.Pages.Add(new PageRange(2, 3)); options.Pdf.OutlineHeadings = true;
        using var input = new MemoryStream(Source()); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(2, pdf.NumberOfPages); Assert.Equal(2, result.Metrics.PageCount);
        Assert.Equal("Middle iv/3", pdf.GetPage(1).Text); Assert.Equal("Last v/3", pdf.GetPage(2).Text);
        Assert.DoesNotContain("First", pdf.GetPage(1).Text, StringComparison.Ordinal); SaveArtifact("selected.pdf", output.ToArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(1, 3)]
    public void SvgUsesActualGlyphOutlinesAndOriginalPhysicalIdentity(int first, int last)
    {
        var settings = new PreviewOptions(); settings.Pages.Add(new PageRange(first, last));
        using var source = new MemoryStream(Source()); DocumentPreview result = new WordConverter().RenderPages(source, DocumentFormat.Docx, settings, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, result.SourcePageCount); Assert.Equal(last - first + 1, result.Pages.Count); Assert.True(source.CanRead);
        foreach (PagePreview page in result.Pages)
        {
            Assert.InRange(page.SourcePage, first, last); Assert.Equal(300, page.Width); Assert.Equal(200, page.Height); Assert.Equal(400, page.PixelWidth);
            Assert.Equal(page.SourcePage == 1 ? "iii" : page.SourcePage == 2 ? "iv" : "v", page.Label);
            XElement svg = XElement.Parse(System.Text.Encoding.UTF8.GetString(page.GetBytes())); XNamespace ns = "http://www.w3.org/2000/svg";
            Assert.Equal("0 0 300 200", (string?)svg.Attribute("viewBox")); Assert.NotEmpty(svg.Descendants(ns + "path")); Assert.Empty(svg.Descendants(ns + "text"));
            Assert.DoesNotContain(svg.DescendantsAndSelf().Attributes(), a => !a.IsNamespaceDeclaration && (a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("/3", string.Concat(svg.Descendants(ns + "title").Select(t => t.Value)), StringComparison.Ordinal);
            byte[] bytes = page.GetBytes(); bytes[0] = 0; Assert.NotEqual(0, page.GetBytes()[0]); SaveArtifact("source-" + page.SourcePage + ".svg", page.GetBytes());
        }
    }

    [Theory]
    [InlineData(72, 300, 200)]
    [InlineData(144, 600, 400)]
    public void PngHasRequestedPixelsAndActualRenderedText(int dpi, int width, int height)
    {
        var settings = new PreviewOptions { Format = PreviewFormat.Png, Dpi = dpi }; settings.Pages.Add(new PageRange(2, 2));
        using var source = new MemoryStream(Source()); PagePreview page = Assert.Single(new WordConverter().RenderPages(source, DocumentFormat.Docx, settings, cancellationToken: TestContext.Current.CancellationToken).Pages);
        using SKBitmap pixels = SKBitmap.Decode(page.GetBytes()); Assert.Equal(width, pixels.Width); Assert.Equal(height, pixels.Height);
        Assert.Equal(SKColors.White, pixels.GetPixel(0, 0)); int ink = 0;
        for (int y = dpi / 3; y < dpi; y++) for (int x = dpi / 3; x < dpi * 3; x++) if (pixels.GetPixel(x, y).Red < 128) ink++;
        Assert.True(ink > 40); Assert.Equal("iv", page.Label); SaveArtifact("middle-" + dpi + ".png", page.GetBytes());
    }

    [Fact]
    public void RasterAndSvgRetainImageBytesAndPhysicalFrame()
    {
        byte[] source = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph() + Geometry); var options = new ConversionOptions(FidelityMode.BestEffort);
        using var svgInput = new MemoryStream(source); PagePreview svg = Assert.Single(new WordConverter().RenderPages(svgInput, DocumentFormat.Docx, options: options, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XNamespace ns = "http://www.w3.org/2000/svg"; XElement image = Assert.Single(XElement.Parse(System.Text.Encoding.UTF8.GetString(svg.GetBytes())).Descendants(ns + "image"));
        Assert.Equal("120", (string?)image.Attribute("width")); Assert.Equal("60", (string?)image.Attribute("height")); Assert.StartsWith("data:image/png;base64,", (string?)image.Attribute("href") ?? "", StringComparison.Ordinal);
        using var pngInput = new MemoryStream(source); PagePreview png = Assert.Single(new WordConverter().RenderPages(pngInput, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Png, Dpi = 72 }, options, TestContext.Current.CancellationToken).Pages);
        using SKBitmap bitmap = SKBitmap.Decode(png.GetBytes()); Assert.Equal(new SKColor(208, 32, 32), bitmap.GetPixel(45, 45)); Assert.Equal(new SKColor(32, 64, 208), bitmap.GetPixel(130, 45));
        SaveArtifact("image.svg", svg.GetBytes()); SaveArtifact("image.png", png.GetBytes());
    }

    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(1, 2, true)]
    public void InvalidAndOverlappingRangesFailWithoutPdfOutput(int first, int last, bool overlap)
    {
        var options = new ConversionOptions(); options.Pdf.Pages.Add(new PageRange(first, last)); if (overlap) options.Pdf.Pages.Add(new PageRange(2, 3));
        using var input = new MemoryStream(Source()); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<ArgumentException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void PreviewBoundsAndCancellationAreChecked()
    {
        using var input = new MemoryStream(Source());
        Assert.Throws<InvalidDataException>(() => new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Png, Dpi = 600, MaximumPixels = 10000 }, cancellationToken: TestContext.Current.CancellationToken));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); input.Position = 0;
        Assert.Throws<OperationCanceledException>(() => new WordConverter().RenderPages(input, DocumentFormat.Docx, cancellationToken: canceled.Token));
        input.Position = 0; Assert.Throws<InvalidDataException>(() => new WordConverter().RenderPages(input, DocumentFormat.Docx, options: new ConversionOptions(maximumOutputBytes: 100), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreviewCallsAreIndependentAndDeterministic()
    {
        var converter = new WordConverter(); byte[] source = Source(); var options = new PreviewOptions(); options.Pages.Add(new PageRange(3, 3));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => { using var input = new MemoryStream(source); return converter.RenderPages(input, DocumentFormat.Docx, options, cancellationToken: TestContext.Current.CancellationToken); }, TestContext.Current.CancellationToken)));
        Assert.All(results, r => Assert.Equal(results[0].Pages[0].GetBytes(), r.Pages[0].GetBytes()));
    }
    private static void SaveArtifact(string name, byte[] bytes)
    { string? path = Environment.GetEnvironmentVariable("WORDNET_INTEROP_ARTIFACTS"); if (!string.IsNullOrEmpty(path)) { Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, name), bytes); } }
}
