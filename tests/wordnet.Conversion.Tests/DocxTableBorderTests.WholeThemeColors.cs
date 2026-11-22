using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class DocxTableBorderTests
{
    private static byte[] WholeTableFill(string attributes, bool inherited, string childProperties = "", bool includeTheme = true)
    {
        string shading = "<w:shd w:val='clear' w:color='auto' " + attributes + "/>";
        string? styles = inherited ? TableStyles(shading, childProperties) : null;
        string body = "<w:tbl><w:tblPr>" + (inherited ? "<w:tblStyle w:val='Derived'/>" : "")
            + "<w:tblW w:w='4800' w:type='dxa'/>" + (inherited ? "" : shading)
            + "<w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>"
            + "<w:tr>" + Cell("Left") + Cell("Right") + "</w:tr></w:tbl>";
        return DocxThemeTests.Package(body, includeTheme, styles: styles);
    }

    [Theory]
    [InlineData(null, "themeFillTint", "80", "89B0EB")]
    [InlineData("auto", "themeFillShade", "80", "113366")]
    [InlineData("FF0000", null, null, "2266CC")]
    public void DirectCellKeepsItsEditableThemeFillAndNeighborColor(string? fallback, string? transform, string? amount, string resolved)
    {
        string attributes = "w:themeFill='accent1'" + (fallback == null ? "" : " w:fill='" + fallback + "'")
            + (transform == null ? "" : " w:" + transform + "='" + amount + "'");
        string first = Cell("Themed", "<w:shd w:val='clear' w:color='000000' " + attributes + "/>");
        string second = Cell("Explicit", "<w:shd w:val='clear' w:color='000000' w:fill='00FF00'/>");
        string table = "<w:tbl><w:tblPr><w:tblW w:w='4800' w:type='dxa'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid><w:tr>" + first + second + "</w:tr></w:tbl>";
        byte[] source = DocxThemeTests.Package(table); Schema(source);
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XElement[] fills = Part(saved, "word/document.xml").Descendants(W + "tcPr").Elements(W + "shd").ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal("accent1", (string?)fills[0].Attribute(W + "themeFill"));
        Assert.Equal(fallback, (string?)fills[0].Attribute(W + "fill"));
        if (transform != null) Assert.Equal(amount, (string?)fills[0].Attribute(W + transform));
        Assert.Null(fills[1].Attribute(W + "themeFill")); Assert.Equal("00FF00", (string?)fills[1].Attribute(W + "fill"));
        using PdfDocument pdf = PdfDocument.Open(Convert(saved, DocumentFormat.Pdf));
        Assert.True(PdfFill(pdf, resolved)); Assert.True(PdfFill(pdf, "00FF00"));
        Assert.Contains("ThemedExplicit", pdf.GetPage(1).Text, StringComparison.Ordinal);
        using PdfDocument strict = PdfDocument.Open(Convert(saved, DocumentFormat.Pdf, FidelityMode.Strict));
        Assert.True(PdfFill(strict, resolved));
    }

    [Fact]
    public void EditingTheThemeChangesOnlyTheDirectCellThatReferencesIt()
    {
        byte[] input = WholeTableFill("w:fill='EEFFEE'", false);
        byte[] cells = EditThemePart(input, "word/document.xml", body =>
        {
            body.Descendants(W + "tblPr").Elements(W + "shd").Remove();
            XElement[] owners = body.Descendants(W + "tcPr").ToArray();
            owners[0].Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "themeFill", "accent1")));
            owners[1].Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", "2266CC")));
        });
        byte[] saved = SaveStyle(LoadStyle(cells), DocumentFormat.Docx);
        byte[] edited = EditThemePart(saved, "word/theme/theme1.xml", theme =>
            theme.Descendants(DrawingTheme + "accent1").Single().Element(DrawingTheme + "srgbClr")!.SetAttributeValue("val", "119955"));
        Schema(edited); using PdfDocument pdf = PdfDocument.Open(Convert(edited, DocumentFormat.Pdf));
        Assert.True(PdfFill(pdf, "119955")); Assert.True(PdfFill(pdf, "2266CC"));
    }

    [Theory]
    [InlineData(false, null, "themeFillTint", "80", "89B0EB")]
    [InlineData(true, "auto", "themeFillShade", "80", "113366")]
    [InlineData(false, "FF0000", null, null, "2266CC")]
    [InlineData(true, null, null, null, "2266CC")]
    public void WholeTableThemeFillPreservesItsOwnerAndOriginalFallback(bool inherited, string? fallback,
        string? transform, string? amount, string expectedRgb)
    {
        string attributes = "w:themeFill='accent1'" + (fallback == null ? "" : " w:fill='" + fallback + "'")
            + (transform == null ? "" : " w:" + transform + "='" + amount + "'");
        byte[] input = WholeTableFill(attributes, inherited); Schema(input);
        byte[] saved = SaveStyle(LoadStyle(input), DocumentFormat.Docx); Schema(saved);
        XDocument body = Part(saved, "word/document.xml");
        XElement owner = inherited ? Part(saved, "word/styles.xml").Descendants(W + "style")
            .Single(style => (string?)style.Attribute(W + "styleId") == "Base") : body.Descendants(W + "tbl").Single();
        XElement fill = owner.Element(W + "tblPr")!.Element(W + "shd")!;
        Assert.Equal("accent1", (string?)fill.Attribute(W + "themeFill"));
        Assert.Equal(fallback, (string?)fill.Attribute(W + "fill"));
        Assert.Equal(transform == "themeFillTint" ? amount : null, (string?)fill.Attribute(W + "themeFillTint"));
        Assert.Equal(transform == "themeFillShade" ? amount : null, (string?)fill.Attribute(W + "themeFillShade"));
        Assert.Empty(body.Descendants(W + "tcPr").Elements(W + "shd"));
        if (inherited)
        {
            Assert.Empty(body.Descendants(W + "tblPr").Elements(W + "shd"));
            Assert.Equal("Derived", (string?)body.Descendants(W + "tblStyle").Single().Attribute(W + "val"));
        }

        byte[] explicitFill = WholeTableFill("w:fill='" + expectedRgb + "'", inherited);
        using PdfDocument expected = PdfDocument.Open(Convert(explicitFill, DocumentFormat.Pdf));
        using PdfDocument actual = PdfDocument.Open(Convert(saved, DocumentFormat.Pdf));
        Assert.True(PdfFill(expected, expectedRgb)); Assert.True(PdfFill(actual, expectedRgb));
        Assert.Equal(expected.GetPage(1).Letters.Select(l => (l.Value, l.StartBaseLine)),
            actual.GetPage(1).Letters.Select(l => (l.Value, l.StartBaseLine)));
        Assert.Contains(Part(Convert(saved, DocumentFormat.Xps), "Documents/1/Pages/1.fpage").Descendants(),
            element => (string?)element.Attribute("Fill") == "#FF" + expectedRgb);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingSavedThemeRecolorsTheWholeTableWithoutChangingFallback(bool inherited)
    {
        byte[] saved = SaveStyle(LoadStyle(WholeTableFill("w:themeFill='accent1' w:fill='FF0000'", inherited)), DocumentFormat.Docx);
        byte[] edited = EditThemePart(saved, "word/theme/theme1.xml", theme =>
            theme.Descendants(DrawingTheme + "accent1").Single().Element(DrawingTheme + "srgbClr")!.SetAttributeValue("val", "119955"));
        Schema(edited);
        using PdfDocument actual = PdfDocument.Open(Convert(edited, DocumentFormat.Pdf));
        Assert.True(PdfFill(actual, "119955")); Assert.False(PdfFill(actual, "FF0000"));
        Assert.Contains("LeftRight", actual.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal("FF0000", (string?)Part(edited, inherited ? "word/styles.xml" : "word/document.xml")
            .Descendants(W + "shd").Single().Attribute(W + "fill"));
    }

    [Fact]
    public void ExplicitChildBackgroundMasksTheParentThemeForRenderingOnly()
    {
        byte[] source = WholeTableFill("w:themeFill='accent1'", true,
            "<w:tblPr><w:shd w:val='clear' w:fill='AABBCC'/></w:tblPr>");
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XDocument styles = Part(saved, "word/styles.xml");
        Assert.Equal(2, styles.Descendants(W + "shd").Count());
        Assert.Single(styles.Descendants(W + "shd").Attributes(W + "themeFill"));
        Assert.Contains("Table.WholeShading", Assert.Throws<NotSupportedException>(() => Convert(saved, DocumentFormat.Pdf, FidelityMode.Strict)).Message, StringComparison.Ordinal);
        using PdfDocument pdf = PdfDocument.Open(Convert(saved, DocumentFormat.Pdf));
        Assert.True(PdfFill(pdf, "AABBCC")); Assert.False(PdfFill(pdf, "2266CC"));
    }

    [Theory]
    [InlineData(true, "w:themeFill='accent1' w:themeFillShade='80' w:themeFillTint='80'", true)]
    [InlineData(false, "w:themeFill='accent1'", false)]
    public void UnresolvedWholeTableThemeFlattensToTheStoredColorWithALoss(bool inherited, string theme, bool includeTheme)
    {
        byte[] source = WholeTableFill(theme + " w:fill='AB1234'", inherited, includeTheme: includeTheme);
        using var input = new MemoryStream(source); using var target = new MemoryStream();
        target.WriteByte(42); target.Position = 0;
        Assert.Contains("Docx.ThemeColor", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, target,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, target.ToArray()); Assert.True(input.CanRead);
        byte[] saved = SaveStyle(LoadStyle(source), DocumentFormat.Docx); Schema(saved);
        XElement shd = Part(saved, inherited ? "word/styles.xml" : "word/document.xml").Descendants(W + "shd").Single();
        Assert.Null(shd.Attribute(W + "themeFill")); Assert.Equal("AB1234", (string?)shd.Attribute(W + "fill"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void StrictStaticOutputRejectsAnEffectiveWholeTableFillBeforeWriting(bool inherited, bool themed)
    {
        byte[] source = WholeTableFill(themed ? "w:themeFill='accent1'" : "w:fill='2266CC'", inherited);
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Contains(themed ? "Table.StyleThemeColor" : "Table.WholeShading", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(false, " FF000")]
    [InlineData(true, "FF000 ")]
    public void ResolvableWholeTableThemeCannotHideAMalformedRgbFallback(bool inherited, string fallback)
    {
        using var source = new MemoryStream(WholeTableFill("w:themeFill='accent1' w:fill='" + fallback + "'", inherited));
        using var output = new MemoryStream(); output.WriteByte(42); output.Position = 0;
        Assert.Contains("Docx.Color", Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(source, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EquivalentHexSpellingsUseTheSameTableAppearance(bool inherited)
    {
        WordDocument first = LoadStyle(WholeTableFill("w:themeFill='accent1' w:themeFillTint='bf' w:fill='abcdef'", inherited));
        WordDocument second = LoadStyle(WholeTableFill("w:themeFill='accent1' w:themeFillTint='BF' w:fill='ABCDEF'", inherited));
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        _ = DocumentComparison.Compare(first, second, options, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        XElement shading = Part(SaveStyle(first, DocumentFormat.Docx), inherited ? "word/styles.xml" : "word/document.xml").Descendants(W + "shd").Single();
        Assert.Equal("BF", (string?)shading.Attribute(W + "themeFillTint"));
        Assert.Equal("ABCDEF", (string?)shading.Attribute(W + "fill"));
    }

    [Theory]
    [InlineData(true, "Comparison.TableStyle")]
    [InlineData(false, "Comparison.Formatting")]
    public void ChangedWholeTableFallbackCannotDisappearInTextComparison(bool inherited, string diagnostic)
    {
        WordDocument first = LoadStyle(WholeTableFill("w:themeFill='accent1' w:fill='00FFFF'", inherited));
        WordDocument second = LoadStyle(WholeTableFill("w:themeFill='accent1' w:fill='FF0000'", inherited));
        byte[] before = SaveStyle(first, DocumentFormat.Docx);
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))
        { CompareParagraphFormatting = false, CompareRunFormatting = false };
        Assert.Contains(diagnostic, Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(first, second, options,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, SaveStyle(first, DocumentFormat.Docx));
    }
}
