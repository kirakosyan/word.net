using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using OpenMcdf;
using UglyToad.PdfPig;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

public sealed class AdditionalFormatTests
{
    private readonly WordConverter converter = new WordConverter();
    private static ConversionOptions BestEffort => new ConversionOptions(FidelityMode.BestEffort);

    [Theory]
    [InlineData(DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void EbookRoundTripPreservesHeadingsEmphasisLinksTablesAndUnicode(DocumentFormat format)
    {
        byte[] docx = TestDocuments.Docx(TestDocuments.Fixture("rich-document.xml"), TestDocuments.LinkRelationship("https://example.com/a%20b?q=1&x=2"));
        byte[] ebook = Convert(docx, DocumentFormat.Docx, format, BestEffort);
        byte[] output = Convert(ebook, format, DocumentFormat.Docx, BestEffort);
        using var stream = new MemoryStream(output);
        using var document = WordprocessingDocument.Open(stream, false);
        W.Body body = document.MainDocumentPart!.Document!.Body!;
        Assert.Equal("A heading", body.Elements<W.Paragraph>().First().InnerText);
        Assert.Equal("Heading1", body.Elements<W.Paragraph>().First().ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Contains(body.Descendants<W.Run>(), r => r.InnerText == "bold" && r.RunProperties?.Bold != null);
        Assert.Contains(body.Descendants<W.Run>(), r => r.InnerText == "italic" && r.RunProperties?.Italic != null);
        Assert.Equal(2, Assert.Single(body.Elements<W.Table>()).Elements<W.TableRow>().Count());
        Assert.Equal("https://example.com/a%20b?q=1&x=2", Assert.Single(document.MainDocumentPart.HyperlinkRelationships).Uri.OriginalString);
        string unicode = "Հայերեն 漢字 😀 café";
        byte[] original = Convert(Encoding.UTF8.GetBytes(unicode), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        Assert.Equal(unicode, ConversionAcceptanceTests.ReadOutput(Convert(Convert(original, DocumentFormat.Docx, format, BestEffort), format, DocumentFormat.Docx, BestEffort), DocumentFormat.Docx));
    }

    [Theory]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void KindleTextSpanningManyRecordsPreservesUtf8(DocumentFormat format)
    {
        string text = string.Concat(Enumerable.Repeat("café Հայաստան 12345. ", 900)).TrimEnd();
        byte[] docx = Convert(Encoding.UTF8.GetBytes(text), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        byte[] ebook = Convert(docx, DocumentFormat.Docx, format, BestEffort);
        Assert.True(ebook.Length > 4096);
        byte[] roundtrip = Convert(ebook, format, DocumentFormat.Docx, BestEffort);
        Assert.Equal(text, ConversionAcceptanceTests.ReadOutput(roundtrip, DocumentFormat.Docx));
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Pdf)]
    public void LossyImportsRequireBestEffortAndDoNotPartiallyWriteInStrictMode(DocumentFormat sourceFormat)
    {
        using var source = new MemoryStream(ConversionAcceptanceTests.Input(sourceFormat)); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => converter.Convert(source, output, sourceFormat, DocumentFormat.Docx));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void PdfPaginationWrapsWithMeasuredWidthsAndPreservesText()
    {
        string text = string.Join("\n", Enumerable.Repeat("A paragraph with several words, some punctuation, and predictable spacing.", 140));
        byte[] docx = Convert(Encoding.UTF8.GetBytes(text), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        byte[] output = Convert(docx, DocumentFormat.Docx, DocumentFormat.Pdf, BestEffort);
        using PdfDocument document = PdfDocument.Open(output);
        Assert.True(document.NumberOfPages >= 3);
        Assert.Equal(ConversionAcceptanceTests.Normalize(text), ConversionAcceptanceTests.Normalize(ConversionAcceptanceTests.ReadOutput(output, DocumentFormat.Pdf)));
        Assert.All(document.GetPages().SelectMany(p => p.Letters), l => Assert.InRange(l.EndBaseLine.X, 48, 548));
        var first = document.GetPage(1).Letters;
        Assert.InRange(first[1].StartBaseLine.X - first[0].StartBaseLine.X, 1, 15);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void FixedPageLimitFailsBeforeOutputIsCopied(DocumentFormat target)
    {
        byte[] docx = Convert(Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Repeat("line", 200))), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        Assert.Throws<InvalidDataException>(() => Convert(docx, DocumentFormat.Docx, target, new ConversionOptions(FidelityMode.BestEffort, maximumPages: 1)));
    }

    [Fact]
    public void PdfBaselineDoesNotSpreadWordsAcrossThePage()
    {
        byte[] pdf = Convert(ConversionAcceptanceTests.Input(DocumentFormat.Docx), DocumentFormat.Docx, DocumentFormat.Pdf, BestEffort);
        using PdfDocument document = PdfDocument.Open(pdf);
        var letters = document.GetPage(1).Letters;
        Assert.Equal(2, letters.Select(l => Math.Round(l.StartBaseLine.Y, 2)).Distinct().Count());
        Assert.True(letters.Max(l => l.EndBaseLine.X) < 350);
    }

    [Fact]
    public void XpsHasResolvableFontsPagesAndGlyphCoordinates()
    {
        byte[] output = Convert(ConversionAcceptanceTests.Input(DocumentFormat.Docx), DocumentFormat.Docx, DocumentFormat.Xps, BestEffort);
        using var memory = new MemoryStream(output); using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        XNamespace xps = "http://schemas.microsoft.com/xps/2005/06";
        using Stream source = zip.GetEntry("Documents/1/Pages/1.fpage")!.Open(); XElement page = XDocument.Load(source).Root!;
        Assert.Equal(xps + "FixedPage", page.Name);
        foreach (XElement glyph in page.Elements(xps + "Glyphs"))
        {
            string font = ((string)glyph.Attribute("FontUri")!).TrimStart('/'); Assert.NotNull(zip.GetEntry(font)); Assert.True(zip.GetEntry(font)!.Length > 10000);
            Assert.InRange((double)glyph.Attribute("OriginX")!, 0, (double)page.Attribute("Width")!);
            Assert.InRange((double)glyph.Attribute("OriginY")!, 0, (double)page.Attribute("Height")!);
        }
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void MissingFixedPageGlyphsFailBeforeWriting(DocumentFormat target)
    {
        byte[] docx = Convert(Encoding.UTF8.GetBytes("漢字"), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        using var input = new MemoryStream(docx); using var output = new MemoryStream();
        Assert.Contains("Layout.Glyph", Assert.Throws<NotSupportedException>(() => converter.Convert(input, output, DocumentFormat.Docx, target, new ConversionOptions(), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void BestEffortMissingGlyphsUseVisibleFallback(DocumentFormat target)
    {
        byte[] docx = Convert(Encoding.UTF8.GetBytes("漢字"), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        using var input = new MemoryStream(docx); using var output = new MemoryStream();
        ConversionResult result = converter.Convert(input, output, DocumentFormat.Docx, target, BestEffort, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Font.MissingGlyph" && d.Count == 2);
        if (target == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("??", pdf.GetPage(1).Text);
        }
        else
        {
            using var zip = new ZipArchive(new MemoryStream(output.ToArray()));
            XDocument page = XDocument.Load(zip.GetEntry("Documents/1/Pages/1.fpage")!.Open());
            Assert.Equal("??", string.Concat(page.Descendants().Select(e => (string?)e.Attribute("UnicodeString"))));
        }
    }

    [Fact]
    public void PdfPreservesConsecutiveExplicitLineBreaks()
    {
        byte[] docx = TestDocuments.Docx("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>First</w:t><w:br/><w:br/><w:t>Third</w:t></w:r></w:p></w:body></w:document>");
        using PdfDocument pdf = PdfDocument.Open(Convert(docx, DocumentFormat.Docx, DocumentFormat.Pdf, BestEffort));
        double[] baselines = pdf.GetPage(1).Letters.Select(l => l.StartBaseLine.Y).Distinct().OrderByDescending(y => y).ToArray();
        Assert.Equal(2, baselines.Length);
        Assert.Equal(2 * 11 * 1.362, baselines[0] - baselines[1], 3);
    }

    [Theory]
    [InlineData("META-INF/encryption.xml", "<encryption/>", true)]
    [InlineData("book/chapter.xhtml", "<!DOCTYPE html [<!ENTITY x SYSTEM 'file:///not-read'>]><html>&x;</html>", false)]
    public void EpubRejectsEncryptedResourcesAndExternalEntities(string name, string xml, bool encryption)
    {
        byte[] input = RewriteEpub(name, xml);
        if (encryption) Assert.Throws<NotSupportedException>(() => Convert(input, DocumentFormat.Epub, DocumentFormat.Docx, BestEffort));
        else Assert.Throws<InvalidDataException>(() => Convert(input, DocumentFormat.Epub, DocumentFormat.Docx, BestEffort));
    }

    [Fact]
    public void EpubRejectsRootEscapesAndLimitsExpansion()
    {
        byte[] input = RewriteEpub("book/book.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='x' href='../../secret' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='x'/></spine></package>");
        Assert.Throws<InvalidDataException>(() => Convert(input, DocumentFormat.Epub, DocumentFormat.Docx, BestEffort));
        Assert.Throws<InvalidDataException>(() => Convert(ConversionAcceptanceTests.Fixture("plain.epub"), DocumentFormat.Epub, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 64)));
    }

    [Fact]
    public void UnsupportedEbookStylingAndWhitespaceAreDiagnosed()
    {
        byte[] input = RewriteEpub("book/chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>T</title></head><body><p><u>underlined</u></p></body></html>");
        Assert.Throws<NotSupportedException>(() => Convert(input, DocumentFormat.Epub, DocumentFormat.Docx, new ConversionOptions()));
        using var source = new MemoryStream(input); using var destination = new MemoryStream();
        ConversionResult result = converter.Convert(source, destination, DocumentFormat.Epub, DocumentFormat.Docx, BestEffort, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Ebook.InlineFormatting");
        byte[] docx = Convert(Encoding.UTF8.GetBytes("  spaces  "), DocumentFormat.Text, DocumentFormat.Docx, BestEffort);
        Assert.Throws<NotSupportedException>(() => Convert(docx, DocumentFormat.Docx, DocumentFormat.Epub, new ConversionOptions()));
    }

    [Theory]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void KindleRejectsEncryptionCorruptRecordsAndTruncation(DocumentFormat format)
    {
        byte[] input = ConversionAcceptanceTests.Input(format); int header = Big32(input, 78);
        byte[] encrypted = (byte[])input.Clone(); encrypted[header + 13] = 2;
        Assert.Throws<NotSupportedException>(() => Convert(encrypted, format, DocumentFormat.Docx, BestEffort));
        byte[] corrupt = (byte[])input.Clone(); corrupt[86] = 0x7f;
        Assert.Throws<InvalidDataException>(() => Convert(corrupt, format, DocumentFormat.Docx, BestEffort));
        Assert.Throws<InvalidDataException>(() => Convert(input.Take(70).ToArray(), format, DocumentFormat.Docx, BestEffort));
        Assert.Throws<InvalidDataException>(() => Convert(input, format, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 8)));
    }

    [Fact]
    public void DocRejectsEncryptedFibAndInvalidTextOffsets()
    {
        byte[] original = ConversionAcceptanceTests.Fixture("plain.doc");
        byte[] Modify(int position, byte value)
        {
            using var memory = new MemoryStream(); memory.Write(original, 0, original.Length); memory.Position = 0;
            using (var storage = RootStorage.Open(memory, StorageModeFlags.LeaveOpen)) { using CfbStream word = storage.OpenStream("WordDocument"); word.Position = position; word.WriteByte(value); }
            return memory.ToArray();
        }
        Assert.Throws<NotSupportedException>(() => Convert(Modify(11, 0x13), DocumentFormat.Doc, DocumentFormat.Docx, BestEffort));
        Assert.Throws<InvalidDataException>(() => Convert(Modify(0x1a5, 0x7f), DocumentFormat.Doc, DocumentFormat.Docx, BestEffort));
    }

    [Fact]
    public void ScannedPdfNeedsOcrAndRasterBudgetIsEnforcedBeforeNativeLoading()
    {
        byte[] input = ConversionAcceptanceTests.Fixture("scanned.pdf");
        Assert.Contains("Ocr.Required", Assert.Throws<NotSupportedException>(() => Convert(input, DocumentFormat.Pdf, DocumentFormat.Docx, BestEffort)).Message, StringComparison.Ordinal);
        var ocr = new EnglishOcrOptions("missing-data", "missing-library", maximumImagePixels: 1);
        Assert.Throws<InvalidDataException>(() => Convert(input, DocumentFormat.Pdf, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort, englishOcr: ocr)));
    }

    [Fact]
    public async Task NewEnginesRespectAsyncCancellationAndStagedOutputLimit()
    {
        using var source = new MemoryStream(ConversionAcceptanceTests.Input(DocumentFormat.Docx)); using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(source, destination, DocumentFormat.Docx, DocumentFormat.Epub, BestEffort, cancellation.Token));
        Assert.Equal(0, source.Position); Assert.Equal(0, destination.Length);
        Assert.Throws<InvalidDataException>(() => converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort, maximumOutputBytes: 100), TestContext.Current.CancellationToken));
        Assert.Equal(0, destination.Length);
    }

    private byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, ConversionOptions options)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); converter.Convert(input, output, from, to, options, TestContext.Current.CancellationToken); return output.ToArray();
    }

    private static byte[] RewriteEpub(string name, string text)
    {
        using var memory = new MemoryStream(); byte[] original = ConversionAcceptanceTests.Fixture("plain.epub"); memory.Write(original, 0, original.Length); memory.Position = 0;
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, true)) { archive.GetEntry(name)?.Delete(); using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
        return memory.ToArray();
    }

    private static int Big32(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
