using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private static readonly (double, double, double)[] StyleColors = { (1, 0, 0), (0, 0, 1), (0, 1, 0), (1, 1, 0), (0, 1, 1), (1, 0, 1) };
    private static string TableStyles(string borders = Borders, string derivedProperties = "") =>
        "<w:styles xmlns:w='" + W + "'><w:style w:type='table' w:styleId='Base'><w:name w:val='Base'/>"
        + "<w:tblPr>" + borders + "</w:tblPr></w:style><w:style w:type='table' w:styleId='Derived'><w:name w:val='Derived'/>"
        + "<w:basedOn w:val='Base'/>" + derivedProperties + "</w:style></w:styles>";

    private static byte[] StyledInput(string styles, Action<XElement>? edit = null, string firstCellProperties = "")
    {
        XDocument source = Part(Input(firstCellProperties: firstCellProperties), "word/document.xml");
        XElement table = source.Descendants(W + "tbl").Single(); XElement properties = table.Element(W + "tblPr")!;
        properties.Element(W + "tblBorders")!.Remove(); properties.AddFirst(new XElement(W + "tblStyle", new XAttribute(W + "val", "Derived")));
        edit?.Invoke(table);
        return TestDocuments.Docx(source.ToString(SaveOptions.DisableFormatting), styles: styles);
    }

    private static WordDocument LoadStyle(byte[] source)
    {
        using var input = new MemoryStream(source);
        return WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
    }
    private static byte[] SaveStyle(WordDocument document, DocumentFormat format)
    { using var output = new MemoryStream(); document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }

    [Fact]
    public void InheritedTableStyleRemainsEditableAndMatchesReferenceBorderColors()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "table-style.docx")); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleStaticOutput" && diagnostic.Severity == DiagnosticSeverity.Info);
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "table-style.pdf")));
        foreach (var color in StyleColors)
        {
            Assert.Contains(actual.GetPage(1).Paths, path => HasStroke(path, color.Item1, color.Item2, color.Item3));
            Assert.Contains(reference.GetPage(1).Paths, path => HasStroke(path, color.Item1, color.Item2, color.Item3));
        }
        Assert.Equal("ABCD", actual.GetPage(1).Text.Replace("\n", ""));
        Assert.Equal("ABCD", reference.GetPage(1).Text.Replace("\n", ""));

        WordDocument document = LoadStyle(source); byte[] before = SaveStyle(document, DocumentFormat.Docx); Schema(before);
        XDocument body = Part(before, "word/document.xml"), styles = Part(before, "word/styles.xml");
        XElement table = body.Descendants(W + "tbl").Single();
        string id = (string)table.Element(W + "tblPr")!.Element(W + "tblStyle")!.Attribute(W + "val")!;
        Assert.Null(table.Element(W + "tblPr")!.Element(W + "tblBorders"));
        XElement derived = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == id);
        string parentId = (string)derived.Element(W + "basedOn")!.Attribute(W + "val")!;
        XElement parent = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == parentId);
        Assert.Equal("FF0000", (string?)parent.Element(W + "tblPr")!.Element(W + "tblBorders")!.Element(W + "top")!.Attribute(W + "color"));
        parent.Element(W + "tblPr")!.Element(W + "tblBorders")!.Element(W + "top")!.SetAttributeValue(W + "color", "884422");
        byte[] edited = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(edited);
        using PdfDocument changed = PdfDocument.Open(Convert(edited, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.Contains(changed.GetPage(1).Paths, path => HasStroke(path, 136 / 255.0, 68 / 255.0, 34 / 255.0));
        Assert.DoesNotContain(changed.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        Assert.Equal(before, SaveStyle(document, DocumentFormat.Docx));
    }

    [Fact]
    public void ConflictingStyleIdentifiersAreRemappedDuringComposition()
    {
        WordDocument first = LoadStyle(StyledInput(TableStyles()));
        WordDocument second = LoadStyle(StyledInput(TableStyles(Borders.Replace("FF0000", "884422"))));
        byte[] secondBefore = SaveStyle(second, DocumentFormat.Docx);
        first.Append(second, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = SaveStyle(first, DocumentFormat.Docx); Schema(word);
        XDocument body = Part(word, "word/document.xml"), styles = Part(word, "word/styles.xml");
        string[] references = body.Descendants(W + "tbl").Select(table => (string)table.Element(W + "tblPr")!.Element(W + "tblStyle")!.Attribute(W + "val")!).ToArray();
        Assert.Equal(2, references.Distinct(StringComparer.Ordinal).Count());
        string Color(string id)
        {
            XElement derived = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == id);
            string parent = (string)derived.Element(W + "basedOn")!.Attribute(W + "val")!;
            return (string)styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == parent)
                .Element(W + "tblPr")!.Element(W + "tblBorders")!.Element(W + "top")!.Attribute(W + "color")!;
        }
        Assert.Equal("FF0000", Color(references[0])); Assert.Equal("884422", Color(references[1]));
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(first, DocumentFormat.Pdf));
        var paths = Enumerable.Range(1, pdf.NumberOfPages).SelectMany(page => pdf.GetPage(page).Paths).ToArray();
        Assert.Contains(paths, path => HasStroke(path, 1, 0, 0));
        Assert.Contains(paths, path => HasStroke(path, 136 / 255.0, 68 / 255.0, 34 / 255.0));
        Assert.Equal(secondBefore, SaveStyle(second, DocumentFormat.Docx));
        byte[] before = SaveStyle(first, DocumentFormat.Docx);
        Assert.Contains("Table.StyleRestyle", Assert.Throws<NotSupportedException>(() => first.Append(second, CompositionStyle.UseDestination, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, SaveStyle(first, DocumentFormat.Docx));
    }

    [Fact]
    public void DestinationRestylingRejectsStyledFragmentsWithoutChangingEitherSession()
    {
        string placeholder = "<w:p><w:bookmarkStart w:id='1' w:name='Slot'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument target = LoadStyle(TestDocuments.Docx(TestDocuments.Body(placeholder)));
        WordDocument fragment = LoadStyle(StyledInput(TableStyles()));
        byte[] targetBefore = SaveStyle(target, DocumentFormat.Docx), sourceBefore = SaveStyle(fragment, DocumentFormat.Docx);
        var options = new FragmentInsertionOptions { Style = CompositionStyle.UseDestination };
        Assert.Contains("Table.StyleRestyle", Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions(
            "Slot", fragment, options, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(targetBefore, SaveStyle(target, DocumentFormat.Docx));
        Assert.Equal(sourceBefore, SaveStyle(fragment, DocumentFormat.Docx));
    }

    [Fact]
    public void ChangedTableStylesCannotBeHiddenByComparisonOptions()
    {
        WordDocument original = LoadStyle(StyledInput(TableStyles()));
        WordDocument revised = LoadStyle(StyledInput(TableStyles(Borders.Replace("FF0000", "884422"))));
        byte[] beforeOriginal = SaveStyle(original, DocumentFormat.Docx), beforeRevised = SaveStyle(revised, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains("Comparison.TableStyle", Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, revised, options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        WordDocument unchanged = DocumentComparison.Compare(original, LoadStyle(StyledInput(TableStyles())), options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Empty(unchanged.Revisions);
        Assert.Equal(beforeOriginal, SaveStyle(original, DocumentFormat.Docx));
        Assert.Equal(beforeRevised, SaveStyle(revised, DocumentFormat.Docx));
    }

    [Fact]
    public void ChildStyleCanExplicitlyDisableAnInheritedBorder()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableBorders");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "table-style-reset.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "table-style-reset.pdf")));
        using PdfDocument actual = PdfDocument.Open(Convert(source, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.DoesNotContain(reference.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        Assert.DoesNotContain(actual.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));
        foreach (var color in StyleColors.Skip(1))
        {
            Assert.Contains(reference.GetPage(1).Paths, path => HasStroke(path, color.Item1, color.Item2, color.Item3));
            Assert.Contains(actual.GetPage(1).Paths, path => HasStroke(path, color.Item1, color.Item2, color.Item3));
        }
        byte[] word = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(word);
        XDocument body = Part(word, "word/document.xml"), styles = Part(word, "word/styles.xml");
        string id = (string)body.Descendants(W + "tblStyle").Single().Attribute(W + "val")!;
        XElement derived = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == id);
        Assert.Equal("nil", (string?)derived.Element(W + "tblPr")!.Element(W + "tblBorders")!.Element(W + "top")!.Attribute(W + "val"));
        string parentId = (string)derived.Element(W + "basedOn")!.Attribute(W + "val")!;
        Assert.Equal("FF0000", (string?)styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == parentId)
            .Element(W + "tblPr")!.Element(W + "tblBorders")!.Element(W + "top")!.Attribute(W + "color"));
    }

    [Fact]
    public void InheritedStyleAppearanceSurvivesReportRowExpansion()
    {
        byte[] source = StyledInput(TableStyles(), table =>
        {
            XElement row = table.Elements(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = SaveStyle(document, DocumentFormat.Docx); Schema(word);
        XElement table = Part(word, "word/document.xml").Descendants(W + "tbl").Single();
        Assert.Equal(4, table.Elements(W + "tr").Count()); Assert.NotNull(table.Element(W + "tblPr")!.Element(W + "tblStyle"));
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Equal("ABCDCDCD", pdf.GetPage(1).Text);
        Assert.Contains(pdf.GetPage(1).Paths, path => HasStroke(path, 0, 1, 1));
        Assert.Contains(pdf.GetPage(1).Paths, path => HasStroke(path, 0, 1, 0));
    }

    [Fact]
    public void InheritedClearTableShadingIsRetainedAndEditableOutputLossIsExplicit()
    {
        byte[] source = StyledInput(TableStyles(Shading)); Schema(source);
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(IsReferenceFill));
        WordDocument document = LoadStyle(source); byte[] word = SaveStyle(document, DocumentFormat.Docx); Schema(word);
        XDocument body = Part(word, "word/document.xml"), styles = Part(word, "word/styles.xml");
        string id = (string)body.Descendants(W + "tblStyle").Single().Attribute(W + "val")!;
        XElement derived = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == id);
        string parentId = (string)derived.Element(W + "basedOn")!.Attribute(W + "val")!;
        Assert.Equal("FFF3C0", (string?)styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == parentId)
            .Element(W + "tblPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleOutput", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Odt, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Odt,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleOutput");
        Assert.Contains(Part(lenient.ToArray(), "content.xml").Descendants().Attributes(), attribute => attribute.Name.LocalName == "background-color" && attribute.Value == "#FFF3C0");
    }

    [Theory]
    [InlineData("conditional", "Docx.TableStyleData")]
    [InlineData("look", "Docx.TableLook")]
    [InlineData("direct", "Table.StyleConflict")]
    [InlineData("cell", "Table.BorderConflict")]
    [InlineData("cell-shading", "Table.ShadingConflict")]
    [InlineData("default", "Docx.TableStyleDefault")]
    [InlineData("missing", "Docx.TableStyleMissing")]
    public void UnsupportedStyleCombinationsFailBeforeWriting(string kind, string diagnostic)
    {
        string styles = kind == "missing" ? "<w:styles xmlns:w='" + W + "'/>" : TableStyles(kind == "cell-shading" ? Shading : Borders, derivedProperties:
            kind == "conditional" ? "<w:tblStylePr w:type='wholeTable'><w:tcPr><w:shd w:val='clear' w:fill='FFFF00'/></w:tcPr></w:tblStylePr>" : "");
        if (kind == "default") styles = styles.Replace("w:styleId='Derived'", "w:styleId='Derived' w:default='1'");
        byte[] source = StyledInput(styles, table =>
        {
            XElement properties = table.Element(W + "tblPr")!;
            if (kind == "default") properties.Element(W + "tblStyle")!.Remove();
            if (kind == "look") properties.Add(new XElement(W + "tblLook", new XAttribute(W + "firstRow", "1")));
            if (kind == "direct") properties.Add(new XElement(Part(Input(), "word/document.xml").Descendants(W + "tblBorders").Single()));
        }, kind == "cell" ? "<w:tcBorders><w:top w:val='single' w:sz='8' w:color='000000'/></w:tcBorders>"
            : kind == "cell-shading" ? "<w:shd w:val='nil'/>" : "");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void CyclicAndOverdeepTableStylesFailWithoutWriting()
    {
        string cycle = "<w:styles xmlns:w='" + W + "'><w:style w:type='table' w:styleId='Base'><w:basedOn w:val='Derived'/></w:style>"
            + "<w:style w:type='table' w:styleId='Derived'><w:basedOn w:val='Base'/></w:style></w:styles>";
        string chain = "<w:styles xmlns:w='" + W + "'>" + string.Concat(Enumerable.Range(0, 70).Select(index =>
            "<w:style w:type='table' w:styleId='" + (index == 0 ? "Derived" : "S" + index) + "'>"
            + (index == 69 ? "" : "<w:basedOn w:val='S" + (index + 1) + "'/>") + "</w:style>")) + "</w:styles>";
        foreach (string styles in new[] { cycle, chain })
        {
            using var input = new MemoryStream(StyledInput(styles)); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
            Assert.Contains("Docx.TableStyleCycle", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
                DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort, maximumNestingDepth: 1000), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
    }

    private static bool HasStroke(UglyToad.PdfPig.Graphics.PdfPath path, double red, double green, double blue)
    {
        if (!path.IsStroked || path.StrokeColor == null) return false;
        var color = path.StrokeColor.ToRGBValues();
        return Math.Abs(color.r - red) < 0.01 && Math.Abs(color.g - green) < 0.01 && Math.Abs(color.b - blue) < 0.01;
    }
}
