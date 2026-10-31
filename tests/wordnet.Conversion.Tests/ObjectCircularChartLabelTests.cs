using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    private static readonly string[] CircularPointLabels = { "East, 30", "West, 30" };
    private static ChartDefinition LabelledCircular(ChartKind kind) => ChartDefinition.CreateCircular(kind,
        new[] { new ChartSlice("East", 30, "2468AC"), new ChartSlice("West", 30, "C03040") }, "Total", showLegend: false)
        .WithDataLabels(ChartDataLabels.CategoryAndValue);

    [Theory]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)]
    public void CircularPointLabelsStayEditableAndFitTheirSectors(ChartKind kind)
    {
        var options = new ConversionOptions(FidelityMode.Lenient); var document = WordDocument.Create(options);
        ChartDefinition original = LabelledCircular(kind);
        document.AddChart(original, width: 360, height: 260, description: "Labelled shares", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(document);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var parsed = Read(saved); Assert.True(parsed.Renderable); Assert.Equal(kind, parsed.Definition.Kind);
        Assert.Equal(ChartDataLabels.CategoryAndValue, parsed.Definition.DataLabels);
        XDocument chart = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/charts/chart1.xml")));
        Assert.Equal("ctr", (string?)chart.Descendants(C + "dLblPos").Single().Attribute("val"));
        using var input = new MemoryStream(saved); var context = Context(options);
        FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        foreach (string text in CircularPointLabels)
        {
            PositionedText label = page.Text.Single(item => item.Text == text); Assert.Equal("FFFFFF", label.Color);
            Assert.Contains(page.Shapes, shape => shape.Geometry == "sector" && label.X > shape.X && label.X < shape.X + shape.Width && label.Y > shape.Y && label.Y < shape.Y + shape.Height);
        }
        using var pdf = new MemoryStream(); ConversionResult result = document.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Chart.DataLabelApproximation");
        Assert.Contains(result.Diagnostics, d => d.Code == "Chart.CircularLayoutApproximation");
        using var reader = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray()); Assert.Contains("East, 30", reader.GetPage(1).Text, StringComparison.Ordinal);
        byte[] perSeries = Xml(saved, "word/charts/chart1.xml", xml =>
        {
            XElement labels = xml.Descendants(C + "dLbls").Single(); labels.Remove(); xml.Descendants(C + "ser").Single().Element(C + "cat")!.AddBeforeSelf(labels);
        });
        Assert.True(Read(perSeries).Renderable); Assert.Equal(ChartDataLabels.CategoryAndValue, Read(perSeries).Definition.DataLabels);
    }

    [Theory]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)]
    public void CircularPointLabelsSurvivePreparedDataBinding(ChartKind kind)
    {
        var document = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        DocumentObject source = document.AddChart(LabelledCircular(kind), cancellationToken: TestContext.Current.CancellationToken);
        ReportData data = ReportData.Prepare(new { Name = "After", Rows = new[] { new { Category = "A", Value = 20 }, new { Category = "B", Value = 25 } } }, new ReportPlan(), cancellationToken: TestContext.Current.CancellationToken);
        var binding = new ChartDataBinding("Rows", "Category", new[] { new ChartSeriesBinding("Name", "Value") });
        DocumentObject updated = document.BindChart(source, data, binding, TestContext.Current.CancellationToken);
        Assert.Equal(ChartDataLabels.CategoryAndValue, updated.Chart!.DataLabels);
        Assert.Equal(ChartDataLabels.CategoryAndValue, Assert.Single(LoadObjects(SaveObjects(document)).Objects).Chart!.DataLabels);
        Assert.Equal(new double?[] { 30, 30 }, source.Chart!.Series[0].Values);
    }

    [Fact]
    public void CircularPointLabelsRejectUnfittableAndUnqualifiedCasesBeforeOutput()
    {
        var options = new ConversionOptions(FidelityMode.Lenient);
        ChartDefinition zero = ChartDefinition.CreateCircular(ChartKind.Pie, new[] { new ChartSlice("A", 1, "2468AC"), new ChartSlice("Zero", 0, "C03040") }, showLegend: false).WithDataLabels(ChartDataLabels.Value);
        var document = WordDocument.Create(options); document.AddChart(zero, cancellationToken: TestContext.Current.CancellationToken);
        using (var output = new MemoryStream())
        {
            output.WriteByte(42); var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.StartsWith("Chart.DataLabelFit:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        ChartDefinition narrow = ChartDefinition.CreateCircular(ChartKind.Doughnut, new[] { new ChartSlice("A very wide label", 1, "2468AC"), new ChartSlice("Large", 99, "C03040") }, showLegend: false).WithDataLabels(ChartDataLabels.Category);
        var small = WordDocument.Create(options); small.AddChart(narrow, width: 300, height: 220, cancellationToken: TestContext.Current.CancellationToken);
        using (var output = new MemoryStream())
        {
            output.WriteByte(42); var error = Assert.Throws<NotSupportedException>(() => small.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.StartsWith("Chart.DataLabelFit:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        var valid = WordDocument.Create(options); valid.AddChart(LabelledCircular(ChartKind.Pie), cancellationToken: TestContext.Current.CancellationToken); byte[] saved = SaveObjects(valid);
        byte[] outside = Xml(saved, "word/charts/chart1.xml", xml => xml.Descendants(C + "dLblPos").Single().SetAttributeValue("val", "outEnd"));
        Assert.False(Read(outside).Renderable);
        using (var input = new MemoryStream(saved))
        {
            var strict = Context(new ConversionOptions(FidelityMode.Strict)); var error = Assert.Throws<NotSupportedException>(() => FixedLayout.Create(DocxReader.Read(input, strict), strict));
            Assert.StartsWith("Chart.DataLabelApproximation:", error.Message, StringComparison.Ordinal);
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => valid.Save(new MemoryStream(), DocumentFormat.Pdf, options, cancelled.Token));
    }

    [Fact]
    public void CircularPointLabelsRejectUnqualifiedTextAndFontFaces()
    {
        var options = new ConversionOptions(FidelityMode.Lenient);
        var nonAscii = ChartDefinition.CreateCircular(ChartKind.Pie, new[] { new ChartSlice("漢", 1, "2468AC"), new ChartSlice("West", 1, "C03040") }, showLegend: false).WithDataLabels(ChartDataLabels.Category);
        var document = WordDocument.Create(options); document.AddChart(nonAscii, width: 360, height: 260, cancellationToken: TestContext.Current.CancellationToken);
        using (var output = new MemoryStream())
        {
            output.WriteByte(42); var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.StartsWith("Chart.DataLabelText:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        var valid = WordDocument.Create(options); valid.AddChart(LabelledCircular(ChartKind.Pie), width: 360, height: 260, cancellationToken: TestContext.Current.CancellationToken);
        byte[] embedded = FixedLayout.FontBytes(0);
#if NET8_0_OR_GREATER
        string digest = Convert.ToHexString(SHA256.HashData(embedded));
#else
        string digest; using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(embedded)).Replace("-", "");
#endif
        Assert.Equal("B85C38ECEA8A7CFB39C24E395A4007474FA5A4FC864F6EE33309EB4948D232D5", digest);
        var replaced = new ConversionOptions(FidelityMode.Lenient); using var source = new MemoryStream(embedded);
        replaced.Fonts.Sources.Add(new FontSource("Noto Sans", source));
        using var destination = new MemoryStream(); destination.WriteByte(42);
        var font = Assert.Throws<NotSupportedException>(() => valid.Save(destination, DocumentFormat.Pdf, replaced, TestContext.Current.CancellationToken));
        Assert.StartsWith("Chart.DataLabelFont:", font.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, destination.ToArray());
    }

    [Fact]
    public void CircularPointLabelsTreatMissingValuesAsGapsAndBoundOverlapWork()
    {
        var options = new ConversionOptions(FidelityMode.Lenient);
        ChartDefinition single = ChartDefinition.CreateCircular(ChartKind.Doughnut,
            new[] { new ChartSlice("Only", 1, "2468AC"), new ChartSlice("Missing", null, "C03040") }, showLegend: false).WithDataLabels(ChartDataLabels.CategoryAndValue);
        var doc = WordDocument.Create(options); doc.AddChart(single, width: 360, height: 260, cancellationToken: TestContext.Current.CancellationToken);
        using (var input = new MemoryStream(SaveObjects(doc)))
        {
            var context = Context(options); FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
            Assert.Contains(page.Text, item => item.Text == "Only, 1"); Assert.DoesNotContain(page.Text, item => item.Text.StartsWith("Missing", StringComparison.Ordinal));
        }
        var many = ChartDefinition.CreateCircular(ChartKind.Pie, Enumerable.Repeat(new ChartSlice("A", 1, "2468AC"), 64), showLegend: false).WithDataLabels(ChartDataLabels.Value);
        var graph = ChartObjectFormat.Create(many, Context(options)); var item = new DocumentObjectData(many, graph, 360, 260, "Bounded sectors");
        var limited = Context(new ConversionOptions(FidelityMode.Lenient, maximumExpandedBytes: 200000));
        var error = Assert.Throws<InvalidDataException>(() => ChartLayout.Create(item, 0, (_, _, _) => 1, limited, true));
        Assert.StartsWith("Object.LayoutLimit:", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)]
    public void CircularPointLabelsPrecedeTheCategoryLegendInReadingOrder(ChartKind kind)
    {
        var chart = ChartDefinition.CreateCircular(kind, new[] { new ChartSlice("East", 30, "2468AC"), new ChartSlice("West", 30, "C03040") }, "Total", "Shares")
            .WithDataLabels(ChartDataLabels.CategoryAndValue);
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(chart, width: 360, height: 260, cancellationToken: TestContext.Current.CancellationToken);
        using var input = new MemoryStream(SaveObjects(doc)); var context = Context(options);
        string[] text = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context)).Text.Select(item => item.Text).ToArray();
        int title = Array.IndexOf(text, "Shares"), east = Array.IndexOf(text, "East, 30"), west = Array.IndexOf(text, "West, 30");
        int eastLegend = Array.IndexOf(text, "East"), westLegend = Array.IndexOf(text, "West");
        Assert.True(title >= 0 && title < east && east < west && west < eastLegend && eastLegend < westLegend);
    }

    [Fact]
    public void CircularPointLabelsUseContrastForLightAndDarkSlices()
    {
        var chart = ChartDefinition.CreateCircular(ChartKind.Pie, new[] { new ChartSlice("Light", 1, "FFFFFF"), new ChartSlice("Dark", 1, "000000") }, showLegend: false)
            .WithDataLabels(ChartDataLabels.Category);
        var options = new ConversionOptions(FidelityMode.Lenient); var doc = WordDocument.Create(options);
        doc.AddChart(chart, width: 360, height: 260, cancellationToken: TestContext.Current.CancellationToken);
        using var input = new MemoryStream(SaveObjects(doc)); var context = Context(options);
        FixedPage page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        Assert.Equal("000000", page.Text.Single(item => item.Text == "Light").Color);
        Assert.Equal("FFFFFF", page.Text.Single(item => item.Text == "Dark").Color);
    }
}
