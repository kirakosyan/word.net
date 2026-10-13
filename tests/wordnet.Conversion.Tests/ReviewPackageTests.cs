using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;
using Wp = global::DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace WordNet.Tests;

public sealed partial class ReviewRegressionTests
{
    [Fact]
    [Trait("Category", "Ocr")]
    public async Task RepeatedConcurrentOcrConversionsKeepIndependentEnginesAndResults()
    {
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA"), native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY");
        Assert.False(string.IsNullOrEmpty(data), "Run eng/prepare-ocr-tests.ps1 before the OCR suite.");
        Assert.False(string.IsNullOrEmpty(native), "WORDNET_TESSERACT_LIBRARY must be provisioned for this host.");
        var options = new ConversionOptions(FidelityMode.BestEffort, englishOcr: new EnglishOcrOptions(data!, native!));
        byte[] input = FidelityDocuments.Fixture("scanned.pdf");
        for (int repeat = 0; repeat < 3; repeat++)
        {
            byte[][] outputs = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() => Convert(input, DocumentFormat.Pdf, DocumentFormat.Docx, out _, options))));
            foreach (byte[] output in outputs)
                Assert.Equal(ConversionAcceptanceTests.Normalize(ConversionAcceptanceTests.Expected),
                    ConversionAcceptanceTests.Normalize(ConversionAcceptanceTests.ReadOutput(output, DocumentFormat.Docx)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberingStyleLinksResolveLevelsAndClosestStartOverride(bool cycle)
    {
        const string relation = "<Relationship Id='numbering' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>";
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='numbering' w:styleId='Linked'><w:pPr><w:numPr><w:numId w:val='"
            + (cycle ? "7" : "9") + "'/></w:numPr></w:pPr></w:style></w:styles>";
        string numbering = "<w:numbering xmlns:w='" + TestDocuments.W + "'><w:abstractNum w:abstractNumId='1'><w:numStyleLink w:val='Linked'/></w:abstractNum>"
            + "<w:abstractNum w:abstractNumId='2'><w:styleLink w:val='Linked'/><w:lvl w:ilvl='0'><w:start w:val='4'/><w:numFmt w:val='decimal'/><w:lvlText w:val='%1.'/></w:lvl></w:abstractNum>"
            + "<w:num w:numId='7'><w:abstractNumId w:val='1'/><w:lvlOverride w:ilvl='0'><w:startOverride w:val='0'/></w:lvlOverride></w:num>"
            + "<w:num w:numId='9'><w:abstractNumId w:val='2'/><w:lvlOverride w:ilvl='0'><w:startOverride w:val='6'/></w:lvlOverride></w:num></w:numbering>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(FidelityDocuments.ListParagraph("First") + FidelityDocuments.ListParagraph("Second")), relation, styles,
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/numbering.xml", numbering));
        if (cycle)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Convert(input, DocumentFormat.Docx, DocumentFormat.Doc));
            Assert.StartsWith("Docx.NumberingCycle:", error.Message, StringComparison.Ordinal);
        }
        else Assert.Equal("0. First\n1. Second", Encoding.UTF8.GetString(Convert(input, DocumentFormat.Docx, DocumentFormat.Text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("height='2560'")]
    [InlineData("width='1600' height='2560'")]
    [InlineData("style='width:1600px;height:2560px'")]
    public void LargeEpubImagesScaleProportionallyAndRetainOriginalPixels(string dimensions)
    {
        byte[] image = FidelityDocuments.Fixture("large-cover.png");
        byte[] epub = Epub("<p>Cover</p><p><img src='cover.png' " + dimensions + " alt='Cover'/></p>", image, "cover.png");
        byte[] output = Convert(epub, DocumentFormat.Epub, DocumentFormat.Docx, out ConversionResult report);
        Assert.Contains(report.Diagnostics, d => d.Code == "Image.Scaled");
        using WordprocessingDocument document = Open(output);
        Wp.Extent extent = Assert.Single(document.MainDocumentPart!.Document!.Descendants<Wp.Extent>());
        Assert.Equal(12573000, extent.Cx!.Value); Assert.Equal(20116800, extent.Cy!.Value);
        using Stream stream = Assert.Single(document.MainDocumentPart.ImageParts).GetStream(); using var copied = new MemoryStream(); stream.CopyTo(copied);
        Assert.Equal(image, copied.ToArray()); AssertStrictFailure(epub, DocumentFormat.Epub, DocumentFormat.Docx);
        Assert.Throws<InvalidDataException>(() => Convert(epub, DocumentFormat.Epub, DocumentFormat.Docx, out _,
            new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 1000000)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void InvalidEpubImageDimensionsStillFail(string value)
    {
        byte[] epub = Epub("<p><img src='cover.png' width='" + value + "'/></p>", FidelityDocuments.Fixture("color-grid.png"), "cover.png");
        Assert.Throws<InvalidDataException>(() => Convert(epub, DocumentFormat.Epub, DocumentFormat.Docx));
    }

    [Fact]
    public void EpubDecodesImageChapterAndUnrelatedManifestUrlsOnce()
    {
        byte[] image = FidelityDocuments.Fixture("color-grid.png");
        byte[] epub = Epub("<p>Unicode path</p><p><img src='images/Caf%C3%A9%20cover.png' width='80'/></p>", image,
            "images/Café cover.png", "images/Caf%C3%A9%20cover.png", "chapter one.xhtml", "chapter%20one.xhtml",
            "<item id='font' href='fonts/My%20Font.otf' media-type='font/otf'/>");
        using WordprocessingDocument document = Open(Convert(epub, DocumentFormat.Epub, DocumentFormat.Docx));
        Assert.Equal("Unicode path", document.MainDocumentPart!.Document!.InnerText);
        using Stream stream = Assert.Single(document.MainDocumentPart.ImageParts).GetStream(); using var copied = new MemoryStream(); stream.CopyTo(copied);
        Assert.Equal(image, copied.ToArray());
    }

    [Theory]
    [InlineData("%2e%2e/%2e%2e/escape.png")]
    [InlineData("images%2fcover.png")]
    [InlineData("images%5ccover.png")]
    [InlineData("%252e%252e/cover.png")]
    [InlineData("bad%2.png")]
    [InlineData("bad%00.png")]
    public void EpubEncodedUnsafeReferencesFail(string href)
    {
        byte[] epub = Epub("<p><img src='" + href + "'/></p>", FidelityDocuments.Fixture("color-grid.png"), "cover.png", href);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Convert(epub, DocumentFormat.Epub, DocumentFormat.Docx));
        Assert.StartsWith("Package.Path:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfTextMatrixScaledLettersDoNotAcquireSpuriousSpaces()
    {
        byte[] input = FidelityDocuments.Fixture("matrix-scaled.pdf");
        using WordprocessingDocument document = Open(Convert(input, DocumentFormat.Pdf, DocumentFormat.Docx));
        Assert.Equal("Scaled words", document.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().First().InnerText);
        Assert.Single(document.MainDocumentPart.ImageParts);
    }

    [Fact]
    [Trait("Category", "Ocr")]
    public void MultiPageOcrReportsAllPagesAndExplicitImageAndDigitalTextLoss()
    {
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA"), native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY");
        Assert.False(string.IsNullOrEmpty(data), "Run eng/prepare-ocr-tests.ps1 before the OCR suite.");
        Assert.False(string.IsNullOrEmpty(native), "WORDNET_TESSERACT_LIBRARY must be provisioned for this host.");
        var options = new ConversionOptions(FidelityMode.BestEffort, englishOcr: new EnglishOcrOptions(data!, native!));
        Convert(FidelityDocuments.Fixture("scanned.pdf"), DocumentFormat.Pdf, DocumentFormat.Docx, out ConversionResult first, options);
        Convert(FidelityDocuments.Fixture("mixed.pdf"), DocumentFormat.Pdf, DocumentFormat.Docx, out ConversionResult second, options);
        Convert(FidelityDocuments.Fixture("blank-scan.pdf"), DocumentFormat.Pdf, DocumentFormat.Docx, out ConversionResult blank, options);
        byte[] output = Convert(FidelityDocuments.Fixture("multipage-ocr.pdf"), DocumentFormat.Pdf, DocumentFormat.Docx, out ConversionResult combined, options);
        Assert.NotEqual(Confidence(first), Confidence(blank));
        double mean = (Confidence(first) + Confidence(second) + Confidence(blank)) / 3;
        Assert.Equal(mean, Confidence(combined), 2);
        Assert.Contains(combined.Diagnostics, d => d.Code == "Ocr.Confidence" && d.Message.EndsWith("across 3 OCR page(s).", StringComparison.Ordinal));
        Assert.Contains(combined.Diagnostics, d => d.Code == "Pdf.OcrImageLoss");
        Assert.Contains(combined.Diagnostics, d => d.Code == "Pdf.OcrTextReplacement");
        string expected = ConversionAcceptanceTests.Expected + "\n" + ConversionAcceptanceTests.Expected + "\nDigital footer remains editable.";
        Assert.Equal(ConversionAcceptanceTests.Normalize(expected), ConversionAcceptanceTests.Normalize(ConversionAcceptanceTests.ReadOutput(output, DocumentFormat.Docx)));

        static double Confidence(ConversionResult result)
        {
            string message = Assert.Single(result.Diagnostics, d => d.Code == "Ocr.Confidence").Message;
            int start = message.IndexOf(": ", StringComparison.Ordinal) + 2;
#if NET8_0_OR_GREATER
            return double.Parse(message.AsSpan(start, message.IndexOf("/100", StringComparison.Ordinal) - start), CultureInfo.InvariantCulture);
#else
            return double.Parse(message.Substring(start, message.IndexOf("/100", StringComparison.Ordinal) - start), CultureInfo.InvariantCulture);
#endif
        }
    }

    private static byte[] Epub(string body, byte[] image, string imagePath, string? imageHref = null,
        string chapterPath = "chapter.xhtml", string chapterHref = "chapter.xhtml", string extraManifest = "")
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='EPUB/package.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add("EPUB/package.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='chapter' href='" + chapterHref + "' media-type='application/xhtml+xml'/>"
                + "<item id='image' href='" + (imageHref ?? imagePath) + "' media-type='image/png'/>" + extraManifest + "</manifest><spine><itemref idref='chapter'/></spine></package>");
            Add("EPUB/" + chapterPath, "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Review fixture</title></head><body>" + body + "</body></html>");
            FidelityDocuments.Add(archive, "EPUB/" + imagePath, image);
            void Add(string name, string content) => FidelityDocuments.Add(archive, name, Encoding.UTF8.GetBytes(content));
        }
        return memory.ToArray();
    }
}
