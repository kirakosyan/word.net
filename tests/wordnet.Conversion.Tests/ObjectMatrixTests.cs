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
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static readonly string[] MatrixTwiceText = { "a", "b", "c", "a", "b", "c" };
    private static readonly string[] MatrixAlignmentNames = { "left", "right" };
    private static readonly string[] MatrixCellText = { "a", "b", "", "c" };
    private static XElement IndependentMatrix()
    {
        XNamespace m = MathObjectFormat.M;
        XElement Run(string text) => new XElement(m + "r", new XElement(m + "t", text));
        XElement Column(string value) => new XElement(m + "mc", new XElement(m + "mcPr", new XElement(m + "count", new XAttribute(m + "val", "1")), new XElement(m + "mcJc", new XAttribute(m + "val", value))));
        return new XElement(m + "m",
            new XElement(m + "mPr", new XElement(m + "plcHide", new XAttribute(m + "val", "1")),
                new XElement(m + "rSpRule", new XAttribute(m + "val", "1")), new XElement(m + "cGpRule", new XAttribute(m + "val", "2")),
                new XElement(m + "mcs", Column("left"), Column("right"))),
            new XElement(m + "mr", new XElement(m + "e", Run("a")), new XElement(m + "e", Run("b"))),
            new XElement(m + "mr", new XElement(m + "e"), new XElement(m + "e", Run("c"))));
    }

    [Fact]
    public void MatrixEditingCopiesInputsPreservesSnapshotsAndComposesGenuineOmml()
    {
        EquationExpression?[][] rows = { new EquationExpression?[] { EquationExpression.Text("a"), EquationExpression.Text("b") }, new EquationExpression?[] { null, EquationExpression.Text("c") } };
        EquationMatrixAlignment[] alignment = { EquationMatrixAlignment.Left, EquationMatrixAlignment.Right };
        EquationExpression expression = EquationExpression.Matrix(rows, alignment, EquationMatrixSpacing.OneAndHalf, EquationMatrixSpacing.Twice);
        rows[0][0] = EquationExpression.Text("changed"); alignment[0] = EquationMatrixAlignment.Center;
        Assert.Equal("a", expression.Children[0].Value); Assert.Equal(EquationMatrixAlignment.Left, expression.MatrixColumnAlignments[0]);
        Assert.Equal(2, expression.MatrixRows); Assert.Equal(2, expression.MatrixColumns); Assert.Equal("", expression.Children[2].Value);
        var source = WordDocument.Create(MathOptions()); DocumentObject original = source.AddEquation(expression, cancellationToken: TestContext.Current.CancellationToken);
        var replacement = EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("z") } });
        DocumentObject updated = source.UpdateEquation(original, replacement, TestContext.Current.CancellationToken);
        Assert.Equal("a", original.Equation!.Children[0].Value); Assert.Equal("z", updated.Equation!.Children[0].Value);
        source.UpdateEquation(updated, expression, TestContext.Current.CancellationToken);
        byte[] before = SaveObjects(source); var destination = WordDocument.Create(MathOptions());
        destination.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        destination.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(before, SaveObjects(source)); byte[] saved = SaveObjects(destination);
        using (var stream = new MemoryStream(saved)) using (var package = WordprocessingDocument.Open(stream, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml"))); XNamespace m = MathObjectFormat.M;
        Assert.Equal(2, xml.Descendants(m + "m").Count()); Assert.Equal(MatrixTwiceText, xml.Descendants(m + "t").Select(t => t.Value));
        Assert.All(xml.Descendants(m + "m"), matrix =>
        {
            Assert.Equal("1", (string?)matrix.Element(m + "mPr")!.Element(m + "plcHide")!.Attribute(m + "val"));
            Assert.Equal(MatrixAlignmentNames, matrix.Descendants(m + "mcJc").Select(e => (string?)e.Attribute(m + "val")));
            Assert.Empty(matrix.Elements(m + "mr").Last().Elements(m + "e").First().Elements());
        });
        WordDocument reread = LoadObjects(saved); Assert.Equal(2, reread.Objects.Count);
        Assert.All(reread.Objects, item => Assert.Equal("matrix(a, b; , c)", MathObjectFormat.Description(item.Equation!)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => destination.UpdateEquation(destination.Objects[0], replacement, cancelled.Token));
        Assert.Equal(saved, SaveObjects(destination));
    }

    [Fact]
    public void IndependentlyConstructedSchemaValidMatrixImportsWithAlignmentAndSpacing()
    {
        var document = WordDocument.Create(MathOptions()); document.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        XNamespace m = MathObjectFormat.M;
        byte[] source = Xml(SaveObjects(document), "word/document.xml", xml => xml.Descendants(m + "oMath").Single().ReplaceNodes(IndependentMatrix()));
        using (var input = new MemoryStream(source)) using (var package = WordprocessingDocument.Open(input, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        EquationExpression matrix = Assert.Single(LoadObjects(source).Objects).Equation!;
        Assert.Equal(EquationKind.Matrix, matrix.Kind); Assert.Equal(2, matrix.MatrixRows); Assert.Equal(2, matrix.MatrixColumns);
        Assert.Equal(EquationMatrixSpacing.OneAndHalf, matrix.MatrixRowSpacing); Assert.Equal(EquationMatrixSpacing.Twice, matrix.MatrixColumnSpacing);
        Assert.Equal(new[] { EquationMatrixAlignment.Left, EquationMatrixAlignment.Right }, matrix.MatrixColumnAlignments);
        Assert.Equal(MatrixCellText, matrix.Children.Select(child => child.Value));
    }

    [Fact]
    public void MatrixImportUsesStandardPropertiesWhenTheyAreOmitted()
    {
        XNamespace m = MathObjectFormat.M; XElement matrix = IndependentMatrix(); matrix.Element(m + "mPr")!.Remove();
        matrix.Elements(m + "mr").Last().Elements(m + "e").First().Add(new XElement(m + "r", new XElement(m + "t", "d")));
        EquationExpression expression = MathObjectFormat.Read(new XElement(m + "oMath", matrix), Context()).Expression;
        Assert.Equal(EquationMatrixSpacing.Standard, expression.MatrixRowSpacing); Assert.Equal(EquationMatrixSpacing.Standard, expression.MatrixColumnSpacing);
        Assert.All(expression.MatrixColumnAlignments, alignment => Assert.Equal(EquationMatrixAlignment.Center, alignment));
        Assert.Equal("d", expression.Children[2].Value);
    }

    [Fact]
    public void MaximumMatrixCellGridFitsSerializedTreeAndReopensWithoutMutatingCallerArrays()
    {
        EquationExpression?[][] cells = Enumerable.Range(0, 32).Select(_ => Enumerable.Range(0, 32).Select(_ => (EquationExpression?)EquationExpression.Text("x", false)).ToArray()).ToArray();
        var matrix = EquationExpression.Matrix(cells); cells[31][31] = EquationExpression.Text("y");
        var source = WordDocument.Create(MathOptions()); source.AddEquation(matrix, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(source); EquationExpression restored = Assert.Single(LoadObjects(saved).Objects).Equation!;
        Assert.Equal(32, restored.MatrixRows); Assert.Equal(32, restored.MatrixColumns); Assert.Equal("x", restored.Children[restored.Children.Count - 1].Value);
        using var stream = new MemoryStream(saved); using var package = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MatrixGeometryUsesColumnEdgesAndFontMathAxisWithoutOverlappingRows()
    {
        var matrix = EquationExpression.Matrix(new[] {
            new EquationExpression?[] { EquationExpression.Text("88", false), EquationExpression.Text("1", false) },
            new EquationExpression?[] { EquationExpression.Text("1", false), EquationExpression.Text("88", false) }
        }, new[] { EquationMatrixAlignment.Right, EquationMatrixAlignment.Left }, EquationMatrixSpacing.Twice, EquationMatrixSpacing.OneAndHalf);
        var (visual, font) = MathVisual(matrix); var glyphs = visual.Text.Where(t => t.NativeShape?.Glyphs.Any(g => g.Glyph >= 0) == true).ToArray();
        Assert.Equal(6, glyphs.Length);
        double End(PositionedText text) => text.X + text.NativeShape!.Width * text.Size;
        Assert.Equal(End(glyphs[1]), End(glyphs[3]), 6); Assert.Equal(glyphs[2].X, glyphs[4].X, 6);
        Assert.Equal(18, glyphs[2].X - End(glyphs[1]), 6);
        Assert.Equal(font.Constants[1] * 12.0 / font.Units, (visual.Ascent - visual.Descent) / 2, 6);
        Assert.True(glyphs[0].Y < glyphs[3].Y);
        var upper = font.Bounds(glyphs[0].NativeShape!.Glyphs.Single().Glyph); var lower = font.Bounds(glyphs[3].NativeShape!.Glyphs.Single().Glyph);
        Assert.True(glyphs[0].Y - upper.Bottom * glyphs[0].Size / font.Units < glyphs[3].Y - lower.Top * glyphs[3].Size / font.Units);
        var nested = EquationExpression.Delimiter(EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")), EquationExpression.Root(EquationExpression.Text("y")) } }), "[", "]");
        Assert.NotEmpty(MathVisual(nested).Visual.Shapes);
    }

    [Fact]
    public void MatrixProducesLogicalPdfTextAndManagedXpsSvgVectors()
    {
        var document = WordDocument.Create(MathOptions()); document.AddEquation(EquationExpression.Matrix(new[] {
            new EquationExpression?[] { EquationExpression.Text("1", false), EquationExpression.Text("2", false) },
            new EquationExpression?[] { EquationExpression.Text("3", false), EquationExpression.Text("4", false) }
        }), cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = new MemoryStream(); document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using (var read = UglyToad.PdfPig.PdfDocument.Open(pdf.ToArray()))
        {
            var pdfPage = Assert.Single(read.GetPages()); NameToken actualText = NameToken.Create("ActualText");
            var mark = Assert.Single(pdfPage.Operations.OfType<BeginMarkedContentWithProperties>(), op => op.Properties?.ContainsKey(actualText) == true);
            Assert.True(mark.Properties!.TryGet(actualText, out HexToken actual)); Assert.Equal("matrix(1, 2; 3, 4)", actual.Data);
            Assert.NotEmpty(pdfPage.Paths);
        }
        using var xps = new MemoryStream(); document.Save(xps, DocumentFormat.Xps, cancellationToken: TestContext.Current.CancellationToken);
        XDocument fixedPage = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")));
        Assert.Contains(fixedPage.Descendants(), e => e.Name.LocalName == "Glyphs" && e.Attribute("Indices") != null);
        using var input = new MemoryStream(SaveObjects(document)); var context = Context(MathOptions());
        var page = Assert.Single(FixedLayout.Create(DocxReader.Read(input, context), context));
        Assert.Contains("<path", Encoding.UTF8.GetString(SvgPreview.Write(page, context)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ragged", false)]
    [InlineData("columns", false)]
    [InlineData("placeholder", true)]
    [InlineData("baseline", true)]
    [InlineData("spacing", true)]
    [InlineData("width", true)]
    [InlineData("unknown", true)]
    [InlineData("cellAttribute", false)]
    [InlineData("cellText", false)]
    public void MatrixImportRejectsMalformedAndUnsupportedVariants(string kind, bool unsupported)
    {
        XNamespace m = MathObjectFormat.M; XElement matrix = IndependentMatrix(); XElement properties = matrix.Element(m + "mPr")!;
        if (kind == "ragged") matrix.Elements(m + "mr").First().Add(new XElement(m + "e"));
        else if (kind == "columns") properties.Descendants(m + "count").First().SetAttributeValue(m + "val", "3");
        else if (kind == "placeholder") properties.Element(m + "plcHide")!.Remove();
        else if (kind == "baseline") properties.AddFirst(new XElement(m + "baseJc", new XAttribute(m + "val", "top")));
        else if (kind == "spacing") properties.Element(m + "rSpRule")!.SetAttributeValue(m + "val", "3");
        else if (kind == "width") properties.Add(new XElement(m + "cSp", new XAttribute(m + "val", "20")));
        else if (kind == "cellAttribute") matrix.Descendants(m + "e").First().SetAttributeValue(m + "val", "unexpected");
        else if (kind == "cellText") matrix.Descendants(m + "e").First().Add("unexpected");
        else properties.Add(new XElement(m + "unknown"));
        XElement math = new XElement(m + "oMath", matrix);
        if (unsupported) Assert.Throws<NotSupportedException>(() => MathObjectFormat.Read(math, Context()));
        else Assert.Throws<InvalidDataException>(() => MathObjectFormat.Read(math, Context()));
    }

    [Fact]
    public void UnsupportedMatrixConversionReportsLossAndStrictModeLeavesOutputUntouched()
    {
        XNamespace m = MathObjectFormat.M; var document = WordDocument.Create(MathOptions());
        document.AddParagraph("Before", cancellationToken: TestContext.Current.CancellationToken);
        document.AddEquation(EquationExpression.Text("x"), cancellationToken: TestContext.Current.CancellationToken);
        document.AddParagraph("After", cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = Xml(SaveObjects(document), "word/document.xml", xml =>
        {
            XElement matrix = IndependentMatrix(); matrix.Element(m + "mPr")!.Element(m + "plcHide")!.Remove();
            xml.Descendants(m + "oMath").Single().ReplaceNodes(matrix);
        });
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        var result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Object.UnsupportedLoss");
        Assert.Contains("Before", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        Assert.Contains("After", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        using var strictInput = new MemoryStream(source); using var sentinel = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, sentinel, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, sentinel.ToArray()); Assert.Throws<NotSupportedException>(() => LoadObjects(source));
    }

    [Fact]
    public void MatrixRenderingRequiresCallerSelectedMathFontAndKeepsOutputUntouched()
    {
        var document = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        document.AddEquation(EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("x") } }), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Equation.Font", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }

    [Fact]
    public void MatrixTextCannotInjectOmmlElements()
    {
        const string value = "<m:f>&";
        var document = WordDocument.Create(MathOptions());
        document.AddEquation(EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text(value, false) } }), cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(document); XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml"))); XNamespace m = MathObjectFormat.M;
        Assert.Equal(value, Assert.Single(xml.Descendants(m + "t")).Value); Assert.Empty(xml.Descendants(m + "f"));
        Assert.Equal(value, Assert.Single(LoadObjects(saved).Objects).Equation!.Children[0].Value);
    }

    [Fact]
    public void MatrixFactoriesEnforceRectangularDimensionsAndImmutableBounds()
    {
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(Array.Empty<EquationExpression?[]>()));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("x") }, Array.Empty<EquationExpression?>() }));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(new[] { new EquationExpression?[] { null } }));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Row(EquationExpression.Text("")) } }));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("x") } }, new[] { EquationMatrixAlignment.Left, EquationMatrixAlignment.Right }));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(Enumerable.Range(0, 65).Select(_ => new EquationExpression?[] { EquationExpression.Text("x") }).ToArray()));
        Assert.Throws<ArgumentException>(() => EquationExpression.Matrix(Enumerable.Range(0, 33).Select(_ => Enumerable.Repeat<EquationExpression?>(EquationExpression.Text("x"), 33).ToArray()).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquationExpression.Matrix(new[] { new EquationExpression?[] { EquationExpression.Text("x") } }, rowSpacing: (EquationMatrixSpacing)9));
    }
}
