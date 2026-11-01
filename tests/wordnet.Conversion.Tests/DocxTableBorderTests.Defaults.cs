using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private static readonly string[] DefaultProbeWords = { "A1", "B1", "A2", "B2" };
    private static readonly string[][] NewTableRows = { new[] { "New", "Table" } };
    private static readonly string[][] BlockedTableRows = { new[] { "X" } };

    [Fact]
    public void ImplicitDefaultStyleStaysEditableAndReferenceDifferenceIsReported()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "default.docx")); Schema(source);
        Schema(File.ReadAllBytes(Path.Combine(directory, "explicit.docx"))); Schema(File.ReadAllBytes(Path.Combine(directory, "no-default.docx")));
        using PdfDocument implicitReference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "default.pdf")));
        using PdfDocument explicitReference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "explicit.pdf")));
        using PdfDocument absentReference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "no-default.pdf")));
        Assert.Equal("-", ColorAt(implicitReference.GetPage(1), "B1")); Assert.Equal("-", ColorAt(absentReference.GetPage(1), "B1"));
        Assert.Equal(First, ColorAt(explicitReference.GetPage(1), "B1")); Assert.Equal(Left, ColorAt(explicitReference.GetPage(1), "A2"));

        using var input = new MemoryStream(source); using var strict = new MemoryStream(); strict.WriteByte(42); strict.Position = 0;
        Assert.Contains("Docx.TableStyleDefault", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, strict,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, strict.ToArray()); Assert.True(input.CanRead);
        input.Position = 0; using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Docx.TableStyleDefault" && diagnostic.Severity == DiagnosticSeverity.Warning);
        using PdfDocument actual = PdfDocument.Open(output.ToArray());
        foreach (string word in DefaultProbeWords) Assert.Equal(ColorAt(explicitReference.GetPage(1), word), ColorAt(actual.GetPage(1), word));

        input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        using var editable = new MemoryStream(); document.Save(editable, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        byte[] saved = editable.ToArray(); Schema(saved);
        Assert.Empty(Part(saved, "word/document.xml").Descendants(W + "tblStyle"));
        XElement defaultStyle = Assert.Single(Part(saved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        Assert.Equal("IndependentTable", (string?)defaultStyle.Attribute(W + "styleId"));
        Assert.Equal(2, defaultStyle.Elements(W + "tblStylePr").Count());
        byte[] resaved = SaveStyle(LoadStyle(saved), DocumentFormat.Docx); Schema(resaved);
        Assert.Empty(Part(resaved, "word/document.xml").Descendants(W + "tblStyle"));
        Assert.Single(Part(resaved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        using var previewInput = new MemoryStream(source);
        Assert.Contains("Docx.TableStyleDefault", Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(previewInput,
            DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.Strict), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.True(previewInput.CanRead);
    }

    [Fact]
    public void ExplicitReferenceKeepsItsDefaultDeclarationWithoutClaimingAnImplicitReference()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults", "explicit.docx");
        byte[] source = File.ReadAllBytes(path); Schema(source);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Docx.TableStyleDefault");
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        string id = (string)Part(saved, "word/document.xml").Descendants(W + "tblStyle").Single().Attribute(W + "val")!;
        Assert.Equal("1", (string?)Part(saved, "word/styles.xml").Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == id).Attribute(W + "default"));
    }

    [Fact]
    public void DefaultStyleCompositionIsAtomicAndOnlyCombinesCompatibleOwners()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "default.docx"));
        WordDocument target = LoadStyle(source), compatible = LoadStyle(source);
        byte[] sourceBefore = SaveStyle(compatible, DocumentFormat.Docx);
        target.Append(compatible, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(target, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(2, Part(saved, "word/document.xml").Descendants(W + "tbl").Count());
        Assert.Empty(Part(saved, "word/document.xml").Descendants(W + "tblStyle"));
        Assert.Single(Part(saved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        Assert.Equal(sourceBefore, SaveStyle(compatible, DocumentFormat.Docx));

        XDocument body = Part(source, "word/document.xml"), styles = Part(source, "word/styles.xml");
        styles.Descendants(W + "shd").First().SetAttributeValue(W + "fill", "ABCDEF");
        WordDocument different = LoadStyle(TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)));
        WordDocument unstyled = LoadStyle(File.ReadAllBytes(Path.Combine(directory, "no-default.docx")));
        byte[] beforeDifferent = SaveStyle(different, DocumentFormat.Docx), beforeUnstyled = SaveStyle(unstyled, DocumentFormat.Docx);
        Assert.Contains("Composition.TableStyleDefault", Assert.Throws<NotSupportedException>(() => target.Append(different, cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Contains("Composition.TableStyleDefault", Assert.Throws<NotSupportedException>(() => target.Append(unstyled, cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Contains("Composition.TableStyleDefault", Assert.Throws<NotSupportedException>(() => unstyled.Append(compatible, cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(saved, SaveStyle(target, DocumentFormat.Docx)); Assert.Equal(beforeDifferent, SaveStyle(different, DocumentFormat.Docx));
        Assert.Equal(beforeUnstyled, SaveStyle(unstyled, DocumentFormat.Docx)); Assert.Equal(sourceBefore, SaveStyle(compatible, DocumentFormat.Docx));
    }

    [Fact]
    public void NewTablesUseWholeTableDefaultsWhileConditionalDefaultsRejectMissingLook()
    {
        string styles = TableStyles().Replace("w:styleId='Derived'", "w:styleId='Derived' w:default='1'");
        byte[] source = StyledInput(styles, table => table.Element(W + "tblPr")!.Element(W + "tblStyle")!.Remove());
        WordDocument document = LoadStyle(source);
        document.AddTable(NewTableRows, cancellationToken: TestContext.Current.CancellationToken);
        document.AddCrossTab(new[] { new CrossTabRecord("North", "Q1", 2m) }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(3, Part(saved, "word/document.xml").Descendants(W + "tbl").Count());
        Assert.Empty(Part(saved, "word/document.xml").Descendants(W + "tblStyle"));
        Assert.Single(Part(saved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Contains(pdf.GetPage(1).Paths, path => HasStroke(path, 1, 0, 0));

        byte[] conditional = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults", "default.docx"));
        WordDocument blocked = LoadStyle(conditional); byte[] before = SaveStyle(blocked, DocumentFormat.Docx);
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => blocked.AddTable(BlockedTableRows, cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => blocked.AddCrossTab(new[] { new CrossTabRecord("A", "B", 1m) }, cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, SaveStyle(blocked, DocumentFormat.Docx));
    }

    [Fact]
    public void DefaultStyleSurvivesReportRowExpansion()
    {
        string styles = TableStyles().Replace("w:styleId='Derived'", "w:styleId='Derived' w:default='1'");
        byte[] source = StyledInput(styles, table =>
        {
            table.Element(W + "tblPr")!.Element(W + "tblStyle")!.Remove();
            XElement row = table.Elements(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(4, Part(saved, "word/document.xml").Descendants(W + "tr").Count());
        Assert.Empty(Part(saved, "word/document.xml").Descendants(W + "tblStyle"));
        Assert.Single(Part(saved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Equal("ABCDCDCD", pdf.GetPage(1).Text); Assert.Contains(pdf.GetPage(1).Paths, path => HasStroke(path, 0, 1, 1));
    }

    [Fact]
    public void ComparisonCannotIgnoreTheImplicitDefaultRelationship()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults");
        WordDocument implicitStyle = LoadStyle(File.ReadAllBytes(Path.Combine(directory, "default.docx")));
        WordDocument explicitStyle = LoadStyle(File.ReadAllBytes(Path.Combine(directory, "explicit.docx")));
        byte[] before = SaveStyle(implicitStyle, DocumentFormat.Docx), other = SaveStyle(explicitStyle, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains("Comparison.TableStyle", Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(implicitStyle, explicitStyle,
            options, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, SaveStyle(implicitStyle, DocumentFormat.Docx)); Assert.Equal(other, SaveStyle(explicitStyle, DocumentFormat.Docx));
    }

    [Fact]
    public void LastDeclaredDefaultWinsAndInvalidFlagsFailBeforeWriting()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleDefaults", "default.docx");
        byte[] source = File.ReadAllBytes(path); XDocument body = Part(source, "word/document.xml"), styles = Part(source, "word/styles.xml");
        XElement second = new XElement(styles.Descendants(W + "style").Single()); second.SetAttributeValue(W + "styleId", "Second");
        second.Descendants(W + "shd").First().SetAttributeValue(W + "fill", "ABCDEF"); styles.Root!.Add(second);
        byte[] withTwo = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(withTwo);
        using var input = new MemoryStream(withTwo);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out ConversionResult result,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Docx.MultipleDefaultStyles" && diagnostic.Severity == DiagnosticSeverity.Info);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        XElement retained = Assert.Single(Part(saved, "word/styles.xml").Descendants(W + "style"), style => (string?)style.Attribute(W + "default") == "1");
        Assert.Equal("Second", (string?)retained.Attribute(W + "styleId"));
        Assert.Equal("ABCDEF", (string?)retained.Descendants(W + "shd").First().Attribute(W + "fill"));

        second.SetAttributeValue(W + "default", "maybe");
        byte[] invalid = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting));
        using var badInput = new MemoryStream(invalid); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableStyle", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(badInput, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
