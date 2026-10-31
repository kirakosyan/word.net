using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class DocxTableBorderTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[] BorderSides = { "top", "left", "bottom", "right", "insideH", "insideV" };
    private const string Borders = "<w:tblBorders><w:top w:val='single' w:sz='8' w:color='FF0000'/><w:left w:val='single' w:sz='8' w:color='0000FF'/>"
        + "<w:bottom w:val='single' w:sz='8' w:color='00FF00'/><w:right w:val='single' w:sz='8' w:color='FFFF00'/>"
        + "<w:insideH w:val='single' w:sz='8' w:color='00FFFF'/><w:insideV w:val='single' w:sz='8' w:color='FF00FF'/></w:tblBorders>";
    private const string Section = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='1440' w:bottom='1440' w:left='1440' w:right='1440' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
    private static string Cell(string text, string properties = "") => "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/>" + properties
        + "</w:tcPr><w:p><w:r><w:rPr><w:rFonts w:ascii='Liberation Sans' w:hAnsi='Liberation Sans'/><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p></w:tc>";
    private static byte[] Input(string borders = Borders, string firstCellProperties = "") => TestDocuments.Docx(TestDocuments.Body(
        "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + borders + "<w:tblLayout w:type='fixed'/></w:tblPr>"
        + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>"
        + "<w:tr>" + Cell("A", firstCellProperties) + Cell("B") + "</w:tr><w:tr>" + Cell("C") + Cell("D") + "</w:tr></w:tbl>" + Section));
    private static byte[] Convert(byte[] source, DocumentFormat format, FidelityMode mode = FidelityMode.BestEffort)
    {
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Docx, format, new ConversionOptions(mode), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    private static XDocument Part(byte[] source, string name)
    { using var zip = new ZipArchive(new MemoryStream(source)); using Stream input = zip.GetEntry(name)!.Open(); return XDocument.Load(input); }
    private static void Schema(byte[] source)
    { using var document = WordprocessingDocument.Open(new MemoryStream(source), false); Assert.Empty(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken)); }

    [Fact]
    public void LibreOfficeReferenceAgreesOnBorderColorsAndRelativeGridGeometry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "table-borders.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "table-borders.pdf")));
        using PdfDocument actual = PdfDocument.Open(Convert(source, DocumentFormat.Pdf));
        var referenceGrid = Grid(reference); var actualGrid = Grid(actual);
        Assert.Equal(referenceGrid.Width, actualGrid.Width, 1);
        Assert.Equal("ABCD", reference.GetPage(1).Text.Replace("\n", ""));
        Assert.Equal("ABCD", actual.GetPage(1).Text.Replace("\n", ""));
        Assert.All(reference.GetPage(1).Letters.Concat(actual.GetPage(1).Letters), letter =>
            Assert.Contains("LiberationSans", letter.FontName!.Replace("-", ""), StringComparison.OrdinalIgnoreCase));

        static (double Width, double Height) Grid(PdfDocument pdf)
        {
            var paths = pdf.GetPage(1).Paths.Where(path => path.IsStroked && path.StrokeColor != null).ToArray();
            double Side(double red, double green, double blue, bool horizontal, bool useRight = false)
            {
                var matching = paths.Where(path =>
                {
                    var color = path.StrokeColor!.ToRGBValues();
                    return Math.Abs(color.r - red) < 0.01 && Math.Abs(color.g - green) < 0.01 && Math.Abs(color.b - blue) < 0.01;
                }).Select(path => path.GetBoundingRectangle()).Where(rect => rect.HasValue).Select(rect => rect!.Value).ToArray();
                Assert.NotEmpty(matching);
                return horizontal ? matching.Average(rect => rect.Bottom) : matching.Average(rect => useRight ? rect.Right : rect.Left);
            }
            double left = Side(0, 0, 1, false), right = Side(1, 1, 0, false, true), middle = Side(1, 0, 1, false);
            double top = Side(1, 0, 0, true), bottom = Side(0, 1, 0, true), center = Side(0, 1, 1, true);
            Assert.InRange(middle, (left + right) / 2 - 1, (left + right) / 2 + 1);
            Assert.True(top > center && center > bottom);
            Assert.InRange((center - bottom) / (top - bottom), 0.4, 0.6);
            return (right - left, top - bottom);
        }
    }

    [Fact]
    public void DirectTableBordersRemainEditableAndPaintAllSixEdges()
    {
        byte[] source = Input(); Schema(source);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = saved.ToArray(); Schema(word);
        XElement table = Part(word, "word/document.xml").Descendants(W + "tbl").Single();
        Assert.Equal(BorderSides, table.Element(W + "tblPr")!.Element(W + "tblBorders")!.Elements().Select(e => e.Name.LocalName));
        Assert.Empty(table.Descendants(W + "tcBorders"));
        Assert.Equal("ABCD", string.Concat(table.Descendants(W + "t").Select(e => e.Value)));

        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Pdf));
        var paths = pdf.GetPage(1).Paths.Where(path => path.IsStroked && path.StrokeColor != null).ToArray();
        Count(1, 0, 0, 2); Count(0, 0, 1, 2); Count(0, 1, 0, 2); Count(1, 1, 0, 2); Count(0, 1, 1, 2); Count(1, 0, 1, 2);
        Assert.Equal("ABCD", pdf.GetPage(1).Text);
        string html = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Html));
        Assert.Contains("border-top:1pt solid #FF0000", html, StringComparison.Ordinal);
        Assert.Contains("border-right:1pt solid #FF00FF", html, StringComparison.Ordinal);
        Assert.Contains("border-bottom:1pt solid #00FFFF", html, StringComparison.Ordinal);
        var odtBorders = Part(Convert(source, DocumentFormat.Odt), "content.xml").Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName.StartsWith("border-", StringComparison.Ordinal)).Select(attribute => attribute.Value).ToArray();
        Assert.Contains(odtBorders, value => value.Contains("#FF0000"));
        Assert.Contains(odtBorders, value => value.Contains("#FF00FF"));
        byte[] xps = Convert(source, DocumentFormat.Xps);
        using var zip = new ZipArchive(new MemoryStream(xps));
        using var reader = new StreamReader(zip.Entries.Single(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open());
        string page = reader.ReadToEnd(); Assert.Contains("#FF0000", page, StringComparison.OrdinalIgnoreCase); Assert.Contains("#FF00FF", page, StringComparison.OrdinalIgnoreCase);
        void Count(double red, double green, double blue, int count) => Assert.Equal(count, paths.Count(path =>
        {
            var color = path.StrokeColor!.ToRGBValues();
            return Math.Abs(color.r - red) < 0.01 && Math.Abs(color.g - green) < 0.01 && Math.Abs(color.b - blue) < 0.01 && Math.Abs(path.LineWidth - 1) < 0.01;
        }));
    }

    [Fact]
    public void DisabledTableEdgeStaysDisabledInEditableAndRenderedOutputs()
    {
        string borders = Borders.Replace("<w:insideH w:val='single' w:sz='8' w:color='00FFFF'/>", "<w:insideH w:val='nil'/>");
        byte[] source = Input(borders); Schema(source);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = saved.ToArray(); Schema(word);
        Assert.Equal("nil", (string?)Part(word, "word/document.xml").Descendants(W + "insideH").Single().Attribute(W + "val"));
        string html = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Html));
        Assert.Contains("border-bottom:none", html, StringComparison.Ordinal);
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Pdf));
        Assert.DoesNotContain(pdf.GetPage(1).Paths, path => path.IsStroked && path.StrokeColor != null
            && Math.Abs(path.StrokeColor.ToRGBValues().g - 1) < 0.01 && Math.Abs(path.StrokeColor.ToRGBValues().b - 1) < 0.01);
    }

    [Fact]
    public void RepeatedReportRowsRecomputeTheOutsideAndInsideBorders()
    {
        string row = "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>";
        string repeat = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>" + row + "</w:sdtContent></w:sdt>";
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Borders + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>" + repeat + "</w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table + Section)));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        document.Merge(new { Items = new[] { new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = saved.ToArray(); Schema(word); Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "tr").Count());
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("ABAB", pdf.GetPage(1).Text);
        Assert.Contains(pdf.GetPage(1).Paths, path => path.IsStroked && path.StrokeColor != null
            && Math.Abs(path.StrokeColor.ToRGBValues().g - 1) < 0.01 && Math.Abs(path.StrokeColor.ToRGBValues().b - 1) < 0.01);
    }

    [Theory]
    [InlineData(DocumentFormat.Rtf)]
    [InlineData(DocumentFormat.Text)]
    public void UnsupportedDestinationReportsTableBorderLossBeforeWriting(DocumentFormat format)
    {
        using var input = new MemoryStream(Input()); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.Decoration", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, format, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(true, "Table.BorderRepeat")]
    [InlineData(false, "Table.BorderSplit")]
    public void MultipageTableBorderApproximationsRemainExplicit(bool repeatHeader, string diagnostic)
    {
        string shortSection = Section.Replace("w:h='15840'", "w:h='5760'").Replace("w:top='1440' w:bottom='1440'", "w:top='720' w:bottom='720'");
        string header = repeatHeader ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("Header") + Cell("Header2") + "</w:tr>" : "";
        string rows = repeatHeader ? string.Concat(Enumerable.Range(0, 30).Select(i => "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>"))
            : "<w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/></w:tcPr>"
                + string.Concat(Enumerable.Range(0, 50).Select(i => "<w:p><w:r><w:t>Line</w:t></w:r></w:p>")) + "</w:tc>" + Cell("B") + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Borders + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>" + header + rows + "</w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table + shortSection))); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void TableBordersInHeaderStoriesRemainExplicitlyUnsupported()
    {
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Borders + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid><w:tr>" + Cell("A") + Cell("B") + "</w:tr></w:tbl>";
        string section = Section.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        string relationships = "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>";
        string header = "<w:hdr xmlns:w='" + W + "'>" + table + "</w:hdr>";
        byte[] source = TestDocuments.Docx(TestDocuments.Body("<w:p/>" + section), relationships,
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/header.xml", header));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.BorderStory", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("<w:gridSpan w:val='2'/>")]
    [InlineData("<w:tcBorders><w:top w:val='single' w:sz='8' w:color='000000'/></w:tcBorders>")]
    public void ConflictingCellsAreDiagnosedAndStrictOutputIsUntouched(string cellProperties)
    {
        byte[] source = Input(firstCellProperties: cellProperties);
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.BorderConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.BorderConflict");
    }

    [Fact]
    public void DuplicateTableBorderSidesFailBeforeOutput()
    {
        string duplicate = Borders.Replace("</w:tblBorders>", "<w:top w:val='single' w:sz='8'/></w:tblBorders>");
        using var input = new MemoryStream(Input(duplicate)); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("w:color='auto'", "Table.BorderColor")]
    [InlineData("w:color='FF0000' w:space='1'", "Table.BorderApproximated")]
    public void AutomaticColorsAndBorderEffectsRemainDiagnosed(string replacement, string diagnostic)
    {
        byte[] source = Input(Borders.Replace("w:color='FF0000'", replacement));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
    public void SynthesizedEmptyGridCellsResolveTheirDefaultFontBeforeFixedLayout()
    {
        using var input = new MemoryStream(Input(borders: "", firstCellProperties: "<w:gridSpan w:val='2'/>"));
        using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.Grid");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("ABCD", pdf.GetPage(1).Text);
    }
}
