using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using OpenMcdf;
using WordNet.Formats;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

// Independently authored HTML, XHTML, Markdown and WordprocessingML inputs.
public sealed class ImportContentRegressionTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[] ListText = { "Intro", "Quoted intro", "Quoted one", "Quoted two", "Quoted outro", "Top", "Nested", "After nested", "Second", "Header", "Cell", "Plain", "Term", "Definition", "Definition item", "Outro" };
    private static readonly string[] ItemText = { "Quoted one", "Quoted two", "Top", "Nested", "Second", "Header", "Cell", "Definition item" };
    private static readonly string[] NestedText = { "First", "Nested", "Continuation" };
    private static readonly string[] HyphenText = { "10\u201120 soft\u00adhyphen", "10\u201120 soft\u00adhyphen" };
    private static readonly string[] BinaryHyphenText = { "10\u201120 soft\u00adhyphen", "10\u201120 soft\u00adhyphen", "" };
    private static readonly string[] EpubText = { "a\u00a0b © & ☺ ≂̸" };

    [Theory]
    [InlineData(DocumentFormat.Html, FidelityMode.BestEffort)]
    [InlineData(DocumentFormat.Html, FidelityMode.Lenient)]
    [InlineData(DocumentFormat.Epub, FidelityMode.BestEffort)]
    [InlineData(DocumentFormat.Epub, FidelityMode.Lenient)]
    public void WrappedListsRetainTextOrderAndListLevels(DocumentFormat format, FidelityMode mode)
    {
        const string body = "<p>Intro</p><blockquote><p>Quoted intro</p><ul><li>Quoted one</li><li>Quoted two</li></ul><p>Quoted outro</p></blockquote>"
            + "<ul><li>Top<div><span><ul><li>Nested</li></ul></span></div>After nested</li><li>Second</li></ul>"
            + "<header><ul><li>Header</li></ul></header><table><tr><td><span><ol><li>Cell</li></ol></span></td><td>Plain</td></tr></table>"
            + "<dl><dt>Term</dt><dd>Definition<ul><li>Definition item</li></ul></dd></dl><p>Outro</p>";
        byte[] input = format == DocumentFormat.Html ? Encoding.UTF8.GetBytes(body) : Epub(Xhtml(body));
        XDocument word = Part(Convert(input, format, mode, out _));
        XElement[] paragraphs = word.Descendants(W + "p").ToArray();
        Assert.Equal(ListText, paragraphs.Select(Text));
        foreach (string item in ItemText)
            Assert.NotNull(paragraphs.Single(p => Text(p) == item).Element(W + "pPr")?.Element(W + "numPr"));
        Assert.Equal("1", (string?)paragraphs.Single(p => Text(p) == "Nested").Descendants(W + "ilvl").Single().Attribute(W + "val"));
        Assert.DoesNotContain(paragraphs.Single(p => Text(p) == "After nested").Descendants(), e => e.Name == W + "numPr");
    }

    [Fact]
    public void WrappedListsRemainEditableInStrictHtmlAndSharedKindleReader()
    {
        const string html = "<ul><li><p>First</p><div><ul><li>Nested</li></ul></div><p>Continuation</p></li></ul>";
        XDocument word = Part(Convert(Encoding.UTF8.GetBytes(html), DocumentFormat.Html, FidelityMode.Strict, out ConversionResult result));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(NestedText, word.Descendants(W + "p").Select(Text));
        // MOBI and KF8 both enter this shared reader after PalmDOC/KF8 decoding.
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Equal(NestedText, EbookContent.Read(Encoding.UTF8.GetBytes(html), context).Paragraphs.Select(p => p.Text));
    }

    [Theory]
    [InlineData(FidelityMode.Strict)]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void MarkdownEntitiesRetainDecodedTextFormattingAndLinks(FidelityMode mode)
    {
        const string markdown = "Tom &amp; Jerry &copy; 2024. a&nbsp;&lt;&nbsp;b &gt; c 5&euro; &mdash; &#169; &#x263A;.\n\n"
            + "**&amp;** *&#169;* [&lt;](https://example.com) `&amp;`\n\n| Head |\n| --- |\n| &nbsp;&copy;&#x263A; |\n";
        XDocument word = Part(Convert(Encoding.UTF8.GetBytes(markdown), DocumentFormat.Markdown, mode, out ConversionResult result));
        Assert.Empty(result.Diagnostics);
        XElement[] paragraphs = word.Descendants(W + "p").ToArray();
        Assert.Equal("Tom & Jerry © 2024. a\u00a0<\u00a0b > c 5€ — © ☺.", Text(paragraphs[0]));
        Assert.Contains(word.Descendants(W + "r"), r => Text(r) == "&" && r.Element(W + "rPr")?.Element(W + "b") != null);
        Assert.Contains(word.Descendants(W + "r"), r => Text(r) == "©" && r.Element(W + "rPr")?.Element(W + "i") != null);
        Assert.Equal("<", Text(word.Descendants(W + "hyperlink").Single()));
        Assert.Contains("&amp;", Text(paragraphs[1]), StringComparison.Ordinal);
        Assert.Equal("\u00a0©☺", Text(paragraphs.Last()));
    }

    [Theory]
    [InlineData(FidelityMode.Strict)]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void DocxHyphenElementsPreserveExactBodyAndCellText(FidelityMode mode)
    {
        const string paragraph = "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>10</w:t><w:noBreakHyphen/><w:t>20 soft</w:t><w:softHyphen/><w:t>hyphen</w:t></w:r></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(paragraph + "<w:tbl><w:tr><w:tc>" + paragraph + "</w:tc></w:tr></w:tbl>"));
        // DOCX-to-DOCX is not a converter pair; load through the editing API.
        using var stream = new MemoryStream(input);
        WordDocument document = WordDocument.Load(stream, DocumentFormat.Docx, out ConversionResult result, new ConversionOptions(mode), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        XDocument word = Part(output.ToArray());
        Assert.Equal(HyphenText, word.Descendants(W + "p").Select(Text));
        Assert.All(word.Descendants(W + "r"), r => Assert.NotNull(r.Element(W + "rPr")?.Element(W + "b")));
    }

    [Theory]
    [InlineData("<w:sym w:font='Wingdings' w:char='F0E0'/>", "\ufffd", "Docx.Symbol")]
    [InlineData("<w:ptab w:relativeTo='margin' w:alignment='right' w:leader='none'/>", "\t", "Docx.PositionalTab")]
    public void DocxUnsupportedCharacterSemanticsAreVisibleAndDiagnosed(string element, string replacement, string code)
    {
        string paragraph = "<w:p><w:r><w:t>left</w:t>" + element + "<w:t>right</w:t></w:r></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(paragraph + "<w:tbl><w:tr><w:tc>" + paragraph + "</w:tc></w:tr></w:tbl>"));
        foreach (FidelityMode mode in new[] { FidelityMode.BestEffort, FidelityMode.Lenient })
        {
            using var source = new MemoryStream(input);
            var context = new ConversionContext(new ConversionOptions(mode), TestContext.Current.CancellationToken);
            var model = DocxReader.Read(source, context);
            Assert.Equal(new[] { "left" + replacement + "right", "left" + replacement + "right" }, model.Paragraphs.Select(p => p.Text));
            Assert.Contains(context.Result(0).Diagnostics, d => d.Code == code && d.Message.Contains("replaced"));
        }
        using var strictInput = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, output, DocumentFormat.Docx, DocumentFormat.Html, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void BinaryDocHyphenControlsPreserveBodyAndCellText()
    {
        string paragraph = TestDocuments.TextParagraph("10~20 soft^hyphen");
        byte[] docx = TestDocuments.Docx(TestDocuments.Body(paragraph + "<w:tbl><w:tr><w:tc>" + paragraph + "</w:tc></w:tr></w:tbl>"));
        using var source = new MemoryStream(docx); using var binary = new MemoryStream();
        new WordConverter().Convert(source, binary, DocumentFormat.Docx, DocumentFormat.Doc, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        binary.Position = 0;
        using (var root = RootStorage.Open(binary, StorageModeFlags.LeaveOpen))
        {
            using CfbStream word = root.OpenStream("WordDocument"); using var bytes = new MemoryStream(); word.CopyTo(bytes);
            byte[] data = bytes.ToArray(); int replaced = 0;
            byte[] marker = Encoding.Unicode.GetBytes("10~20 soft^hyphen");
            for (int i = 0; i <= data.Length - marker.Length; i++)
            {
                if (!data.Skip(i).Take(marker.Length).SequenceEqual(marker)) continue;
                data[i + 4] = 0x1e; data[i + 20] = 0x1f; replaced++;
            }
            Assert.Equal(2, replaced); word.Position = 0; word.Write(data, 0, data.Length);
        }
        binary.Position = 0;
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        // Binary Word includes a final empty paragraph after the table.
        Assert.Equal(BinaryHyphenText, DocFormat.Read(binary, context).Paragraphs.Select(p => p.Text));
        Assert.DoesNotContain(context.Result(0).Diagnostics, d => d.Code == "Doc.SpecialContent");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<!DOCTYPE html PUBLIC '-//W3C//DTD XHTML 1.1//EN' 'http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd'>")]
    [InlineData("<!DOCTYPE html SYSTEM 'file:///this-must-never-be-read.dtd'>")]
    public void EpubAcceptsInertDoctypesAndNamedCharacters(string doctype)
    {
        byte[] input = Epub("<?xml version='1.0'?>" + doctype + Xhtml("<p title='a&nbsp;b'>a&nbsp;b &copy; &amp; &#x263A; &NotEqualTilde;</p>"));
        XDocument word = Part(Convert(input, DocumentFormat.Epub, FidelityMode.Strict, out ConversionResult result));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(EpubText, word.Descendants(W + "p").Select(Text));
    }

    [Theory]
    [InlineData("<!DOCTYPE html [<!ENTITY secret SYSTEM 'file:///secret'>]>", "<p>&secret;</p>", "Epub.ContentEntity")]
    [InlineData("<!DOCTYPE html [<!ENTITY secret 'expanded'>]>", "<p title='&secret;'>text</p>", "Epub.ContentEntity")]
    [InlineData("", "<p>&unknown;</p>", "Epub.ContentEntity")]
    [InlineData("<!DOCTYPE html>", "<p>unclosed", "Epub.ContentXml")]
    public void EpubRejectsEntitiesAndMalformedChaptersWithPartName(string doctype, string body, string code)
    {
        using var input = new MemoryStream(Epub(doctype + Xhtml(body))); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Epub, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.StartsWith(code + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains("book/chapter.xhtml", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private static string Xhtml(string body) => "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>T</title></head><body>" + body + "</body></html>";

    internal static byte[] Epub(string chapter)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book/book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add("book/book.opf", "<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><manifest><item id='chapter' href='chapter.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='chapter'/></spine></package>");
            Add("book/chapter.xhtml", chapter);
            void Add(string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
        }
        return memory.ToArray();
    }

    private static byte[] Convert(byte[] input, DocumentFormat format, FidelityMode mode, out ConversionResult result)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        result = new WordConverter().Convert(source, output, format, DocumentFormat.Docx, new ConversionOptions(mode), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    private static XDocument Part(byte[] bytes)
    { using var archive = new ZipArchive(new MemoryStream(bytes)); using var stream = archive.GetEntry("word/document.xml")!.Open(); return XDocument.Load(stream); }
    private static string Text(XElement element) => string.Concat(element.Descendants(W + "t").Select(t => t.Value));
}
