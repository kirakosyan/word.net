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
    private static readonly string[] SingleStackCategory = { "A" };
    private static ChartDefinition Stacks(ChartKind kind) => new ChartDefinition(kind, Categories,
        new[] { new ChartSeries("North", "2468AC", new double?[] { 8, -4, null, 0 }), new ChartSeries("South", "C03040", new double?[] { 4, -2, 3, null }) },
        "Stacked", showLegend: false);

    [Theory]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void StackedChartsRetainEditableDataAndSignedVectorGeometry(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.BestEffort); var doc = WordDocument.Create(options);
        doc.AddChart(Stacks(kind), width: 320, height: 210, description: "Signed stacks", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var parsed = Read(saved); Assert.True(parsed.Renderable); Assert.Equal(kind, parsed.Definition.Kind);
        Assert.Equal(new double?[] { 8, -4, null, 0 }, parsed.Definition.Series[0].Values);
        Assert.Equal(new double?[] { 4, -2, 3, null }, parsed.Definition.Series[1].Values);
        XDocument chart = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/charts/chart1.xml")));
        Assert.Equal("stacked", (string?)chart.Descendants(C + "grouping").Single().Attribute("val"));
        Assert.Equal("100", (string?)chart.Descendants(C + "overlap").Single().Attribute("val"));
        Assert.Equal(kind == ChartKind.StackedBar ? "bar" : "col", (string?)chart.Descendants(C + "barDir").Single().Attribute("val"));
        using var input = new MemoryStream(saved); var context = Context(options); DocumentModel model = DocxReader.Read(input, context);
        FixedPage page = Assert.Single(FixedLayout.Create(model, context));
        var north = page.Shapes.Where(shape => shape.Color == "2468AC" && shape.Stroke == 0).ToArray();
        var south = page.Shapes.Where(shape => shape.Color == "C03040" && shape.Stroke == 0).ToArray();
        Assert.Equal(3, north.Length); Assert.Equal(3, south.Length);
        if (kind == ChartKind.StackedBar)
        {
            var positive = north.Single(shape => shape.Width == north.Max(item => item.Width));
            var negative = north.Single(shape => shape.Width > 0 && shape.Width < positive.Width);
            var positiveNext = south.Single(shape => Math.Abs(shape.Y - positive.Y) < 0.0001);
            var negativeNext = south.Single(shape => Math.Abs(shape.Y - negative.Y) < 0.0001);
            Assert.Equal(positive.X + positive.Width, positiveNext.X, 6);
            Assert.Equal(negative.X, negativeNext.X + negativeNext.Width, 6);
            Assert.Equal(2, positive.Width / positiveNext.Width, 6); Assert.Equal(2, negative.Width / negativeNext.Width, 6);
        }
        else
        {
            var positive = north.Single(shape => shape.Height == north.Max(item => item.Height));
            var negative = north.Single(shape => shape.Height > 0 && shape.Height < positive.Height);
            var positiveNext = south.Single(shape => Math.Abs(shape.X - positive.X) < 0.0001);
            var negativeNext = south.Single(shape => Math.Abs(shape.X - negative.X) < 0.0001);
            Assert.Equal(positive.Y, positiveNext.Y + positiveNext.Height, 6);
            Assert.Equal(negative.Y + negative.Height, negativeNext.Y, 6);
            Assert.Equal(2, positive.Height / positiveNext.Height, 6); Assert.Equal(2, negative.Height / negativeNext.Height, 6);
        }
        using var pdf = new MemoryStream(); ConversionResult result = doc.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Chart.StackedLayoutApproximation");
        using (var reader = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray())) Assert.Contains("First", reader.GetPage(1).Text, StringComparison.Ordinal);
        using var xps = new MemoryStream(); doc.Save(xps, DocumentFormat.Xps, options, TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), e => ((string?)e.Attribute("Fill"))?.EndsWith("2468AC", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("#2468AC", Encoding.UTF8.GetString(SvgPreview.Write(page, context)), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void StackedChartBindingKeepsImmutableSourceAndWorkbook(ChartKind kind)
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.BestEffort));
        DocumentObject source = doc.AddChart(Stacks(kind), cancellationToken: TestContext.Current.CancellationToken);
        ReportData data = ReportData.Prepare(new { Name = "Updated", Rows = new[] { new { Category = "East", A = 4, B = 3 }, new { Category = "West", A = -2, B = 1 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "A"), new ChartSeriesBinding("Name", "B") });
        DocumentObject result = doc.BindChart(source, data, binding, TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Chart!.Kind); Assert.Equal(new double?[] { 8, -4, null, 0 }, source.Chart!.Series[0].Values);
        Assert.Equal(new double?[] { 4, -2 }, result.Chart.Series[0].Values);
        Assert.Equal(kind, Assert.Single(LoadObjects(SaveObjects(doc)).Objects).Chart!.Kind);
    }

    [Theory]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void StackedLegendPositionFollowsTheAdmittedOfficeOrder(ChartKind kind)
    {
        ChartDefinition original = Stacks(kind);
        var legend = new ChartDefinition(kind, original.Categories, original.Series, original.Title, showLegend: true);
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(legend, width: 320, height: 210, cancellationToken: TestContext.Current.CancellationToken);
        using var source = new MemoryStream(SaveObjects(doc)); var context = Context(options);
        FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(source, context), context));
        double north = page.Text.Single(t => t.Text == "North").Y, south = page.Text.Single(t => t.Text == "South").Y;
        Assert.Equal(kind == ChartKind.StackedBar, north < south);
    }

    [Fact]
    public void StackedChartRejectsUnqualifiedOverlapAndNonFiniteTotals()
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(Stacks(ChartKind.StackedColumn), cancellationToken: TestContext.Current.CancellationToken); byte[] saved = SaveObjects(doc);
        byte[] overlap = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "overlap").Single().SetAttributeValue("val", "0"));
        Assert.Throws<NotSupportedException>(() => Read(overlap));
        byte[] percent = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "grouping").Single().SetAttributeValue("val", "percentStacked"));
        Assert.Throws<NotSupportedException>(() => Read(percent));
        var tooLarge = new ChartDefinition(ChartKind.StackedColumn, SingleStackCategory,
            new[] { new ChartSeries("A", "2468AC", new double?[] { double.MaxValue }), new ChartSeries("B", "C03040", new double?[] { double.MaxValue }) });
        var overflow = WordDocument.Create(options); overflow.AddChart(tooLarge, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => overflow.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void StackedChartsRejectStrictAndUnqualifiedProfilesBeforeOutput(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(Stacks(kind), description: "Signed stacks", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        using (var source = new MemoryStream(saved))
        {
            var context = Context(new ConversionOptions(FidelityMode.Strict)); DocumentModel model = DocxReader.Read(source, context);
            var error = Assert.Throws<NotSupportedException>(() => FixedLayout.Create(model, context));
            Assert.StartsWith("Chart.StackedLayoutApproximation:", error.Message, StringComparison.Ordinal);
        }
        for (int mode = 0; mode < 4; mode++)
        {
            var outputOptions = new ConversionOptions(mode == 0 ? FidelityMode.Strict : FidelityMode.Lenient);
            if (mode == 1) { outputOptions.Pdf.Accessibility = PdfAccessibility.PdfUa1; outputOptions.Pdf.Language = "en"; }
            if (mode == 2) outputOptions.Pdf.Conformance = PdfConformance.PdfA2b;
            if (mode == 3) outputOptions.Typography.Enabled = true;
            using var output = new MemoryStream(); output.WriteByte(42);
            Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Pdf, outputOptions, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
    }

    [Theory]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void EmptyAndZeroStacksHaveExplicitBehavior(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.Lenient);
        var empty = new ChartDefinition(kind, SingleStackCategory, new[] { new ChartSeries("None", "2468AC", new double?[] { null }) }, showLegend: false);
        var emptyDocument = WordDocument.Create(options); emptyDocument.AddChart(empty, cancellationToken: TestContext.Current.CancellationToken);
        using (var output = new MemoryStream())
        {
            output.WriteByte(42);
            var error = Assert.Throws<NotSupportedException>(() => emptyDocument.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.StartsWith("Chart.EmptyData:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        var zero = new ChartDefinition(kind, SingleStackCategory, new[] { new ChartSeries("Zero", "2468AC", new double?[] { 0 }) }, showLegend: false);
        var zeroDocument = WordDocument.Create(options); zeroDocument.AddChart(zero, cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = new MemoryStream(); zeroDocument.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using var reader = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray()); Assert.Contains("A", reader.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void StackedAccumulatorsRespectResourceAndCancellationBounds()
    {
        ChartDefinition definition = Stacks(ChartKind.StackedColumn);
        var graph = ChartObjectFormat.Create(definition, Context(new ConversionOptions(FidelityMode.Lenient)));
        var item = new DocumentObjectData(definition, graph, 320, 210, "Bounded stacks");
        var limited = Context(new ConversionOptions(FidelityMode.Lenient, maximumExpandedBytes: 64));
        var error = Assert.Throws<InvalidDataException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, limited));
        Assert.StartsWith("Limit.ChartStack:", error.Message, StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var context = new WordNet.Internal.ConversionContext(new ConversionOptions(FidelityMode.Lenient), cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, context));
    }
}
