using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class OcrMetadataTests
{
    private static ConversionContext Context(int regions = 50000) => new ConversionContext(new ConversionOptions(FidelityMode.BestEffort)
    { OcrMetadata = new OcrMetadataOptions { Enabled = true, MaximumRegions = regions } }, TestContext.Current.CancellationToken);
    private static string Row(string text, int x = 20, int y = 10, int width = 40, int height = 20, int line = 1, double confidence = 80)
        => string.Join("\t", "5", "1", "1", "1", line.ToString(CultureInfo.InvariantCulture), "1", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture), width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), confidence.ToString(CultureInfo.InvariantCulture), text) + "\n";

    [Fact]
    public void NumericGeometryUnicodeOffsetsAndConfidenceGroupsMatchSpecification()
    {
        string tsv = Row("A😀") + Row("café", x: 80, confidence: 60) + Row("Second", y: 50, line: 2, confidence: 100);
        OcrPage page = OcrGeometry.Build(tsv, "A😀 café\nSecond", 3, 17, 72, 36, 200, 100, 200, 100, OcrTransform.Identity, 78, null, Context());
        OcrRegion[] words = page.Regions.Where(r => r.Kind == OcrRegionKind.Word).ToArray();
        Assert.Equal(3, page.SourcePage); Assert.Equal(17, page.DocumentTextStart); Assert.Equal(78, page.MeanConfidence);
        Assert.Equal(0, words[0].TextStart); Assert.Equal(3, words[0].TextLength); Assert.Equal(4, words[1].TextStart); Assert.Equal(9, words[2].TextStart);
        Assert.Equal(7.2, words[0].Left, 8); Assert.Equal(3.6, words[0].Top, 8); Assert.Equal(14.4, words[0].Width, 8); Assert.Equal(7.2, words[0].Height, 8);
        Assert.Equal(70, page.Regions.First(r => r.Kind == OcrRegionKind.Line).Confidence); Assert.Equal(80, Assert.Single(page.Regions, r => r.Kind == OcrRegionKind.Paragraph).Confidence);
        Assert.Equal("café", page.Text.Substring(words[1].TextStart!.Value, words[1].TextLength));
    }

    [Theory]
    [InlineData(90, 30, 70, 40, 10)]
    [InlineData(180, 170, 30, 10, 40)]
    [InlineData(270, 130, 20, 40, 10)]
    public void QuarterTurnsInvertToTheOriginalTopLeftPagePlane(int angle, double left, double top, double width, double height)
    {
        int w = angle == 180 ? 200 : 100, h = angle == 180 ? 100 : 200;
        OcrPage page = OcrGeometry.Build(Row("Word", x: 20, y: 30, width: 10, height: 40), "Word", 1, 0, 200, 100, 200, 100, w, h, OcrTransform.Rotation(200, 100, w, h, angle), 80, null, Context());
        OcrRegion word = Assert.Single(page.Regions, r => r.Kind == OcrRegionKind.Word);
        Assert.Equal(left, word.Left, 8); Assert.Equal(top, word.Top, 8); Assert.Equal(width, word.Width, 8); Assert.Equal(height, word.Height, 8); Assert.False(word.IsClipped);
    }

    [Fact]
    public void DeskewAndComposedRotationsIncludeIntermediateCanvasTranslations()
    {
        OcrPage skew = OcrGeometry.Build(Row("Word", x: 50, y: 30), "Word", 1, 0, 200, 100, 200, 100, 206, 112, OcrTransform.Rotation(200, 100, 206, 112, 3), 80, null, Context());
        OcrPoint a = Assert.Single(skew.Regions, r => r.Kind == OcrRegionKind.Word).Polygon[0]; Assert.Equal(45.711899795691046, a.X, 8); Assert.Equal(26.809437777257102, a.Y, 8);
        OcrTransform composed = OcrTransform.Rotation(200, 100, 100, 200, 90).Then(OcrTransform.Rotation(100, 200, 111, 205, 3));
        OcrPage rotated = OcrGeometry.Build(Row("Word", x: 40, y: 50, width: 20, height: 30), "Word", 1, 0, 200, 100, 200, 100, 111, 205, composed, 80, null, Context());
        OcrPoint b = Assert.Single(rotated.Regions, r => r.Kind == OcrRegionKind.Word).Polygon[0]; Assert.Equal(48.3831567471505, b.X, 8); Assert.Equal(68.22639549145045, b.Y, 8);
    }

    [Fact]
    public void EmptyUnknownAndClippedRegionsRemainExplicit()
    {
        OcrPage empty = OcrGeometry.Build("", "", 1, 0, 200, 100, 200, 100, 200, 100, OcrTransform.Identity, 0, null, Context()); Assert.Empty(empty.Regions);
        ConversionContext context = Context(); OcrPage unmapped = OcrGeometry.Build(Row("Different"), "Editable", 1, 0, 200, 100, 200, 100, 200, 100, OcrTransform.Identity, 80, null, context);
        Assert.All(unmapped.Regions, r => Assert.Null(r.TextStart)); Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Ocr.UnmappedRegion");
        OcrPage clipped = OcrGeometry.Build(Row("Word", x: 0, y: 0), "Word", 1, 0, 200, 100, 200, 100, 206, 112, OcrTransform.Rotation(200, 100, 206, 112, 3), 80, null, Context());
        Assert.Contains(clipped.Regions, r => r.IsClipped); Assert.All(clipped.Regions.SelectMany(r => r.Polygon), p => { Assert.InRange(p.X, 0, 200); Assert.InRange(p.Y, 0, 100); });
    }

    [Fact]
    public void WordAndGroupCountsShareOneAggregateAdmissionBudget()
    {
        Assert.Throws<InvalidDataException>(() => OcrGeometry.Build(Row("Word"), "Word", 1, 0, 200, 100, 200, 100, 200, 100, OcrTransform.Identity, 80, null, Context(regions: 2)));
        Assert.Throws<InvalidDataException>(() => OcrGeometry.Build(Row("Word", confidence: double.NaN), "Word", 1, 0, 200, 100, 200, 100, 200, 100, OcrTransform.Identity, 80, null, Context()));
        Assert.Throws<InvalidDataException>(() => OcrGeometry.Build(Row("Word", x: 199), "Word", 1, 0, 200, 100, 200, 100, 200, 100, OcrTransform.Identity, 80, null, Context()));
    }

    [Theory]
    [Trait("Category", "Ocr")]
    [InlineData("ocr-clean.pdf", false)]
    [InlineData("ocr-noisy.pdf", true)]
    [InlineData("ocr-rotated90.pdf", true)]
    [InlineData("ocr-rotated180.pdf", true)]
    [InlineData("ocr-skewed.pdf", true)]
    [InlineData("ocr-columns.pdf", false)]
    [InlineData("multipage-ocr.pdf", false)]
    [InlineData("mixed.pdf", false)]
    public void ActualEnglishScansHaveOriginalGeometryAndExactEditableSpans(string fixture, bool orientation)
    {
        var options = Policy(orientation, fixture == "ocr-columns.pdf"); using var input = new MemoryStream(ConversionAcceptanceTests.Fixture(fixture)); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.OcrPages); using var zip = new ZipArchive(new MemoryStream(output.ToArray())); using var xml = zip.GetEntry("word/document.xml")!.Open(); XElement root = XElement.Load(xml); XNamespace w = TestDocuments.W;
        string canonical = string.Join("\n", root.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
        foreach (OcrPage page in result.OcrPages)
        {
            Assert.Equal(page.Text, canonical.Substring(page.DocumentTextStart, page.Text.Length)); Assert.InRange(page.MeanConfidence, 0, 100);
            if (string.IsNullOrWhiteSpace(page.Text)) { Assert.Empty(page.Regions); Assert.Equal(0, page.MeanConfidence); continue; }
            OcrRegion[] words = page.Regions.Where(r => r.Kind == OcrRegionKind.Word).ToArray(); Assert.NotEmpty(words); Assert.All(words, region =>
            {
                Assert.NotNull(region.TextStart); Assert.True(region.TextLength > 0); Assert.False(string.IsNullOrWhiteSpace(page.Text.Substring(region.TextStart!.Value, region.TextLength)));
                Assert.InRange(region.Left, 0, page.Width); Assert.InRange(region.Top, 0, page.Height); Assert.InRange(region.Left + region.Width, 0, page.Width + .000001); Assert.InRange(region.Top + region.Height, 0, page.Height + .000001);
                Assert.True(region.Width > 0 && region.Height > 0); Assert.InRange(region.Confidence ?? 0, 0, 100);
            });
            Assert.Contains(page.Regions, r => r.Kind == OcrRegionKind.Line); Assert.Contains(page.Regions, r => r.Kind == OcrRegionKind.Paragraph);
            if (fixture is "ocr-clean.pdf" or "ocr-rotated90.pdf" or "ocr-rotated180.pdf")
            {
                OcrRegion annual = Assert.Single(words, r => page.Text.Substring(r.TextStart!.Value, r.TextLength) == "Annual");
                // Independent Pillow fixture: 42px Noto at (120,120), ink box near
                // (120,133)-(259,165), then the fixture's exact right-angle transpose.
                double left = fixture == "ocr-rotated90.pdf" ? 47.88 : fixture == "ocr-rotated180.pdf" ? 518.76 : 43.2;
                double top = fixture == "ocr-rotated90.pdf" ? 518.76 : fixture == "ocr-rotated180.pdf" ? 408.6 : 47.88;
                Assert.InRange(annual.Left, left - 2, left + 2); Assert.InRange(annual.Top, top - 2, top + 2);
            }
        }
        Assert.DoesNotContain(result.Diagnostics, d => InteropText.Contains(d.Message, "business document"));
    }

    [Fact]
    [Trait("Category", "Ocr")]
    public void CaptureIsOptInAndLimitsFailBeforeDestinationCopying()
    {
        using var input = new MemoryStream(ConversionAcceptanceTests.Fixture("ocr-clean.pdf")); using var output = new MemoryStream(); var options = Policy(false, false); options.OcrMetadata.Enabled = false;
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken); Assert.Empty(result.OcrPages);
        input.Position = 0; options.OcrMetadata.Enabled = true; options.OcrMetadata.MaximumRegions = 1; using var bounded = new MemoryStream(); bounded.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, bounded, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, bounded.ToArray());
    }
    private static ConversionOptions Policy(bool orientation, bool columns)
    {
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA"), native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY"); Assert.False(string.IsNullOrEmpty(data) || string.IsNullOrEmpty(native), "Run eng/prepare-ocr-tests.ps1 in the test process.");
        return new ConversionOptions(FidelityMode.BestEffort, englishOcr: new EnglishOcrOptions(data!, native!, orientation, columns)) { OcrMetadata = new OcrMetadataOptions { Enabled = true } };
    }
}
