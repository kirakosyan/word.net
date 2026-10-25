using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class ChartBindingTests
{
    private static readonly string[] InitialCategories = { "Old" };
    private static readonly string[] BoundCategories = { "A", "B" };
    private static readonly string[] BoundCache = { "10", "20" };
    private static ReportData Data(object rows) => ReportData.Prepare(new { Rows = rows, Name = "Revenue" }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
    private static ChartDataBinding Binding(string value = "Value") => new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", value) });
    private static WordDocument Document(ReportData? report = null)
    {
        var document = WordDocument.Create(new ConversionOptions(FidelityMode.BestEffort));
        if (report != null)
        {
            string field(string path) => "<w:p><w:fldSimple w:instr='MERGEFIELD " + path + "'><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>";
            string template = field("Name") + "<w:tbl><w:tblGrid><w:gridCol w:w='3000'/><w:gridCol w:w='3000'/></w:tblGrid>"
                + "<w:sdt><w:sdtPr><w:tag w:val='repeat:Groups'/></w:sdtPr><w:sdtContent><w:tr><w:tc>" + field("Key")
                + "</w:tc><w:tc>" + field("Total") + "</w:tc></w:tr></w:sdtContent></w:sdt></w:tbl>";
            document = WordDocument.Load(new MemoryStream(TestDocuments.Docx(TestDocuments.Body(template))), DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
            document.Merge(report.Values, cancellationToken: TestContext.Current.CancellationToken);
        }
        document.AddChart(new ChartDefinition(ChartKind.ClusteredColumn, InitialCategories, new[] { new ChartSeries("Old", "2468AC", new double?[] { 1 }) }, title: "Revenue report", showLegend: false),
            description: "Revenue by category", cancellationToken: TestContext.Current.CancellationToken);
        return document;
    }
    private static byte[] Save(WordDocument document, DocumentFormat format = DocumentFormat.Docx)
    { using var output = new MemoryStream(); document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }

    [Fact]
    public void GroupedReportBindsRealChartCachesWorkbookAndRenderedData()
    {
        var plan = new ReportPlan(new[] { new ReportCollectionOperation("Groups", "Items", "Category", "Category",
            aggregates: new[] { new ReportAggregate("Total", ReportAggregateKind.Sum, "Amount") }) });
        ReportData data = ReportData.Prepare(new
        {
            Name = "Revenue",
            Items = new[] {
            new { Category = "B", Amount = 20m }, new { Category = "A", Amount = 4m }, new { Category = "A", Amount = 6m } }
        },
            plan, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument document = Document(data); DocumentObject original = document.Objects[0];
        DocumentObject updated = document.BindChart(original, data, new ChartDataBinding("Groups", "Key", new[] { new ChartSeriesBinding("Name", "Total") }), TestContext.Current.CancellationToken);
        Assert.Equal(original.Id, updated.Id); Assert.Equal(original.Description, updated.Description);
        Assert.Equal(BoundCategories, updated.Chart!.Categories); Assert.Equal(new double?[] { 10, 20 }, updated.Chart.Series[0].Values);
        byte[] saved = Save(document);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        using var zip = new ZipArchive(new MemoryStream(saved));
        using var chartStream = zip.GetEntry("word/charts/chart1.xml")!.Open(); XDocument chart = XDocument.Load(chartStream);
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        Assert.Equal(BoundCache, chart.Descendants(c + "numCache").Descendants(c + "v").Select(e => e.Value));
        using var workbookStream = zip.GetEntry("word/embeddings/chart1.xlsx")!.Open(); using var workbookBytes = new MemoryStream(); workbookStream.CopyTo(workbookBytes); workbookBytes.Position = 0;
        using (var workbook = SpreadsheetDocument.Open(workbookBytes, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(workbook, TestContext.Current.CancellationToken));
            var cells = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<global::DocumentFormat.OpenXml.Spreadsheet.Cell>().ToDictionary(cell => cell.CellReference!.Value!);
            Assert.Equal("10", cells["B2"].CellValue!.Text); Assert.Equal("20", cells["B3"].CellValue!.Text);
            Assert.Equal("Revenue", cells["B1"].InnerText); Assert.Equal("A", cells["A2"].InnerText); Assert.Equal("B", cells["A3"].InnerText);
        }
        byte[] pdfBytes = Save(document, DocumentFormat.Pdf);
        using (PdfDocument pdf = PdfDocument.Open(pdfBytes))
        { Assert.Equal(1, pdf.NumberOfPages); Assert.Contains("Revenue", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("20", pdf.GetPage(1).Text, StringComparison.Ordinal); }
        using var source = new MemoryStream(saved);
        DocumentPreview preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Svg }, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        byte[] svg = preview.Pages[0].GetBytes(); XDocument vector = XDocument.Parse(System.Text.Encoding.UTF8.GetString(svg));
        var bars = vector.Descendants().Where(e => e.Name.LocalName == "rect" && (string?)e.Attribute("fill") == "#2468AC").ToArray();
        var heights = bars.Select(b => double.Parse(b.Attribute("height")!.Value, CultureInfo.InvariantCulture)).OrderBy(h => h).ToArray();
        Assert.Equal(2, heights.Length); Assert.Equal(2, heights[1] / heights[0], 3);
        string? output = Environment.GetEnvironmentVariable("WORDNET_ROADMAP_EVIDENCE");
        if (output != null)
        { Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "bound-chart.docx"), saved); File.WriteAllBytes(Path.Combine(output, "bound-chart.pdf"), pdfBytes); File.WriteAllBytes(Path.Combine(output, "bound-chart.svg"), svg); }
    }

    [Fact]
    public void NullGapsZeroAndNegativeNumbersAreDistinctAndBindingsAreAtomic()
    {
        WordDocument document = Document(); DocumentObject original = document.Objects[0];
        ReportData data = Data(new[] { new { Category = "Gap", Value = (decimal?)null }, new { Category = "Zero", Value = (decimal?)0 }, new { Category = "Negative", Value = (decimal?)-5 } });
        DocumentObject updated = document.BindChart(original, data, Binding(), TestContext.Current.CancellationToken);
        Assert.Equal(new double?[] { null, 0, -5 }, updated.Chart!.Series[0].Values);
        byte[] before = Save(document);
        Assert.Throws<ArgumentException>(() => document.BindChart(original, data, Binding(), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => document.BindChart(updated, data, Binding("Missing"), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => document.BindChart(updated, Data(new[] { new { Category = "A", Value = "not numeric" } }), Binding(), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => document.BindChart(updated, Data(Array.Empty<object>()), Binding(), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.BindChart(updated, data, Binding(), cancellation.Token));
        Assert.Equal(before, Save(document));
        Assert.Throws<ArgumentException>(() => Document().BindChart(updated, data, Binding(), TestContext.Current.CancellationToken));
    }
    [Fact]
    public void OversizedProjectionAndWrongSeriesCountLeaveTheChartUnchanged()
    {
        WordDocument document = Document(); DocumentObject handle = document.Objects[0]; byte[] before = Save(document);
        ReportData large = ReportData.Prepare(new { Name = "Revenue", Rows = Enumerable.Range(0, 10001).Select(i => new { Category = "C", Value = i }).ToArray() },
            new ReportPlan(), new ReportOptions { MaximumRows = 20000 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.BindChart(handle, large, Binding(), TestContext.Current.CancellationToken));
        var mismatch = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "Value"), new ChartSeriesBinding("Name", "Value") });
        Assert.Throws<InvalidDataException>(() => document.BindChart(handle, Data(new[] { new { Category = "C", Value = 1 } }), mismatch, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

}
