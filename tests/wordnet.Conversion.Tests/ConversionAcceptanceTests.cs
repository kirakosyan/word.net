using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

/// <summary>Real minimal-content acceptance cases for every agreed format direction.</summary>
public sealed class ConversionAcceptanceTests
{
    internal const string Expected = "Wordnet conversion test\nEnglish printed text 12345.";
    private readonly WordConverter converter = new WordConverter();

    [Theory]
    [Trait("Category", "Conversion")]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pdf, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pdf, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Markdown, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Markdown, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Text)]
    [InlineData(DocumentFormat.Text, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Text)]
    [InlineData(DocumentFormat.Text, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Azw3)]
    [InlineData(DocumentFormat.Azw3, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Azw3)]
    [InlineData(DocumentFormat.Azw3, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Epub, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Epub, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Mobi, DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Mobi, DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Xps)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Xps)]
    public void ConversionPreservesCoreTextInRealFormat(DocumentFormat sourceFormat, DocumentFormat destinationFormat)
    {
        using var source = new MemoryStream(Input(sourceFormat));
        using var destination = new MemoryStream();
        ConversionResult result = converter.Convert(source, destination, sourceFormat, destinationFormat, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Equal(destination.Length, result.BytesWritten);
        Assert.Equal(Normalize(Expected), Normalize(ReadOutput(destination.ToArray(), destinationFormat)));
        if (sourceFormat == DocumentFormat.Doc) Assert.Contains(result.Diagnostics, d => d.Code == "Doc.Formatting");
        if (sourceFormat == DocumentFormat.Pdf) Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.Reconstruction");
    }

    [Theory]
    [Trait("Category", "Ocr")]
    [InlineData(DocumentFormat.Doc, "scanned.pdf", false)]
    [InlineData(DocumentFormat.Docx, "scanned.pdf", false)]
    [InlineData(DocumentFormat.Doc, "mixed.pdf", true)]
    [InlineData(DocumentFormat.Docx, "mixed.pdf", true)]
    public void EnglishScannedAndMixedPdfProducesEditableWordContent(DocumentFormat destinationFormat, string fixture, bool mixed)
    {
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA");
        string? native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY");
        Assert.False(string.IsNullOrEmpty(data), "Provision OCR test prerequisites with eng/prepare-ocr-tests.ps1; WORDNET_TESSDATA is required.");
        Assert.False(string.IsNullOrEmpty(native), "WORDNET_TESSERACT_LIBRARY must identify a Tesseract 5 library for the test process architecture.");
        var options = new ConversionOptions(FidelityMode.BestEffort, englishOcr: new EnglishOcrOptions(data!, native!));
        byte[] inputBytes = Fixture(fixture);
        using (PdfDocument input = PdfDocument.Open(inputBytes)) Assert.True(input.GetPage(1).NumberOfImages > 0);
        using var source = new MemoryStream(inputBytes);
        using var output = new MemoryStream();
        ConversionResult result = converter.Convert(source, output, DocumentFormat.Pdf, destinationFormat, options, TestContext.Current.CancellationToken);
        string expected = Expected + (mixed ? "\nDigital footer remains editable." : "");
        // Exact content on this clean English fixture means zero character/word errors; order is checked too.
        Assert.Equal(Normalize(expected), Normalize(ReadOutput(output.ToArray(), destinationFormat)));
        Assert.Contains(result.Diagnostics, d => d.Code == "Ocr.English");
        Assert.Contains(result.Diagnostics, d => d.Code == "Ocr.Confidence");
    }

    internal static byte[] Input(DocumentFormat format) => format switch
    {
        DocumentFormat.Docx => TestDocuments.Docx(TestDocuments.Body(string.Concat(Expected.Split('\n').Select(TestDocuments.TextParagraph)))),
        DocumentFormat.Text => Encoding.UTF8.GetBytes(Expected),
        DocumentFormat.Markdown => Encoding.UTF8.GetBytes(Expected.Replace("\n", "\n\n")),
        DocumentFormat.Doc => Fixture("plain.doc"),
        DocumentFormat.Pdf => Fixture("digital.pdf"),
        DocumentFormat.Epub => Fixture("plain.epub"),
        DocumentFormat.Mobi => Fixture("plain.mobi"),
        DocumentFormat.Azw3 => Fixture("plain.azw3"),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, name));
    internal static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static string ReadOutput(byte[] bytes, DocumentFormat format)
    {
        using var stream = new MemoryStream(bytes);
        if (format == DocumentFormat.Docx)
        {
            using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
            Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
            Assert.Empty(document.MainDocumentPart!.ImageParts);
            return string.Join("\n", document.MainDocumentPart.Document!.Body!.Elements<W.Paragraph>().Select(p => p.InnerText));
        }
        if (format == DocumentFormat.Doc)
        {
            using var root = RootStorage.Open(stream);
            using CfbStream word = root.OpenStream("WordDocument");
            using var reader = new BinaryReader(word, Encoding.Unicode, true);
            Assert.Equal(0xa5ec, reader.ReadUInt16()); Assert.Equal(0xc1, reader.ReadUInt16());
            word.Position = 24; int first = reader.ReadInt32(), last = reader.ReadInt32();
            word.Position = 76; int characters = reader.ReadInt32();
            Assert.Equal(characters * 2, last - first);
            word.Position = first;
            string text = Encoding.Unicode.GetString(reader.ReadBytes(characters * 2));
            Assert.EndsWith("\r", text, StringComparison.Ordinal);
            return text.TrimEnd('\r').Replace('\r', '\n');
        }
        if (format == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(stream);
            Assert.True(pdf.NumberOfPages > 0);
            foreach (var page in pdf.GetPages()) Assert.All(page.Letters, l => { Assert.InRange(l.StartBaseLine.X, 0, page.Width); Assert.InRange(l.StartBaseLine.Y, 0, page.Height); });
            return string.Join("\n", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
        }
        if (format is DocumentFormat.Text or DocumentFormat.Markdown) return Encoding.UTF8.GetString(bytes).Replace("\\.", ".");
        if (format is DocumentFormat.Epub or DocumentFormat.Xps)
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (format == DocumentFormat.Epub)
            {
                using var media = new StreamReader(zip.Entries[0].Open()); Assert.Equal("mimetype", zip.Entries[0].FullName); Assert.Equal("application/epub+zip", media.ReadToEnd());
                using Stream content = zip.GetEntry("EPUB/content.xhtml")!.Open();
                XDocument html = XDocument.Load(content); return string.Join("\n", html.Descendants(XName.Get("p", "http://www.w3.org/1999/xhtml")).Select(p => p.Value));
            }
            Assert.NotNull(zip.GetEntry("FixedDocumentSequence.fdseq"));
            return string.Join("\n", zip.Entries.Where(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Select(e =>
            {
                using Stream page = e.Open(); XDocument xml = XDocument.Load(page);
                return string.Join("\n", xml.Descendants(XName.Get("Glyphs", "http://schemas.microsoft.com/xps/2005/06")).GroupBy(g => (string?)g.Attribute("OriginY")).Select(group => string.Concat(group.Select(g => (string?)g.Attribute("UnicodeString")))));
            }));
        }
        // The writer emits uncompressed UTF-8 text records. Validate the actual container and version.
        Assert.Equal("BOOKMOBI", Encoding.ASCII.GetString(bytes, 60, 8));
        int header = Big32(bytes, 78), firstText = Big32(bytes, 86);
        Assert.Equal("MOBI", Encoding.ASCII.GetString(bytes, header + 16, 4));
        Assert.Equal(format == DocumentFormat.Azw3 ? 8 : 6, Big32(bytes, header + 36));
        int textBytes = Big32(bytes, header + 4);
        XDocument markup = XDocument.Parse(Encoding.UTF8.GetString(bytes, firstText, textBytes));
        return string.Join("\n", markup.Descendants(XName.Get("p", "http://www.w3.org/1999/xhtml")).Select(p => p.Value));
    }

    private static int Big32(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
