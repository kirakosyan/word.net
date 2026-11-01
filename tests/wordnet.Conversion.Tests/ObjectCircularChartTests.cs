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
    private static readonly string[] CircularColors = { "2468AC", "C03040", "80B060", "A0A0A0", "707070" };
    private static ChartSlice[] CircularSlices() => new[]
    {
        new ChartSlice("North", 3, "2468AC"), new ChartSlice("South", 2, "C03040"),
        new ChartSlice("East", 1, "80B060"), new ChartSlice("West", null, "A0A0A0"), new ChartSlice("Zero", 0, "707070"),
    };

    [Theory]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)]
    public void CircularChartsKeepEditableDataAndAnalyticVectorSectors(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        ChartDefinition definition = ChartDefinition.CreateCircular(kind, CircularSlices(), "Total", "Distribution", showLegend: false);
        doc.AddChart(definition, width: 320, height: 240, description: "Distribution", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var parsed = Read(saved); Assert.True(parsed.Renderable); Assert.Equal(kind, parsed.Definition.Kind);
        Assert.Equal(CircularColors, parsed.Definition.SliceColors);
        Assert.Equal(new double?[] { 3, 2, 1, null, 0 }, parsed.Definition.Series[0].Values);
        using var input = new MemoryStream(saved); var context = Context(options);
        FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        FixedShape[] sectors = page.Shapes.Where(shape => shape.Geometry == "sector").ToArray(); Assert.Equal(3, sectors.Length);
        Assert.Equal(-Math.PI / 2, sectors[0].SectorStart, 8);
        Assert.Equal(Math.PI, sectors[0].SectorSweep, 8); Assert.Equal(Math.PI * 2 / 3, sectors[1].SectorSweep, 8);
        Assert.Equal(Math.PI / 3, sectors[2].SectorSweep, 8); Assert.Equal(2 * Math.PI, sectors.Sum(shape => shape.SectorSweep), 8);
        Assert.All(sectors, shape => Assert.Equal(kind == ChartKind.Doughnut ? 0.5 : 0, shape.SectorHole));
        Assert.All(sectors, shape => { var path = VectorPath.Create(shape); Assert.Equal('M', path[0].Kind); Assert.Equal('Z', path[path.Count - 1].Kind); Assert.Contains(path, command => command.Kind == 'C'); });
        using var pdf = new MemoryStream(); ConversionResult result = doc.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Chart.CircularLayoutApproximation");
        using var xps = new MemoryStream(); doc.Save(xps, DocumentFormat.Xps, options, TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), element => ((string?)element.Attribute("Fill"))?.EndsWith("2468AC", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("#2468AC", Encoding.UTF8.GetString(SvgPreview.Write(page, context)), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)]
    public void CircularBindingPreservesColoursAndSourceSnapshot(ChartKind kind)
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        DocumentObject source = doc.AddChart(ChartDefinition.CreateCircular(kind, CircularSlices(), "Before", showLegend: false), cancellationToken: TestContext.Current.CancellationToken);
        ReportData data = ReportData.Prepare(new { Name = "After", Rows = new[] { new { Category = "A", Value = 8 }, new { Category = "B", Value = 7 }, new { Category = "C", Value = 6 }, new { Category = "D", Value = 5 }, new { Category = "E", Value = 4 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "Value") });
        DocumentObject updated = doc.BindChart(source, data, binding, TestContext.Current.CancellationToken);
        Assert.Equal(kind, updated.Chart!.Kind); Assert.Equal(source.Chart!.SliceColors, updated.Chart.SliceColors);
        Assert.Equal(new double?[] { 3, 2, 1, null, 0 }, source.Chart.Series[0].Values);
        Assert.Equal(new double?[] { 8, 7, 6, 5, 4 }, updated.Chart.Series[0].Values);
        Assert.Equal(kind, Assert.Single(LoadObjects(SaveObjects(doc)).Objects).Chart!.Kind);
    }

    [Fact]
    public void CircularChartsRejectUnsupportedValuesAppearancesAndProfiles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChartSlice("A", -1, "2468AC"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChartSlice("A", double.NaN, "2468AC"));
        Assert.Throws<ArgumentException>(() => ChartDefinition.CreateCircular(ChartKind.Pie, Enumerable.Repeat(new ChartSlice("A", 1, "2468AC"), 65)));
        ChartDefinition definition = ChartDefinition.CreateCircular(ChartKind.Pie, CircularSlices(), showLegend: false);
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(definition, cancellationToken: TestContext.Current.CancellationToken); byte[] saved = SaveObjects(doc);
        byte[] changedAngle = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "firstSliceAng").Single().SetAttributeValue("val", "45"));
        Assert.False(Read(changedAngle).Renderable);
        byte[] absentColor = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dPt").Last().Remove());
        Assert.False(Read(absentColor).Renderable);
        XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        byte[] implicitOutline = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dPt").First().Descendants(drawing + "ln").Single().Remove());
        Assert.False(Read(implicitOutline).Renderable);
        byte[] repeatedPoint = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dPt").Last().Element(C + "idx")!.SetAttributeValue("val", "0"));
        Assert.Throws<InvalidDataException>(() => Read(repeatedPoint));
        byte[] exploded = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dPt").First().Add(new XElement(C + "explosion", new XAttribute("val", "5"))));
        Assert.False(Read(exploded).Renderable);
        using (var input = new MemoryStream(saved))
        {
            var strict = Context(new ConversionOptions(FidelityMode.Strict));
            var error = Assert.Throws<NotSupportedException>(() => FixedLayout.Create(DocxReader.Read(input, strict), strict));
            Assert.StartsWith("Chart.CircularLayoutApproximation:", error.Message, StringComparison.Ordinal);
        }
        var empty = WordDocument.Create(options); empty.AddChart(ChartDefinition.CreateCircular(ChartKind.Doughnut, new[] { new ChartSlice("A", 0, "2468AC") }), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        var noData = Assert.Throws<NotSupportedException>(() => empty.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Chart.EmptyData:", noData.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var profile = new ConversionOptions(FidelityMode.Lenient);
            if (mode == 0) { profile.Pdf.Accessibility = PdfAccessibility.PdfUa1; profile.Pdf.Language = "en"; }
            if (mode == 1) profile.Pdf.Conformance = PdfConformance.PdfA2b;
            if (mode == 2) profile.Typography.Enabled = true;
            using var protectedOutput = new MemoryStream(); protectedOutput.WriteByte(42);
            Assert.Throws<NotSupportedException>(() => doc.Save(protectedOutput, DocumentFormat.Pdf, profile, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, protectedOutput.ToArray());
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => doc.Save(new MemoryStream(), DocumentFormat.Pdf, options, cancelled.Token));
    }

    [Fact]
    public void CircularSectorsNormalizeExtremeValuesAndRejectInvisibleSlicesOrExhaustedWork()
    {
        var huge = ChartDefinition.CreateCircular(ChartKind.Pie, new[] { new ChartSlice("A", double.MaxValue, "2468AC"), new ChartSlice("B", double.MaxValue, "C03040") }, showLegend: false);
        var options = new ConversionOptions(FidelityMode.Lenient); var graph = ChartObjectFormat.Create(huge, Context(options));
        var item = new DocumentObjectData(huge, graph, 320, 240, "Extreme values");
        ObjectVisual visual = ChartLayout.Create(item, 0, (_, _, _) => 1, Context(options), true);
        Assert.Equal(2, visual.Shapes.Count); Assert.All(visual.Shapes, shape => Assert.Equal(Math.PI, shape.SectorSweep, 8));
        var limited = Context(new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 32));
        var exhausted = Assert.Throws<InvalidDataException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, limited, true));
        Assert.StartsWith("Object.LayoutLimit:", exhausted.Message, StringComparison.Ordinal);
        var tiny = ChartDefinition.CreateCircular(ChartKind.Doughnut, new[] { new ChartSlice("A", double.MaxValue, "2468AC"), new ChartSlice("B", double.Epsilon, "C03040") }, showLegend: false);
        var doc = WordDocument.Create(options); doc.AddChart(tiny, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        var spacing = Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Chart.SliceSpacing:", spacing.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void CircularUpdateAndBindingKeepSnapshotsAndRejectLossyData()
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        DocumentObject source = doc.AddChart(ChartDefinition.CreateCircular(ChartKind.Pie, CircularSlices(), "Before", showLegend: false), cancellationToken: TestContext.Current.CancellationToken);
        byte[] before = SaveObjects(doc);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "Value") });
        ReportData tooFew = ReportData.Prepare(new { Name = "After", Rows = new[] { new { Category = "A", Value = 2 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => doc.BindChart(source, tooFew, binding, TestContext.Current.CancellationToken));
        ReportData negative = ReportData.Prepare(new { Name = "After", Rows = new[] { new { Category = "A", Value = -1 }, new { Category = "B", Value = 1 }, new { Category = "C", Value = 1 }, new { Category = "D", Value = 1 }, new { Category = "E", Value = 1 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => doc.BindChart(source, negative, binding, TestContext.Current.CancellationToken));
        Assert.Equal(before, SaveObjects(doc));
        DocumentObject updated = doc.UpdateChart(source, ChartDefinition.CreateCircular(ChartKind.Doughnut, CircularSlices(), "After", showLegend: false), TestContext.Current.CancellationToken);
        Assert.Equal(ChartKind.Pie, source.Chart!.Kind); Assert.Equal(ChartKind.Doughnut, updated.Chart!.Kind);
        Assert.Equal(CircularColors, updated.Chart.SliceColors); Assert.Equal(ChartKind.Doughnut, Assert.Single(LoadObjects(SaveObjects(doc)).Objects).Chart!.Kind);
        Assert.Throws<ArgumentException>(() => doc.UpdateChart(source, source.Chart, TestContext.Current.CancellationToken));
    }
}
