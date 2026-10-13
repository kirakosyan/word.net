using System;
using System.IO;
using System.Linq;
using System.Text;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

public sealed class DocFormattingTests
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly WordConverter converter = new WordConverter();

    [Fact]
    public void ImportsEmphasisFromIndependentApachePoiProducer()
    {
        using WordprocessingDocument output = Open(Convert(Fixture("poi-emphasis.doc"), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Body body = output.MainDocumentPart!.Document!.Body!;
        Assert.Equal("Bold and italic from Apache POI.", body.InnerText);
        W.Run run = Assert.Single(body.Descendants<W.Run>());
        Assert.NotNull(run.RunProperties?.Bold);
        Assert.NotNull(run.RunProperties?.Italic);
    }

    [Theory]
    [InlineData("simple-table.doc", 2, "Cell 1,1", "Cell 2,3")]
    [InlineData("simple-table2.doc", 3, "Row 1/Cell 1", "Row 3/Cell 3")]
    public void ImportsIndependentlyProducedEditableTables(string name, int rows, string first, string last)
    {
        using WordprocessingDocument docx = Open(Convert(Fixture("apache-poi/" + name), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Table table = Assert.Single(docx.MainDocumentPart!.Document!.Body!.Elements<W.Table>());
        Assert.Equal(rows, table.Elements<W.TableRow>().Count());
        Assert.All(table.Elements<W.TableRow>(), row => Assert.Equal(3, row.Elements<W.TableCell>().Count()));
        Assert.Equal(first, table.Descendants<W.TableCell>().First().InnerText);
        Assert.Equal(last, table.Descendants<W.TableCell>().Last().InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(docx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RetainsHeadingsEmphasisAndEditableTableHeaders()
    {
        byte[] original = TestDocuments.Docx(TestDocuments.Fixture("rich-document.xml"), TestDocuments.LinkRelationship("https://example.com/a%20b?q=1&x=2"));
        byte[] binary = Convert(original, DocumentFormat.Docx, DocumentFormat.Doc);
        Assert.Equal(new byte[] { 0xd0, 0xcf, 0x11, 0xe0 }, binary.Take(4));
        using WordprocessingDocument output = Open(Convert(binary, DocumentFormat.Doc, DocumentFormat.Docx));
        W.Body body = output.MainDocumentPart!.Document!.Body!;
        Assert.Equal("Heading1", body.Elements<W.Paragraph>().First().ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Contains(body.Descendants<W.Run>(), r => r.InnerText == "bold" && r.RunProperties?.Bold != null);
        Assert.Contains(body.Descendants<W.Run>(), r => r.InnerText == "italic" && r.RunProperties?.Italic != null);
        W.Table table = Assert.Single(body.Elements<W.Table>());
        Assert.Equal(2, table.Elements<W.TableRow>().Count());
        Assert.NotNull(table.Elements<W.TableRow>().First().TableRowProperties?.GetFirstChild<W.TableHeader>());
        Assert.Empty(new OpenXmlValidator().Validate(output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RetainsManyParagraphAndCharacterFormattingPages()
    {
        string paragraphs = string.Concat(Enumerable.Range(0, 140).Select(i => $"<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>B{i}</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>I{i}</w:t></w:r><w:r><w:t>P{i}</w:t></w:r></w:p>"));
        byte[] original = TestDocuments.Docx($"<w:document xmlns:w='{WordNamespace}'><w:body>{paragraphs}</w:body></w:document>");
        using WordprocessingDocument result = Open(Convert(Convert(original, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        var output = result.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().ToArray();
        Assert.Equal(140, output.Length);
        for (int i = 0; i < output.Length; i++)
        {
            W.Run[] runs = output[i].Elements<W.Run>().ToArray(); Assert.Equal(3, runs.Length);
            Assert.Equal("B" + i, runs[0].InnerText); Assert.NotNull(runs[0].RunProperties?.Bold); Assert.Null(runs[0].RunProperties?.Italic);
            Assert.Equal("I" + i, runs[1].InnerText); Assert.NotNull(runs[1].RunProperties?.Italic); Assert.Null(runs[1].RunProperties?.Bold);
            Assert.Equal("P" + i, runs[2].InnerText); Assert.Null(runs[2].RunProperties?.Bold); Assert.Null(runs[2].RunProperties?.Italic);
        }
    }

    [Fact]
    public void RetainsUnicodeTabsLineBreaksAndEmptyCells()
    {
        string body = "<w:p><w:r><w:t>Հայերեն 漢字 😀 café</w:t><w:tab/><w:t>tab</w:t><w:br/><w:t>next</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p/></w:tc><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        byte[] original = TestDocuments.Docx($"<w:document xmlns:w='{WordNamespace}'><w:body>{body}</w:body></w:document>");
        using WordprocessingDocument result = Open(Convert(Convert(original, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Body output = result.MainDocumentPart!.Document!.Body!;
        Assert.Contains("Հայերեն 漢字 😀 café", output.InnerText, StringComparison.Ordinal);
        Assert.Single(output.Descendants<W.TabChar>()); Assert.Single(output.Descendants<W.Break>());
        W.TableCell[] cells = output.Descendants<W.TableCell>().ToArray(); Assert.Equal(2, cells.Length);
        Assert.Equal("", cells[0].InnerText); Assert.Equal("Cell", cells[1].InnerText);
    }

    [Fact]
    public void PreservesLargeTablesAcrossFormattingPages()
    {
        string rows = string.Concat(Enumerable.Range(0, 90).Select(i => $"<w:tr><w:tc><w:p><w:r><w:t>Row {i}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:rPr><w:i/></w:rPr><w:t>Value {i}</w:t></w:r></w:p></w:tc></w:tr>"));
        byte[] original = TestDocuments.Docx($"<w:document xmlns:w='{WordNamespace}'><w:body><w:tbl>{rows}</w:tbl></w:body></w:document>");
        using WordprocessingDocument result = Open(Convert(Convert(original, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Table table = Assert.Single(result.MainDocumentPart!.Document!.Body!.Elements<W.Table>());
        Assert.Equal(90, table.Elements<W.TableRow>().Count());
        Assert.Equal("Row 89Value 89", table.Elements<W.TableRow>().Last().InnerText);
        Assert.All(table.Elements<W.TableRow>(), row => Assert.NotNull(row.Elements<W.TableCell>().Last().Descendants<W.Run>().First().RunProperties?.Italic));
    }

    [Fact]
    public void RejectsMergedTablesExplicitly()
    {
        using var input = new MemoryStream(Fixture("apache-poi/table-merges.doc")); using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => converter.Convert(input, output, DocumentFormat.Doc, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void RejectsEmptyFormattingRangeFromOldMalformedFixture()
    {
        Assert.Throws<InvalidDataException>(() => Convert(Fixture("invalid-empty-papx.doc"), DocumentFormat.Doc, DocumentFormat.Docx));
    }

    [Fact]
    public void RejectsOutOfBoundsFormattingPageBeforeWriting()
    {
        byte[] binary = Convert(Encoding.UTF8.GetBytes("Formatting page bounds"), DocumentFormat.Text, DocumentFormat.Doc);
        byte[] corrupt = EditStreams(binary, (word, table) =>
        {
            int bte = BitConverter.ToInt32(word, 154 + 13 * 8), size = BitConverter.ToInt32(word, 158 + 13 * 8);
            int count = (size - 4) / 8;
            Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, table, bte + (count + 1) * 4, 4);
        });
        using var input = new MemoryStream(corrupt); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => converter.Convert(input, output, DocumentFormat.Doc, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void DirectBoldOperandsRespectInheritedHeadingStyle(int operand, bool bold)
    {
        byte[] source = Convert(Encoding.UTF8.GetBytes("# Styled heading"), DocumentFormat.Markdown, DocumentFormat.Docx);
        byte[] binary = Convert(source, DocumentFormat.Docx, DocumentFormat.Doc);
        // Change only the first CHPX bold operand using MS-DOC's published toggle encodings.
        byte[] modified = EditStreams(binary, (word, table) => word[FirstCharacterProperties(word, table) + 3] = (byte)operand);
        using WordprocessingDocument result = Open(Convert(modified, DocumentFormat.Doc, DocumentFormat.Docx));
        W.Paragraph paragraph = result.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().First();
        Assert.Equal("Heading1", paragraph.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Equal("Styled heading", paragraph.InnerText);
        Assert.Equal(bold, paragraph.Descendants<W.Run>().First().RunProperties?.Bold != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsMalformedCharacterPropertiesWithoutPartialOutput(bool truncated)
    {
        byte[] binary = Convert(Encoding.UTF8.GetBytes("Properties"), DocumentFormat.Text, DocumentFormat.Doc);
        byte[] corrupt = EditStreams(binary, (word, table) =>
        {
            int offset = FirstCharacterProperties(word, table);
            if (truncated) word[offset] = 1; // One byte cannot contain a two-byte SPRM.
            else word[offset + 3] = 2; // Not one of the four legal Boolean operands.
        });
        using var input = new MemoryStream(corrupt); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => converter.Convert(input, output, DocumentFormat.Doc, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void RejectsCyclicStyleInheritance()
    {
        byte[] binary = Convert(Encoding.UTF8.GetBytes("Cyclic style"), DocumentFormat.Text, DocumentFormat.Doc);
        byte[] corrupt = EditStreams(binary, (word, table) =>
        {
            int styleSheet = BitConverter.ToInt32(word, 162);
            int firstStyle = styleSheet + 2 + BitConverter.ToUInt16(table, styleSheet) + 2;
            table[firstStyle + 2] = 1; table[firstStyle + 3] = 0; // Normal based on Normal.
        });
        Assert.Throws<InvalidDataException>(() => Convert(corrupt, DocumentFormat.Doc, DocumentFormat.Docx));
    }

    private static int FirstCharacterProperties(byte[] word, byte[] table)
    {
        int bte = BitConverter.ToInt32(word, 154 + 12 * 8), size = BitConverter.ToInt32(word, 158 + 12 * 8);
        int count = (size - 4) / 8;
        int page = BitConverter.ToInt32(table, bte + (count + 1) * 4) * 512;
        return page + word[page + (word[page + 511] + 1) * 4] * 2;
    }

    private byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        converter.Convert(input, output, from, to, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken); return output.ToArray();
    }

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, name));

    private static byte[] EditStreams(byte[] binary, Action<byte[], byte[]> edit)
    {
        using var memory = new MemoryStream(); memory.Write(binary, 0, binary.Length); memory.Position = 0;
        using (var root = RootStorage.Open(memory, StorageModeFlags.LeaveOpen))
        {
            byte[] Read(string name) { using CfbStream stream = root.OpenStream(name); using var result = new MemoryStream(); stream.CopyTo(result); return result.ToArray(); }
            byte[] word = Read("WordDocument"), table = Read("1Table"); edit(word, table);
            using CfbStream wordStream = root.OpenStream("WordDocument"); wordStream.Write(word, 0, word.Length);
            using CfbStream tableStream = root.OpenStream("1Table"); tableStream.Write(table, 0, table.Length);
        }
        return memory.ToArray();
    }
}
