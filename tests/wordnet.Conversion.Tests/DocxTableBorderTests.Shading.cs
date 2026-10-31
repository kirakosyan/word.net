using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private const string Shading = "<w:shd w:val='clear' w:color='auto' w:fill='FFF3C0'/>";
    private static readonly string[] ShadingMarker = { "background-color:#FFF3C0" };

    [Fact]
    public void DirectTableShadingRemainsEditableAndFillsSimpleCells()
    {
        byte[] source = Input(shading: Shading); Schema(source);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = saved.ToArray(); Schema(word);
        var table = Part(word, "word/document.xml").Descendants(W + "tbl").Single();
        Assert.Equal("FFF3C0", (string?)table.Element(W + "tblPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        Assert.Empty(table.Descendants(W + "tcPr").Elements(W + "shd"));
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Pdf));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(IsReferenceFill)); Assert.Equal("ABCD", pdf.GetPage(1).Text);
        string html = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Html));
        Assert.Equal(4, html.Split(ShadingMarker, StringSplitOptions.None).Length - 1);
        var colors = Part(Convert(source, DocumentFormat.Odt), "content.xml").Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName == "background-color").Select(attribute => attribute.Value).ToArray();
        Assert.Contains("#FFF3C0", colors);
        byte[] xps = Convert(source, DocumentFormat.Xps);
        using var zip = new ZipArchive(new MemoryStream(xps));
        using var reader = new StreamReader(zip.Entries.Single(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open());
        Assert.Contains("FFF3C0", reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndependentReferenceRetainsTheMissingTableShadingResultAndChecksTheCellControl()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "table-shading.docx")); Schema(source);
        byte[] cellSource = File.ReadAllBytes(Path.Combine(directory, "cell-shading.docx")); Schema(cellSource);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "table-shading.pdf")));
        using PdfDocument control = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "cell-shading.pdf")));
        using PdfDocument actual = PdfDocument.Open(Convert(source, DocumentFormat.Pdf));
        Assert.DoesNotContain(reference.GetPage(1).Paths, IsReferenceFill);
        Assert.Contains(control.GetPage(1).Paths, IsReferenceFill);
        Assert.Contains(actual.GetPage(1).Paths, IsReferenceFill);
        Assert.Equal("ABCD", reference.GetPage(1).Text.Replace("\n", ""));
        Assert.Equal("ABCD", control.GetPage(1).Text.Replace("\n", ""));
        Assert.Equal("ABCD", actual.GetPage(1).Text.Replace("\n", ""));
    }

    [Theory]
    [InlineData("<w:shd w:val='clear' w:fill='00FF00'/>")]
    [InlineData("<w:shd w:val='nil'/>")]
    public void TableAndDirectCellShadingConflictIsDiagnosedBeforeWriting(string cellShading)
    {
        byte[] source = Input(firstCellProperties: cellShading, shading: Shading);
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.ShadingConflict");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergedAndNestedTableShadingRemainExplicitlyUnsupported(bool merged)
    {
        byte[] source;
        if (merged) source = Input(borders: "", firstCellProperties: "<w:gridSpan w:val='2'/>", shading: Shading);
        else
        {
            string nested = "<w:tbl><w:tblGrid><w:gridCol w:w='1200'/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Nested</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
            string cell = "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/></w:tcPr><w:p/>" + nested + "<w:p/></w:tc>";
            string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Shading + "<w:tblLayout w:type='fixed'/></w:tblPr>"
                + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid><w:tr>" + cell + Cell("B") + "</w:tr></w:tbl>";
            source = TestDocuments.Docx(TestDocuments.Body(table + Section));
        }
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("<w:shd w:val='pct20' w:fill='FFF3C0'/>", "Table.ShadingApproximated")]
    [InlineData("<w:shd w:val='clear' w:fill='auto'/>", "Table.ShadingColor")]
    [InlineData("<w:shd w:val='clear' w:color='CC0000' w:fill='FFF3C0'/>", "Table.ShadingApproximated")]
    [InlineData("<w:shd w:val='clear' w:fill='FFF3C0'><w:unsupported/></w:shd>", "Table.ShadingApproximated")]
    public void UnsupportedTableShadingUsesExplicitLossDiagnostics(string shading, string diagnostic)
    {
        using var input = new MemoryStream(Input(borders: "", shading: shading)); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(DocumentFormat.Rtf)]
    [InlineData(DocumentFormat.Text)]
    public void UnsupportedDestinationReportsTableShadingLoss(DocumentFormat format)
    {
        using var input = new MemoryStream(Input(borders: "", shading: Shading)); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.Decoration", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, format, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void RepeatedReportRowsRecomputeTableShading()
    {
        string row = "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>";
        string repeat = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>" + row + "</w:sdtContent></w:sdt>";
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Shading + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>" + repeat + "</w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table + Section)));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = saved.ToArray(); Schema(word); Assert.Equal(3, Part(word, "word/document.xml").Descendants(W + "tr").Count());
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("ABABAB", pdf.GetPage(1).Text);
        Assert.Equal(6, pdf.GetPage(1).Paths.Count(IsReferenceFill));
    }

    [Theory]
    [InlineData(true, "Table.ShadingRepeat")]
    [InlineData(false, "Table.ShadingSplit")]
    public void MultipageTableShadingApproximationsRemainExplicit(bool repeatHeader, string diagnostic)
    {
        string section = Section.Replace("w:h='15840'", "w:h='5760'").Replace("w:top='1440' w:bottom='1440'", "w:top='720' w:bottom='720'");
        string header = repeatHeader ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("Header") + Cell("Header2") + "</w:tr>" : "";
        string rows = repeatHeader ? string.Concat(Enumerable.Range(0, 30).Select(i => "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>"))
            : "<w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/></w:tcPr>"
                + string.Concat(Enumerable.Range(0, 50).Select(i => "<w:p><w:r><w:t>Line</w:t></w:r></w:p>")) + "</w:tc>" + Cell("B") + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Shading + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>" + header + rows + "</w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table + section))); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void TableShadingInHeaderStoriesRemainsExplicitlyUnsupported()
    {
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + Shading + "<w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid><w:tr>" + Cell("A") + Cell("B") + "</w:tr></w:tbl>";
        string section = Section.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        string relationships = "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>";
        string header = "<w:hdr xmlns:w='" + W + "'>" + table + "</w:hdr>";
        byte[] source = TestDocuments.Docx(TestDocuments.Body("<w:p/>" + section), relationships,
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/header.xml", header));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingStory", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void DuplicateTableShadingFailsBeforeWriting()
    {
        using var input = new MemoryStream(Input(borders: "", shading: Shading + Shading)); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableShading", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void UnexpectedTableBorderChildIsDiagnosedBeforeWriting()
    {
        string borders = Borders.Replace("<w:top w:val='single' w:sz='8' w:color='FF0000'/>",
            "<w:top w:val='single' w:sz='8' w:color='FF0000'><w:unsupported/></w:top>");
        using var input = new MemoryStream(Input(borders)); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.BorderApproximated", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private static bool IsReferenceFill(UglyToad.PdfPig.Graphics.PdfPath path)
    {
        if (!path.IsFilled || path.FillColor == null) return false;
        var color = path.FillColor.ToRGBValues();
        return Math.Abs(color.r - 1) < 0.01 && Math.Abs(color.g - 243 / 255.0) < 0.01 && Math.Abs(color.b - 192 / 255.0) < 0.01;
    }
}
