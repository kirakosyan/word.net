using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class P1LayoutTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private const string Page = "<w:sectPr><w:pgSz w:w='12000' w:h='12000'/><w:pgMar w:top='1440' w:bottom='1440' w:left='1440' w:right='1440' w:header='720' w:footer='720'/></w:sectPr>";
    private static string P(string text, string properties = "", string run = "") => "<w:p><w:pPr>" + properties + "</w:pPr><w:r><w:rPr>" + "<w:rFonts w:ascii='Noto Sans' w:hAnsi='Noto Sans'/>" + run + "</w:rPr><w:t xml:space='preserve'>" + text + "</w:t></w:r></w:p>";
    private static byte[] Export(byte[] bytes, DocumentFormat format = DocumentFormat.Pdf)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        if (format == DocumentFormat.Docx) { WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); return P1FieldTests.Save(document, format); }
        new WordConverter().Convert(input, output, DocumentFormat.Docx, format, new ConversionOptions(), TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LigatureFreeDefaultsAndInheritanceUsePublishedFontMetrics(int policy)
    {
        string size = "<w:sz w:val='22'/>";
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'>";
        if (policy == 1) styles += "<w:docDefaults><w:rPrDefault><w:rPr>" + size + "</w:rPr></w:rPrDefault></w:docDefaults>";
        if (policy == 2) styles += "<w:style w:type='paragraph' w:default='1' w:styleId='Base'><w:name w:val='Body'/><w:rPr>" + size + "</w:rPr></w:style>";
        if (policy == 3) styles += "<w:style w:type='paragraph' w:styleId='Base'><w:name w:val='Base'/><w:rPr>" + size + "</w:rPr></w:style><w:style w:type='paragraph' w:default='1' w:styleId='Child'><w:name w:val='Child'/><w:basedOn w:val='Base'/></w:style>";
        styles += "</w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(P("one") + P("two") + P("three") + Page), styles: styles);
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes); var page = pdf.GetPage(1);
        double[] baselines = page.Letters.Select(l => page.Height - l.StartBaseLine.Y).Distinct().ToArray();
        // Noto Sans pinned hhea: UPEM=1000, ascender=1069, descender=-293.
        Assert.Equal(3, baselines.Length);
        for (int i = 0; i < 3; i++) Assert.InRange(Math.Abs(72 + 11 * 1.069 + i * 11 * 1.362 - baselines[i]), 0, .01);
        Assert.All(page.Letters, l => Assert.Equal(11, l.FontSize, 3));
        byte[] word = Export(input, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Equal(3, P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "r").Descendants(W + "sz").Count(e => (string?)e.Attribute(W + "val") == "22"));
        RoadmapLayoutTests.Evidence("p1-defaults-" + policy, input, bytes);
    }

    [Fact]
    public void CellParagraphsShareTheSameAbsentSpacingPolicyAsBodyParagraphs()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tr><w:tc>" + P("one") + P("two") + "</w:tc></w:tr></w:tbl>" + Page));
        using var pdf = PdfDocument.Open(Export(input)); var letters = pdf.GetPage(1).Letters;
        Assert.InRange(Math.Abs(letters[0].StartBaseLine.Y - letters[3].StartBaseLine.Y - 11 * 1.362), 0, .01);
    }

    [Fact]
    public void CharacterAndDirectOverridesWinWithoutInventingStyleNames()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:default='1' w:styleId='Report'><w:name w:val='Report body'/><w:rPr><w:sz w:val='26'/></w:rPr></w:style><w:style w:type='character' w:styleId='Small'><w:name w:val='Small'/><w:rPr><w:sz w:val='18'/></w:rPr></w:style></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(P("one", run: "<w:rStyle w:val='Small'/><w:sz w:val='28'/>") + P("two", run: "<w:rStyle w:val='Small'/>") + P("three") + Page), styles: styles);
        using var pdf = PdfDocument.Open(Export(input));
        Assert.Equal(14, pdf.GetPage(1).Letters[0].FontSize, 3); Assert.Equal(9, pdf.GetPage(1).Letters[3].FontSize, 3); Assert.Equal(13, pdf.GetPage(1).Letters[6].FontSize, 3);
        byte[] word = Export(input, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.All(P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "pStyle"), e => Assert.Equal("Report", (string?)e.Attribute(W + "val")));
        Assert.Contains("Report body", P1FieldTests.Xml(word, "word/styles.xml").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStyleDoesNotInventAnEditableSourceIdentity()
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(P("one", "<w:pStyle w:val='Missing'/>"))));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Empty(P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "pStyle"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("bad")]
    public void InvalidSizesFailWithoutCommittingOutput(string value)
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(P("one", run: "<w:sz w:val='" + value + "'/>") + Page)));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicAndDuplicateStylesAreRejectedAtomically(bool duplicate)
    {
        string style = "<w:style w:type='paragraph' w:default='1' w:styleId='A'><w:name w:val='A'/><w:basedOn w:val='A'/></w:style>";
        if (duplicate) style += style;
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(P("one")), styles: "<w:styles xmlns:w='" + TestDocuments.W + "'>" + style + "</w:styles>"));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void EmptyParagraphMarkMetricsAndExplicitSpacingHaveIndependentCoordinates()
    {
        string blank = "<w:p><w:pPr><w:spacing w:before='60' w:after='80'/><w:rPr><w:sz w:val='40'/></w:rPr></w:pPr></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(P("one", "<w:spacing w:after='120'/>") + blank + P("two") + Page));
        using var pdf = PdfDocument.Open(Export(input)); var page = pdf.GetPage(1);
        double first = page.Height - page.Letters[0].StartBaseLine.Y, second = page.Height - page.Letters[3].StartBaseLine.Y;
        Assert.InRange(Math.Abs(second - first - (11 * 1.362 + 6 + 3 + 20 * 1.362 + 4)), 0, .01);
        byte[] word = Export(input, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Equal("40", (string?)P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "p").ElementAt(1).Element(W + "pPr")?.Element(W + "rPr")?.Element(W + "sz")?.Attribute(W + "val"));
    }

    [Theory]
    [InlineData("auto", 480, 29.964)]
    [InlineData("atLeast", 600, 30)]
    [InlineData("exact", 600, 30)]
    public void ExplicitLineSpacingKeepsItsUnits(string rule, int amount, double advance)
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body(P("one</w:t><w:br/><w:t>two", "<w:spacing w:line='" + amount + "' w:lineRule='" + rule + "'/>") + Page));
        using var pdf = PdfDocument.Open(Export(input)); double[] y = pdf.GetPage(1).Letters.Select(l => l.StartBaseLine.Y).Distinct().ToArray();
        Assert.Equal(2, y.Length); Assert.InRange(Math.Abs(y[0] - y[1] - advance), 0, .01);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:tblW w:w='6000' w:type='dxa'/>")]
    [InlineData("<w:tblW w:w='5000' w:type='pct'/>")]
    public void ImplicitAutofitAdmitsDemandingContentWhileFixedGridStaysFixed(string width)
    {
        string row = "<w:tr><w:tc>" + P("WWWWWWWWWWWWWWWWWWWW") + "</w:tc><w:tc>" + P("Z") + "</w:tc></w:tr>";
        string table = "<w:tbl><w:tblPr>" + width + "{layout}</w:tblPr><w:tblGrid><w:gridCol w:w='3000'/><w:gridCol w:w='3000'/></w:tblGrid>" + row + "</w:tbl>";
        byte[] implicitInput = TestDocuments.Docx(TestDocuments.Body(table.Replace("{layout}", "") + Page));
        using var implicitPdf = PdfDocument.Open(Export(implicitInput));
        using var fixedPdf = PdfDocument.Open(Export(TestDocuments.Docx(TestDocuments.Body(table.Replace("{layout}", "<w:tblLayout w:type='fixed'/>") + Page))));
        double implicitX = implicitPdf.GetPage(1).Letters.Single(l => l.Value == "Z").StartBaseLine.X;
        double fixedX = fixedPdf.GetPage(1).Letters.Single(l => l.Value == "Z").StartBaseLine.X;
        Assert.True(implicitX > fixedX + 20); Assert.Equal(InteropText.Contains(width, "pct") ? 300 : 222, fixedX, 3);
        Assert.Equal(21, implicitPdf.GetPage(1).Letters.Count); Assert.Equal(21, fixedPdf.GetPage(1).Letters.Count);
        byte[] word = Export(implicitInput, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Empty(P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "tblLayout"));
        RoadmapLayoutTests.Evidence("p1-demanding-table-" + (InteropText.Contains(width, "pct") ? "pct" : width.Length == 0 ? "auto" : "dxa"), implicitInput, Export(implicitInput));
    }

    [Fact]
    public void ImplicitAutofitSplitsRowsAndRepeatsHeadersWithoutDuplicatingBodyText()
    {
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='2500'/><w:gridCol w:w='2500'/></w:tblGrid>"
            + "<w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" + P("Header") + "</w:tc></w:tr>"
            + "<w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" + string.Concat(Enumerable.Range(0, 45).Select(i => P("Row " + i.ToString("D2", CultureInfo.InvariantCulture)))) + "</w:tc></w:tr></w:tbl>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(table + Page.Replace("w:h='12000'", "w:h='6000'")));
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes); Assert.True(pdf.NumberOfPages >= 3);
        Assert.All(pdf.GetPages(), page => Assert.StartsWith("Header", page.Text, StringComparison.Ordinal));
        Assert.Equal(string.Concat(Enumerable.Range(0, 45).Select(i => "Row " + i.ToString("D2", CultureInfo.InvariantCulture))), string.Concat(pdf.GetPages().Select(p => p.Text.Substring("Header".Length))));
        RoadmapLayoutTests.Evidence("p1-implicit-split-rows", input, bytes);
    }

    [Fact]
    public void ConventionalNoteMarkersDoNotAddASecondSeparatorSpace()
    {
        string noteText = string.Concat(Enumerable.Range(0, 55).Select(i => P("Note " + i.ToString("D2", CultureInfo.InvariantCulture), run: "<w:sz w:val='20'/>")));
        noteText = noteText.Replace("<w:t xml:space='preserve'>Note 00", "<w:footnoteRef/><w:t xml:space='preserve'> Note 00");
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='-1' w:type='separator'><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:id='0' w:type='continuationSeparator'><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote><w:footnote w:id='1'>" + noteText + "</w:footnote></w:footnotes>";
        string body = P("Body").Replace("</w:r>", "<w:footnoteReference w:id='1'/></w:r>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body + Page), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes));
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes);
        Assert.Equal(5, pdf.NumberOfPages); // 40% area, 13 lines/page at 10 pt.
        Assert.Contains("Body1", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("1 Note 00", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.DoesNotContain("1  Note", pdf.GetPage(1).Text, StringComparison.Ordinal);
        string all = string.Concat(pdf.GetPages().Select(p => p.Text));
        foreach (int i in Enumerable.Range(0, 55)) Assert.Equal(1, all.Split(new[] { "Note " + i.ToString("D2", CultureInfo.InvariantCulture) }, StringSplitOptions.None).Length - 1);
        byte[] word = Export(input, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Single(P1FieldTests.Xml(word, "word/footnotes.xml").Descendants(W + "footnoteRef"));
        RoadmapLayoutTests.Evidence("p1-conventional-note-continuation", input, bytes);
    }
}
