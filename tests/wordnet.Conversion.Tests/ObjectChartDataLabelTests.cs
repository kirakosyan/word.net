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
    private static readonly string[] LabelCategories = { "First", "Second" };
    private static readonly string[] CombinedPointLabels = { "First, 30", "Second, -20" };
    private static readonly double?[] LabelValues = { 30, -20 };
    private static ChartDefinition LabelChart(ChartKind kind, ChartDataLabels labels) => new ChartDefinition(kind, LabelCategories,
        new[] { new ChartSeries("North", "2468AC", LabelValues) }, showLegend: false).WithDataLabels(labels);

    [Theory]
    [InlineData(ChartKind.ClusteredColumn)]
    [InlineData(ChartKind.ClusteredBar)]
    [InlineData(ChartKind.StackedColumn)]
    [InlineData(ChartKind.StackedBar)]
    public void ChartLabelsAreEditableAndCenteredInsideBothSignedBars(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        ChartDefinition source = LabelChart(kind, ChartDataLabels.CategoryAndValue);
        doc.AddChart(source, width: 360, height: 240, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var parsed = Read(saved); Assert.True(parsed.Renderable); Assert.Equal(ChartDataLabels.CategoryAndValue, parsed.Definition.DataLabels);
        Assert.Equal(LabelValues, parsed.Definition.Series[0].Values);
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/charts/chart1.xml")));
        XElement labels = xml.Descendants(C + "dLbls").Single(); Assert.Equal("ctr", (string?)labels.Element(C + "dLblPos")!.Attribute("val"));
        byte[] perSeries = Xml(saved, "word/charts/chart1.xml", chart =>
        {
            XElement pointLabels = chart.Descendants(C + "dLbls").Single(); pointLabels.Remove();
            chart.Descendants(C + "ser").Single().Element(C + "cat")!.AddBeforeSelf(pointLabels);
        });
        Assert.True(Read(perSeries).Renderable); Assert.Equal(ChartDataLabels.CategoryAndValue, Read(perSeries).Definition.DataLabels);
        using var input = new MemoryStream(saved); var context = Context(options);
        FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        foreach (string text in CombinedPointLabels)
        {
            PositionedText label = page.Text.Single(item => item.Text == text); Assert.Equal("FFFFFF", label.Color);
            Assert.Contains(page.Shapes, rectangle => rectangle.Color == "2468AC" && label.X > rectangle.X && label.X < rectangle.X + rectangle.Width
                && label.Y > rectangle.Y && label.Y < rectangle.Y + rectangle.Height);
        }
        using var pdf = new MemoryStream(); ConversionResult result = doc.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Chart.DataLabelApproximation");
        using var reader = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray()); Assert.Contains("First, 30", reader.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChartDataLabels.Value)]
    [InlineData(ChartDataLabels.Category)]
    [InlineData(ChartDataLabels.CategoryAndValue)]
    public void ChartLabelsSurviveBindingWithoutChangingSource(ChartDataLabels labels)
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        DocumentObject source = doc.AddChart(LabelChart(ChartKind.ClusteredColumn, labels), cancellationToken: TestContext.Current.CancellationToken);
        ReportData data = ReportData.Prepare(new { Name = "Changed", Rows = new[] { new { Category = "East", A = 18 }, new { Category = "West", A = 25 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "A") });
        DocumentObject result = doc.BindChart(source, data, binding, TestContext.Current.CancellationToken);
        Assert.Equal(labels, result.Chart!.DataLabels); Assert.Equal(LabelValues, source.Chart!.Series[0].Values);
        Assert.Equal(labels, Assert.Single(LoadObjects(SaveObjects(doc)).Objects).Chart!.DataLabels);
    }

    [Fact]
    public void ChartLabelsRejectUnknownAppearanceAndUnfittableOrUnqualifiedOutput()
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(LabelChart(ChartKind.ClusteredColumn, ChartDataLabels.Value), width: 360, height: 240, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc);
        byte[] outside = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dLblPos").Single().SetAttributeValue("val", "outEnd"));
        Assert.False(Read(outside).Renderable);
        byte[] implicitFlags = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "showSerName").Single().Remove());
        Assert.False(Read(implicitFlags).Renderable);
        WordDocument imported = LoadObjects(outside); DocumentObject objectHandle = Assert.Single(imported.Objects);
        ReportData data = ReportData.Prepare(new { Name = "Changed", Rows = new[] { new { Category = "East", A = 18 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "A") });
        Assert.Throws<NotSupportedException>(() => imported.BindChart(objectHandle, data, binding, TestContext.Current.CancellationToken));
        using (var input = new MemoryStream(saved))
        {
            var strict = Context(new ConversionOptions(FidelityMode.Strict));
            var failure = Assert.Throws<NotSupportedException>(() => FixedLayout.Create(DocxReader.Read(input, strict), strict));
            Assert.StartsWith("Chart.DataLabelApproximation:", failure.Message, StringComparison.Ordinal);
        }
        var zeros = new ChartDefinition(ChartKind.ClusteredColumn, LabelCategories, new[] { new ChartSeries("Zero", "2468AC", new double?[] { 0, 1 }) }, showLegend: false).WithDataLabels(ChartDataLabels.Value);
        var zeroDocument = WordDocument.Create(options); zeroDocument.AddChart(zeros, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        var fit = Assert.Throws<NotSupportedException>(() => zeroDocument.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Chart.DataLabelFit:", fit.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.Throws<NotSupportedException>(() => new ChartDefinition(ChartKind.Line, LabelCategories, new[] { new ChartSeries("Line", "2468AC", LabelValues) }).WithDataLabels(ChartDataLabels.Value));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelChart(ChartKind.ClusteredColumn, (ChartDataLabels)42));
        for (int mode = 0; mode < 3; mode++)
        {
            var profile = new ConversionOptions(FidelityMode.Lenient);
            if (mode == 0) { profile.Pdf.Accessibility = PdfAccessibility.PdfUa1; profile.Pdf.Language = "en"; }
            if (mode == 1) profile.Pdf.Conformance = PdfConformance.PdfA2b;
            if (mode == 2) profile.Typography.Enabled = true;
            using var protectedOutput = new MemoryStream(); protectedOutput.WriteByte(42);
            Assert.Throws<NotSupportedException>(() => doc.Save(protectedOutput, DocumentFormat.Pdf, profile, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, protectedOutput.ToArray());
        }
    }

    [Fact]
    public void ChartLabelsChargeAggregateTextAndHonorCancellation()
    {
        ChartDefinition definition = LabelChart(ChartKind.ClusteredColumn, ChartDataLabels.CategoryAndValue);
        var graph = ChartObjectFormat.Create(definition, Context(new ConversionOptions(FidelityMode.Lenient)));
        var item = new DocumentObjectData(definition, graph, 360, 240, "Bounded labels");
        var limited = Context(new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 150));
        var error = Assert.Throws<InvalidDataException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, limited));
        Assert.StartsWith("Object.LayoutLimit:", error.Message, StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var context = new WordNet.Internal.ConversionContext(new ConversionOptions(FidelityMode.Lenient), cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, context));
    }

    [Fact]
    public void ImportedSeriesLabelsMustBeUniformAndExplicit()
    {
        var definition = new ChartDefinition(ChartKind.ClusteredColumn, LabelCategories,
            new[] { new ChartSeries("North", "2468AC", LabelValues), new ChartSeries("South", "C03040", LabelValues) }, showLegend: false)
            .WithDataLabels(ChartDataLabels.CategoryAndValue);
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        doc.AddChart(definition, cancellationToken: TestContext.Current.CancellationToken);
        byte[] uniform = Xml(SaveObjects(doc), "word/charts/chart1.xml", chart =>
        {
            XElement labels = chart.Descendants(C + "dLbls").Single(); labels.Remove();
            foreach (XElement series in chart.Descendants(C + "ser")) series.Element(C + "cat")!.AddBeforeSelf(new XElement(labels));
        });
        Assert.True(Read(uniform).Renderable); Assert.Equal(ChartDataLabels.CategoryAndValue, Read(uniform).Definition.DataLabels);
        byte[] nonuniform = Xml(uniform, "word/charts/chart1.xml", chart => chart.Descendants(C + "showVal").Last().SetAttributeValue("val", "0"));
        Assert.False(Read(nonuniform).Renderable);
        byte[] missing = Xml(uniform, "word/charts/chart1.xml", chart => chart.Descendants(C + "dLbls").Last().Remove());
        Assert.False(Read(missing).Renderable);
    }
}
