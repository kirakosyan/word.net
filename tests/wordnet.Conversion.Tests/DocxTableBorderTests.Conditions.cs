using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private const string FirstRowBlue = "<w:tblStylePr w:type='firstRow'><w:tcPr><w:shd w:val='clear' w:color='auto' w:fill='2266CC'/></w:tcPr></w:tblStylePr>";

    [Theory]
    [InlineData("look-on", true)]
    [InlineData("look-off", false)]
    public void FirstRowMaskMatchesIndependentReferenceAndRemainsEditable(string name, bool enabled)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleConditions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        using PdfDocument actual = PdfDocument.Open(Convert(source, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.Equal(enabled, reference.GetPage(1).Paths.Any(IsBlueFill));
        Assert.Equal(enabled, actual.GetPage(1).Paths.Any(IsBlueFill));
        Assert.Contains("Header", actual.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("Value", actual.GetPage(1).Text, StringComparison.Ordinal);
        if (enabled)
        {
            Assert.Equal(2, actual.GetPage(1).Paths.Count(IsBlueFill));
            var fills = actual.GetPage(1).Paths.Where(IsBlueFill).Select(path => path.GetBoundingRectangle()!.Value).ToArray();
            var headers = actual.GetPage(1).GetWords().Where(word => word.Text == "Header").ToArray();
            var values = actual.GetPage(1).GetWords().Where(word => word.Text == "Value").ToArray();
            Assert.Equal(2, headers.Length); Assert.Equal(2, values.Length);
            foreach (var word in headers) Assert.Contains(fills, fill => CoversCenter(fill, word.BoundingBox));
            foreach (var word in values) Assert.DoesNotContain(fills, fill => CoversCenter(fill, word.BoundingBox));
        }
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument body = Part(saved, "word/document.xml"), styles = Part(saved, "word/styles.xml");
        Assert.Equal(enabled ? "0020" : "0000", (string?)body.Descendants(W + "tblLook").Single().Attribute(W + "val"));
        Assert.Equal("2266CC", (string?)styles.Descendants(W + "tblStylePr").Single().Element(W + "tcPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        Assert.Empty(body.Descendants(W + "tcPr").Elements(W + "shd"));
        string html = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Html, FidelityMode.Strict));
        if (enabled) Assert.Contains("background-color:#2266CC", html, StringComparison.Ordinal);
        else Assert.DoesNotContain("background-color:#2266CC", html, StringComparison.Ordinal);
        XDocument odt = Part(Convert(source, DocumentFormat.Odt), "content.xml");
        Assert.Equal(enabled, odt.Descendants().Attributes().Any(attribute => attribute.Name.LocalName == "background-color" && attribute.Value == "#2266CC"));
        using var xps = new ZipArchive(new MemoryStream(Convert(source, DocumentFormat.Xps, FidelityMode.Strict)));
        using var pageReader = new StreamReader(xps.Entries.Single(entry => entry.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open());
        string page = pageReader.ReadToEnd();
        if (enabled) Assert.Contains("#FF2266CC", page, StringComparison.OrdinalIgnoreCase);
        else Assert.DoesNotContain("#FF2266CC", page, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0020", "1", true)]
    [InlineData("0000", "0", false)]
    public void AgreeingExplicitLookFlagsAreRetained(string mask, string flag, bool enabled)
    {
        byte[] source = StyledInput(TableStyles("", FirstRowBlue), table => table.Element(W + "tblPr")!.Add(
            new XElement(W + "tblLook", new XAttribute(W + "val", mask), new XAttribute(W + "firstRow", flag))));
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.Equal(enabled, pdf.GetPage(1).Paths.Any(IsBlueFill));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx);
        using (var package = WordprocessingDocument.Open(new MemoryStream(saved), false))
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2010).Validate(package, TestContext.Current.CancellationToken));
        XElement look = Part(saved, "word/document.xml").Descendants(W + "tblLook").Single();
        Assert.Equal(mask, (string?)look.Attribute(W + "val")); Assert.Equal(flag, (string?)look.Attribute(W + "firstRow"));
        Assert.Null(look.Attribute(W + "lastRow"));
    }

    [Theory]
    [InlineData("look-absent", true)]
    [InlineData("look-conflict-off", true)]
    [InlineData("look-conflict-on", false)]
    public void AmbiguousLookReferenceResultsRemainExplicitlyUnsupported(string name, bool referencePaints)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleConditions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx"));
        using (var package = WordprocessingDocument.Open(new MemoryStream(source), false))
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2010).Validate(package, TestContext.Current.CancellationToken));
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.Equal(referencePaints, reference.GetPage(1).Paths.Any(IsBlueFill));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableLook", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void DirectClearCellColorOverridesTheFirstRowStyleLikeTheIndependentReference()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleConditions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "override-color.docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "override-color.pdf")));
        using PdfDocument actual = PdfDocument.Open(Convert(source, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.Contains(reference.GetPage(1).Paths, IsBlueFill);
        Assert.Contains(reference.GetPage(1).Paths, IsOverrideFill);
        Assert.Single(actual.GetPage(1).Paths, IsBlueFill); Assert.Single(actual.GetPage(1).Paths, IsOverrideFill);
        var words = actual.GetPage(1).GetWords().Where(word => word.Text == "Header").OrderBy(word => word.BoundingBox.Left).ToArray();
        Assert.Equal(2, words.Length);
        Assert.True(CoversCenter(actual.GetPage(1).Paths.Single(IsOverrideFill).GetBoundingRectangle()!.Value, words[0].BoundingBox));
        Assert.True(CoversCenter(actual.GetPage(1).Paths.Single(IsBlueFill).GetBoundingRectangle()!.Value, words[1].BoundingBox));
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XElement first = Part(saved, "word/document.xml").Descendants(W + "tc").First();
        Assert.Equal("DD4422", (string?)first.Element(W + "tcPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        Assert.Equal("2266CC", (string?)Part(saved, "word/styles.xml").Descendants(W + "tblStylePr").Single()
            .Element(W + "tcPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        string html = Encoding.UTF8.GetString(Convert(source, DocumentFormat.Html, FidelityMode.Strict));
        Assert.Contains("background-color:#DD4422", html, StringComparison.Ordinal);
        Assert.Contains("background-color:#2266CC", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("override-nil")]
    [InlineData("whole-and-condition")]
    public void UnqualifiedShadingPrecedenceRetainsReferenceAndFailsBeforeOutput(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleConditions");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Schema(source);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, name + ".pdf")));
        Assert.Contains(reference.GetPage(1).Paths, IsBlueFill);
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingConflict", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        if (name == "whole-and-condition")
        {
            input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken);
            byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
            XDocument styles = Part(saved, "word/styles.xml");
            Assert.Contains(styles.Descendants(W + "shd"), element => (string?)element.Attribute(W + "fill") == "FFF3C0");
            Assert.Contains(styles.Descendants(W + "shd"), element => (string?)element.Attribute(W + "fill") == "2266CC");
        }
    }

    [Fact]
    public void InheritedFirstRowStyleCanBeEditedAfterDocxSave()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TableStyleConditions");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "look-on.docx"));
        XDocument body = Part(input, "word/document.xml"), styles = Part(input, "word/styles.xml");
        body.Descendants(W + "tblStyle").Single().SetAttributeValue(W + "val", "Child");
        styles.Root!.Add(new XElement(W + "style", new XAttribute(W + "type", "table"), new XAttribute(W + "styleId", "Child"),
            new XElement(W + "name", new XAttribute(W + "val", "Child")), new XElement(W + "basedOn", new XAttribute(W + "val", "IndependentTable"))));
        byte[] inherited = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(inherited);
        WordDocument document = LoadStyle(inherited); byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        body = Part(saved, "word/document.xml"); styles = Part(saved, "word/styles.xml");
        string childId = (string)body.Descendants(W + "tblStyle").Single().Attribute(W + "val")!;
        XElement child = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == childId);
        string parentId = (string)child.Element(W + "basedOn")!.Attribute(W + "val")!;
        XElement parent = styles.Descendants(W + "style").Single(style => (string?)style.Attribute(W + "styleId") == parentId);
        parent.Descendants(W + "shd").Single().SetAttributeValue(W + "fill", "884422");
        byte[] edited = TestDocuments.Docx(body.ToString(SaveOptions.DisableFormatting), styles: styles.ToString(SaveOptions.DisableFormatting)); Schema(edited);
        using PdfDocument pdf = PdfDocument.Open(ConvertWithInheritanceLoss(edited));
        Assert.DoesNotContain(pdf.GetPage(1).Paths, IsBlueFill);
        Assert.Equal(2, pdf.GetPage(1).Paths.Count(path => HasFill(path, 136 / 255.0, 68 / 255.0, 34 / 255.0)));
        Assert.Equal(saved, SaveStyle(document, DocumentFormat.Docx));
    }

    [Fact]
    public void FirstRowConditionReevaluatesAfterReportExpansion()
    {
        byte[] source = StyledInput(TableStyles("", FirstRowBlue), table =>
        {
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "0020")));
            XElement row = table.Elements(W + "tr").Last();
            row.ReplaceWith(new XElement(W + "sdt", new XElement(W + "sdtPr", new XElement(W + "tag", new XAttribute(W + "val", "repeat:Items"))),
                new XElement(W + "sdtContent", new XElement(row))));
        });
        WordDocument document = LoadStyle(source);
        document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(document, DocumentFormat.Docx); Schema(saved);
        Assert.Equal(4, Part(saved, "word/document.xml").Descendants(W + "tr").Count());
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(document, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.GetPage(1).Paths.Count(IsBlueFill));
    }

    [Fact]
    public void ConditionalStyleAndLookChangesCannotBeIgnoredByComparison()
    {
        byte[] Source(string condition, string mask) => StyledInput(TableStyles("", condition), table =>
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", mask))));
        WordDocument original = LoadStyle(Source(FirstRowBlue, "0020"));
        WordDocument changedFill = LoadStyle(Source(FirstRowBlue.Replace("2266CC", "884422"), "0020"));
        WordDocument changedLook = LoadStyle(Source(FirstRowBlue, "0000"));
        byte[] before = SaveStyle(original, DocumentFormat.Docx), fillBefore = SaveStyle(changedFill, DocumentFormat.Docx), lookBefore = SaveStyle(changedLook, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        foreach (WordDocument changed in new[] { changedFill, changedLook })
            Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, changed, options,
                new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(before, SaveStyle(original, DocumentFormat.Docx));
        Assert.Equal(fillBefore, SaveStyle(changedFill, DocumentFormat.Docx)); Assert.Equal(lookBefore, SaveStyle(changedLook, DocumentFormat.Docx));
    }

    [Fact]
    public void ConditionalStyleIdentifiersRemainIndependentDuringComposition()
    {
        byte[] Source(string condition) => StyledInput(TableStyles("", condition), table =>
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "0020"))));
        WordDocument first = LoadStyle(Source(FirstRowBlue));
        WordDocument second = LoadStyle(Source(FirstRowBlue.Replace("2266CC", "DD4422")));
        byte[] sourceBefore = SaveStyle(second, DocumentFormat.Docx);
        first.Append(second, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveStyle(first, DocumentFormat.Docx); Schema(saved);
        string?[] ids = Part(saved, "word/document.xml").Descendants(W + "tblStyle").Select(style => (string?)style.Attribute(W + "val")).ToArray();
        Assert.Equal(2, ids.Length); Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        using PdfDocument pdf = PdfDocument.Open(SaveStyle(first, DocumentFormat.Pdf));
        var paths = Enumerable.Range(1, pdf.NumberOfPages).SelectMany(page => pdf.GetPage(page).Paths).ToArray();
        Assert.Equal(2, paths.Count(IsBlueFill)); Assert.Equal(2, paths.Count(IsOverrideFill));
        Assert.Equal(sourceBefore, SaveStyle(second, DocumentFormat.Docx));
    }

    [Fact]
    public void ConditionalFirstRowRepeatedAcrossPagesIsExplicitlyUnsupported()
    {
        byte[] source = StyledInput(TableStyles("", FirstRowBlue), table =>
        {
            table.Element(W + "tblPr")!.Add(new XElement(W + "tblLook", new XAttribute(W + "val", "0020")));
            XElement first = table.Elements(W + "tr").First(), last = table.Elements(W + "tr").Last();
            first.AddFirst(new XElement(W + "trPr", new XElement(W + "tblHeader")));
            for (int i = 0; i < 30; i++) table.Add(new XElement(last));
            XElement section = table.Ancestors(W + "body").Single().Element(W + "sectPr")!;
            section.Element(W + "pgSz")!.SetAttributeValue(W + "h", "5760");
            section.Element(W + "pgMar")!.SetAttributeValue(W + "top", "720");
            section.Element(W + "pgMar")!.SetAttributeValue(W + "bottom", "720");
        });
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Table.ShadingRepeat", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("missing-look", "Docx.TableLook")]
    [InlineData("conflicting-flags", "Docx.TableLook")]
    [InlineData("conflicting-on", "Docx.TableLook")]
    [InlineData("unknown-bits", "Docx.TableLook")]
    [InlineData("whole-shading", "Table.ShadingConflict")]
    [InlineData("direct-cell", "Table.ShadingConflict")]
    [InlineData("unsupported-region", "Docx.TableStyleData")]
    [InlineData("unsupported-cell-property", "Docx.TableStyleData")]
    public void UnsupportedConditionalCombinationsLeaveOutputUntouched(string kind, string diagnostic)
    {
        string condition = kind == "unsupported-region" ? FirstRowBlue.Replace("firstRow", "wholeTable")
            : kind == "unsupported-cell-property" ? FirstRowBlue.Replace("</w:tcPr>", "<w:vAlign w:val='center'/></w:tcPr>") : FirstRowBlue;
        string styles = TableStyles(kind == "whole-shading" ? Shading : "", condition);
        byte[] source = StyledInput(styles, table =>
        {
            if (kind == "missing-look") return;
            var look = new XElement(W + "tblLook", new XAttribute(W + "val", kind == "conflicting-on" ? "0000" : kind == "unknown-bits" ? "8020" : "0020"));
            if (kind == "conflicting-flags") look.Add(new XAttribute(W + "firstRow", "0"));
            if (kind == "conflicting-on") look.Add(new XAttribute(W + "firstRow", "1"));
            table.Element(W + "tblPr")!.Add(look);
        }, kind == "direct-cell" ? "<w:shd w:val='nil'/>" : "");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void DuplicateTableLooksFailBeforeOutput()
    {
        byte[] source = StyledInput(TableStyles("", FirstRowBlue), table => table.Element(W + "tblPr")!.Add(
            new XElement(W + "tblLook", new XAttribute(W + "val", "0020")), new XElement(W + "tblLook", new XAttribute(W + "val", "0000"))));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableLook", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void DuplicateDirectCellShadingFailsBeforeOutput()
    {
        byte[] source = StyledInput(TableStyles("", FirstRowBlue), table => table.Element(W + "tblPr")!.Add(
            new XElement(W + "tblLook", new XAttribute(W + "val", "0020"))),
            "<w:shd w:val='clear' w:fill='DD4422'/><w:shd w:val='nil'/>");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.TableShading", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private static bool IsBlueFill(UglyToad.PdfPig.Graphics.PdfPath path) => HasFill(path, 34 / 255.0, 102 / 255.0, 204 / 255.0);
    private static bool IsOverrideFill(UglyToad.PdfPig.Graphics.PdfPath path) => HasFill(path, 221 / 255.0, 68 / 255.0, 34 / 255.0);
    private static bool HasFill(UglyToad.PdfPig.Graphics.PdfPath path, double red, double green, double blue)
    {
        if (!path.IsFilled || path.FillColor == null) return false;
        var color = path.FillColor.ToRGBValues();
        return Math.Abs(color.r - red) < 0.01 && Math.Abs(color.g - green) < 0.01 && Math.Abs(color.b - blue) < 0.01;
    }
    private static bool CoversCenter(UglyToad.PdfPig.Core.PdfRectangle fill, UglyToad.PdfPig.Core.PdfRectangle text)
    {
        double x = (text.Left + text.Right) / 2, y = (text.Bottom + text.Top) / 2;
        return x >= fill.Left && x <= fill.Right && y >= fill.Bottom && y <= fill.Top;
    }
}
