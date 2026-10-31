using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private static readonly XNamespace DrawingTheme = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly string?[] ComposedThemeFallbacks = { "00FFFF", "FF0000" };

    private static byte[] EditThemePart(byte[] bytes, string path, Action<XDocument> edit)
    {
        using var output = new MemoryStream(); output.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry part = zip.GetEntry(path)!; XDocument document;
            using (Stream input = part.Open()) document = XDocument.Load(input);
            edit(document); part.Delete();
            using Stream target = zip.CreateEntry(path).Open(); document.Save(target);
        }
        return output.ToArray();
    }

    private static bool PdfFill(PdfDocument document, string rgb)
    {
        int red = System.Convert.ToInt32(rgb.Substring(0, 2), 16), green = System.Convert.ToInt32(rgb.Substring(2, 2), 16), blue = System.Convert.ToInt32(rgb.Substring(4, 2), 16);
        return document.GetPage(1).Paths.Any(path => HasFill(path, red / 255d, green / 255d, blue / 255d));
    }

    [Theory]
    [InlineData("theme", null, "2266CC")]
    [InlineData("theme-tint", "themeFillTint", "90B2E5")]
    [InlineData("theme-shade", "themeFillShade", "113366")]
    public void ConditionalThemeFillKeepsItsReferenceAndReportsReferenceColorDifference(string name, string? transform, string resolved)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        using PdfDocument explicitRed = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "explicit-red.pdf")));
        using PdfDocument explicitBlue = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "explicit-blue.pdf")));
        Assert.True(PdfFill(reference, "FF0000")); Assert.True(PdfFill(explicitRed, "FF0000")); Assert.True(PdfFill(explicitBlue, "2266CC"));
        Assert.False(PdfFill(reference, resolved));

        using var input = new MemoryStream(source); using var strict = new MemoryStream(); strict.WriteByte(42); strict.Position = 0;
        Assert.Contains("Table.StyleThemeColor", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, strict,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, strict.ToArray()); Assert.True(input.CanRead);
        input.Position = 0; using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleThemeColor" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Docx.ThemeColor");
        using PdfDocument actual = PdfDocument.Open(output.ToArray()); Assert.True(PdfFill(actual, resolved));
        Assert.Equal(reference.GetPage(1).Letters.Select(letter => letter.Value), actual.GetPage(1).Letters.Select(letter => letter.Value));

        WordDocument document = LoadStyle(source); byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        XElement shading = Part(saved, "word/styles.xml").Descendants(W + "tblStylePr").Single().Descendants(W + "shd").Single();
        Assert.Equal("accent1", (string?)shading.Attribute(W + "themeFill")); Assert.Equal("FF0000", (string?)shading.Attribute(W + "fill"));
        Assert.Equal(transform == "themeFillTint" ? "80" : null, (string?)shading.Attribute(W + "themeFillTint"));
        Assert.Equal(transform == "themeFillShade" ? "80" : null, (string?)shading.Attribute(W + "themeFillShade"));
        using var previewInput = new MemoryStream(source);
        Assert.Contains("Table.StyleThemeColor", Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(previewInput,
            DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.Strict), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.True(previewInput.CanRead);
    }

    [Fact]
    public void ChangingTheRetainedThemeChangesTheConditionalFillAfterReload()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] saved = SaveStyle(LoadStyle(File.ReadAllBytes(path)), DocumentFormat.Docx); Schema(saved);
        byte[] edited = EditThemePart(saved, "word/theme/theme1.xml", theme =>
            theme.Descendants(DrawingTheme + "accent1").Single().Element(DrawingTheme + "srgbClr")!.SetAttributeValue("val", "119955"));
        Schema(edited); WordDocument document = LoadStyle(edited);
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.True(PdfFill(pdf, "119955")); Assert.False(PdfFill(pdf, "FF0000"));
        XElement shading = Part(SaveStyle(document, DocumentFormat.Docx), "word/styles.xml").Descendants(W + "tblStylePr").Single().Descendants(W + "shd").Single();
        Assert.Equal("accent1", (string?)shading.Attribute(W + "themeFill")); Assert.Equal("FF0000", (string?)shading.Attribute(W + "fill"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    public void OmittedAndAutomaticFallbacksRemainDistinctInEditableStyles(string? fallback)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Descendants(W + "shd").Single().SetAttributeValue(W + "fill", fallback));
        Schema(source); byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XElement shading = Part(saved, "word/styles.xml").Descendants(W + "shd").Single();
        Assert.Equal(fallback, (string?)shading.Attribute(W + "fill")); Assert.Equal("accent1", (string?)shading.Attribute(W + "themeFill"));
    }

    [Fact]
    public void ExplicitChildFillReplacesInheritedThemeSemanticsWithoutFlatteningTheParent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Root!.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "styleId", "Child"),
                new XElement(W + "name", new XAttribute(W + "val", "Child")), new XElement(W + "basedOn", new XAttribute(W + "val", "IndependentTable")),
                new XElement(W + "tblStylePr", new XAttribute(W + "type", "firstRow"), new XElement(W + "tcPr",
                    new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", "336699")))))));
        source = EditThemePart(source, "word/document.xml", body => body.Descendants(W + "tblStyle").Single().SetAttributeValue(W + "val", "Child")); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "Table.StyleThemeColor" or "Table.StyleInheritance");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(PdfFill(pdf, "336699"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument styles = Part(saved, "word/styles.xml");
        Assert.Contains(styles.Descendants(W + "shd"), shading => (string?)shading.Attribute(W + "themeFill") == "accent1");
        Assert.Contains(styles.Descendants(W + "shd"), shading => (string?)shading.Attribute(W + "fill") == "336699");
    }

    [Fact]
    public void InheritedConditionalThemeIsRetainedAndBothRenderingDifferencesAreReported()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Root!.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "styleId", "Child"),
                new XElement(W + "name", new XAttribute(W + "val", "Child")), new XElement(W + "basedOn", new XAttribute(W + "val", "IndependentTable")))));
        source = EditThemePart(source, "word/document.xml", body => body.Descendants(W + "tblStyle").Single().SetAttributeValue(W + "val", "Child")); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleThemeColor" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleInheritance" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(PdfFill(pdf, "2266CC"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument styles = Part(saved, "word/styles.xml");
        Assert.Contains(styles.Descendants(W + "basedOn"), element => (string?)element.Attribute(W + "val") == "IndependentTable");
        Assert.Contains(styles.Descendants(W + "shd"), element => (string?)element.Attribute(W + "themeFill") == "accent1");
    }

    [Fact]
    public void ConditionalThemeReferenceSurvivesReportExpansion()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/document.xml", body =>
        {
            XElement row = body.Descendants(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(4, Part(saved, "word/document.xml").Descendants(W + "tr").Count());
        Assert.Equal("accent1", (string?)Part(saved, "word/styles.xml").Descendants(W + "shd").Single().Attribute(W + "themeFill"));
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.StyleThemeColor");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(PdfFill(pdf, "2266CC"));
    }

    [Fact]
    public void CompositionKeepsConflictingConditionalThemeStyleFallbacksIndependent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = File.ReadAllBytes(path);
        byte[] changed = EditThemePart(source, "word/styles.xml", styles => styles.Descendants(W + "shd").Single().SetAttributeValue(W + "fill", "00FFFF"));
        WordDocument first = LoadStyle(source), second = LoadStyle(changed); byte[] before = SaveStyle(second, DocumentFormat.Docx);
        first.Append(second, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(first, DocumentFormat.Docx); Schema(saved);
        XDocument styles = Part(saved, "word/styles.xml"), body = Part(saved, "word/document.xml");
        Assert.Equal(2, body.Descendants(W + "tblStyle").Select(element => (string?)element.Attribute(W + "val")).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ComposedThemeFallbacks, styles.Descendants(W + "tblStylePr").Descendants(W + "shd").Select(element => (string?)element.Attribute(W + "fill")).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(styles.Descendants(W + "tblStylePr").Descendants(W + "shd"), element => Assert.Equal("accent1", (string?)element.Attribute(W + "themeFill")));
        Assert.Equal(before, SaveStyle(second, DocumentFormat.Docx));
    }

    [Fact]
    public void ComparisonCannotIgnoreDifferentThemeFallbackMetadata()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = File.ReadAllBytes(path);
        byte[] revised = EditThemePart(source, "word/styles.xml", styles => styles.Descendants(W + "shd").Single().SetAttributeValue(W + "fill", "00FFFF"));
        WordDocument first = LoadStyle(source), second = LoadStyle(revised);
        byte[] beforeFirst = SaveStyle(first, DocumentFormat.Docx), beforeSecond = SaveStyle(second, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains("Comparison.TableStyle", Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(first, second, options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(beforeFirst, SaveStyle(first, DocumentFormat.Docx)); Assert.Equal(beforeSecond, SaveStyle(second, DocumentFormat.Docx));
    }

    [Theory]
    [InlineData("bad-token")]
    [InlineData("bad-tint")]
    public void MalformedConditionalThemeFillFailsBeforeWriting(string scenario)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = EditThemePart(File.ReadAllBytes(path), "word/styles.xml", styles =>
            styles.Descendants(W + "shd").Single().SetAttributeValue(W + (scenario == "bad-token" ? "themeFill" : "themeFillTint"), scenario == "bad-token" ? "bad-token" : "GG"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.ThemeColor", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("missing-theme")]
    [InlineData("both-transforms")]
    public void UnresolvedConditionalThemeUsesDiagnosedFallback(string scenario)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleThemes", "theme.docx");
        byte[] source = File.ReadAllBytes(path);
        if (scenario == "both-transforms")
            source = EditThemePart(source, "word/styles.xml", styles =>
            {
                XElement shading = styles.Descendants(W + "shd").Single();
                shading.SetAttributeValue(W + "themeFillTint", "80"); shading.SetAttributeValue(W + "themeFillShade", "80");
            });
        else
        {
            source = EditThemePart(source, "word/_rels/document.xml.rels", relations => relations.Root!.Elements()
                .Single(relation => ((string?)relation.Attribute("Type"))?.EndsWith("/theme", StringComparison.Ordinal) == true).Remove());
            source = EditThemePart(source, "[Content_Types].xml", types => types.Root!.Elements()
                .Single(element => (string?)element.Attribute("PartName") == "/word/theme/theme1.xml").Remove());
            using var removed = new MemoryStream(); removed.Write(source, 0, source.Length);
            using (var zip = new ZipArchive(removed, ZipArchiveMode.Update, true)) zip.GetEntry("word/theme/theme1.xml")!.Delete();
            source = removed.ToArray();
        }
        Schema(source);
        using var input = new MemoryStream(source); using var strict = new MemoryStream(); strict.WriteByte(42); strict.Position = 0;
        Assert.Contains("Docx.ThemeColor", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, strict,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, strict.ToArray());
        input.Position = 0; using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Docx.ThemeColor" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(PdfFill(pdf, "FF0000"));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        Assert.Null(Part(saved, "word/styles.xml").Descendants(W + "shd").Single().Attribute(W + "themeFill"));
    }
}
