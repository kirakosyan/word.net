using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

public sealed class ImplementedConversionTests
{
    private static readonly string[] ExpectedTableCells = { "Name", "Value", "Alpha", "42" };
    private readonly WordConverter converter = new WordConverter();

    [Fact]
    [Trait("Category", "Conversion")]
    public void TextToDocxCreatesValidEditableUnicodeParagraphs()
    {
        const string text = "\n  Hello & բարև 🌍  \tcolumn\nSecond\n";
        byte[] bytes = Convert(Encoding.UTF8.GetBytes(text), DocumentFormat.Text, DocumentFormat.Docx);
        using var stream = new MemoryStream(bytes);
        using WordprocessingDocument docx = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(docx, TestContext.Current.CancellationToken));
        W.Paragraph[] paragraphs = docx.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Equal(4, paragraphs.Length);
        Assert.Equal("  Hello & բարև 🌍  column", paragraphs[1].InnerText);
        Assert.Single(paragraphs[1].Descendants<W.TabChar>());
        Assert.Equal("Second", paragraphs[2].InnerText);
        Assert.Empty(paragraphs[3].InnerText);
    }

    [Fact]
    [Trait("Category", "Conversion")]
    public void IndependentDocxToTextPreservesWhitespaceAndBreaks()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Fixture("plain-document.xml"));
        string result = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Docx, DocumentFormat.Text));
        Assert.Equal("\n  Hello & բարև 🌍  \tcolumn\nnext line\nLast paragraph.\n", result);
    }

    [Fact]
    [Trait("Category", "Conversion")]
    public void MarkdownToDocxHasValidStylesLinksTablesAndEditableCode()
    {
        byte[] bytes = Convert(Encoding.UTF8.GetBytes(TestDocuments.Fixture("sample.md")), DocumentFormat.Markdown, DocumentFormat.Docx);
        using var stream = new MemoryStream(bytes);
        using WordprocessingDocument docx = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(docx, TestContext.Current.CancellationToken));
        W.Body body = docx.MainDocumentPart!.Document!.Body!;
        Assert.Equal("Heading1", body.Elements<W.Paragraph>().First().ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Contains(body.Descendants<W.Run>(), run => run.InnerText == "bold" && run.RunProperties?.Bold != null);
        Assert.Contains(body.Descendants<W.Run>(), run => run.InnerText == "italic" && run.RunProperties?.Italic != null);
        Assert.Equal("https://example.com/a%20b?q=1&x=2", Assert.Single(docx.MainDocumentPart.HyperlinkRelationships).Uri.OriginalString);
        W.Table table = Assert.Single(body.Elements<W.Table>());
        Assert.Equal(ExpectedTableCells, table.Descendants<W.TableCell>().Select(cell => cell.InnerText));
        Assert.Single(table.Descendants<W.TableHeader>());
        Assert.Contains(body.Elements<W.Paragraph>(), paragraph => paragraph.InnerText == "first linesecond line"
            && paragraph.Descendants<W.Break>().Any());
    }

    [Fact]
    [Trait("Category", "Conversion")]
    public void IndependentDocxToMarkdownPreservesSupportedStructure()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Fixture("rich-document.xml"),
            TestDocuments.LinkRelationship("https://example.com/a%20b?q=1&x=2"));
        string result = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Docx, DocumentFormat.Markdown));
        Assert.Equal("# A heading\n\nA **bold** and *italic* example with [link](<https://example.com/a%20b?q=1&x=2>)\\.\n\n"
            + "| Name | Value |\n| --- | --- |\n| Alpha | 42 |\n", result);
    }

    [Fact]
    public void CapabilityDiscoveryIncludesOnlyTheAgreedDirections()
    {
        var supported = new HashSet<(DocumentFormat, DocumentFormat)>
        {
            (DocumentFormat.Doc, DocumentFormat.Docx), (DocumentFormat.Docx, DocumentFormat.Doc),
            (DocumentFormat.Doc, DocumentFormat.Xps), (DocumentFormat.Docx, DocumentFormat.Xps),
        };
        foreach (DocumentFormat word in new[] { DocumentFormat.Doc, DocumentFormat.Docx })
            foreach (DocumentFormat format in new[] { DocumentFormat.Pdf, DocumentFormat.Markdown, DocumentFormat.Text, DocumentFormat.Epub, DocumentFormat.Mobi, DocumentFormat.Azw3 })
            { supported.Add((word, format)); supported.Add((format, word)); }
        Assert.Equal(28, supported.Count);
        supported.Add((DocumentFormat.Doc, DocumentFormat.Html));
        supported.Add((DocumentFormat.Docx, DocumentFormat.Html));
        supported.Add((DocumentFormat.Html, DocumentFormat.Doc));
        supported.Add((DocumentFormat.Html, DocumentFormat.Docx));
        supported.Add((DocumentFormat.Doc, DocumentFormat.Rtf));
        supported.Add((DocumentFormat.Docx, DocumentFormat.Rtf));
        supported.Add((DocumentFormat.Rtf, DocumentFormat.Doc));
        supported.Add((DocumentFormat.Rtf, DocumentFormat.Docx));
        supported.Add((DocumentFormat.Docx, DocumentFormat.Odt));
        supported.Add((DocumentFormat.Odt, DocumentFormat.Docx));
        Assert.Equal(38, supported.Count);
#if NET8_0_OR_GREATER
        DocumentFormat[] formats = Enum.GetValues<DocumentFormat>();
#else
        var formats = (DocumentFormat[])Enum.GetValues(typeof(DocumentFormat));
#endif
        foreach (DocumentFormat from in formats)
        {
            foreach (DocumentFormat to in formats)
            {
                Assert.Equal(supported.Contains((from, to)), converter.CanConvert(from, to));
            }
        }

        Assert.False(converter.CanConvert((DocumentFormat)999, DocumentFormat.Docx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("a\r\nb\rc\n")]
    [InlineData("Հայերեն العربية עברית 漢字 😀")]
    public void TextRoundTripPreservesLogicalCharacters(string text)
    {
        byte[] docx = Convert(Encoding.UTF8.GetBytes(text), DocumentFormat.Text, DocumentFormat.Docx);
        string actual = Encoding.UTF8.GetString(Convert(docx, DocumentFormat.Docx, DocumentFormat.Text));
        Assert.Equal(text.Replace("\r\n", "\n").Replace('\r', '\n'), actual);
    }

    [Fact]
    public void Utf8BomIsAcceptedAndNotInsertedIntoDocumentText()
    {
        byte[] text = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        byte[] docx = Convert(text, DocumentFormat.Text, DocumentFormat.Docx);
        Assert.Equal("hello", Encoding.UTF8.GetString(Convert(docx, DocumentFormat.Docx, DocumentFormat.Text)));
    }

    [Fact]
    public void StrictLossFailsBeforeWritingAndBestEffortReportsLoss()
    {
        byte[] docx = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r></w:p>"));
        using var source = new MemoryStream(docx);
        using var destination = new MemoryStream();
        destination.WriteByte(42);
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Text));
        Assert.Contains("Text.Formatting", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, destination.ToArray());
        source.Position = 0;
        ConversionResult result = converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Equal("Text.Formatting", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(4, result.BytesWritten);
        Assert.Equal("bold", Encoding.UTF8.GetString(destination.ToArray(), 1, 4));
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
        Assert.Throws<NotSupportedException>(() => ((IList<ConversionDiagnostic>)result.Diagnostics).Clear());
    }

    [Theory]
    [InlineData("![alternative](https://example.com/image.png)", "Image.Unsupported", "alternative")]
    [InlineData("<script>alert(1)</script>", "Markdown.UnsupportedBlock", "<script>alert(1)</script>")]
    [InlineData("[visible](javascript:alert)", "Link.UnsupportedTarget", "visible")]
    public void UnsupportedMarkdownRequiresExplicitBestEffort(string markdown, string code, string expectedText)
    {
        byte[] input = Encoding.UTF8.GetBytes(markdown);
        Assert.Throws<NotSupportedException>(() => Convert(input, DocumentFormat.Markdown, DocumentFormat.Docx));
        using var source = new MemoryStream(input);
        using var destination = new MemoryStream();
        ConversionResult result = converter.Convert(source, destination, DocumentFormat.Markdown, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(expectedText, Encoding.UTF8.GetString(Convert(destination.ToArray(), DocumentFormat.Docx, DocumentFormat.Text)));
    }

    [Fact]
    public void MarkdownSpecialCharactersRemainLiteral()
    {
        const string text = "# [name](url) *asterisk* _under_ ![image] 1. item | cell";
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text)));
        string markdown = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Docx, DocumentFormat.Markdown));
        Assert.Equal("\\# \\[name\\]\\(url\\) \\*asterisk\\* \\_under\\_ \\!\\[image\\] 1\\. item \\| cell\n", markdown);
    }

    [Fact]
    public void AdjacentWordRunsWithIdenticalFormattingHaveOneMarkdownDelimiterPair()
    {
        string body = "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>bo</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>ld</w:t></w:r></w:p>";
        Assert.Equal("**bold**\n", Encoding.UTF8.GetString(Convert(TestDocuments.Docx(TestDocuments.Body(body)), DocumentFormat.Docx, DocumentFormat.Markdown)));
    }

    [Fact]
    public void PipeInsideTableCodeDoesNotCreateAnExtraCell()
    {
        const string markdown = "| Code |\n| --- |\n| `a\\|b` |\n";
        byte[] docx = Convert(Encoding.UTF8.GetBytes(markdown), DocumentFormat.Markdown, DocumentFormat.Docx);
        string exported = Encoding.UTF8.GetString(Convert(docx, DocumentFormat.Docx, DocumentFormat.Markdown));
        Assert.Equal(markdown, exported);
    }

    [Fact]
    public void CodeBlockTrailingBlankLineSurvivesMarkdownExport()
    {
        const string markdown = "```\nfirst\n\n```\n";
        byte[] docx = Convert(Encoding.UTF8.GetBytes(markdown), DocumentFormat.Markdown, DocumentFormat.Docx);
        Assert.Equal(markdown, Encoding.UTF8.GetString(Convert(docx, DocumentFormat.Docx, DocumentFormat.Markdown)));
    }

    [Fact]
    public async Task AsyncConversionSupportsNonSeekableStreamsAndLeavesThemOpen()
    {
        using var source = new NonSeekableInput(Encoding.UTF8.GetBytes("async text"));
        using var destination = new MemoryStream();
        ConversionResult result = await converter.ConvertAsync(source, destination, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.Equal(destination.Length, result.BytesWritten);
        Assert.True(source.CanRead);
        Assert.True(destination.CanWrite);
        Assert.Equal("async text", Encoding.UTF8.GetString(Convert(destination.ToArray(), DocumentFormat.Docx, DocumentFormat.Text)));
    }

    [Fact]
    public void InputStartsAtCurrentPositionAndOutputDoesNotTruncate()
    {
        byte[] docx = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("OK")));
        using var source = new MemoryStream(new byte[] { 9, 8 }.Concat(docx).ToArray());
        source.Position = 2;
        using var destination = new MemoryStream(Encoding.UTF8.GetBytes("prefix----suffix"));
        destination.Position = 6;
        converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Text);
        Assert.Equal("prefixOK--suffix", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Fact]
    public void UnsupportedPairsAndAliasedStreamsFailWithoutReading()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("untouched"));
        using var destination = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => converter.Convert(source, destination, DocumentFormat.Xps, DocumentFormat.Docx));
        Assert.Equal(0, source.Position);
        Assert.Throws<ArgumentException>(() => converter.Convert(source, source, DocumentFormat.Text, DocumentFormat.Docx));
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public async Task PreCancelledConversionsDoNotTouchStreams()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("text"));
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => converter.Convert(source, destination, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(source, destination, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void CancellationDuringInputLeavesOutputUntouched()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new NonSeekableInput(Encoding.UTF8.GetBytes("text"), cancellation.Cancel);
        using var destination = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => converter.Convert(source, destination, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), cancellation.Token));
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task SharedConverterIsDeterministicAcrossConcurrentConversions()
    {
        Task<byte[]>[] tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Convert(Encoding.UTF8.GetBytes("stable"), DocumentFormat.Text, DocumentFormat.Docx))).ToArray();
        byte[][] results = await Task.WhenAll(tasks);
        foreach (byte[] result in results.Skip(1))
        {
            Assert.Equal(results[0], result);
        }
    }

    private byte[] Convert(byte[] input, DocumentFormat from, DocumentFormat to)
    {
        using var source = new MemoryStream(input);
        using var destination = new MemoryStream();
        converter.Convert(source, destination, from, to);
        return destination.ToArray();
    }

    private sealed class NonSeekableInput : Stream
    {
        private readonly MemoryStream inner;
        private readonly Action? onRead;

        internal NonSeekableInput(byte[] bytes, Action? onRead = null)
        {
            inner = new MemoryStream(bytes);
            this.onRead = onRead;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, Math.Min(count, 3));
            onRead?.Invoke();
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
