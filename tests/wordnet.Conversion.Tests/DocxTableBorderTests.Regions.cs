using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private const string H1 = "80AAFF", H2 = "AACCFF", V1 = "88EE88", V2 = "AAFFAA";
    private const string First = "FFE0E0", Last = "FF90B0", Left = "F0E050", Right = "F4B050";
    private static readonly string?[] DirectOverrides = { "DD4422", "1122CC" };
    private static readonly string[] BandProbeWords = { "A1", "A2", "B1", "C1" };
    private static readonly string?[] InheritedBandSizes = { "2", "1" };
    private static readonly string[] Horizontal = { H1, H1, H1, H1, H2, H2, H2, H2, H1, H1, H1, H1, H2, H2, H2, H2 };
    private static readonly string[] Vertical = { V1, V2, V1, V2, V1, V2, V1, V2, V1, V2, V1, V2, V1, V2, V1, V2 };

    [Theory]
    [InlineData("regions")]
    [InlineData("regions-overrides")]
    [InlineData("bands-h")]
    [InlineData("bands-v")]
    [InlineData("bands-both")]
    [InlineData("bands-header")]
    [InlineData("bands-disable-h")]
    [InlineData("bands-disable-v")]
    [InlineData("bands-flag-only")]
    [InlineData("bands-last-flag")]
    [InlineData("bands-first-col")]
    [InlineData("bands-last-col")]
    [InlineData("row-column-priority")]
    [InlineData("corner-first-row")]
    [InlineData("corner-first-column")]
    [InlineData("corner-both")]
    [InlineData("regions-two")]
    public void ConditionalRegionsMatchIndependentColorsAndRemainEditable(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        if (name is "regions" or "regions-overrides" or "bands-both" or "bands-disable-h" or "bands-disable-v" or "row-column-priority" or "corner-first-row" or "corner-first-column" or "corner-both" or "regions-two")
            Assert.Contains(result.Diagnostics, d => d.Code == "Table.StylePrecedence" && d.Severity == DiagnosticSeverity.Info);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Table.StyleBandDefault");
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        string[] expected = RegionColors(name); int columns = name == "regions-two" ? 2 : 4;
        for (int index = 0; index < expected.Length; index++)
        {
            string word = ((char)('A' + index % columns)).ToString() + (index / columns + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected[index], ColorAt(reference.GetPage(1), word));
            Assert.Equal(expected[index], ColorAt(actual.GetPage(1), word));
        }
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument sourceBody = Part(source, "word/document.xml"), savedBody = Part(saved, "word/document.xml");
        Assert.Equal((string?)sourceBody.Descendants(W + "tblLook").Single().Attribute(W + "val"), (string?)savedBody.Descendants(W + "tblLook").Single().Attribute(W + "val"));
        string[] sourceRegions = Part(source, "word/styles.xml").Descendants(W + "tblStylePr").Select(element => (string)element.Attribute(W + "type")!).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] savedRegions = Part(saved, "word/styles.xml").Descendants(W + "tblStylePr").Select(element => (string)element.Attribute(W + "type")!).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(sourceRegions, savedRegions);
        if (name == "regions-overrides")
            Assert.Equal(DirectOverrides, savedBody.Descendants(W + "tcPr").Elements(W + "shd").Select(element => (string?)element.Attribute(W + "fill")));
        if (name == "regions")
        {
            using var previewInput = new MemoryStream(source);
            DocumentPreview preview = new WordConverter().RenderPages(previewInput, DocumentFormat.Docx,
                options: new ConversionOptions(FidelityMode.Strict), cancellationToken: TestContext.Current.CancellationToken);
            string svg = Encoding.UTF8.GetString(Assert.Single(preview.Pages).GetBytes());
            Assert.Contains("#FF0000", svg, StringComparison.OrdinalIgnoreCase); Assert.Contains("#88EE88", svg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(preview.Result.Diagnostics, diagnostic => diagnostic.Code == "Table.StylePrecedence"); Assert.True(previewInput.CanRead);
        }
    }

    [Fact]
    public void NilCellShadingWithSeveralRegionsRemainsUnqualified()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "regions-override-nil.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "regions-override-nil.pdf")));
        Assert.Equal("FF0000", ColorAt(reference.GetPage(1), "A1"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void OmittedBandSizeRetainsEditableSourceAndReportsConflictingDefaults()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "bands-implicit.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "bands-implicit.pdf")));
        Assert.Equal(H1, ColorAt(reference.GetPage(1), "A1")); Assert.Equal(H2, ColorAt(reference.GetPage(1), "A2"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBandDefault", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandDefault" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument actual = PdfDocument.Open(lenient.ToArray());
        Assert.Equal(H1, ColorAt(actual.GetPage(1), "A1")); Assert.Equal(H2, ColorAt(actual.GetPage(1), "A2"));
        input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Empty(Part(saved, "word/styles.xml").Descendants(W + "tblStyleRowBandSize"));
        using var previewInput = new MemoryStream(source);
        Assert.Contains("Table.StyleBandDefault", Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(previewInput,
            DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.Strict), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.True(previewInput.CanRead);
    }

    [Theory]
    [InlineData("parent-row-child-empty", "-", "-", false)]
    [InlineData("parent-row-child-col", "-", Left, false)]
    [InlineData("parent-col-child-row", First, "-", false)]
    [InlineData("parent-row-child-row", "336699", "-", true)]
    public void InheritedRegionReferenceDifferencesAreExplicit(string name, string referenceTop, string referenceLowerLeft, bool fullyOverridden)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.Equal(referenceTop, ColorAt(reference.GetPage(1), "B1"));
        Assert.Equal(referenceLowerLeft, ColorAt(reference.GetPage(1), "A2"));
        byte[] output = fullyOverridden ? Convert(source, DocumentFormat.Pdf, FidelityMode.Strict) : ConvertWithInheritanceLoss(source);
        if (!fullyOverridden)
        {
            using var previewInput = new MemoryStream(source);
            Assert.Contains("Table.StyleInheritance", Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(previewInput,
                DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.Strict), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
            Assert.True(previewInput.CanRead);
        }
        using PdfDocument actual = PdfDocument.Open(output);
        Assert.Equal(fullyOverridden ? "336699" : First, ColorAt(actual.GetPage(1), "B1"));
        if (name == "parent-col-child-row") Assert.Equal(Left, ColorAt(actual.GetPage(1), "A2"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Contains(Part(saved, "word/styles.xml").Descendants(W + "basedOn"), element => (string?)element.Attribute(W + "val") == "Parent");
    }

    [Theory]
    [InlineData("parent-row-child-empty", "0000")]
    [InlineData("parent-row-child-col", "0080")]
    public void DisabledInheritedRegionsDoNotPreventQualifiedOutput(string name, string look)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] original = File.ReadAllBytes(Path.Combine(directory, name + ".docx"));
        XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        body.Descendants(W + "tblLook").Single().SetAttributeValue(W + "val", look);
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleInheritance");
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        foreach (string word in new[] { "A1", "B1", "A2", "B2" }) Assert.Equal(ColorAt(reference.GetPage(1), word), ColorAt(actual.GetPage(1), word));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal(look, (string?)Part(saved, "word/document.xml").Descendants(W + "tblLook").Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData("regions-one")]
    [InlineData("regions-one-row")]
    [InlineData("regions-one-col")]
    public void OverlappingSingleRowOrColumnRegionsRemainUnqualified(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.Equal("FF0000", ColorAt(reference.GetPage(1), "A1"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleRegionOverlap", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal("01E0", (string?)Part(saved, "word/document.xml").Descendants(W + "tblLook").Single().Attribute(W + "val"));
        Assert.Equal(12, Part(saved, "word/styles.xml").Descendants(W + "tblStylePr").Count());
    }

    [Theory]
    [InlineData("row-size-two", "A1", "A2", "A3")]
    [InlineData("column-size-two", "A1", "B1", "C1")]
    public void NondefaultBandSizesStayEditableWhileReferenceMismatchIsDiagnosed(string name, string first, string second, string third)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.NotEqual(ColorAt(reference.GetPage(1), first), ColorAt(reference.GetPage(1), second));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBandSize", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandSize" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument actual = PdfDocument.Open(lenient.ToArray());
        Assert.Equal(ColorAt(actual.GetPage(1), first), ColorAt(actual.GetPage(1), second));
        Assert.NotEqual(ColorAt(actual.GetPage(1), first), ColorAt(actual.GetPage(1), third));
        input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        string property = name.StartsWith("row", StringComparison.Ordinal) ? "tblStyleRowBandSize" : "tblStyleColBandSize";
        Assert.Equal("2", (string?)Part(saved, "word/styles.xml").Descendants(W + property).Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData("row-size-two", "tblStyleRowBandSize", "bands-h")]
    [InlineData("column-size-two", "tblStyleColBandSize", "bands-v")]
    public void ExplicitUnitBandSizesMatchTheIndependentSingleWidthControl(string name, string property, string referenceName)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] original = File.ReadAllBytes(Path.Combine(directory, name + ".docx"));
        XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        styles.Descendants(W + property).Single().SetAttributeValue(W + "val", "1");
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "Table.StyleBandSize" or "Table.StyleBandDefault");
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, referenceName + ".pdf")));
        foreach (string word in BandProbeWords) Assert.Equal(ColorAt(reference.GetPage(1), word), ColorAt(actual.GetPage(1), word));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal("1", (string?)Part(saved, "word/styles.xml").Descendants(W + property).Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData("row-size-zero", "tblStyleRowBandSize", "A1", "A2")]
    [InlineData("column-size-zero", "tblStyleColBandSize", "A1", "B1")]
    public void ZeroBandSizeIsRetainedAndItsStaticOutputIsExplicitlyUnqualified(string name, string property, string first, string second)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.NotEqual(ColorAt(reference.GetPage(1), first), ColorAt(reference.GetPage(1), second));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.StyleBandSize", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandSize");
        using PdfDocument pdf = PdfDocument.Open(lenient.ToArray()); Assert.Equal("-", ColorAt(pdf.GetPage(1), "A1"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal("0", (string?)Part(saved, "word/styles.xml").Descendants(W + property).Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData("row-size-two", "tblStyleRowBandSize", "A1", "A2", "A3", "A4")]
    [InlineData("column-size-two", "tblStyleColBandSize", "A1", "B1", "C1", "D1")]
    public void SizeThreeUsesBoundedBestEffortGroupingAndRetainsItsSource(string name, string property, string first, string second, string third, string fourth)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions");
        byte[] original = File.ReadAllBytes(Path.Combine(directory, name + ".docx"));
        XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        styles.Descendants(W + property).Single().SetAttributeValue(W + "val", "3");
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandSize");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal(ColorAt(pdf.GetPage(1), first), ColorAt(pdf.GetPage(1), second));
        Assert.Equal(ColorAt(pdf.GetPage(1), first), ColorAt(pdf.GetPage(1), third));
        Assert.NotEqual(ColorAt(pdf.GetPage(1), first), ColorAt(pdf.GetPage(1), fourth));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Equal("3", (string?)Part(saved, "word/styles.xml").Descendants(W + property).Single().Attribute(W + "val"));
    }

    [Fact]
    public void ChildBandSizeOverridesTheInheritedSizeAndBothRemainEditable()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions", "row-size-two.docx");
        byte[] original = File.ReadAllBytes(path); XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        body.Descendants(W + "tblStyle").Single().SetAttributeValue(W + "val", "Child");
        styles.Root!.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "styleId", "Child"),
            new XElement(W + "name", new XAttribute(W + "val", "Child")), new XElement(W + "basedOn", new XAttribute(W + "val", "IndependentTable")),
            new XElement(W + "tblPr", new XElement(W + "tblStyleRowBandSize", new XAttribute(W + "val", "1")))));
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(source);
        using PdfDocument pdf = PdfDocument.Open(ConvertWithInheritanceLoss(source));
        Assert.Equal(H1, ColorAt(pdf.GetPage(1), "A1")); Assert.Equal(H2, ColorAt(pdf.GetPage(1), "A2"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        string?[] sizes = Part(saved, "word/styles.xml").Descendants(W + "tblStyleRowBandSize").Select(element => (string?)element.Attribute(W + "val")).ToArray();
        Assert.Equal(InheritedBandSizes, sizes);
    }

    [Fact]
    public void ChangedBandSizesCannotBeHiddenByComparisonOptions()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions", "row-size-two.docx");
        byte[] original = File.ReadAllBytes(path); XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        styles.Descendants(W + "tblStyleRowBandSize").Single().SetAttributeValue(W + "val", "1");
        byte[] revised = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting));
        WordDocument first = LoadStyle(original), second = LoadStyle(revised);
        byte[] firstBefore = SaveStyle(first, DocumentFormat.Docx), secondBefore = SaveStyle(second, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains("Comparison.TableStyle", Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(first, second, options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(firstBefore, SaveStyle(first, DocumentFormat.Docx)); Assert.Equal(secondBefore, SaveStyle(second, DocumentFormat.Docx));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("above-word-limit")]
    public void InvalidOrUnqualifiedBandSizesFailBeforeOutput(string kind)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions", "row-size-two.docx");
        byte[] original = File.ReadAllBytes(path); XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        XElement band = styles.Descendants(W + "tblStyleRowBandSize").Single();
        if (kind == "duplicate") band.AddAfterSelf(new XElement(band));
        else band.SetAttributeValue(W + "val", kind == "negative" ? "-1" : "4");
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        if (kind == "above-word-limit")
            Assert.Contains("Docx.TableStyleData", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
                DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        else Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ConditionalRegionsFollowInsertedReportRowsAndAcrossOutputFormats()
    {
        string band(string name, string color) => "<w:tblStylePr w:type='" + name + "'><w:tcPr><w:shd w:val='clear' w:fill='" + color + "'/></w:tcPr></w:tblStylePr>";
        string styles = TableStyles("", band("band1Horz", H1) + band("band2Horz", H2)).Replace("<w:tblPr></w:tblPr>",
            "<w:tblPr><w:tblStyleRowBandSize w:val='1'/></w:tblPr>");
        byte[] source = StyledInput(styles, table =>
        {
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "0000")));
            XElement row = table.Elements(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(4, Part(saved, "word/document.xml").Descendants(W + "tr").Count());
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(path => HasFill(path, 128 / 255.0, 170 / 255.0, 1)));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(path => HasFill(path, 170 / 255.0, 204 / 255.0, 1)));
        string html = Encoding.UTF8.GetString(SaveStyle(document, DocumentFormat.Html));
        Assert.Contains("background-color:#80AAFF", html, StringComparison.Ordinal); Assert.Contains("background-color:#AACCFF", html, StringComparison.Ordinal);
        XDocument odt = Part(SaveStyle(document, DocumentFormat.Odt), "content.xml");
        Assert.Contains(odt.Descendants().Attributes(), attribute => attribute.Name.LocalName == "background-color" && attribute.Value == "#80AAFF");
        Assert.Contains(odt.Descendants().Attributes(), attribute => attribute.Name.LocalName == "background-color" && attribute.Value == "#AACCFF");
        using var xps = new ZipArchive(new MemoryStream(SaveStyle(document, DocumentFormat.Xps)));
        using var pageReader = new StreamReader(xps.Entries.Single(entry => entry.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open());
        string page = pageReader.ReadToEnd(); Assert.Contains("#FF80AAFF", page, StringComparison.OrdinalIgnoreCase); Assert.Contains("#FFAACCFF", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonunitBandGroupingRecomputesAfterReportExpansionWithLossReported()
    {
        string band(string name, string color) => "<w:tblStylePr w:type='" + name + "'><w:tcPr><w:shd w:val='clear' w:fill='" + color + "'/></w:tcPr></w:tblStylePr>";
        string styles = TableStyles("", band("band1Horz", H1) + band("band2Horz", H2)).Replace("<w:tblPr></w:tblPr>",
            "<w:tblPr><w:tblStyleRowBandSize w:val='2'/></w:tblPr>");
        byte[] source = StyledInput(styles, table =>
        {
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "0000")));
            XElement row = table.Elements(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream();
        ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleBandSize" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(path => HasFill(path, 128 / 255.0, 170 / 255.0, 1)));
        Assert.Equal(4, pdf.GetPage(1).Paths.Count(path => HasFill(path, 170 / 255.0, 204 / 255.0, 1)));
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal("2", (string?)Part(saved, "word/styles.xml").Descendants(W + "tblStyleRowBandSize").Single().Attribute(W + "val"));
    }

    [Fact]
    public void InheritedConditionalRegionOverridesItsParentWithoutFlattening()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleRegions", "regions.docx");
        byte[] original = File.ReadAllBytes(path); XDocument body = Part(original, "word/document.xml"), styles = Part(original, "word/styles.xml");
        body.Descendants(W + "tblStyle").Single().SetAttributeValue(W + "val", "Child");
        styles.Root!.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "styleId", "Child"),
            new XElement(W + "name", new XAttribute(W + "val", "Child")), new XElement(W + "basedOn", new XAttribute(W + "val", "IndependentTable")),
            new XElement(W + "tblStylePr", new XAttribute(W + "type", "firstRow"), new XElement(W + "tcPr",
                new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", "336699"))))));
        byte[] source = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(source);
        using PdfDocument pdf = PdfDocument.Open(ConvertWithInheritanceLoss(source));
        Assert.Equal("FF0000", ColorAt(pdf.GetPage(1), "A1")); Assert.Equal("336699", ColorAt(pdf.GetPage(1), "B1"));
        Assert.Equal("00FF00", ColorAt(pdf.GetPage(1), "D1")); Assert.Equal(V1, ColorAt(pdf.GetPage(1), "B2"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument savedStyles = Part(saved, "word/styles.xml");
        Assert.Equal(2, savedStyles.Descendants(W + "style").Count());
        Assert.Contains(savedStyles.Descendants(W + "basedOn"), element => (string?)element.Attribute(W + "val") == "IndependentTable");
        Assert.Contains(savedStyles.Descendants(W + "shd"), element => (string?)element.Attribute(W + "fill") == "336699");
        Assert.Contains(savedStyles.Descendants(W + "shd"), element => (string?)element.Attribute(W + "fill") == First);
    }

    private static byte[] ConvertWithInheritanceLoss(byte[] source)
    {
        using var input = new MemoryStream(source); using var strict = new MemoryStream(); strict.WriteByte(42); strict.Position = 0;
        Assert.Contains("Table.StyleInheritance", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, strict,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, strict.ToArray());
        input.Position = 0; using var lenient = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenient, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleInheritance" && diagnostic.Severity == DiagnosticSeverity.Warning);
        return lenient.ToArray();
    }

    private static string ColorAt(Page page, string text)
    {
        var word = Assert.Single(page.GetWords(), candidate => candidate.Text == text);
        var path = page.Paths.Where(candidate => candidate.IsFilled && candidate.FillColor != null && candidate.GetBoundingRectangle().HasValue
            && CoversCenter(candidate.GetBoundingRectangle()!.Value, word.BoundingBox)).LastOrDefault();
        if (path == null) return "-";
        var rgb = path.FillColor!.ToRGBValues();
        return ((int)Math.Round(rgb.r * 255)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
            + ((int)Math.Round(rgb.g * 255)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
            + ((int)Math.Round(rgb.b * 255)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string[] RegionColors(string name) => name switch
    {
        "regions" => new[] { "FF0000", First, First, "00FF00", Left, V1, V2, Right, Left, V1, V2, Right, "0000FF", Last, Last, "FF00FF" },
        "regions-overrides" => new[] { "DD4422", First, First, "00FF00", Left, V1, V2, Right, Left, V1, "1122CC", Right, "0000FF", Last, Last, "FF00FF" },
        "bands-h" or "bands-disable-v" => Horizontal,
        "bands-v" or "bands-both" or "bands-disable-h" => Vertical,
        "bands-header" => new[] { First, First, First, First, H1, H1, H1, H1, H2, H2, H2, H2, H1, H1, H1, H1 },
        "bands-flag-only" => new[] { "-", "-", "-", "-", H1, H1, H1, H1, H2, H2, H2, H2, H1, H1, H1, H1 },
        "bands-last-flag" => new[] { H1, H1, H1, H1, H2, H2, H2, H2, H1, H1, H1, H1, "-", "-", "-", "-" },
        "bands-first-col" => new[] { "-", V1, V2, V1, "-", V1, V2, V1, "-", V1, V2, V1, "-", V1, V2, V1 },
        "bands-last-col" => new[] { V1, V2, V1, "-", V1, V2, V1, "-", V1, V2, V1, "-", V1, V2, V1, "-" },
        "row-column-priority" => new[] { First, First, First, First, Left, "-", "-", "-", Left, "-", "-", "-", Left, "-", "-", "-" },
        "corner-first-row" => new[] { First, First, First, First, "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-" },
        "corner-first-column" => new[] { Left, "-", "-", "-", Left, "-", "-", "-", Left, "-", "-", "-", Left, "-", "-", "-" },
        "corner-both" => new[] { "FF0000", First, First, First, Left, "-", "-", "-", Left, "-", "-", "-", Left, "-", "-", "-" },
        "regions-two" => new[] { "FF0000", "00FF00", "0000FF", "FF00FF" },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
