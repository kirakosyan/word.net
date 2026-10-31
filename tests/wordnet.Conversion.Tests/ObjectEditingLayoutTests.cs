using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static ChartDefinition Definition(ChartKind kind = ChartKind.ClusteredColumn) => new ChartDefinition(kind, Categories,
        new[] { new ChartSeries("North", "2468AC", North), new ChartSeries("South", "C03040", South) }, "Values");
    private static ConversionOptions MathOptions()
    {
        var options = new ConversionOptions(FidelityMode.Lenient); options.Fonts.MathFamily = "STIX Two Math";
        options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf"))); return options;
    }
    [Theory]
    [InlineData(ChartKind.ClusteredColumn)]
    [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.ClusteredBar)]
    public void PublicChartEditingRebuildsWorkbookAndCachesWithValidSchema(ChartKind kind)
    {
        var document = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); DocumentObject original = document.AddChart(Definition(kind), cancellationToken: TestContext.Current.CancellationToken);
        var replacement = new ChartDefinition(kind, Categories, new[] { new ChartSeries("Changed", "A020F0", new double?[] { 12, null, -4, 0 }) }, "Edited");
        DocumentObject updated = document.UpdateChart(original, replacement, TestContext.Current.CancellationToken);
        Assert.Equal(original.Id, updated.Id); Assert.Equal(12, updated.Chart!.Series[0].Values[0]);
        byte[] saved = SaveObjects(document);
        using (var stream = new MemoryStream(saved)) using (var package = WordprocessingDocument.Open(stream, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        WordDocument reloaded = LoadObjects(saved); Assert.Equal(replacement.Series[0].Values, Assert.Single(reloaded.Objects).Chart!.Series[0].Values);
        byte[] workbook = PartBytes(saved, "word/embeddings/chart1.xlsx"); ObjectWorkbook book = ObjectWorkbook.Read(workbook, Context());
        Assert.Equal(new object?[] { 12d, null, -4d, 0d }, book.Range("Data!B2:B5"));
        Assert.Throws<ArgumentException>(() => document.UpdateChart(original, replacement, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => reloaded.UpdateChart(updated, replacement, TestContext.Current.CancellationToken));
        Assert.Equal(saved, SaveObjects(document));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.UpdateChart(updated, Definition(), cancelled.Token));
        Assert.Equal(saved, SaveObjects(document));
        document.RemoveObject(updated, TestContext.Current.CancellationToken); Assert.Empty(document.Objects);
        using var removed = new MemoryStream(SaveObjects(document)); using var zip = new ZipArchive(removed);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("charts/") || e.FullName.EndsWith(".xlsx", StringComparison.Ordinal));
    }
    [Fact]
    public void EquationUpdatesWriteGenuineOmmlAndRetainPlacement()
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        DocumentObject first = doc.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")), description: "x divided by two", cancellationToken: TestContext.Current.CancellationToken);
        DocumentObject updated = doc.UpdateEquation(first, EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("3")), TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, updated.Id); Assert.Equal("x divided by two", updated.Description);
        byte[] saved = SaveObjects(doc); XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml")));
        Assert.Equal("3", xml.Descendants(MathObjectFormat.M + "den").Single().Value);
        using var stream = new MemoryStream(saved); using var package = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.Equal("3", Assert.Single(LoadObjects(saved).Objects).Equation!.Children[1].Value);
    }
    [Fact]
    public void MathMetricsMatchPinnedIndependentFontToolsConstantsGlyphsAndVariants()
    {
        string folder = Path.Combine(TestDocuments.FixtureDirectory, "P2"); byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "STIXTwoMath.ttf"));
        var font = new MathFont(bytes, Context()); using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "math-reference.json")));
        JsonElement root = reference.RootElement; Assert.Equal(root.GetProperty("units").GetInt32(), font.Units);
        Assert.Equal(root.GetProperty("constants").GetProperty("ScriptPercentScaleDown").GetInt32(), font.ScriptPercent);
        Assert.Equal(root.GetProperty("constants").GetProperty("DisplayOperatorMinHeight").GetInt32(), font.DisplayOperatorMinHeight);
        Assert.Equal(root.GetProperty("constants").EnumerateObject().Skip(4).Take(51).Select(p => p.Value.GetInt32()), font.Constants);
        foreach (JsonProperty property in root.GetProperty("glyphs").EnumerateObject())
        {
            JsonElement g = property.Value; int id = g.GetProperty("id").GetInt32(); Assert.Equal(g.GetProperty("advance").GetInt32(), font.Advance(id));
            var bounds = font.Bounds(id); Assert.Equal(g.GetProperty("box").EnumerateArray().Select(v => v.GetInt32()), new[] { bounds.Left, bounds.Bottom, bounds.Right, bounds.Top });
        }
        foreach (JsonProperty property in root.GetProperty("italics").EnumerateObject()) Assert.Equal(property.Value.GetInt32(), font.Italic(int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture)));
        foreach (JsonProperty property in root.GetProperty("kerns").EnumerateObject())
            foreach (JsonProperty corner in property.Value.EnumerateObject())
            {
                int glyph = int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture), side = int.Parse(corner.Name, System.Globalization.CultureInfo.InvariantCulture);
                int[] heights = corner.Value.GetProperty("heights").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                int[] values = corner.Value.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                for (int i = 0; i < values.Length; i++) Assert.Equal(values[i], font.Kern(glyph, side, i == 0 ? -10000 : heights[i - 1]));
            }
        foreach (JsonProperty property in root.GetProperty("variants").EnumerateObject())
        {
            MathFont.Construction actual = font.Vertical[int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture)];
            Assert.Equal(property.Value.GetProperty("variants").EnumerateArray().Select(v => (v[0].GetInt32(), v[1].GetInt32())), actual.Variants);
            JsonElement assembly = property.Value.GetProperty("assembly");
            if (assembly.ValueKind != JsonValueKind.Null) Assert.Equal(assembly.GetProperty("parts").EnumerateArray().Select(v => (v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32(), v[3].GetInt32(), v[4].GetInt32() == 1)), actual.Parts);
        }
    }
    [Fact]
    public void ChartsAndMathProduceRealVectorsAndChangingValuesChangesGeometry()
    {
        ConversionOptions options = MathOptions(); var doc = WordDocument.Create(options); doc.AddChart(Definition(), cancellationToken: TestContext.Current.CancellationToken);
        doc.AddEquation(EquationExpression.Fraction(EquationExpression.Superscript(EquationExpression.Text("x"), EquationExpression.Text("2")), EquationExpression.Root(EquationExpression.Text("3"))), description: "x squared divided by square root of three", cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(doc); using var input = new MemoryStream(saved); var c = Context(options); DocumentModel model = DocxReader.Read(input, c);
        var page = Assert.Single(FixedLayout.Create(model, c)); Assert.Contains(page.Shapes, s => s.Color == "2468AC" && s.Stroke == 0 && s.Height > 0);
        Assert.Contains(page.Text, t => t.NativeShape?.Glyphs.Any(g => g.Glyph > 0) == true);
        Assert.Single(page.Text, t => t.Text == "(x^(2))/(sqrt(3))");
        Assert.Contains(page.Shapes, s => s.Color == "000000" && s.Stroke == 0 && s.Height > 0); // fraction/radical rules
        using var pdf = new MemoryStream(); doc.Save(pdf, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken); using (var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray())) Assert.Single(read.GetPages());
        using var xps = new MemoryStream(); doc.Save(xps, DocumentFormat.Xps, options, TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), e => e.Name.LocalName == "Glyphs" && e.Attribute("Indices") != null);
        byte[] svg = SvgPreview.Write(page, c); Assert.Contains("<path", Encoding.UTF8.GetString(svg), StringComparison.Ordinal);
        doc.UpdateChart(doc.Objects[0], new ChartDefinition(ChartKind.Line, Categories, new[] { new ChartSeries("Changed", "2468AC", new double?[] { 10, -5, null, 4 }) }), TestContext.Current.CancellationToken);
        using var changed = new MemoryStream(SaveObjects(doc)); var changedContext = Context(options); var newPage = Assert.Single(FixedLayout.Create(DocxReader.Read(changed, changedContext), changedContext));
        Assert.Contains(FixedShape.PaintOrder(newPage.Shapes), s => s.Color == "2468AC" && s.Stroke > 0 && s.Width != 0 && s.Height != 0);
        Assert.NotEqual(svg, SvgPreview.Write(newPage, changedContext));
        string? output = Environment.GetEnvironmentVariable("WORDNET_P2_EVIDENCE");
        if (output != null)
        { Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "objects.docx"), saved); File.WriteAllBytes(Path.Combine(output, "objects.pdf"), pdf.ToArray()); File.WriteAllBytes(Path.Combine(output, "objects.svg"), svg); }
    }
    [Fact]
    public void MathWithoutExplicitMathFontAndOversizedChartLabelsFailBeforeWriting()
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); doc.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        var chart = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); chart.AddChart(Definition(), 36, 36, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => chart.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }
}
