using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Tokens;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterprisePdfOptionsTests
{
    [Theory]
    [InlineData("color-grid.png")]
    [InlineData("color-grid-alpha.png")]
    public void PngUsesPredictionAndPreservesPixelsAndLinkedImage(string fixture)
    {
        string picture = FidelityDocuments.ImageParagraph().Replace("<w:r>", "<w:fldSimple w:instr='HYPERLINK &quot;https://example.com/logo&quot;'><w:r>")
            .Replace("</w:r>", "</w:r></w:fldSimple>");
        ConversionOptions options = Options(); options.Pdf.Conformance = PdfConformance.PdfA2b;
        byte[] output = new WordConverter().ConvertToPdf(FidelityDocuments.Docx(fixture, picture), options);
        using PdfDocument pdf = PdfDocument.Open(output);
        var image = Assert.Single(pdf.GetPage(1).GetImages());
        Assert.True(image.TryGetPng(out byte[]? png));
        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.Equal(80, bitmap.Width); Assert.Equal(40, bitmap.Height);
        Assert.True(bitmap.GetPixel(10, 20).Red > 180); Assert.True(bitmap.GetPixel(60, 20).Blue > 180);
        if (fixture == "color-grid-alpha.png")
        {
            IToken value = image.ImageDictionary.Data["SMask"];
            if (value is IndirectReferenceToken reference)
            { var token = pdf.Structure.TokenScanner.Get(reference.Data); Assert.NotNull(token); value = token.Data; }
            var mask = Assert.IsType<StreamToken>(value);
            byte[] opacity = mask.Decode(DefaultFilterProvider.Instance).ToArray();
            Assert.Equal(80 * 40, opacity.Length); Assert.All(opacity, sample => Assert.Equal(128, sample));
            Assert.True(mask.StreamDictionary.Data.ContainsKey(NameToken.DecodeParms.Data));
        }
        Assert.Contains("/Predictor 15", Encoding.ASCII.GetString(output), StringComparison.Ordinal);
        Assert.Contains(pdf.GetPage(1).GetHyperlinks(), h => h.Uri == "https://example.com/logo");
        Assert.All(pdf.GetPage(1).GetAnnotations(), a => Assert.Equal(4, a.AnnotationDictionary.Data[NameToken.Create("F").Data] is NumericToken flags ? flags.Int : 0));
    }

    [Fact]
    public void CompressionSwitchRetainsTextAndEmbeddedImages()
    {
        string content = string.Concat(Enumerable.Repeat(TestDocuments.TextParagraph("Enterprise compressed content with an image."), 40));
        byte[] input = FidelityDocuments.Docx("color-grid.jpg", content + FidelityDocuments.ImageParagraph());
        byte[] compressed = new WordConverter().ConvertToPdf(input, Options());
        ConversionOptions options = Options(); options.Pdf.Compress = false;
        byte[] plain = new WordConverter().ConvertToPdf(input, options);
        using PdfDocument left = PdfDocument.Open(compressed), right = PdfDocument.Open(plain);
        Assert.Equal(left.NumberOfPages, right.NumberOfPages);
        Assert.Equal(string.Concat(left.GetPages().Select(p => p.Text)), string.Concat(right.GetPages().Select(p => p.Text)));
        Assert.Equal(1, right.GetPages().Sum(p => p.NumberOfImages));
        IToken filter = ContentStream(left).StreamDictionary.Data[NameToken.Filter.Data];
        if (filter is ArrayToken array) filter = Assert.Single(array.Data);
        Assert.Equal(NameToken.FlateDecode, filter);
        Assert.False(ContentStream(right).StreamDictionary.Data.ContainsKey(NameToken.Filter.Data));
        Assert.True(plain.Length > compressed.Length);
    }

    [Fact]
    public void HeadingOutlinesAreOptionalHierarchicalAndPointAtActualPages()
    {
        string heading1 = "<w:p><w:pPr><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>First heading</w:t></w:r></w:p>";
        string heading2 = "<w:p><w:pPr><w:outlineLvl w:val='1'/></w:pPr><w:r><w:t>Child heading</w:t></w:r></w:p>";
        string next = "<w:p><w:pPr><w:pageBreakBefore/><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>Second heading</w:t></w:r></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(heading1 + heading2 + next));
        using PdfDocument defaultPdf = PdfDocument.Open(new WordConverter().ConvertToPdf(input, Options()));
        Assert.False(defaultPdf.TryGetBookmarks(out _));
        ConversionOptions options = Options(); options.Pdf.OutlineHeadings = true;
        using PdfDocument outlined = PdfDocument.Open(new WordConverter().ConvertToPdf(input, options));
        Assert.True(outlined.TryGetBookmarks(out Bookmarks? bookmarks));
        Assert.NotNull(bookmarks);
        Assert.Equal(2, bookmarks.Roots.Count);
        var first = Assert.IsType<DocumentBookmarkNode>(bookmarks.Roots[0]);
        Assert.Equal("First heading", first.Title); Assert.Equal(1, first.PageNumber);
        Assert.Equal("Child heading", Assert.Single(first.Children).Title);
        Assert.Equal(2, Assert.IsType<DocumentBookmarkNode>(bookmarks.Roots[1]).PageNumber);
    }

    [Fact]
    public void ArchivalOutputHasFixedClockMetadataOutputIntentAndSelectableText()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Enterprise archival å ø æ é €")));
        ConversionOptions options = Options(); options.Pdf.Conformance = PdfConformance.PdfA2b;
        byte[] first = new WordConverter().ConvertToPdf(input, options), second = new WordConverter().ConvertToPdf(input, options);
        Assert.Equal(first, second);
        using PdfDocument pdf = PdfDocument.Open(first);
        Assert.Equal("Enterprise archival å ø æ é €", pdf.GetPage(1).Text);
        Assert.StartsWith("wordnet 1.4.0", pdf.Information.Producer, StringComparison.Ordinal);
        Assert.True(pdf.TryGetXmpMetadata(out var metadata));
        XDocument xmp = metadata.GetXDocument();
        XNamespace aid = "http://www.aiim.org/pdfa/ns/id/", schema = "http://ns.adobe.com/xap/1.0/";
        Assert.Contains(xmp.Descendants().Attributes(aid + "part"), a => a.Value == "2");
        Assert.Contains(xmp.Descendants().Attributes(aid + "conformance"), a => a.Value == "B");
        XNamespace pdfSchema = "http://ns.adobe.com/pdf/1.3/";
        Assert.Single(xmp.Descendants().Attributes(pdfSchema + "Producer").Select(a => a.Value)
            .Concat(xmp.Descendants(pdfSchema + "Producer").Select(e => e.Value)));
        Assert.Contains(xmp.Descendants().Attributes(schema + "CreateDate"), a => a.Value == "2026-10-15T12:00:00Z");
        Assert.Contains("/OutputIntents", Encoding.ASCII.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidConformanceDoesNotChangeTheDestination()
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Enterprise"))));
        using var output = new MemoryStream(); output.WriteByte(42);
        ConversionOptions options = Options(); options.Pdf.Conformance = (PdfConformance)99;
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private static ConversionOptions Options() => new ConversionOptions(FidelityMode.Lenient)
    { Clock = () => new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero) };

    private static StreamToken ContentStream(PdfDocument pdf)
    {
        IToken contents = pdf.GetPage(1).Dictionary.Data[NameToken.Contents.Data];
        if (contents is ArrayToken array) contents = Assert.Single(array.Data);
        var reference = Assert.IsType<IndirectReferenceToken>(contents);
        var token = pdf.Structure.TokenScanner.Get(reference.Data);
        Assert.NotNull(token);
        return Assert.IsType<StreamToken>(token.Data);
    }
}
