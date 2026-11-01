using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig.Graphics.Operations.MarkedContent;
using UglyToad.PdfPig.Tokens;
using WordNet.Formats;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static XElement IndependentNary(string symbol = "∑", string placement = "undOvr")
    {
        XNamespace m = MathObjectFormat.M;
        XElement Run(string text) => new XElement(m + "r", new XElement(m + "t", text));
        return new XElement(m + "nary", new XElement(m + "naryPr", new XElement(m + "chr", new XAttribute(m + "val", symbol)),
                new XElement(m + "limLoc", new XAttribute(m + "val", placement))),
            new XElement(m + "sub", Run("i=1")), new XElement(m + "sup", Run("n")), new XElement(m + "e", Run("x")));
    }

    [Theory]
    [InlineData(EquationNaryOperator.Summation, EquationLimitPlacement.AboveBelow, "∑")]
    [InlineData(EquationNaryOperator.Summation, EquationLimitPlacement.Side, "∑")]
    [InlineData(EquationNaryOperator.Product, EquationLimitPlacement.AboveBelow, "∏")]
    [InlineData(EquationNaryOperator.Product, EquationLimitPlacement.Side, "∏")]
    [InlineData(EquationNaryOperator.Integral, EquationLimitPlacement.AboveBelow, "∫")]
    [InlineData(EquationNaryOperator.Integral, EquationLimitPlacement.Side, "∫")]
    public void NaryEditingPreservesImmutableSnapshotsAndWritesGenuineOmml(EquationNaryOperator operation, EquationLimitPlacement placement, string symbol)
    {
        var expression = EquationExpression.Nary(operation, EquationExpression.Text("x"), EquationExpression.Text("1"), EquationExpression.Text("n"), placement);
        var document = WordDocument.Create(MathOptions()); var original = document.AddEquation(expression, display: false, cancellationToken: TestContext.Current.CancellationToken);
        var changed = document.UpdateEquation(original, EquationExpression.Nary(operation, EquationExpression.Text("y"), placement: placement), TestContext.Current.CancellationToken);
        Assert.Equal("x", original.Equation!.Children[0].Value); Assert.Equal("y", changed.Equation!.Children[0].Value);
        document.UpdateEquation(changed, expression, TestContext.Current.CancellationToken);
        byte[] source = SaveObjects(document); var composed = WordDocument.Create(MathOptions()); composed.Append(document, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(source, SaveObjects(document)); byte[] bytes = SaveObjects(composed);
        using (var input = new MemoryStream(bytes)) using (var package = WordprocessingDocument.Open(input, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        XNamespace m = MathObjectFormat.M; XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(bytes, "word/document.xml")));
        XElement nary = Assert.Single(xml.Descendants(m + "nary")); Assert.Equal(symbol, (string?)nary.Element(m + "naryPr")!.Element(m + "chr")!.Attribute(m + "val"));
        Assert.Equal(placement == EquationLimitPlacement.Side ? "subSup" : "undOvr", (string?)nary.Element(m + "naryPr")!.Element(m + "limLoc")!.Attribute(m + "val"));
        Assert.Equal("1", nary.Element(m + "sub")!.Value); Assert.Equal("n", nary.Element(m + "sup")!.Value); Assert.Equal("x", nary.Element(m + "e")!.Value);
        EquationExpression restored = Assert.Single(LoadObjects(bytes).Objects).Equation!;
        Assert.Equal(operation, restored.NaryOperator); Assert.Equal(placement, restored.NaryLimitPlacement);
        Assert.True(restored.NaryHasLowerLimit); Assert.True(restored.NaryHasUpperLimit);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NaryOptionalLimitsAreExplicitAndRoundTrip(bool lower, bool upper)
    {
        var expression = EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("f"),
            lower ? EquationExpression.Text("0") : null, upper ? EquationExpression.Text("1") : null);
        var document = WordDocument.Create(MathOptions()); document.AddEquation(expression, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(document); XNamespace m = MathObjectFormat.M;
        XElement nary = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml"))).Descendants(m + "nary").Single();
        Assert.Equal(lower ? "0" : "1", (string?)nary.Element(m + "naryPr")!.Element(m + "subHide")!.Attribute(m + "val"));
        Assert.Equal(upper ? "0" : "1", (string?)nary.Element(m + "naryPr")!.Element(m + "supHide")!.Attribute(m + "val"));
        using (var input = new MemoryStream(saved)) using (var package = WordprocessingDocument.Open(input, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        EquationExpression restored = Assert.Single(LoadObjects(saved).Objects).Equation!;
        Assert.Equal(lower, restored.NaryHasLowerLimit); Assert.Equal(upper, restored.NaryHasUpperLimit);
        Assert.Equal(lower ? "0" : "", restored.Children[1].Value); Assert.Equal(upper ? "1" : "", restored.Children[2].Value);
    }

    [Fact]
    public void IndependentlyConstructedNaryImportsAsEditableSemantics()
    {
        var document = WordDocument.Create(MathOptions()); document.AddEquation(EquationExpression.Text("placeholder"), cancellationToken: TestContext.Current.CancellationToken);
        XNamespace m = MathObjectFormat.M;
        byte[] input = Xml(SaveObjects(document), "word/document.xml", xml => xml.Descendants(m + "oMath").Single().ReplaceNodes(IndependentNary()));
        using (var stream = new MemoryStream(input)) using (var package = WordprocessingDocument.Open(stream, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        EquationExpression expression = Assert.Single(LoadObjects(input).Objects).Equation!;
        Assert.Equal(EquationKind.Nary, expression.Kind); Assert.Equal(EquationNaryOperator.Summation, expression.NaryOperator);
        Assert.Equal(EquationLimitPlacement.AboveBelow, expression.NaryLimitPlacement);
        Assert.Equal("x", expression.Children[0].Value); Assert.Equal("i=1", expression.Children[1].Value); Assert.Equal("n", expression.Children[2].Value);
    }

    [Theory]
    [InlineData(EquationLimitPlacement.Side)]
    [InlineData(EquationLimitPlacement.AboveBelow)]
    public void NaryLimitsHaveSeparatedFontBasedBaselines(EquationLimitPlacement placement)
    {
        var expression = EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x", false), EquationExpression.Text("1", false), EquationExpression.Text("2", false), placement);
        var (visual, font) = MathVisual(expression); var glyphs = visual.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).ToArray();
        Assert.Equal(4, glyphs.Length); PositionedText op = glyphs[0];
        PositionedText lower = placement == EquationLimitPlacement.Side ? glyphs[1] : glyphs[2];
        PositionedText upper = placement == EquationLimitPlacement.Side ? glyphs[2] : glyphs[1];
        Assert.True(upper.Y < op.Y); Assert.True(lower.Y > op.Y); Assert.True(glyphs[3].X > op.X);
        if (placement == EquationLimitPlacement.AboveBelow)
        {
            var ob = font.Bounds(op.NativeShape!.Glyphs.Single().Glyph); var ub = font.Bounds(upper.NativeShape!.Glyphs.Single().Glyph); var lb = font.Bounds(lower.NativeShape!.Glyphs.Single().Glyph);
            double topGap = op.Y - ob.Top * op.Size / font.Units - (upper.Y - ub.Bottom * upper.Size / font.Units);
            double bottomGap = lower.Y - lb.Top * lower.Size / font.Units - (op.Y - ob.Bottom * op.Size / font.Units);
            Assert.True(topGap + 0.000001 >= font.Constants[14] * 12.0 / font.Units);
            Assert.True(bottomGap + 0.000001 >= font.Constants[16] * 12.0 / font.Units);
            double upperRise = op.Y - ob.Top * op.Size / font.Units - upper.Y;
            double lowerDrop = lower.Y - (op.Y - ob.Bottom * op.Size / font.Units);
            Assert.True(upperRise + 0.000001 >= font.Constants[15] * 12.0 / font.Units);
            Assert.True(lowerDrop + 0.000001 >= font.Constants[17] * 12.0 / font.Units);
        }
    }

    [Fact]
    public void NaryDisplayUsesFontVariantsAndReportsAnInsufficientVariant()
    {
        var options = MathOptions(); var context = Context(options); byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf"));
        var font = new MathFont(bytes, context); var layout = new MathLayout(font, 0, bytes, context);
        var integral = EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x"));
        ObjectVisual visual = layout.Create(new DocumentObjectData(integral, true, 12, ""));
        int actual = visual.Text.First(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).NativeShape!.Glyphs.Single().Glyph;
        // Pinned independently with fontTools; eng/verify-nary-font.py rechecks these values.
        Assert.Equal(1800, font.DisplayOperatorMinHeight); Assert.Equal(1699, actual);
        Assert.Equal(2347, font.Vertical[font.Character(0x222b)].Variants.Single(v => v.Glyph == 1699).Advance);
        Assert.DoesNotContain(context.Result(0).Diagnostics, d => d.Code == "Equation.NarySizeFallback");
        var fallbackContext = Context(MathOptions()); var fallbackFont = new MathFont(bytes, fallbackContext);
        var sum = EquationExpression.Nary(EquationNaryOperator.Summation, EquationExpression.Text("x"));
        ObjectVisual fallback = new MathLayout(fallbackFont, 0, bytes, fallbackContext).Create(new DocumentObjectData(sum, true, 12, ""));
        int fallbackGlyph = fallback.Text.First(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).NativeShape!.Glyphs.Single().Glyph;
        Assert.Equal(1647, fallbackGlyph);
        Assert.Contains(fallbackContext.Result(0).Diagnostics, d => d.Code == "Equation.NarySizeFallback");
        var strict = Context(new ConversionOptions()); var strictFont = new MathFont(bytes, strict);
        var error = Assert.Throws<NotSupportedException>(() => new MathLayout(strictFont, 0, bytes, strict).Create(new DocumentObjectData(sum, true, 12, "")));
        Assert.StartsWith("Equation.NarySizeFallback", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NaryDisplayUsesAQualifiedOperatorAssemblyWhenTheVariantIsTooSmall()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf"));
        int U16(int p) => bytes[p] * 256 + bytes[p + 1];
        int U32(int p) => checked(U16(p) * 65536 + U16(p + 2));
        int record = Enumerable.Range(0, U16(4)).Select(i => 12 + i * 16).Single(p => Encoding.ASCII.GetString(bytes, p, 4) == "MATH");
        int math = U32(record + 8), constants = math + U16(math + 4);
        bytes[constants + 6] = 0x0b; bytes[constants + 7] = 0xb8; // 3,000 font units exceeds the largest integral variant.
        var context = Context(MathOptions()); var font = new MathFont(bytes, context);
        var expression = EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x"));
        ObjectVisual visual = new MathLayout(font, 0, bytes, context).Create(new DocumentObjectData(expression, true, 12, ""));
        Assert.True(visual.Text.Count(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true) >= 4);
        Assert.DoesNotContain(context.Result(0).Diagnostics, d => d.Code == "Equation.NarySizeFallback");
    }

    [Theory]
    [InlineData("operator", true)]
    [InlineData("grow", true)]
    [InlineData("location", true)]
    [InlineData("hidden", true)]
    [InlineData("placeholder", true)]
    [InlineData("operand", true)]
    [InlineData("duplicate", false)]
    [InlineData("attribute", false)]
    public void NaryImportRejectsUnsupportedAndMalformedVariants(string failure, bool unsupported)
    {
        XNamespace m = MathObjectFormat.M; XElement nary = IndependentNary(); XElement properties = nary.Element(m + "naryPr")!;
        if (failure == "operator") properties.Element(m + "chr")!.SetAttributeValue(m + "val", "∐");
        else if (failure == "grow") properties.Add(new XElement(m + "grow", new XAttribute(m + "val", "1")));
        else if (failure == "location") properties.Element(m + "limLoc")!.Remove();
        else if (failure == "hidden") properties.Add(new XElement(m + "subHide", new XAttribute(m + "val", "1")));
        else if (failure == "placeholder") nary.Element(m + "sub")!.RemoveNodes();
        else if (failure == "operand") nary.Element(m + "e")!.RemoveNodes();
        else if (failure == "duplicate") nary.Add(new XElement(m + "sub"));
        else nary.Element(m + "e")!.SetAttributeValue(m + "val", "bad");
        XElement math = new XElement(m + "oMath", nary);
        if (unsupported) Assert.Throws<NotSupportedException>(() => MathObjectFormat.Read(math, Context()));
        else Assert.Throws<InvalidDataException>(() => MathObjectFormat.Read(math, Context()));
    }

    [Fact]
    public void UnsupportedNaryConversionReportsLossAndStrictModeLeavesOutputUntouched()
    {
        XNamespace m = MathObjectFormat.M; var document = WordDocument.Create(MathOptions());
        document.AddParagraph("Before", cancellationToken: TestContext.Current.CancellationToken);
        document.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        document.AddParagraph("After", cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = Xml(SaveObjects(document), "word/document.xml", xml =>
        {
            XElement nary = IndependentNary(); nary.Element(m + "naryPr")!.Add(new XElement(m + "grow", new XAttribute(m + "val", "1")));
            xml.Descendants(m + "oMath").Single().ReplaceNodes(nary);
        });
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        var result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Object.UnsupportedLoss");
        Assert.Contains("Before", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal); Assert.Contains("After", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        using var strictInput = new MemoryStream(source); using var sentinel = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, sentinel, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, sentinel.ToArray()); Assert.Throws<NotSupportedException>(() => LoadObjects(source));
    }

    [Fact]
    public void NaryOperandsCanNestMatricesAndMissingGlyphsRemainExplicit()
    {
        var matrix = EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("1"), EquationExpression.Text("2") } });
        var expression = EquationExpression.Nary(EquationNaryOperator.Integral, matrix, placement: EquationLimitPlacement.Side);
        var document = WordDocument.Create(MathOptions()); document.AddEquation(expression, display: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(EquationKind.Matrix, Assert.Single(LoadObjects(SaveObjects(document)).Objects).Equation!.Children[0].Kind);
        Assert.NotEmpty(MathVisual(expression).Visual.Text);
        var outer = EquationExpression.Matrix(new[] { new EquationExpression?[] { expression } });
        var other = WordDocument.Create(MathOptions()); other.AddEquation(outer, display: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(EquationKind.Nary, Assert.Single(LoadObjects(SaveObjects(other)).Objects).Equation!.Children[0].Kind);
        Assert.NotEmpty(MathVisual(outer).Visual.Text);
        var missing = EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("🦄", false));
        var error = Assert.Throws<NotSupportedException>(() => MathVisual(missing)); Assert.StartsWith("Equation.Glyph", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NaryFactoriesEnforceOperandsEnumsAndCancellation()
    {
        Assert.Throws<ArgumentException>(() => EquationExpression.Nary(EquationNaryOperator.Summation, EquationExpression.Text("")));
        Assert.Throws<ArgumentException>(() => EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x"), EquationExpression.Text("")));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquationExpression.Nary((EquationNaryOperator)42, EquationExpression.Text("x")));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x"), placement: (EquationLimitPlacement)42));
        EquationExpression tree = EquationExpression.Text("x");
        for (int i = 0; i < 31; i++) tree = EquationExpression.Nary(EquationNaryOperator.Integral, tree);
        Assert.Throws<ArgumentException>(() => EquationExpression.Nary(EquationNaryOperator.Integral, tree));
        var document = WordDocument.Create(MathOptions()); byte[] before = SaveObjects(document);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.AddEquation(EquationExpression.Nary(EquationNaryOperator.Integral, EquationExpression.Text("x")), cancellationToken: cancelled.Token));
        Assert.Equal(before, SaveObjects(document));
    }

    [Fact]
    public void NaryManagedOutputHasLogicalPdfTextAndXpsSvgVectors()
    {
        var document = WordDocument.Create(MathOptions()); document.AddEquation(EquationExpression.Nary(EquationNaryOperator.Integral,
            EquationExpression.Text("x", false), EquationExpression.Text("0", false), EquationExpression.Text("1", false)), cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = new MemoryStream(); document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using (var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray()))
        {
            var page = Assert.Single(read.GetPages()); NameToken key = NameToken.Create("ActualText");
            var mark = Assert.Single(page.Operations.OfType<BeginMarkedContentWithProperties>(), op => op.Properties?.ContainsKey(key) == true);
            Assert.True(mark.Properties!.TryGet(key, out HexToken actual)); Assert.Equal("integral_(0)^(1)(x)", actual.Data); Assert.NotEmpty(page.Paths);
        }
        using var xps = new MemoryStream(); document.Save(xps, DocumentFormat.Xps, cancellationToken: TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), e => e.Name.LocalName == "Glyphs" && e.Attribute("Indices") != null);
        using var input = new MemoryStream(SaveObjects(document)); var context = Context(MathOptions());
        var fixedPageModel = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        Assert.Contains("<path", Encoding.UTF8.GetString(SvgPreview.Write(fixedPageModel, context)), StringComparison.Ordinal);
    }
}
