using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;
using A = global::DocumentFormat.OpenXml.Drawing;
using W = global::DocumentFormat.OpenXml.Wordprocessing;
using Wp = global::DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace WordNet.Tests;

public sealed partial class DocumentFidelityTests
{
    private static readonly string[] Widths = { "2400", "4800" };
    private static readonly string[] ListText = { "First", "Nested", "Second" };
    private static readonly int[] Levels = { 0, 1, 0 };
    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    public void PreservesFontSizeColorTableWidthsAndPadding(DocumentFormat intermediate)
    {
        byte[] input = FidelityDocuments.Docx(body: FidelityDocuments.Formatting + FidelityDocuments.Table);
        byte[] encoded = Convert(input, DocumentFormat.Docx, intermediate);
        using WordprocessingDocument output = Open(Convert(encoded, intermediate, DocumentFormat.Docx));
        W.Run title = output.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().First().Elements<W.Run>().Single();
        Assert.Equal("Blue title", title.InnerText); Assert.NotNull(title.RunProperties!.Bold);
        Assert.Equal("36", title.RunProperties.FontSize!.Val!.Value); Assert.Equal("336699", title.RunProperties.Color!.Val!.Value);
        W.Table table = Assert.Single(output.MainDocumentPart.Document.Body.Elements<W.Table>());
        Assert.Equal("7200", table.GetFirstChild<W.TableProperties>()!.TableWidth!.Width!.Value);
        Assert.Equal(Widths, table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Select(c => c.Width!.Value));
        W.TableCell cell = table.Descendants<W.TableCell>().First();
        Assert.Equal("2400", cell.TableCellProperties!.TableCellWidth!.Width!.Value);
        var margins = cell.TableCellProperties.GetFirstChild<W.TableCellMargin>()!;
        Assert.Equal("100", margins.GetFirstChild<W.TopMargin>()!.Width!.Value);
        Assert.Equal("200", margins.GetFirstChild<W.LeftMargin>()!.Width!.Value);
        Assert.Equal("120", margins.GetFirstChild<W.BottomMargin>()!.Width!.Value);
        Assert.Equal("160", margins.GetFirstChild<W.RightMargin>()!.Width!.Value);
        Validate(output);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    public void PreservesEditableNumberedAndNestedBulletedLists(DocumentFormat intermediate)
    {
        byte[] input = FidelityDocuments.Docx(body: FidelityDocuments.ListParagraph("First") + FidelityDocuments.ListParagraph("Nested", 1) + FidelityDocuments.ListParagraph("Second"), lists: true);
        using WordprocessingDocument output = Open(Convert(Convert(input, DocumentFormat.Docx, intermediate), intermediate, DocumentFormat.Docx));
        W.Paragraph[] paragraphs = output.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Equal(ListText, paragraphs.Select(p => p.InnerText));
        Assert.Equal(Levels, paragraphs.Select(p => p.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value));
        W.Numbering numbering = output.MainDocumentPart.NumberingDefinitionsPart!.Numbering!;
        Assert.Contains(numbering.Descendants<W.StartNumberingValue>(), s => s.Val!.Value == 3);
        Assert.Contains(numbering.Descendants<W.NumberingFormat>(), s => s.Val!.Value == W.NumberFormatValues.Bullet);
        Validate(output);
    }

    [Fact]
    public void PdfUsesFontSizesColorsCellWidthsPaddingAndListStarts()
    {
        byte[] docx = FidelityDocuments.Docx(body: FidelityDocuments.Formatting + FidelityDocuments.Table + FidelityDocuments.ListParagraph("Item"), lists: true);
        using PdfDocument pdf = PdfDocument.Open(Convert(docx, DocumentFormat.Docx, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal(18, letters[0].FontSize, 3);
        var rgb = letters[0].Color.ToRGBValues();
        Assert.Equal(0.2, rgb.r, 3); Assert.Equal(0.4, rgb.g, 3); Assert.Equal(0.6, rgb.b, 3);
        Assert.Contains("3. Item", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal(58, letters.First(l => l.Value == "F").StartBaseLine.X, 2);
        Assert.Equal(168, letters.First(l => l.Value == "S").StartBaseLine.X, 2);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc, "color-grid.png")]
    [InlineData(DocumentFormat.Doc, "color-grid.jpg")]
    [InlineData(DocumentFormat.Epub, "color-grid.png")]
    [InlineData(DocumentFormat.Epub, "color-grid.jpg")]
    [InlineData(DocumentFormat.Doc, "color-grid-alpha.png")]
    [InlineData(DocumentFormat.Epub, "color-grid-alpha.png")]
    public void PreservesEmbeddedImageBytesDimensionsAndText(DocumentFormat intermediate, string imageName)
    {
        byte[] original = FidelityDocuments.Docx(imageName);
        byte[] encoded = Convert(original, DocumentFormat.Docx, intermediate);
        using WordprocessingDocument output = Open(Convert(encoded, intermediate, DocumentFormat.Docx));
        Assert.Equal("Blue title", output.MainDocumentPart!.Document!.Body!.InnerText);
        ImagePart image = Assert.Single(output.MainDocumentPart.ImageParts);
        using Stream stream = image.GetStream(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        Assert.Equal(FidelityDocuments.Fixture(imageName), bytes.ToArray());
        Wp.Extent extent = Assert.Single(output.MainDocumentPart.Document.Descendants<Wp.Extent>());
        Assert.Equal(1524000L, extent.Cx!.Value); Assert.Equal(762000L, extent.Cy!.Value);
        using SKBitmap bitmap = Decode(bytes.ToArray()); Assert.Equal(80, bitmap.Width);
        Validate(output);
    }

    [Theory]
    [InlineData(DocumentFormat.Docx, "color-grid.png")]
    [InlineData(DocumentFormat.Docx, "color-grid.jpg")]
    [InlineData(DocumentFormat.Doc, "color-grid.png")]
    [InlineData(DocumentFormat.Doc, "color-grid.jpg")]
    public void WordToPdfEmbedsRealPixelsAtRequestedSize(DocumentFormat source, string imageName)
    {
        byte[] input = FidelityDocuments.Docx(imageName);
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        var image = Assert.Single(pdf.GetPage(1).GetImages());
        Assert.Equal(80, image.WidthInSamples); Assert.Equal(40, image.HeightInSamples);
        Assert.Equal(120, image.BoundingBox.Width, 3); Assert.Equal(60, image.BoundingBox.Height, 3);
        byte[] pixels = imageName.EndsWith("jpg", StringComparison.Ordinal) ? image.RawMemory.ToArray() : image.TryGetPng(out byte[]? png) ? png : throw new InvalidDataException("Missing PNG");
        using SKBitmap bitmap = Decode(pixels);
        Assert.True(bitmap.GetPixel(10, 20).Red > 180); Assert.True(bitmap.GetPixel(60, 20).Blue > 180);
        Assert.Contains("Blue title", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void IndependentPdfImageRetainsSizePixelsAndReadingOrder(DocumentFormat target)
    {
        byte[] output = Convert(FidelityDocuments.Fixture("illustrated.pdf"), DocumentFormat.Pdf, target);
        if (target == DocumentFormat.Doc) output = Convert(output, target, DocumentFormat.Docx);
        using WordprocessingDocument document = Open(output);
        W.Paragraph[] paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Contains("Before the illustration", paragraphs[0].InnerText, StringComparison.Ordinal);
        Assert.Single(paragraphs[1].Descendants<W.Drawing>());
        Assert.Contains("After the illustration", paragraphs[2].InnerText, StringComparison.Ordinal);
        Assert.Equal(1524000L, Assert.Single(document.MainDocumentPart.Document.Descendants<Wp.Extent>()).Cx!.Value);
        using Stream stream = Assert.Single(document.MainDocumentPart.ImageParts).GetStream(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        using SKBitmap bitmap = Decode(bytes.ToArray()); Assert.Equal(80, bitmap.Width); Assert.True(bitmap.GetPixel(60, 20).Blue > 180);
        Validate(document);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3277")]
    [InlineData("oops")]
    public void RejectsInvalidFontSizesBeforeWriting(string value)
    {
        byte[] input = FidelityDocuments.Docx(body: FidelityDocuments.Formatting.Replace("w:val='36'", "w:val='" + value + "'"));
        Fails<InvalidDataException>(input, DocumentFormat.Docx);
    }

    [Theory]
    [InlineData(0L, 762000L)]
    [InlineData(-1L, 762000L)]
    [InlineData(1524000L, 0L)]
    public void RejectsInvalidImageDimensionsBeforeWriting(long width, long height) =>
        Fails<InvalidDataException>(FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph(width, height)), DocumentFormat.Docx);

    [Fact]
    public void ImagePixelBudgetFailsBeforeWriting()
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png");
        Fails<InvalidDataException>(input, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 10000));
    }

    [Fact]
    public void RejectsMissingListDefinitionsBeforeWriting() => Fails<InvalidDataException>(FidelityDocuments.Docx(body: FidelityDocuments.ListParagraph("Broken")), DocumentFormat.Docx);

    [Theory]
    [InlineData(DocumentFormat.Text)]
    [InlineData(DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void UnsupportedImageOutputsAreExplicitAndPreserveStrictDestination(DocumentFormat target)
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph());
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, target));
        Assert.Equal(new byte[] { 42 }, output.ToArray()); source.Position = 0; output.SetLength(0);
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, target, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Image.Output");
    }

    private static void Fails<T>(byte[] input, DocumentFormat format, ConversionOptions? options = null) where T : Exception
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<T>(() => new WordConverter().Convert(source, output, format, format == DocumentFormat.Docx ? DocumentFormat.Doc : DocumentFormat.Docx, options ?? new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
    private static byte[] Convert(byte[] input, DocumentFormat from, DocumentFormat to)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        new WordConverter().Convert(source, output, from, to, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken); return output.ToArray();
    }
    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);
    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Invalid image");
    private static void Validate(WordprocessingDocument output) => Assert.Empty(new OpenXmlValidator().Validate(output, TestContext.Current.CancellationToken));
}
