using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordNet.Formats;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static readonly double?[] BarValues = { 8, -4, null, 0 };
    private static readonly double?[] BarSecondValues = { -4, 2, 3, null };
    private static ChartDefinition Bars() => new ChartDefinition(ChartKind.ClusteredBar, Categories,
        new[] { new ChartSeries("North", "2468AC", BarValues), new ChartSeries("South", "C03040", BarSecondValues) }, "Bars", showLegend: false);

    [Fact]
    public void HorizontalBarsKeepEditableDataAndRenderSignedVectorGeometry()
    {
        var options = new ConversionOptions(FidelityMode.BestEffort); var doc = WordDocument.Create(options);
        doc.AddChart(Bars(), width: 320, height: 210, description: "Signed values", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var parsed = Read(saved); Assert.True(parsed.Renderable); Assert.Equal(ChartKind.ClusteredBar, parsed.Definition.Kind);
        Assert.Equal(BarValues, parsed.Definition.Series[0].Values); Assert.Equal(BarSecondValues, parsed.Definition.Series[1].Values);
        XDocument chart = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/charts/chart1.xml")));
        Assert.Equal("bar", (string?)chart.Descendants(C + "barDir").Single().Attribute("val"));
        Assert.Equal("clustered", (string?)chart.Descendants(C + "grouping").Single().Attribute("val"));
        Assert.Equal("l", (string?)chart.Descendants(C + "catAx").Single().Element(C + "axPos")!.Attribute("val"));
        Assert.Equal("b", (string?)chart.Descendants(C + "valAx").Single().Element(C + "axPos")!.Attribute("val"));
        using var input = new MemoryStream(saved); var context = Context(options); DocumentModel model = DocxReader.Read(input, context);
        var page = Assert.Single(FixedLayout.Create(model, context));
        var north = page.Shapes.Where(shape => shape.Color == "2468AC" && shape.Stroke == 0).OrderBy(shape => shape.Y).ToArray();
        Assert.Equal(3, north.Length); Assert.Contains(north, shape => shape.Width == 0);
        var positive = north.Single(shape => shape.Width > 0 && shape.Width == north.Max(item => item.Width));
        var negative = north.Single(shape => shape.Width > 0 && shape.Width < positive.Width);
        Assert.Equal(2, positive.Width / negative.Width, 6); Assert.Equal(positive.X, negative.X + negative.Width, 6);
        Assert.True(positive.Y > negative.Y);
        Assert.True(page.Text.Single(item => item.Text == "First").X > negative.X);
        double slot = Math.Abs(page.Text.Single(item => item.Text == "First").Y - page.Text.Single(item => item.Text == "Second").Y);
        Assert.Equal(slot / 3.5, positive.Height, 6);
        var southFirst = page.Shapes.Single(shape => shape.Color == "C03040" && shape.Stroke == 0 && shape.Width > 0
            && Math.Abs(shape.X + shape.Width - positive.X) < 0.0001);
        Assert.True(southFirst.Y < positive.Y);
        using var pdf = new MemoryStream(); ConversionResult rendered = doc.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(rendered.Diagnostics, diagnostic => diagnostic.Code == "Chart.BarLayoutApproximation");
        using (var reader = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray())) Assert.Contains("First", reader.GetPage(1).Text, StringComparison.Ordinal);
        using var xps = new MemoryStream(); doc.Save(xps, DocumentFormat.Xps, options, TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), element => ((string?)element.Attribute("Fill"))?.EndsWith("2468AC", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("#2468AC", Encoding.UTF8.GetString(SvgPreview.Write(page, context)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HorizontalBarBindingRetainsKindAndImmutableSource()
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.BestEffort)); DocumentObject source = doc.AddChart(Bars(), cancellationToken: TestContext.Current.CancellationToken);
        ReportData data = ReportData.Prepare(new { Name = "Updated", Rows = new[] { new { Category = "East", A = 4, B = 3 }, new { Category = "West", A = -2, B = 1 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "A"), new ChartSeriesBinding("Name", "B") });
        DocumentObject result = doc.BindChart(source, data, binding, TestContext.Current.CancellationToken);
        Assert.Equal(ChartKind.ClusteredBar, result.Chart!.Kind); Assert.Equal(BarValues, source.Chart!.Series[0].Values);
        Assert.Equal(new double?[] { 4, -2 }, result.Chart.Series[0].Values);
        Assert.Equal(ChartKind.ClusteredBar, Assert.Single(LoadObjects(SaveObjects(doc)).Objects).Chart!.Kind);
    }

    [Fact]
    public void HorizontalBarsRejectUnqualifiedProfilesAndUnsupportedChartForms()
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(Bars(), description: "Signed values", cancellationToken: TestContext.Current.CancellationToken); byte[] saved = SaveObjects(doc);
        byte[] stacked = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "grouping").Single().SetAttributeValue("val", "stacked"));
        Assert.Throws<NotSupportedException>(() => Read(stacked));
        byte[] rotatedAxis = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "catAx").Single().Element(C + "axPos")!.SetAttributeValue("val", "b"));
        Assert.False(Read(rotatedAxis).Renderable);
        byte[] customGap = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "barChart").Single().Elements(C + "axId").First().AddBeforeSelf(new XElement(C + "gapWidth", new XAttribute("val", "200"))));
        Assert.False(Read(customGap).Renderable);
        using (var strictOutput = new MemoryStream())
        {
            strictOutput.WriteByte(42);
            var failure = Assert.Throws<NotSupportedException>(() => doc.Save(strictOutput, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken));
            Assert.StartsWith("Object.EditableOutputLoss:", failure.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, strictOutput.ToArray());
        }
        using (var strictInput = new MemoryStream(saved))
        {
            var strictContext = Context(new ConversionOptions(FidelityMode.Strict)); DocumentModel model = DocxReader.Read(strictInput, strictContext);
            var failure = Assert.Throws<NotSupportedException>(() => FixedLayout.Create(model, strictContext));
            Assert.StartsWith("Chart.BarLayoutApproximation:", failure.Message, StringComparison.Ordinal);
        }
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var outputOptions = new ConversionOptions(FidelityMode.Lenient);
            if (mode == 0) { outputOptions.Pdf.Accessibility = PdfAccessibility.PdfUa1; outputOptions.Pdf.Language = "en"; }
            if (mode == 1) outputOptions.Pdf.Conformance = PdfConformance.PdfA2b;
            if (mode == 2) outputOptions.Typography.Enabled = true;
            using var output = new MemoryStream(); output.WriteByte(42);
            Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Pdf, outputOptions, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
    }
}
