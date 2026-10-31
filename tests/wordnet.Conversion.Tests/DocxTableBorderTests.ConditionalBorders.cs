using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private static readonly (double Red, double Green, double Blue)[] ConditionalEdgeColors =
    {
        (1, 0, 0), (0, 170 / 255d, 0), (0, 0, 1), (170 / 255d, 0, 170 / 255d)
    };

    [Theory]
    [InlineData("all-edges", true, true, true, true)]
    [InlineData("edge-shading", true, true, true, true)]
    [InlineData("top-enabled", true, false, false, false)]
    [InlineData("top-disabled", false, false, false, false)]
    [InlineData("corner", true, false, true, false)]
    public void ConditionalOuterEdgesMatchIndependentColorsAndStayEditable(string name, bool red, bool green, bool blue, bool purple)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBorderConflict");
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        bool[] expected = { red, green, blue, purple };
        for (int index = 0; index < expected.Length; index++)
        {
            var color = ConditionalEdgeColors[index];
            Assert.Equal(expected[index], reference.GetPage(1).Paths.Any(path => HasStroke(path, color.Red, color.Green, color.Blue)));
            Assert.Equal(expected[index], actual.GetPage(1).Paths.Any(path => HasStroke(path, color.Red, color.Green, color.Blue)));
            if (expected[index])
            {
                AssertOuterEdgePosition(reference.GetPage(1), index);
                AssertOuterEdgePosition(actual.GetPage(1), index);
            }
        }
        Assert.Equal(reference.GetPage(1).Letters.Select(letter => letter.Value), actual.GetPage(1).Letters.Select(letter => letter.Value));
        if (name == "edge-shading")
        {
            Assert.True(PdfFill(reference, "E0E0FF")); Assert.True(PdfFill(actual, "E0E0FF"));
        }
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument sourceStyles = Part(source, "word/styles.xml"), savedStyles = Part(saved, "word/styles.xml");
        Assert.Equal(sourceStyles.Descendants(W + "tcBorders").Count(), savedStyles.Descendants(W + "tcBorders").Count());
        Assert.Equal(sourceStyles.Descendants(W + "tcBorders").Elements().Select(element => element.Name.LocalName).OrderBy(value => value, StringComparer.Ordinal),
            savedStyles.Descendants(W + "tcBorders").Elements().Select(element => element.Name.LocalName).OrderBy(value => value, StringComparer.Ordinal));
        if (name == "edge-shading") Assert.Equal("E0E0FF", (string?)savedStyles.Descendants(W + "shd").Single().Attribute(W + "fill"));
    }

    private static void AssertOuterEdgePosition(UglyToad.PdfPig.Content.Page page, int side)
    {
        var color = ConditionalEdgeColors[side];
        var paths = page.Paths.Where(path => HasStroke(path, color.Red, color.Green, color.Blue)).ToArray();
        var words = page.GetWords().ToArray(); Assert.NotEmpty(words); Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var rectangle = path.GetBoundingRectangle(); Assert.True(rectangle.HasValue);
            var edge = rectangle!.Value;
            Assert.True(side switch
            {
                0 => edge.Bottom >= words.Max(word => word.BoundingBox.Top),
                1 => edge.Top <= words.Min(word => word.BoundingBox.Bottom),
                2 => edge.Right <= words.Min(word => word.BoundingBox.Left),
                _ => edge.Left >= words.Max(word => word.BoundingBox.Right)
            });
        }
    }

    [Theory]
    [InlineData("parent-child", false)]
    [InlineData("parent-reset", true)]
    public void InheritedConditionalBorderDifferenceIsExplicit(string name, bool reset)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.DoesNotContain(reference.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        if (!reset)
        {
            output.WriteByte(42); output.Position = 0;
            Assert.Contains("Table.StyleInheritance", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
                DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 42 }, output.ToArray()); input.Position = 0; output.SetLength(0); output.Position = 0;
        }
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(reset ? FidelityMode.Strict : FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        if (!reset) Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleInheritance" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        Assert.Equal(!reset, actual.GetPage(1).Paths.Any(path => HasStroke(path, 1, 0, 0)));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument styles = Part(saved, "word/styles.xml");
        Assert.Contains(styles.Descendants(W + "basedOn"), element => (string?)element.Attribute(W + "val") == "Parent");
        if (reset) Assert.Contains(styles.Descendants(W + "tcBorders").Elements(W + "top"), edge => (string?)edge.Attribute(W + "val") == "nil");
    }

    [Fact]
    public void InteriorConditionalBordersStayEditableButStaticConflictsReject()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "bottom-interior.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "bottom-interior.pdf")));
        Assert.Contains(reference.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBorderConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray()); input.Position = 0; output.SetLength(0); output.Position = 0;
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBorderConflict" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument actual = PdfDocument.Open(output.ToArray()); Assert.Contains(actual.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        using var editable = new MemoryStream(); LoadStyle(source).Save(editable, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Schema(editable.ToArray()); Assert.Equal("FF0000", (string?)Part(editable.ToArray(), "word/styles.xml").Descendants(W + "tcBorders").Elements(W + "bottom").Single().Attribute(W + "color"));
    }

    [Fact]
    public void ConditionalAndWholeTableBordersKeepTheirConflictExplicit()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Descendants(W + "style").Single().Element(W + "name")!.AddAfterSelf(new XElement(W + "tblPr",
                new XElement(W + "tblBorders", new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "8"), new XAttribute(W + "color", "0000FF"))))));
        Schema(source); WordDocument document = LoadStyle(source);
        using var editable = new MemoryStream(); document.Save(editable, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken); Schema(editable.ToArray());
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBorderConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; output.SetLength(0); output.Position = 0;
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBorderConflict" && diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ConditionalAndDirectCellBordersKeepTheirConflictExplicit()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/document.xml", body =>
            body.Descendants(W + "tcPr").First().Add(new XElement(W + "tcBorders", new XElement(W + "top",
                new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "8"), new XAttribute(W + "color", "0000FF")))));
        Schema(source); byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal("0000FF", (string?)Part(saved, "word/document.xml").Descendants(W + "tcBorders").Elements(W + "top").Single().Attribute(W + "color"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.BorderConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; output.SetLength(0); output.Position = 0;
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBorderConflict" && diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void OverlappingCornerAndRowBordersRequireQualifiedPrecedence()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
        {
            XElement corner = new XElement(styles.Descendants(W + "tblStylePr").Single());
            corner.SetAttributeValue(W + "type", "nwCell");
            corner.Descendants(W + "top").Single().SetAttributeValue(W + "color", "0000FF");
            styles.Descendants(W + "style").Single().Add(corner);
        });
        source = EditThemePart(source, "word/document.xml", body => body.Descendants(W + "tblLook").Single().SetAttributeValue(W + "val", "00A0"));
        Schema(source); byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal(2, Part(saved, "word/styles.xml").Descendants(W + "tcBorders").Count());
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBorderConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void BorderOnlyConditionsRequireExplicitTableLook()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/document.xml", body => body.Descendants(W + "tblLook").Single().Remove());
        Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(input.CanRead);
    }

    [Fact]
    public void BorderOnlyBandRetainsDataAndReportsItsUnqualifiedDefaults()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Descendants(W + "tblStylePr").Single().SetAttributeValue(W + "type", "band1Horz"));
        Schema(source); byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal("band1Horz", (string?)Part(saved, "word/styles.xml").Descendants(W + "tblStylePr").Single().Attribute(W + "type"));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBorderConflict" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandDefault" && diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void BorderOnlyConditionalDefaultRejectsNewTablesWithoutMutatingDocument()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Descendants(W + "style").Single().SetAttributeValue(W + "default", "1"));
        source = EditThemePart(source, "word/document.xml", body => body.Descendants(W + "tblStyle").Single().Remove());
        Schema(source); WordDocument document = LoadStyle(source); byte[] before = SaveStyle(document, DocumentFormat.Docx);
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => document.AddTable(BlockedTableRows,
            cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => document.AddCrossTab(new[] { new CrossTabRecord("A", "B", 1m) },
            cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, SaveStyle(document, DocumentFormat.Docx));
    }

    [Fact]
    public void ConditionalBorderStyleChangesCannotBeIgnoredOrAliasedDuringComposition()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = File.ReadAllBytes(path);
        byte[] changed = EditThemePart(source, "word/styles.xml", styles => styles.Descendants(W + "tcBorders").Elements(W + "top").Single().SetAttributeValue(W + "color", "884422"));
        WordDocument first = LoadStyle(source), second = LoadStyle(changed); byte[] beforeFirst = SaveStyle(first, DocumentFormat.Docx), beforeSecond = SaveStyle(second, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)) { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains("Comparison.TableStyle", Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(first, second, options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(beforeFirst, SaveStyle(first, DocumentFormat.Docx)); Assert.Equal(beforeSecond, SaveStyle(second, DocumentFormat.Docx));
        first.Append(second, cancellationToken: TestContext.Current.CancellationToken);
        byte[] combined = SaveStyle(first, DocumentFormat.Docx); Schema(combined);
        Assert.Equal(2, Part(combined, "word/document.xml").Descendants(W + "tblStyle").Select(element => (string?)element.Attribute(W + "val")).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(Part(combined, "word/styles.xml").Descendants(W + "tcBorders").Elements(W + "top"), edge => (string?)edge.Attribute(W + "color") == "884422");
        Assert.Equal(beforeSecond, SaveStyle(second, DocumentFormat.Docx));
    }

    [Fact]
    public void ConditionalOuterBorderRecomputesAfterReportRowExpansion()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "all-edges.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/document.xml", body =>
        {
            XElement row = body.Descendants(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved); Assert.Equal(6, Part(saved, "word/document.xml").Descendants(W + "tr").Count());
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(path => HasStroke(path, 0, 170 / 255d, 0)));
    }

    [Fact]
    public void DuplicateConditionalBorderSetsFailBeforeOutput()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleBorders", "top-enabled.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
        {
            XElement borders = styles.Descendants(W + "tcBorders").Single(); borders.AddAfterSelf(new XElement(borders));
        });
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableStyle", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(input.CanRead);
    }
}
