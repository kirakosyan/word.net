using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class ProductionReviewTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private const string Letter = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='1440' w:bottom='1440' w:left='1440' w:right='1440'/></w:sectPr>";
    private const string Grid = "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>";

    [Theory]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Xps)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Xps)]
    public void WordDefaultTableCanOverhangTextMarginsInsidePhysicalPage(DocumentFormat source, DocumentFormat target)
    {
        string grid = "<w:tblGrid>" + string.Concat(Enumerable.Repeat("<w:gridCol w:w='3192'/>", 3)) + "</w:tblGrid>";
        byte[] input = Docx("<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr>" + grid + Row(Cell("Alpha") + Cell("Beta") + Cell("Gamma")) + "</w:tbl>" + Letter);
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        byte[] output = Convert(input, source, target);
        if (target == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output);
            Assert.Equal("AlphaBetaGamma", pdf.GetPage(1).Text);
            Assert.Equal(391.2, pdf.GetPage(1).Letters.First(l => l.Value == "G").StartBaseLine.X, 3);
        }
        else
        {
            using var zip = new ZipArchive(new MemoryStream(output));
            XDocument page = ReadXml(zip, "Documents/1/Pages/1.fpage");
            Assert.Equal("AlphaBetaGamma", string.Concat(page.Descendants().Attributes("UnicodeString").Select(a => a.Value)));
        }
    }

    [Fact]
    public void TableOutsidePhysicalPageFailsBeforeWriting()
    {
        byte[] input = Docx("<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='12000'/></w:tblGrid>" + Row(Cell("Wide")) + "</w:tbl>" + Letter);
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Contains("Layout.TableWidth", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void NegativeTableIndentSurvivesWordAndPositionsText(DocumentFormat source)
    {
        byte[] input = Docx("<w:tbl><w:tblPr><w:tblInd w:w='-108' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='3192'/></w:tblGrid>" + Row(Cell("Indented")) + "</w:tbl>" + Letter);
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        Assert.Equal("Indented", pdf.GetPage(1).Text);
        Assert.Equal(66.6, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3);
    }

    [Theory]
    [InlineData("before", DocumentFormat.Text)]
    [InlineData("after", DocumentFormat.Text)]
    [InlineData("sdt", DocumentFormat.Text)]
    [InlineData("customXml", DocumentFormat.Text)]
    [InlineData("width", DocumentFormat.Text)]
    [InlineData("mixed-width", DocumentFormat.Text)]
    [InlineData("before", DocumentFormat.Pdf)]
    [InlineData("after", DocumentFormat.Pdf)]
    [InlineData("sdt", DocumentFormat.Pdf)]
    [InlineData("customXml", DocumentFormat.Pdf)]
    [InlineData("width", DocumentFormat.Pdf)]
    [InlineData("mixed-width", DocumentFormat.Pdf)]
    public void OmittedGridColumnsAndWrappedCellsRetainContent(string kind, DocumentFormat target)
    {
        string grid = kind == "width" ? Grid.Replace("<w:gridCol w:w='2400'/>", "<w:gridCol/>") : Grid;
        if (kind == "mixed-width") grid = "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol/><w:gridCol w:w='2400'/></w:tblGrid>";
        string middle = Cell("Beta");
        if (kind == "sdt") middle = "<w:sdt><w:sdtContent>" + middle + "</w:sdtContent></w:sdt>";
        if (kind == "customXml") middle = "<w:customXml>" + middle + "</w:customXml>";
        string second = kind is "before" or "after"
            ? "<w:tr><w:trPr><w:grid" + (kind == "before" ? "Before" : "After") + " w:val='1'/></w:trPr>" + Cell("Delta") + Cell("Epsilon") + "</w:tr>"
            : Row(Cell("Delta") + Cell("Epsilon"));
        byte[] output = Convert(Docx("<w:tbl>" + grid + Row(Cell("Alpha") + middle + Cell("Gamma")) + second + "</w:tbl>"), DocumentFormat.Docx, target, out ConversionResult result);
        string text = target == DocumentFormat.Text ? Encoding.UTF8.GetString(output) : PdfText(output);
        foreach (string word in new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" }) Assert.Contains(word, text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code is "Table.Grid" or "Docx.TableGrid" or "Docx.TableStructure");
    }

    [Theory]
    [InlineData(-108, 8892)]
    [InlineData(-108, 9000)]
    [InlineData(0, 8000)]
    public void BinaryWordUsesAbsoluteRowEdgesAndPadsBothSides(int rowLeft, int rowRight)
    {
        byte[] input = Convert(Docx("<w:tbl>" + Row(Cell("Alpha") + Cell("Beta")) + Row(Cell("Gamma") + Cell("Delta")) + "</w:tbl>"), DocumentFormat.Docx, DocumentFormat.Doc);
        // Independently perturb MS-DOC TDefTable's absolute rgdxaCenter edges:
        // rows can have a different indent, a different right edge, or both.
        input = RewriteWord(input, word =>
        {
            int[] rows = Enumerable.Range(0, word.Length - 11).Where(i => word[i] == 0x08 && word[i + 1] == 0xd6 && word[i + 2] == 8 && word[i + 3] == 0 && word[i + 4] == 2).ToArray();
            Assert.Equal(2, rows.Length);
            int secondRow = rows.OrderBy(position => ParagraphEnd(word, position)).Last();
            Put16(word, secondRow + 5, rowLeft); Put16(word, secondRow + 7, 4500); Put16(word, secondRow + 9, rowRight);
        });
        using PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Doc, DocumentFormat.Pdf));
        Assert.Equal("AlphaBetaGammaDelta", pdf.GetPage(1).Text);
        Assert.Equal(48, pdf.GetPage(1).Letters.First(l => l.Value == "A").StartBaseLine.X, 3);
        Assert.Equal(48 + rowLeft / 20.0, pdf.GetPage(1).Letters.First(l => l.Value == "G").StartBaseLine.X, 3);
        byte[] docx = Convert(input, DocumentFormat.Doc, DocumentFormat.Docx, out ConversionResult result);
        using var zip = new ZipArchive(new MemoryStream(docx));
        XDocument xml = ReadXml(zip, "word/document.xml");
        Assert.Equal(rowLeft == 0 ? null : rowLeft.ToString(CultureInfo.InvariantCulture), (string?)xml.Descendants(W + "tblInd").SingleOrDefault()?.Attribute(W + "w"));
        Assert.Contains(result.Diagnostics, d => d.Code is "Doc.TableGrid" or "Table.Grid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BinaryWordOrientationUsesPublishedDevModeValues(int orientation)
    {
        byte[] doc = Convert(Docx(TestDocuments.TextParagraph("Orientation") + Letter), DocumentFormat.Docx, DocumentFormat.Doc);
        doc = RewriteWord(doc, word => word[SectionOperand(word, 0x301d)] = (byte)orientation);
        using var zip = new ZipArchive(new MemoryStream(Convert(doc, DocumentFormat.Doc, DocumentFormat.Docx)));
        Assert.Equal(orientation == 2 ? "landscape" : "portrait", (string?)ReadXml(zip, "word/document.xml").Descendants(W + "pgSz").Single().Attribute(W + "orient") ?? "portrait");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void BinaryWordWritesOrientationIndependentlyOfItsReader(bool landscape, int expected)
    {
        string section = landscape ? Letter.Replace("w:h='15840'", "w:h='15840' w:orient='landscape'") : Letter;
        byte[] doc = Convert(Docx(TestDocuments.TextParagraph("Orientation") + section), DocumentFormat.Docx, DocumentFormat.Doc);
        using var source = new MemoryStream(doc); using var root = RootStorage.Open(source);
        using CfbStream stream = root.OpenStream("WordDocument"); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        byte[] word = bytes.ToArray();
        // MS-DOC 2.9.236 defines 0x01 portrait and 0x02 landscape. This assertion
        // reads the binary operand directly; it does not call the library reader.
        Assert.Equal(expected, word[SectionOperand(word, 0x301d)]);
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void NegativeVerticalMarginsUseAbsoluteDistanceWithLoss(DocumentFormat source)
    {
        byte[] input = Docx(TestDocuments.TextParagraph("Fixed margin") + Letter);
        if (source == DocumentFormat.Docx) input = Docx(TestDocuments.TextParagraph("Fixed margin") + Letter.Replace("w:top='1440'", "w:top='-1440'").Replace("w:bottom='1440'", "w:bottom='-1440'"));
        else input = RewriteWord(Convert(input, DocumentFormat.Docx, source), word => { Put16(word, SectionOperand(word, 0x9023), -1440); Put16(word, SectionOperand(word, 0x9024), -1440); });
        byte[] output = Convert(input, source, DocumentFormat.Pdf, out ConversionResult result);
        using PdfDocument pdf = PdfDocument.Open(output);
        Assert.Equal("Fixed margin", pdf.GetPage(1).Text);
        Assert.InRange(pdf.GetPage(1).Letters[0].StartBaseLine.Y, 700, 720);
        Assert.Contains(result.Diagnostics, d => d.Code == (source == DocumentFormat.Doc ? "Doc.FixedMargin" : "Docx.FixedMargin"));
        if (source == DocumentFormat.Docx)
        {
            using var original = new MemoryStream(input); using var destination = new MemoryStream(); destination.WriteByte(42);
            Assert.Contains("Docx.FixedMargin", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(original, destination, source, DocumentFormat.Pdf)).Message, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 42 }, destination.ToArray());
        }
    }

    [Theory]
    [InlineData("zero-row", DocumentFormat.Docx)]
    [InlineData("zero-column", DocumentFormat.Docx)]
    [InlineData("long-row", DocumentFormat.Docx)]
    [InlineData("span-gap", DocumentFormat.Docx)]
    [InlineData("empty-table", DocumentFormat.Docx)]
    [InlineData("empty-row", DocumentFormat.Docx)]
    [InlineData("columns", DocumentFormat.Docx)]
    [InlineData("zero-row", DocumentFormat.Doc)]
    [InlineData("zero-column", DocumentFormat.Doc)]
    [InlineData("long-row", DocumentFormat.Doc)]
    [InlineData("span-gap", DocumentFormat.Doc)]
    [InlineData("empty-table", DocumentFormat.Doc)]
    [InlineData("empty-row", DocumentFormat.Doc)]
    [InlineData("columns", DocumentFormat.Doc)]
    public void HtmlTableCompatibilityCasesKeepEditableText(string kind, DocumentFormat target)
    {
        string table = kind switch
        {
            "zero-row" => "<table><tr><td rowspan='0'>Keep</td><td>First</td></tr><tr><td>Last</td></tr></table>",
            "zero-column" => "<table><tr><td colspan='0'>Keep</td></tr></table>",
            "long-row" => "<table><tr><td rowspan='5'>Keep</td><td>First</td></tr><tr><td>Last</td></tr></table>",
            "span-gap" => "<table><tr><td>Keep</td><td rowspan='2'>Last</td></tr><tr></tr></table>",
            "empty-table" => "<p>Keep</p><table></table>",
            "empty-row" => "<table><tr></tr><tr><td>Keep</td></tr></table>",
            _ => "<table><colgroup><col span='3'/></colgroup><tr><td>Keep</td><td>First</td><td>Last</td></tr></table>"
        };
        byte[] input = Epub(table), output = Convert(input, DocumentFormat.Epub, target, out ConversionResult report);
        if (target == DocumentFormat.Doc) output = Convert(output, target, DocumentFormat.Docx);
        using WordprocessingDocument document = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));
        var content = document.MainDocumentPart!.Document!;
        Assert.Contains("Keep", content.InnerText, StringComparison.Ordinal);
        if (kind is "zero-row" or "long-row" or "span-gap" or "columns") Assert.Contains("Last", content.InnerText, StringComparison.Ordinal);
        if (kind is not "zero-row" and not "columns") Assert.Contains(report.Diagnostics, d => d.Code.StartsWith("Ebook.", StringComparison.Ordinal));
    }

    [Fact]
    public void HtmlRowHeadersDoNotBecomeRepeatingHeaders()
    {
        string rows = string.Concat(Enumerable.Range(0, 70).Select(i => "<tr><th scope='row'>Label" + i.ToString(CultureInfo.InvariantCulture) + "</th><td>Value</td></tr>"));
        byte[] docx = Convert(Epub("<table>" + rows + "</table>"), DocumentFormat.Epub, DocumentFormat.Docx);
        using var zip = new ZipArchive(new MemoryStream(docx));
        Assert.Empty(ReadXml(zip, "word/document.xml").Descendants(W + "tblHeader"));
        using PdfDocument pdf = PdfDocument.Open(Convert(docx, DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.True(pdf.NumberOfPages > 1);
        Assert.Contains("Label69Value", string.Concat(pdf.GetPages().Select(p => p.Text)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Ocr")]
    public void DefaultOcrDoesNotReconstructColumnsEvenWithOrientationCorrection(bool correctOrientation)
    {
        string data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA") ?? "", native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY") ?? "";
        Assert.False(string.IsNullOrEmpty(data) || string.IsNullOrEmpty(native), "Run eng/prepare-ocr-tests.ps1 in the process launching tests.");
        var ocr = new EnglishOcrOptions(data, native, correctOrientation);
        byte[] output = Convert(ConversionAcceptanceTests.Fixture("ocr-columns.pdf"), DocumentFormat.Pdf, DocumentFormat.Docx, out ConversionResult result, new ConversionOptions(FidelityMode.BestEffort, englishOcr: ocr));
        string text = ConversionAcceptanceTests.ReadOutput(output, DocumentFormat.Docx);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Ocr.Columns");
        // Native Tesseract versions can choose different line/column layouts.
        // The default must keep their content without applying our reconstruction.
        foreach (string phrase in new[] { "Left column first", "Left column final", "Right column first", "Right column final" })
            Assert.Contains(phrase, text, StringComparison.Ordinal);
    }

    [Fact]
    public void OptInColumnOrderingPreservesParagraphBoundaries()
    {
        string tsv = "";
        for (int side = 0; side < 2; side++)
            for (int line = 0; line < 3; line++)
                for (int word = 0; word < 2; word++)
                    tsv += string.Format(CultureInfo.InvariantCulture, "5\t1\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t45\t10\t99\t{6}\n", side + 1, line == 2 ? 2 : 1, line + 1, word + 1, 30 + side * 300 + word * 55, 30 + line * 40, (side == 0 ? "Left" : "Right") + line + word);
        Assembly assembly = typeof(WordConverter).Assembly;
        Type contextType = assembly.GetType("WordNet.Internal.ConversionContext", true)!;
        object context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new ConversionOptions(), TestContext.Current.CancellationToken }, CultureInfo.InvariantCulture)!;
        MethodInfo method = Assembly.Load("wordnet.Ocr").GetType("WordNet.Ocr.OcrReadingOrder", true)!.GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static)!;
        string actual = (string)method.Invoke(null, new[] { tsv, (object)600, 300, context })!;
        Assert.Equal("Left00 Left01\nLeft10 Left11\n\nLeft20 Left21\n\nRight00 Right01\nRight10 Right11\n\nRight20 Right21", actual);
    }

    private static byte[] Docx(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string Cell(string text) => "<w:tc>" + TestDocuments.TextParagraph(text) + "</w:tc>";
    private static string Row(string cells) => "<w:tr>" + cells + "</w:tr>";
    private static XDocument ReadXml(ZipArchive zip, string path) { using Stream stream = zip.GetEntry(path)!.Open(); return XDocument.Load(stream); }
    private static string PdfText(byte[] bytes) { using PdfDocument pdf = PdfDocument.Open(bytes); return string.Concat(pdf.GetPages().Select(p => p.Text)); }
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to) => Convert(bytes, from, to, out _);
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, out ConversionResult result, ConversionOptions? options = null)
    {
        using var source = new MemoryStream(bytes); using var output = new MemoryStream();
        result = new WordConverter().Convert(source, output, from, to, options ?? new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static byte[] RewriteWord(byte[] doc, Action<byte[]> edit)
    {
        using var memory = new MemoryStream(); memory.Write(doc, 0, doc.Length); memory.Position = 0;
        using (var root = RootStorage.Open(memory, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = root.OpenStream("WordDocument"); using var copy = new MemoryStream(); stream.CopyTo(copy);
            byte[] word = copy.ToArray(); edit(word); stream.Position = 0; stream.Write(word, 0, word.Length); root.Flush();
        }
        return memory.ToArray();
    }

    private static int SectionOperand(byte[] word, int code)
    {
        // The writer places its SEPX records after its final 512-byte FKP page.
        // Locate the first next-page section record, then walk fixed-size sprms
        // according to MS-DOC's spra operand lengths, independently of its reader.
        int start = Enumerable.Range(1024, word.Length - 1028).First(i => word[i] == 0x09 && word[i + 1] == 0x30 && word[i + 2] == 2 && word[i + 3] == 0x1d && word[i + 4] == 0x30);
        int length = BitConverter.ToUInt16(word, start - 2), end = start + length;
        while (start < end)
        {
            int sprm = BitConverter.ToUInt16(word, start); start += 2;
            if (sprm == code) return start;
            start += (sprm >> 13) switch { 0 or 1 => 1, 2 or 4 or 5 => 2, 3 => 4, 7 => 3, _ => throw new InvalidDataException("Unexpected variable section sprm in fixture.") };
        }
        throw new InvalidDataException("Section operand missing from fixture.");
    }

    private static void Put16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }

    private static int ParagraphEnd(byte[] word, int property)
    {
        // PAPX FKP run positions establish logical row order independently of
        // the order in which its property blobs were packed into the page.
        int page = property / 512 * 512, count = word[page + 511];
        for (int r = 0; r < count; r++)
        {
            int offset = page + word[page + (count + 1) * 4 + r * 13] * 2;
            int size = word[offset++];
            if (size == 0) size = word[offset++] * 2; else size = size * 2 - 1;
            if (property >= offset && property < offset + size) return BitConverter.ToInt32(word, page + (r + 1) * 4);
        }
        throw new InvalidDataException("Property is outside its paragraph FKP runs.");
    }

    private static byte[] Epub(string body)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add("book.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='text' href='chapter.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='text'/></spine></package>");
            Add("chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Independent table case</title></head><body>" + body + "</body></html>");
            void Add(string name, string text) { using Stream stream = zip.CreateEntry(name).Open(); byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); }
        }
        return output.ToArray();
    }
}
