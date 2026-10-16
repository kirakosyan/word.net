using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class PreflightReviewTests
{
    [Theory]
    [InlineData("\u0627", false)]
    [InlineData("\u0627", true)]
    [InlineData("\U0001f642", false)]
    [InlineData("\U0001f642", true)]
    public void StrictInspectionCollectsComplexScriptLossesAndCanCompleteLayout(string text, bool layout)
    {
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text))));
        var converter = new WordConverter();
        DocumentInspection report = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, includeLayout: layout, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.WouldRejectStrict);
        Assert.Equal(layout, report.LayoutPerformed);
        ConversionDiagnostic shaping = Assert.Single(report.Diagnostics, d => d.Code == "Layout.Shaping");
        Assert.Equal(DiagnosticPhase.Destination, Assert.Single(shaping.Occurrences).Phase);
        source.Position = 0; using var output = new MemoryStream(); output.WriteByte(42);
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith(shaping.Code + ":", failure.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("code", "Layout.CodeFont")]
    [InlineData("link", "Layout.Hyperlink")]
    [InlineData("keep", "Layout.KeepWithTable")]
    [InlineData("cell", "Layout.CellPageControls")]
    public void DefaultInspectionFindsModelLossesWithoutPagination(string kind, string code)
    {
        string? styles = null, relationships = null;
        string body;
        if (kind == "code")
        {
            styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='WordnetCodeBlock'><w:name w:val='Code'/></w:style></w:styles>";
            body = "<w:p><w:pPr><w:pStyle w:val='WordnetCodeBlock'/></w:pPr><w:r><w:t>Code text</w:t></w:r></w:p>";
        }
        else if (kind == "link")
        {
            relationships = TestDocuments.LinkRelationship("https://example.org/review");
            body = "<w:p><w:hyperlink r:id='link'><w:r><w:t>Review link</w:t></w:r></w:hyperlink></w:p>";
        }
        else if (kind == "keep") body = "<w:p><w:pPr><w:keepNext/></w:pPr><w:r><w:t>Keep heading</w:t></w:r></w:p>" + Cell(TestDocuments.TextParagraph("Cell text"));
        else body = Cell("<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Cell text</w:t></w:r></w:p>");
        DocumentFormat format = kind == "link" ? DocumentFormat.Xps : DocumentFormat.Pdf;
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body), relationships, styles));
        var converter = new WordConverter();
        DocumentInspection report = converter.Inspect(source, DocumentFormat.Docx, format, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(report.WouldRejectStrict); Assert.Null(report.PageCount);
        ConversionDiagnostic finding = Assert.Single(report.Diagnostics, d => d.Code == code);
        Assert.Equal(DiagnosticPhase.Destination, Assert.Single(finding.Occurrences).Phase);
        source.Position = 0; using var output = new MemoryStream();
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => converter.Convert(source, output, DocumentFormat.Docx, format,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith(code + ":", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CellGlyphWarningsLocateTheParagraphAndPredictStrictRejection()
    {
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(Cell(TestDocuments.TextParagraph("\u4e2d")))));
        var options = new ConversionOptions(FidelityMode.BestEffort);
        // A caller face with '?' but without this CJK glyph exercises the layout fallback.
        using var font = new MemoryStream(FixedLayout.FontBytes(0));
        options.Fonts.Sources.Add(new FontSource("Noto Sans", font));
        var converter = new WordConverter();
        DocumentInspection report = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, options, true, TestContext.Current.CancellationToken);
        Assert.True(report.WouldRejectStrict);
        ConversionDiagnostic glyph = Assert.Single(report.Diagnostics, d => d.Code == "Font.MissingGlyph");
        DiagnosticOccurrence occurrence = Assert.Single(glyph.Occurrences);
        Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]/p[1]", occurrence.Location.Path);
        Assert.Equal(DiagnosticPhase.Layout, occurrence.Phase);
        source.Position = 0; using var output = new MemoryStream();
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.Glyph:", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("indent", "Layout.IndentApproximated")]
    [InlineData("font", "Layout.TextScaledToFit")]
    [InlineData("cell", "Table.WidthApproximated")]
    public void LenientApproximationReportsThatStrictWouldReject(string kind, string code)
    {
        string paragraph = "<w:p><w:pPr>" + (kind == "indent" ? "<w:ind w:left='30000'/>" : "") + "</w:pPr><w:r><w:rPr>"
            + (kind == "font" ? "<w:sz w:val='3276'/>" : "") + "</w:rPr><w:t>Review geometry</w:t></w:r></w:p>";
        if (kind == "cell") paragraph = "<w:tbl><w:tblGrid><w:gridCol w:w='20'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='20'/>"
            + "<w:tcMar><w:left w:type='dxa' w:w='200'/><w:right w:type='dxa' w:w='200'/></w:tcMar></w:tcPr>" + paragraph + "</w:tc></w:tr></w:tbl>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph)));
        var converter = new WordConverter();
        DocumentInspection report = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), true, TestContext.Current.CancellationToken);
        Assert.True(report.WouldRejectStrict); Assert.Contains(report.Diagnostics, d => d.Code == code);
        source.Position = 0; using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FontOnlyInspectionResolvesCoalescedRunsOnceAndRecordsListFonts()
    {
        using var file = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-cff.otf"));
        FontSource face = Assert.Single(FontSource.FromStream(file));
        string run = "<w:r><w:rPr><w:rFonts w:ascii='" + face.FamilyName + "'/></w:rPr><w:t>AVAé</w:t></w:r>";
        using var source = new MemoryStream(FidelityDocuments.Docx(body: "<w:p><w:pPr><w:numPr><w:ilvl w:val='0'/><w:numId w:val='7'/></w:numPr></w:pPr>" + run + "</w:p>", lists: true));
        var options = new ConversionOptions(FidelityMode.BestEffort); options.Fonts.Sources.Add(face);
        DocumentInspection report = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, options, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, Assert.Single(report.Diagnostics, d => d.Code == "Font.MissingGlyph").Count);
        Assert.Contains(report.Fonts, f => f.RequestedFamily == "Noto Sans");
        Assert.Contains(report.Fonts, f => f.RequestedFamily == face.FamilyName && f.ResolvedFamily == "Noto Sans");
    }

    [Fact]
    public void RowAndNumberingFindingsIdentifyTheirOriginalElements()
    {
        string table = "<w:tbl><w:tr><w:trPr><w:cnfStyle w:val='100000000000'/></w:trPr><w:tc>" + TestDocuments.TextParagraph("Cell") + "</w:tc></w:tr></w:tbl>";
        string numbering = "<w:numbering xmlns:w='" + TestDocuments.W + "'><w:numIdMacAtCleanup w:val='4'/></w:numbering>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table),
            "<Relationship Id='numbering' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>",
            extra: new KeyValuePair<string, string>("word/numbering.xml", numbering)));
        DocumentInspection report = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        DiagnosticOccurrence row = Assert.Single(Assert.Single(report.Diagnostics, d => d.Code == "Docx.TableFormatting").Occurrences);
        Assert.Equal("word/document.xml", row.Location.Part); Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]", row.Location.Path);
        DiagnosticOccurrence number = Assert.Single(Assert.Single(report.Diagnostics, d => d.Code == "Docx.NumberingFeature").Occurrences);
        Assert.Equal("word/numbering.xml", number.Location.Part); Assert.Equal("/numbering[1]/numIdMacAtCleanup[1]", number.Location.Path);
    }

    [Fact]
    public void NoteFallbackLossIdentifiesTheNotePartAndElement()
    {
        using var source = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "contract-notes.docx"));
        DocumentInspection report = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Doc, cancellationToken: TestContext.Current.CancellationToken);
        DiagnosticOccurrence note = Assert.Single(Assert.Single(report.Diagnostics, d => d.Code == "Notes.PlacedAtEnd").Occurrences);
        Assert.Equal("word/footnotes.xml", note.Location.Part); Assert.StartsWith("/footnotes[1]/footnote[", note.Location.Path, StringComparison.Ordinal);
    }

    private static string Cell(string content) => "<w:tbl><w:tr><w:tc>" + content + "</w:tc></w:tr></w:tbl>";
}
