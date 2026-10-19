using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static readonly double?[] North = { 10, 0, -2, 6 };
    private static readonly double?[] South = { -5, 8, null, 4 };
    private static readonly string[] Categories = { "First", "Second", "Third", "Fourth" };
    private static readonly XNamespace C = ChartObjectFormat.C;
    private static ConversionContext Context(ConversionOptions? options = null) => new ConversionContext(options ?? new ConversionOptions(), TestContext.Current.CancellationToken);
    private static byte[] Input(string kind = "column") => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", kind + ".docx"));
    private static (ChartDefinition Definition, ObjectGraph Graph, bool Renderable) Read(byte[] bytes, ConversionContext? context = null)
    {
        context ??= Context(); using var stream = new MemoryStream(bytes); using var package = new DocxPackage(stream, context);
        return ChartObjectFormat.Read(package, "word/charts/chart1.xml", context);
    }
    private static byte[] Xml(byte[] bytes, string name, Action<XDocument> edit)
    {
        using var stream = new MemoryStream(); stream.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.GetEntry(name)!; XDocument document;
            using (Stream source = entry.Open()) document = XDocument.Load(source);
            edit(document); entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(document.ToString(SaveOptions.DisableFormatting));
        }
        return stream.ToArray();
    }
    [Theory]
    [InlineData("column", ChartKind.ClusteredColumn)]
    [InlineData("line", ChartKind.Line)]
    public void IndependentChartAndWorkbookHaveRealValuesLocalClosureAndValidSchema(string name, ChartKind kind)
    {
        byte[] bytes = Input(name); var parsed = Read(bytes);
        Assert.Equal(kind, parsed.Definition.Kind); Assert.True(parsed.Renderable);
        Assert.Equal(Categories, parsed.Definition.Categories); Assert.Equal(North, parsed.Definition.Series[0].Values); Assert.Equal(South, parsed.Definition.Series[1].Values);
        Assert.Equal("North", parsed.Definition.Series[0].Name); Assert.Equal("2468AC", parsed.Definition.Series[0].Color); Assert.Equal(3, parsed.Graph.Parts.Count);
        using var source = new MemoryStream(bytes); using var package = WordprocessingDocument.Open(source, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData("cache")]
    [InlineData("external")]
    [InlineData("cycle")]
    [InlineData("points")]
    [InlineData("wrong-type")]
    public void UnsafeOrInconsistentChartGraphsFailAdmission(string kind)
    {
        byte[] bytes = Input();
        if (kind == "cache") bytes = Xml(bytes, "word/charts/chart1.xml", doc => doc.Descendants(C + "numCache").First().Element(C + "pt")!.Element(C + "v")!.Value = "99");
        else if (kind == "points") bytes = Xml(bytes, "word/charts/chart1.xml", doc => doc.Descendants(C + "numCache").First().Element(C + "ptCount")!.SetAttributeValue("val", "10001"));
        else if (kind == "wrong-type") bytes = Xml(bytes, "[Content_Types].xml", doc => doc.Root!.Elements().Single(e => (string?)e.Attribute("PartName") == "/word/embeddings/data.xlsx").SetAttributeValue("ContentType", "application/vnd.ms-excel.sheet.macroEnabled.12"));
        else bytes = Xml(bytes, "word/charts/_rels/chart1.xml.rels", doc =>
        { XElement relation = doc.Root!.Elements().Single(); if (kind == "external") relation.SetAttributeValue("TargetMode", "External"); else relation.SetAttributeValue("Target", "chart1.xml"); });
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }
    [Theory]
    [InlineData("formula")]
    [InlineData("hidden")]
    [InlineData("dimension")]
    public void NestedWorkbookFormulasHiddenSheetsAndDeclaredRangeBombsFail(string kind)
    {
        byte[] source = Input(); using var stream = new MemoryStream(source); using var package = new ZipArchive(stream);
        using Stream part = package.GetEntry("word/embeddings/data.xlsx")!.Open(); using var data = new MemoryStream(); part.CopyTo(data);
        byte[] workbook = Xml(data.ToArray(), kind == "hidden" ? "xl/workbook.xml" : "xl/worksheets/sheet1.xml", doc =>
        {
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            if (kind == "hidden") doc.Descendants(s + "sheet").Single().SetAttributeValue("state", "hidden");
            else if (kind == "dimension") doc.Descendants(s + "dimension").Single().SetAttributeValue("ref", "A1:XFD1048576");
            else doc.Descendants(s + "c").First().Add(new XElement(s + "f", "1+1"));
        });
        Assert.Throws<InvalidDataException>(() => ObjectWorkbook.Read(workbook, Context()));
    }
    [Fact]
    public void SharedGraphsChargeBytesOnceButEveryPlacementConsumesBudget()
    {
        var parsed = Read(Input()); var context = Context();
        var first = new DocumentObjectData(parsed.Definition, parsed.Graph, 300, 180, "Description");
        byte[] copy = parsed.Graph.Parts[0].Read(); copy[0] ^= 1; Assert.NotEqual(copy, parsed.Graph.Parts[0].Read());
        for (int i = 0; i < 128; i++) context.Objects.Place(first.Remap());
        Assert.Throws<InvalidDataException>(() => context.Objects.Place(first.Remap()));
        Assert.NotEqual(first.Id, first.Remap().Id);
    }
    [Fact]
    public void ImmutableDefinitionsCopyInputsAndRejectMalformedTreesAndValues()
    {
        double?[] values = (double?[])North.Clone(); string[] categories = (string[])Categories.Clone();
        var series = new ChartSeries("Series", "2468ac", values); var chart = new ChartDefinition(ChartKind.Line, categories, new[] { series });
        values[0] = 999; categories[0] = "Changed"; Assert.Equal(10, chart.Series[0].Values[0]); Assert.Equal("First", chart.Categories[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<double?>)series.Values)[0] = 999);
        Assert.Throws<ArgumentException>(() => new ChartSeries("Series", "2468AC", new double?[] { double.NaN }));
        EquationExpression[] children = { EquationExpression.Text("x") }; EquationExpression expression = EquationExpression.Row(children);
        children[0] = EquationExpression.Text("y"); Assert.Equal("x", expression.Children[0].Value);
        Assert.Throws<ArgumentException>(() => EquationExpression.Text("\ud800"));
        expression = EquationExpression.Text("x");
        for (int i = 0; i < 31; i++) expression = EquationExpression.Root(expression);
        Assert.Throws<ArgumentException>(() => EquationExpression.Root(expression));
    }
    [Fact]
    public void WorkbookWriterUsesActualPlainCellsAndLocalRanges()
    {
        ChartDefinition chart = Read(Input()).Definition; var context = Context();
        byte[] bytes = ObjectWorkbook.Write(chart, context); ObjectWorkbook book = ObjectWorkbook.Read(bytes, context);
        Assert.Equal("North", book.Range("Data!$B$1").Single());
        Assert.Equal(North.Cast<object?>(), book.Range("Data!$B$2:$B$5"));
        Assert.Throws<InvalidDataException>(() => book.Range("[remote.xlsx]Data!$B$2:$B$5"));
        Assert.Throws<InvalidDataException>(() => book.Range("Data!$A$1:$XFD$1048576"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Read(Input(), new ConversionContext(new ConversionOptions(), cancelled.Token)));
    }
}
