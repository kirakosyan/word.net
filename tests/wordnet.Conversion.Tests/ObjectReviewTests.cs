using System;
using System.IO;
using System.Linq;
using System.Text;
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
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NestedEquationRowsSerializeAsOrderedTextWithoutInventedScripts(int count)
    {
        var children = Enumerable.Range(0, count).Select(i => EquationExpression.Text(((char)('a' + i)).ToString())).ToArray();
        var expression = EquationExpression.Fraction(EquationExpression.Row(EquationExpression.Row(EquationExpression.Row(children)), EquationExpression.Text("z")), EquationExpression.Text("2"));
        var doc = WordDocument.Create(MathOptions()); doc.AddEquation(expression, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = SaveObjects(doc); XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(bytes, "word/document.xml"))); XNamespace m = MathObjectFormat.M;
        Assert.Equal(string.Concat(children.Select(c => c.Value)) + "z", string.Concat(xml.Descendants(m + "num").Single().Descendants(m + "t").Select(t => t.Value)));
        Assert.DoesNotContain(xml.Descendants(), e => e.Name == m + "sSub" || e.Name == m + "sSup" || e.Name == m + "sSubSup");
        using var stream = new MemoryStream(bytes); using var package = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.Equal(MathObjectFormat.Description(expression), MathObjectFormat.Description(Assert.Single(LoadObjects(bytes).Objects).Equation!));
    }

    [Fact]
    public void WordControlCharacterItalicsDoNotChangeVisibleEquationAppearance()
    {
        var doc = WordDocument.Create(MathOptions()); doc.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")), cancellationToken: TestContext.Current.CancellationToken);
        XNamespace m = MathObjectFormat.M, w = TestDocuments.W;
        byte[] source = Xml(SaveObjects(doc), "word/document.xml", xml => xml.Descendants(m + "f").Single().AddFirst(new XElement(m + "fPr", new XElement(m + "ctrlPr",
            new XElement(w + "rPr", new XElement(w + "rFonts", new XAttribute(w + "ascii", "Cambria Math"), new XAttribute(w + "hAnsi", "Cambria Math")), new XElement(w + "i"))))));
        using var read = new MemoryStream(source); var context = Context(MathOptions()); var model = DocxReader.Read(read, context);
        Assert.True(model.BodyParagraphs.SelectMany(p => p.Runs).Single(r => r.Object != null).Object!.Renderable);
        Assert.Single(FixedLayout.Create(model, context));
        byte[] visibleItalic = Xml(source, "word/document.xml", xml => xml.Descendants(m + "r").First().Element(w + "rPr")!.Add(new XElement(w + "i")));
        using var changed = new MemoryStream(visibleItalic); var c = Context(MathOptions());
        Assert.Throws<NotSupportedException>(() => FixedLayout.Create(DocxReader.Read(changed, c), c));
    }

    private static (ObjectVisual Visual, MathFont Font) MathVisual(EquationExpression expression, bool display = false)
    {
        var context = Context(MathOptions()); byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf"));
        var font = new MathFont(bytes, context);
        return (new MathLayout(font, 0, bytes, context).Create(new DocumentObjectData(expression, display, 12, "")), font);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RadicalRuleMeetsTheTopOfItsActualGlyph(bool tall)
    {
        EquationExpression body = tall ? EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("3")) : EquationExpression.Text("3");
        var result = MathVisual(EquationExpression.Root(body));
        PositionedText root = result.Visual.Text.First(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true);
        int glyph = root.NativeShape!.Glyphs.Single().Glyph; double glyphTop = root.Y - result.Font.Bounds(glyph).Top * root.Size / result.Font.Units;
        FixedShape rule = result.Visual.Shapes.Last(); Assert.Equal(glyphTop, rule.Y, 6);
        PositionedText lastBody = result.Visual.Text.Last(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true);
        Assert.True(lastBody.Y > rule.Y);
    }

    [Fact]
    public void NestedScriptsStopAtTheIndependentScriptscriptSize()
    {
        var expression = EquationExpression.Superscript(EquationExpression.Text("x"), EquationExpression.Superscript(EquationExpression.Text("y"), EquationExpression.Superscript(EquationExpression.Text("z"), EquationExpression.Text("w"))));
        var visual = MathVisual(expression).Visual;
        double[] sizes = visual.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).Select(t => t.Size).ToArray();
        Assert.Equal(4, sizes.Length); Assert.Equal(12, sizes[0], 6); Assert.Equal(8.4, sizes[1], 6); Assert.Equal(6.6, sizes[2], 6); Assert.Equal(6.6, sizes[3], 6);
        var nestedRoot = MathVisual(EquationExpression.Root(EquationExpression.Text("x"), EquationExpression.Root(EquationExpression.Text("y"), EquationExpression.Text("3")))).Visual;
        Assert.All(nestedRoot.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true), t => Assert.True(t.Size >= 6.6 - 0.000001));
    }

    [Fact]
    public void ExactSpacingObjectWithoutDescriptionDoesNotOverlapPreviousText()
    {
        var doc = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); doc.AddParagraph("Previous", cancellationToken: TestContext.Current.CancellationToken); doc.AddChart(Definition(), cancellationToken: TestContext.Current.CancellationToken);
        XNamespace w = TestDocuments.W;
        byte[] source = Xml(SaveObjects(doc), "word/document.xml", xml => xml.Descendants(w + "p").Last().AddFirst(new XElement(w + "pPr", new XElement(w + "spacing", new XAttribute(w + "line", 240), new XAttribute(w + "lineRule", "exact")))));
        using var input = new MemoryStream(source); var context = Context(new ConversionOptions(FidelityMode.Lenient));
        var page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context)); double previous = page.Text.Where(t => t.SourceParagraph?.Runs.Any(r => r.Text == "Previous") == true).Max(t => t.Y);
        Assert.All(page.Shapes.Where(s => s.Color == "2468AC"), s => Assert.True(s.Y > previous));
    }

    [Fact]
    public void ObjectDescriptionsUpdateAtomicallyAlongsideBusinessSemantics()
    {
        var doc = WordDocument.Create(MathOptions()); var chart = doc.AddChart(Definition(), description: "Old chart", cancellationToken: TestContext.Current.CancellationToken);
        chart = doc.UpdateChart(chart, Definition(), description: "New chart", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("New chart", chart.Description); byte[] before = SaveObjects(doc);
        Assert.Throws<ArgumentException>(() => doc.UpdateChart(chart, Definition(), description: "Bad\ttext", cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, SaveObjects(doc));
        chart = doc.UpdateChart(chart, Definition(), description: "", cancellationToken: TestContext.Current.CancellationToken); Assert.Equal("", chart.Description);
        var equation = doc.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")), description: "x over two", cancellationToken: TestContext.Current.CancellationToken);
        equation = doc.UpdateEquation(equation, EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("3")), description: "x over three", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("x over three", equation.Description);
        using var text = new MemoryStream(); doc.Save(text, DocumentFormat.Text, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("x over three", Encoding.UTF8.GetString(text.ToArray()), StringComparison.Ordinal); Assert.DoesNotContain("x over two", Encoding.UTF8.GetString(text.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingApiObjectsRemovesOnlyTheirDedicatedEmptyParagraphs()
    {
        var doc = WordDocument.Create(MathOptions()); doc.AddParagraph("Before", cancellationToken: TestContext.Current.CancellationToken);
        doc.AddChart(Definition(), cancellationToken: TestContext.Current.CancellationToken); doc.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        doc.RemoveObject(doc.Objects[0], TestContext.Current.CancellationToken); doc.RemoveObject(Assert.Single(doc.Objects), TestContext.Current.CancellationToken);
        Assert.Equal("Before", Assert.Single(doc.Paragraphs).Text);
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(SaveObjects(doc), "word/document.xml"))); Assert.Single(xml.Descendants((XNamespace)TestDocuments.W + "p"));
        var imported = LoadObjects(Input()); int count = imported.Paragraphs.Count; imported.RemoveObject(imported.Objects[0], TestContext.Current.CancellationToken); Assert.Equal(count, imported.Paragraphs.Count);
    }

    [Fact]
    public void RepeatedRegistrationOfTheMathFaceHasDeterministicSelection()
    {
        var options = MathOptions(); options.Fonts.Sources.Add(options.Fonts.Sources[0]);
        var doc = WordDocument.Create(options); doc.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.ToArray()); Assert.Single(pdf.GetPages());
    }

    [Theory]
    [InlineData("angle")]
    [InlineData("multiple")]
    [InlineData("tab")]
    [InlineData("long")]
    public void UnsupportedOmmlInputNeverSurfacesPublicFactoryArgumentErrors(string kind)
    {
        XNamespace m = MathObjectFormat.M;
        XElement Text(string value) => new XElement(m + "r", new XElement(m + "t", value));
        XElement content = kind is "angle" or "multiple"
            ? new XElement(m + "d", new XElement(m + "dPr", new XElement(m + "begChr", new XAttribute(m + "val", "⟨")), new XElement(m + "endChr", new XAttribute(m + "val", "⟩"))), new XElement(m + "e", Text("x")))
            : Text(kind == "tab" ? "x\ty" : new string('x', 1025));
        if (kind == "multiple") content.Add(new XElement(m + "e", Text("y")));
        Assert.Throws<NotSupportedException>(() => MathObjectFormat.Read(new XElement(m + "oMath", content), Context()));
    }

    [Theory]
    [InlineData(FidelityMode.Lenient, false)]
    [InlineData(FidelityMode.BestEffort, false)]
    [InlineData(FidelityMode.Lenient, true)]
    [InlineData(FidelityMode.BestEffort, true)]
    public void ConversionOmitsOutOfProfileObjectsWithExplicitLossButStrictPreservesOutput(FidelityMode mode, bool wordChart)
    {
        XNamespace m = MathObjectFormat.M;
        byte[] source;
        if (wordChart) source = WordChartExtensions(Input());
        else
        {
            var doc = WordDocument.Create(); doc.AddParagraph("Before", cancellationToken: TestContext.Current.CancellationToken); doc.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken); doc.AddParagraph("After", cancellationToken: TestContext.Current.CancellationToken);
            source = Xml(SaveObjects(doc), "word/document.xml", xml => xml.Descendants(m + "oMath").Single().ReplaceNodes(new XElement(m + "nary", new XElement(m + "e", new XElement(m + "r", new XElement(m + "t", "x"))))));
        }
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        var result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(mode), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Object.UnsupportedLoss");
        if (!wordChart) Assert.Contains("Before\n\nAfter", Encoding.UTF8.GetString(output.ToArray()).Replace("\r", ""), StringComparison.Ordinal);
        using var strictInput = new MemoryStream(source); using var sentinel = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, sentinel, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, sentinel.ToArray());
        Assert.Throws<NotSupportedException>(() => LoadObjects(source));
    }

    private static byte[] WordChartExtensions(byte[] source)
    {
        XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006", c14 = "http://schemas.microsoft.com/office/drawing/2007/8/2/chart";
        source = Xml(source, "word/charts/chart1.xml", xml => xml.Root!.AddFirst(new XElement(mc + "AlternateContent", new XElement(mc + "Choice", new XAttribute(XNamespace.Xmlns + "c14", c14), new XAttribute("Requires", "c14"), new XElement(c14 + "style", new XAttribute("val", 102))), new XElement(mc + "Fallback", new XElement(C + "style", new XAttribute("val", 2))))));
        return Xml(source, "word/document.xml", xml =>
        {
            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing", wp14 = "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing";
            xml.Descendants(wp + "inline").First().SetAttributeValue(wp14 + "anchorId", "1234ABCD"); xml.Descendants(wp + "inline").First().SetAttributeValue(wp14 + "editId", "01234567");
        });
    }

    [Fact]
    public void NumericCategoryCachesAreUnsupportedRatherThanCorrupt()
    {
        byte[] source = Xml(Input(), "word/charts/chart1.xml", xml =>
        {
            foreach (XElement cat in xml.Descendants(C + "cat")) cat.ReplaceNodes(new XElement(C + "numLit", new XElement(C + "formatCode", "General"), new XElement(C + "ptCount", new XAttribute("val", 4)), Enumerable.Range(0, 4).Select(i => new XElement(C + "pt", new XAttribute("idx", i), new XElement(C + "v", 2020 + i)))));
        });
        var error = Assert.Throws<NotSupportedException>(() => Read(source)); Assert.StartsWith("Chart.Categories", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedChartTextDoesNotLeakFactoryArgumentErrors(bool category)
    {
        byte[] source = Xml(Input(), "word/charts/chart1.xml", xml =>
        {
            if (category)
                foreach (XElement cat in xml.Descendants(C + "cat")) cat.ReplaceNodes(new XElement(C + "strLit", new XElement(C + "ptCount", new XAttribute("val", 4)), Enumerable.Range(0, 4).Select(i => new XElement(C + "pt", new XAttribute("idx", i), new XElement(C + "v", "Line\n" + i)))));
            else
                foreach (XElement tx in xml.Descendants(C + "ser").Select(e => e.Element(C + "tx")!)) tx.ReplaceNodes(new XElement(C + "v", "Line\nname"));
        });
        var error = Assert.Throws<NotSupportedException>(() => Read(source)); Assert.StartsWith("Chart.Profile", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("newline")]
    [InlineData("tab")]
    [InlineData("long")]
    public void UnsupportedImportedDescriptionsUseTheInputProfileErrorContract(string kind)
    {
        byte[] source = Xml(Input(), "word/document.xml", xml =>
        {
            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            xml.Descendants(wp + "docPr").First().SetAttributeValue("descr", kind == "long" ? new string('x', 4097) : kind == "tab" ? "x\ty" : "x\ny");
        });
        var error = Assert.Throws<NotSupportedException>(() => LoadObjects(source)); Assert.StartsWith("Object.Profile", error.Message, StringComparison.Ordinal);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        var result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Object.UnsupportedLoss");
    }

    [Theory]
    [InlineData(FidelityMode.Lenient, "native")]
    [InlineData(FidelityMode.BestEffort, "native")]
    [InlineData(FidelityMode.Lenient, "tagged")]
    [InlineData(FidelityMode.BestEffort, "tagged")]
    [InlineData(FidelityMode.Lenient, "ua")]
    [InlineData(FidelityMode.BestEffort, "ua")]
    [InlineData(FidelityMode.Lenient, "pdfa")]
    [InlineData(FidelityMode.BestEffort, "pdfa")]
    public void UnsupportedObjectLossCannotBypassNativeOrPdfConformanceBoundaries(FidelityMode mode, string profile)
    {
        var doc = WordDocument.Create(); doc.Title = "Object combination guard";
        doc.AddParagraph("Report", headingLevel: 1, cancellationToken: TestContext.Current.CancellationToken);
        doc.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        XNamespace m = MathObjectFormat.M;
        byte[] source = Xml(SaveObjects(doc), "word/document.xml", xml => xml.Descendants(m + "oMath").Single().ReplaceNodes(new XElement(m + "nary", new XElement(m + "e", new XElement(m + "r", new XElement(m + "t", "x"))))));
        var options = new ConversionOptions(mode); options.Pdf.Language = "en";
        if (profile == "native") { options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; }
        else if (profile == "tagged") options.Pdf.Accessibility = PdfAccessibility.Tagged;
        else if (profile == "ua") options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        else options.Pdf.Conformance = PdfConformance.PdfA2b;
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.Write(new byte[] { 7, 8, 9 }, 0, 3); output.Position = 0;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }

    [Fact]
    public void UnsupportedOfficeExtensionsCannotHideUnsafeChartRelationships()
    {
        byte[] source = Xml(WordChartExtensions(Input()), "word/charts/_rels/chart1.xml.rels", xml => xml.Root!.Elements().Single().SetAttributeValue("TargetMode", "External"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }
}
