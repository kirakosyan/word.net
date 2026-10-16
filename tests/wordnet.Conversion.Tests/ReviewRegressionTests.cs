using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;
using Wp = global::DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace WordNet.Tests;

public sealed partial class ReviewRegressionTests
{
    [Theory]
    [InlineData("color-grid.png", 6, 0xf01e)]
    [InlineData("color-grid.jpg", 5, 0xf01d)]
    public void DocPicturesContainConsistentStoreAndBlipRecords(string fixture, int type, int blipType)
    {
        byte[] doc = Convert(FidelityDocuments.Docx(fixture), DocumentFormat.Docx, DocumentFormat.Doc);
        using var memory = new MemoryStream(doc); using var root = RootStorage.Open(memory);
        using CfbStream data = root.OpenStream("Data"); using var reader = new BinaryReader(data);
        int pictureLength = reader.ReadInt32(); data.Position = 68;
        Assert.Equal(0x000f, reader.ReadUInt16()); Assert.Equal(0xf004, reader.ReadUInt16());
        int shapeLength = reader.ReadInt32(); data.Position += shapeLength;
        Assert.Equal((type << 4) | 2, reader.ReadUInt16()); Assert.Equal(0xf007, reader.ReadUInt16());
        int storeLength = reader.ReadInt32(); Assert.Equal(type, reader.ReadByte()); Assert.Equal(type, reader.ReadByte());
        byte[] uid = reader.ReadBytes(16); Assert.Equal(255, reader.ReadUInt16());
        int blipLength = reader.ReadInt32(); Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32()); Assert.Equal(new byte[4], reader.ReadBytes(4));
        Assert.Equal(36 + blipLength, storeLength);
        Assert.Equal(type == 6 ? 0x6e00 : 0x46a0, reader.ReadUInt16()); Assert.Equal(blipType, reader.ReadUInt16());
        Assert.Equal(blipLength - 8, reader.ReadInt32()); Assert.Equal(uid, reader.ReadBytes(16));
        Assert.Equal(255, reader.ReadByte()); byte[] original = FidelityDocuments.Fixture(fixture);
        Assert.Equal(original, reader.ReadBytes(original.Length)); Assert.Equal(pictureLength, data.Position);
    }

    [Fact]
    public void NestedDocOrderedLevelsUseFirstPlaceholderOffset()
    {
        byte[] doc = Convert(Encoding.UTF8.GetBytes("0. Parent\n\n   0. Child\n\n1. Next\n"), DocumentFormat.Markdown, DocumentFormat.Doc);
        using var memory = new MemoryStream(doc); using var root = RootStorage.Open(memory);
        using CfbStream word = root.OpenStream("WordDocument"); using var fib = new BinaryReader(word);
        word.Position = 154 + 73 * 8; int offset = fib.ReadInt32(), length = fib.ReadInt32();
        using CfbStream table = root.OpenStream("1Table"); using var reader = new BinaryReader(table);
        table.Position = offset + length;
        for (int level = 0; level < 9; level++)
        {
            byte[] record = reader.ReadBytes(28);
            Assert.Equal(1, record[6]); Assert.All(record.Skip(7).Take(8), b => Assert.Equal(0, b));
            int characters = reader.ReadUInt16(); string label = Encoding.Unicode.GetString(reader.ReadBytes(characters * 2));
            Assert.Equal(((char)level).ToString() + ".", label);
        }
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Epub)]
    public void ZeroStartListsContinueAndRestartNestedCounters(DocumentFormat intermediate)
    {
        const string markdown = "0. First\n\n   0. Child\n   1. Sibling\n\n1. Second\n\n   0. Restart\n";
        byte[] word = Convert(Encoding.UTF8.GetBytes(markdown), DocumentFormat.Markdown, DocumentFormat.Docx);
        if (intermediate != DocumentFormat.Docx)
        {
            word = Convert(word, DocumentFormat.Docx, intermediate);
            word = Convert(word, intermediate, DocumentFormat.Docx);
        }
        string text = Encoding.UTF8.GetString(Convert(word, DocumentFormat.Docx, DocumentFormat.Text));
        Assert.Equal("0. First\n  0. Child\n  1. Sibling\n1. Second\n  0. Restart", text);
        using WordprocessingDocument document = Open(word);
        Assert.Contains(document.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!.Descendants<W.StartNumberingValue>(), s => s.Val!.Value == 0);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(Convert(word, DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Contains("0. First", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("1. Second", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovedTextIsIncludedOnlyAtAcceptedDestination(bool block)
    {
        string text = block ? TestDocuments.TextParagraph("Moved") : "<w:r><w:t>Moved</w:t></w:r>";
        string revisions = "<w:moveFrom>" + text + "</w:moveFrom><w:moveTo>" + text + "</w:moveTo>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(block ? revisions : "<w:p>" + revisions + "</w:p>"));
        byte[] result = Convert(input, DocumentFormat.Docx, DocumentFormat.Text, out ConversionResult report);
        Assert.Equal("Moved", Encoding.UTF8.GetString(result));
        Assert.Contains(report.Diagnostics, d => d.Code == "Revisions.Accepted" && d.Severity == DiagnosticSeverity.Warning);
        using var strictSource = new MemoryStream(input); using var strictOutput = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictSource, strictOutput, DocumentFormat.Docx, DocumentFormat.Text,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Empty(strictOutput.ToArray());
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    public void RepeatedTableHeaderDoesNotForceRunEmphasis(DocumentFormat intermediate)
    {
        string table = "<w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc><w:p>"
            + "<w:r><w:rPr><w:b w:val='0'/></w:rPr><w:t>Plain</w:t></w:r>"
            + "<w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        byte[] encoded = Convert(TestDocuments.Docx(TestDocuments.Body(table)), DocumentFormat.Docx, intermediate);
        using WordprocessingDocument document = Open(Convert(encoded, intermediate, DocumentFormat.Docx));
        Assert.Single(document.MainDocumentPart!.Document!.Descendants<W.TableHeader>());
        W.Run[] runs = document.MainDocumentPart.Document.Descendants<W.Run>().ToArray();
        Assert.Null(runs.First(r => r.InnerText == "Plain").RunProperties?.Bold);
        Assert.NotNull(runs.First(r => r.InnerText == "Bold").RunProperties?.Bold);
    }

    [Theory]
    [InlineData("paragraph", "<w:b/>", true)]
    [InlineData("paragraph", "<w:b w:val='0'/>", false)]
    [InlineData("character", "<w:b/>", true)]
    [InlineData("character", "<w:b w:val='0'/>", false)]
    public void SameTypeStyleInheritanceResolvesEmphasisBeforeCombining(string type, string emphasis, bool bold)
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='" + type + "' w:styleId='Base'><w:rPr><w:b/></w:rPr></w:style>"
            + "<w:style w:type='" + type + "' w:styleId='Derived'><w:basedOn w:val='Base'/><w:rPr>" + emphasis + "</w:rPr></w:style></w:styles>";
        string body = type == "paragraph"
            ? "<w:p><w:pPr><w:pStyle w:val='Derived'/></w:pPr><w:r><w:t>Styled</w:t></w:r></w:p>"
            : "<w:p><w:r><w:rPr><w:rStyle w:val='Derived'/></w:rPr><w:t>Styled</w:t></w:r></w:p>";
        string markdown = Encoding.UTF8.GetString(Convert(TestDocuments.Docx(TestDocuments.Body(body), styles: styles), DocumentFormat.Docx, DocumentFormat.Markdown));
        Assert.Equal(bold ? "**Styled**" : "Styled", markdown.Trim());
    }

    [Fact]
    public void StrictDocxImportAcceptsMetadataGeometryAndFirstPagePolicy()
    {
        string roots = "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='main' Type='" + TestDocuments.R + "/officeDocument' Target='word/document.xml'/>"
            + "<Relationship Id='core' Type='http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties' Target='docProps/core.xml'/></Relationships>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:lang w:val='en-US'/></w:rPr><w:t>Content</w:t></w:r></w:p>"),
            rootRelationships: roots, extra: new KeyValuePair<string, string>("docProps/core.xml", "<coreProperties xmlns='http://schemas.openxmlformats.org/package/2006/metadata/core-properties'/>"));
        Assert.Equal("Content", Encoding.UTF8.GetString(Convert(input, DocumentFormat.Docx, DocumentFormat.Text, out ConversionResult report, new ConversionOptions())));
        Assert.Contains(report.Diagnostics, d => d.Code == "Docx.AuxiliaryMetadata");
        byte[] geometry = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Content") + "<w:sectPr><w:pgSz w:w='15840' w:h='12240'/></w:sectPr>"));
        using (UglyToad.PdfPig.PdfDocument pdf = UglyToad.PdfPig.PdfDocument.Open(Convert(geometry, DocumentFormat.Docx, DocumentFormat.Pdf, out _, new ConversionOptions())))
        {
            Assert.Equal(792, pdf.GetPage(1).Width);
            Assert.Equal(612, pdf.GetPage(1).Height);
        }
        byte[] header = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Content") + "<w:sectPr><w:titlePg/></w:sectPr>"));
        using var firstPage = UglyToad.PdfPig.PdfDocument.Open(Convert(header, DocumentFormat.Docx, DocumentFormat.Pdf, out _, new ConversionOptions()));
        Assert.Equal("Content", firstPage.GetPage(1).Text);
    }

    [Theory]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void KindleOutputUsesOutputBudgetInsteadOfInputZipEntryLimit(DocumentFormat target)
    {
        string text = string.Concat(Enumerable.Repeat("Long content 12345. ", 2000));
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text)));
        var options = new ConversionOptions(FidelityMode.BestEffort, maximumArchiveEntries: 3);
        byte[] result = Convert(source, DocumentFormat.Docx, target, out _, options);
        Assert.Equal(text, ConversionAcceptanceTests.ReadOutput(result, target));
        Assert.Throws<InvalidDataException>(() => Convert(source, DocumentFormat.Docx, target, out _,
            new ConversionOptions(FidelityMode.BestEffort, maximumOutputBytes: 1024, maximumArchiveEntries: 3)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterFinalDestinationWriteReportsSuccessfulCompletion(bool asynchronous)
    {
        using var cancellation = new CancellationTokenSource(); using var output = new CancellingDestination(cancellation);
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Complete"))));
        var converter = new WordConverter(); var options = new ConversionOptions();
        ConversionResult report = asynchronous
            ? await converter.ConvertAsync(source, output, DocumentFormat.Docx, DocumentFormat.Text, options, cancellation.Token)
            : converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Text, options, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested); Assert.Equal(8, report.BytesWritten);
        Assert.Equal("Complete", Encoding.UTF8.GetString(output.ToArray()));
    }

    private sealed class CancellingDestination : MemoryStream
    {
        private readonly CancellationTokenSource cancellation;
        internal CancellingDestination(CancellationTokenSource cancellation) => this.cancellation = cancellation;
        public override void Write(byte[] buffer, int offset, int count) { base.Write(buffer, offset, count); cancellation.Cancel(); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
#if NET8_0_OR_GREATER
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = buffer.ToArray(); Write(bytes, 0, bytes.Length); return default; }
#endif
    }

    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to) => Convert(bytes, from, to, out _);
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, out ConversionResult report, ConversionOptions? options = null)
    {
        using var source = new MemoryStream(bytes); using var output = new MemoryStream();
        report = new WordConverter().Convert(source, output, from, to, options ?? new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    private static WordprocessingDocument Open(byte[] bytes)
    {
        var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken)); return document;
    }
    private static void AssertStrictFailure(byte[] bytes, DocumentFormat from, DocumentFormat to)
    {
        using var source = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, from, to));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
