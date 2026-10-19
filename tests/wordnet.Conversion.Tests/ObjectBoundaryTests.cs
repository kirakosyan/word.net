using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    [Fact]
    public void UnsupportedChartWrapperEffectsAndLinksFailInsteadOfBeingDiscarded()
    {
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        foreach (string placement in new[] { "spacing", "hidden", "link" })
        {
            byte[] source = Xml(Input(), "word/document.xml", xml =>
            {
                XElement inline = xml.Descendants(wp + "inline").Single();
                if (placement == "spacing") inline.SetAttributeValue("distT", "12700");
                else if (placement == "hidden") inline.Element(wp + "docPr")!.SetAttributeValue("hidden", 1);
                else inline.Element(wp + "docPr")!.Add(new XElement(a + "hlinkClick", new XAttribute(DocxPackage.DocumentRelationships + "id", "remote")));
            });
            Assert.Throws<NotSupportedException>(() => LoadObjects(source));
        }
    }
    [Fact]
    public void SharedChartStorageStillChargesEveryPlacementPointAndAggregateLayoutWork()
    {
        var definition = new ChartDefinition(ChartKind.Line, Enumerable.Repeat("", 10000), new[] { new ChartSeries("Values", "2468AC", Enumerable.Repeat<double?>(1, 10000)) });
        var context = Context(); var graph = ChartObjectFormat.Create(definition, context); var item = new DocumentObjectData(definition, graph, 300, 180, "");
        var budget = new ObjectBudget(context); for (int i = 0; i < 10; i++) budget.Place(item.Remap());
        Assert.Throws<InvalidDataException>(() => budget.Place(item.Remap()));
        var bounded = new ObjectBudget(Context(new ConversionOptions(maximumCharacters: 500)));
        bounded.Render(250, 1); bounded.Render(250, 1);
        Assert.Throws<InvalidDataException>(() => bounded.Render(1, 1));
    }
    [Theory]
    [InlineData("overlap")]
    [InlineData("offset")]
    [InlineData("rule")]
    [InlineData("variant-count")]
    public void MalformedMathTablesFailBeforeOutlineRendering(string failure)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf"));
        int U16(int p) => bytes[p] * 256 + bytes[p + 1]; int U32(int p) => checked(U16(p) * 65536 + U16(p + 2));
        void Write(int p, int value) { bytes[p] = (byte)(value >> 8); bytes[p + 1] = (byte)value; }
        int Record(string name) => Enumerable.Range(0, U16(4)).Select(i => 12 + i * 16).Single(p => Encoding.ASCII.GetString(bytes, p, 4) == name);
        int record = Record("MATH"), math = U32(record + 8), constants = math + U16(math + 4), variants = math + U16(math + 8);
        if (failure == "overlap") Array.Copy(bytes, Record("head") + 8, bytes, record + 8, 4);
        else if (failure == "offset") Write(math + 4, 65535);
        else if (failure == "rule") Write(constants + 8 + 34 * 4, 0);
        else { int construction = variants + U16(variants + 10); Write(construction + 2, 1025); }
        Assert.Throws<InvalidDataException>(() => new MathFont(bytes, Context()));
    }
    [Fact]
    public void MathEmbeddingPermissionsAreCheckedAndLargeDelimitersUseFontAssemblies()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf")); byte[] original = (byte[])bytes.Clone();
        int U16(int p) => bytes[p] * 256 + bytes[p + 1];
        int record = Enumerable.Range(0, U16(4)).Select(i => 12 + i * 16).Single(p => Encoding.ASCII.GetString(bytes, p, 4) == "OS/2");
        int os = checked(U16(record + 8) * 65536 + U16(record + 10)); bytes[os + 8] = 0; bytes[os + 9] = 2;
        Assert.Throws<NotSupportedException>(() => new MathFont(bytes, Context()));
        var context = Context(MathOptions()); var font = new MathFont(original, context);
        EquationExpression tree = EquationExpression.Text("x");
        for (int i = 0; i < 5; i++) tree = EquationExpression.Fraction(tree, EquationExpression.Text("2"));
        tree = EquationExpression.Delimiter(tree);
        ObjectVisual result = new MathLayout(font, 0, original, context).Create(new DocumentObjectData(tree, true, 12, "nested fractions"));
        Assert.True(result.Ascent > 30); Assert.True(result.Descent > 0); Assert.True(result.Width > 0);
        Assert.True(result.Text.Count(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true) > 12);
    }
    [Theory]
    [InlineData("reverse-axis")]
    [InlineData("markers")]
    [InlineData("thick-line")]
    public void PreservedUnqualifiedChartAppearanceCannotSilentlyRender(string failure)
    {
        byte[] source = Xml(Input("line"), "word/charts/chart1.xml", doc =>
        {
            if (failure == "reverse-axis") doc.Descendants(C + "orientation").First().SetAttributeValue("val", "maxMin");
            else if (failure == "markers") doc.Descendants(C + "symbol").First().SetAttributeValue("val", "circle");
            else doc.Descendants(System.Xml.Linq.XName.Get("ln", "http://schemas.openxmlformats.org/drawingml/2006/main")).First().SetAttributeValue("w", 25400);
        });
        WordDocument document = LoadObjects(source); Assert.Equal(2, document.Objects.Count);
        document.RemoveObject(document.Objects.Single(o => o.Equation != null), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }
    [Fact]
    public void AxisTitlesRoundTripAndOrdinaryEditsRetainObjects()
    {
        var definition = new ChartDefinition(ChartKind.Line, Categories, new[] { new ChartSeries("North", "2468AC", North) }, categoryTitle: "Quarter", valueTitle: "Amount");
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); doc.AddChart(definition, cancellationToken: TestContext.Current.CancellationToken);
        doc.AddParagraph("After chart", cancellationToken: TestContext.Current.CancellationToken); doc.Title = "Report";
        WordDocument saved = LoadObjects(SaveObjects(doc)); DocumentObject chart = Assert.Single(saved.Objects);
        Assert.Equal("Quarter", chart.Chart!.CategoryTitle); Assert.Equal("Amount", chart.Chart.ValueTitle); Assert.Equal("After chart", saved.Paragraphs[saved.Paragraphs.Count - 1].Text);
    }
    [Fact]
    public void FractionGeometryUsesIndependentMathConstantsAndInlineMathChangesLineHeight()
    {
        ConversionOptions options = MathOptions(); var doc = WordDocument.Create(options);
        doc.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")), cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = SaveObjects(doc); using var input = new MemoryStream(source); var context = Context(options); var model = DocxReader.Read(input, context);
        var page = Assert.Single(FixedLayout.Create(model, context)); PositionedText[] glyphs = page.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).ToArray();
        Assert.Equal(2, glyphs.Length); Assert.Equal(15.36, glyphs[1].Y - glyphs[0].Y, 6);
        Assert.Contains(page.Shapes, s => Math.Abs(s.Height - 0.816) < 0.000001);
        string? folder = Environment.GetEnvironmentVariable("WORDNET_P2_EVIDENCE");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "math-simple.svg"), SvgPreview.Write(page, context));
            using var pdf = new MemoryStream(); doc.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken); File.WriteAllBytes(Path.Combine(folder, "math-simple.pdf"), pdf.ToArray());
        }
        XNamespace w = TestDocuments.W; XNamespace m = MathObjectFormat.M;
        byte[] inline = Xml(source, "word/document.xml", xml =>
        {
            XElement p = xml.Descendants(w + "p").First(); XElement math = p.Element(m + "oMathPara")!.Element(m + "oMath")!;
            p.ReplaceNodes(new XElement(w + "r", new XElement(w + "t", "Before ")), new XElement(math), new XElement(w + "r", new XElement(w + "t", " after")));
        });
        using var read = new MemoryStream(inline); var c = Context(options); var inlinePage = Assert.Single(FixedLayout.Create(DocxReader.Read(read, c), c));
        PositionedText before = inlinePage.Text.First(t => t.NativeShape == null); PositionedText after = inlinePage.Text.Last(t => t.NativeShape == null);
        Assert.Equal("Before  after", string.Concat(inlinePage.Text.Where(t => t.NativeShape == null).Select(t => t.Text)));
        Assert.Equal(before.Y, after.Y, 6); Assert.True(after.X > before.X);
        Assert.True(inlinePage.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).All(t => t.Y >= 48 && t.Y <= inlinePage.Settings.Height - 48));
    }
}
