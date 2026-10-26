using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class TableLayoutTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private const string Grid = "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>";
    private const string SmallPage = "<w:sectPr><w:pgSz w:w='12240' w:h='5760'/><w:pgMar w:top='720' w:bottom='720' w:left='720' w:right='720' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    public void MultipleParagraphsAndMergesRemainEditable(DocumentFormat intermediate)
    {
        string rows = "<w:tr>" + Cell("<w:gridSpan w:val='2'/>", P("Wide") + P("Second", "<w:jc w:val='right'/>")) + Cell("", P("Right")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:vMerge w:val='restart'/>", P("Vertical")) + Cell("", P("A")) + Cell("", P("B")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:vMerge/>", "<w:p/>") + Cell("", P("C")) + Cell("", P("D")) + "</w:tr>";
        byte[] input = Docx(rows);
        byte[] exported = Convert(input, DocumentFormat.Docx, intermediate, strict: true);
        byte[] restored = Convert(exported, intermediate, DocumentFormat.Docx);
        using WordprocessingDocument package = WordprocessingDocument.Open(new MemoryStream(restored), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        using var zip = new ZipArchive(new MemoryStream(restored));
        using Stream stream = zip.GetEntry("word/document.xml")!.Open();
        XElement[] cells = XDocument.Load(stream).Descendants(W + "tc").ToArray();
        Assert.Equal(8, cells.Length);
        Assert.Equal(2, cells[0].Elements(W + "p").Count());
        Assert.Equal("right", (string?)cells[0].Elements(W + "p").Last().Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val"));
        Assert.Equal("2", (string?)cells[0].Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val"));
        Assert.Equal("restart", (string?)cells[2].Element(W + "tcPr")?.Element(W + "vMerge")?.Attribute(W + "val"));
        Assert.Equal("continue", (string?)cells[5].Element(W + "tcPr")?.Element(W + "vMerge")?.Attribute(W + "val"));
        Assert.Equal("WideSecondRightVerticalABCD", string.Concat(cells.Descendants(W + "t").Select(t => t.Value)));
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void RepeatsHeadersAndSplitsLongRowsWithoutLosingText(DocumentFormat source)
    {
        string paragraphs = string.Concat(Enumerable.Range(0, 35).Select(i => P("Entry" + i.ToString("D2", CultureInfo.InvariantCulture))));
        string rows = "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("<w:gridSpan w:val='3'/>", P("Header")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:gridSpan w:val='3'/>", paragraphs) + "</w:tr>";
        byte[] input = ExplicitLayoutFixture.LegacyMetrics(Docx(rows, SmallPage));
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        Assert.True(pdf.NumberOfPages >= 4);
        string text = "";
        for (int page = 1; page <= pdf.NumberOfPages; page++)
        {
            var current = pdf.GetPage(page);
            Assert.StartsWith("Header", current.Text, StringComparison.Ordinal);
            Assert.All(current.Letters, letter => Assert.InRange(letter.StartBaseLine.Y, 36, current.Height - 36));
            text += current.Text.Substring("Header".Length);
        }
        Assert.Equal(string.Concat(Enumerable.Range(0, 35).Select(i => "Entry" + i.ToString("D2", CultureInfo.InvariantCulture))), text);
    }

    [Fact]
    public void RepeatingHeaderMovesWithFirstBodyRow()
    {
        string filler = string.Concat(Enumerable.Range(0, 8).Select(i => P("Filler" + i.ToString(CultureInfo.InvariantCulture))));
        string rows = "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("<w:gridSpan w:val='3'/>", P("Header")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:gridSpan w:val='3'/>", P("Body") + P("Second")) + "</w:tr>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(filler + "<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr>" + Grid + rows + "</w:tbl>" + SmallPage));
        using PdfDocument pdf = PdfDocument.Open(Convert(ExplicitLayoutFixture.LegacyMetrics(input), DocumentFormat.Docx, DocumentFormat.Pdf, strict: true));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.DoesNotContain("Header", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal("HeaderBodySecond", pdf.GetPage(2).Text);
    }

    [Fact]
    public void MergedPdfCellsOccupyCorrectColumnsWithoutDuplicatingText()
    {
        string rows = "<w:tr>" + Cell("<w:gridSpan w:val='2'/>", P("Wide")) + Cell("", P("Right")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:vMerge w:val='restart'/>", P("Vertical") + P("Below")) + Cell("", P("A")) + Cell("", P("B")) + "</w:tr>"
            + "<w:tr>" + Cell("<w:vMerge/>", "<w:p/>") + Cell("", P("C")) + Cell("", P("D")) + "</w:tr>";
        using PdfDocument pdf = PdfDocument.Open(Convert(Docx(rows), DocumentFormat.Docx, DocumentFormat.Pdf, strict: true));
        Assert.Equal(1, pdf.NumberOfPages);
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal("WideRightVerticalBelowABCD", string.Concat(letters.Select(l => l.Value)));
        Assert.Equal(288, letters[4].StartBaseLine.X, 3);
        Assert.Equal(168, letters[22].StartBaseLine.X, 3);
    }

    [Theory]
    [InlineData("<w:vMerge/>")]
    [InlineData("<w:gridSpan w:val='0'/>")]
    [InlineData("<w:gridSpan w:val='999999999'/>")]
    public void RejectsInvalidMergeBeforeWriting(string properties)
    {
        using var input = new MemoryStream(Docx("<w:tr>" + Cell(properties, "<w:p/>") + Cell("", P("A")) + Cell("", P("B")) + "</w:tr>"));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:tr/>")]
    public void EmptyTableStructureIsRejectedBeforeWriting(string rows)
    {
        using var input = new MemoryStream(Docx(rows)); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Doc));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void NonSplittingOversizedRowFailsInsteadOfClipping()
    {
        string rows = "<w:tr><w:trPr><w:cantSplit/></w:trPr>" + Cell("<w:gridSpan w:val='3'/>", string.Concat(Enumerable.Repeat(P("Line"), 40))) + "</w:tr>";
        using var input = new MemoryStream(Docx(rows, SmallPage)); using var output = new MemoryStream();
        Assert.Contains("Layout.TableGroupHeight", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf)).Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    public void RichEpubCellStillReportsUnsupportedCellCss(DocumentFormat target)
    {
        using var input = new MemoryStream();
        byte[] fixture = ConversionAcceptanceTests.Fixture("plain.epub"); input.Write(fixture, 0, fixture.Length); input.Position = 0;
        using (var zip = new ZipArchive(input, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.Entries.First(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal));
            XDocument xml;
            using (Stream stream = entry.Open()) xml = XDocument.Load(stream);
            XNamespace h = "http://www.w3.org/1999/xhtml";
            xml.Descendants(h + "body").Single().ReplaceNodes(new XElement(h + "table", new XElement(h + "tr",
                new XElement(h + "td", new XAttribute("style", "background-color:red"), new XElement(h + "p", "First"), new XElement(h + "p", "Second")))));
            using Stream rewritten = entry.Open(); rewritten.SetLength(0); xml.Save(rewritten);
        }
        input.Position = 0;
        using var output = new MemoryStream();
        Assert.Contains("Ebook.Css", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Epub, target)).Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    private static string P(string text, string properties = "") => "<w:p><w:pPr>" + properties + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";
    private static string Cell(string properties, string content) => "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + content + "</w:tc>";
    private static byte[] Docx(string rows, string section = "") => TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr>" + Grid + rows + "</w:tbl>" + section));
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, bool strict = false)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, from, to, new ConversionOptions(strict ? FidelityMode.Strict : FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
}
