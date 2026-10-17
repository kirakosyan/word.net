using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    [Fact]
    public void ImageStreamsRespectPositionOwnershipLimitsAndCancellation()
    {
        byte[] pixels = FidelityDocuments.Fixture("color-grid.png");
        using var source = new MemoryStream(); source.WriteByte(42); source.Write(pixels, 0, pixels.Length); source.Position = 1;
        TemplateImage image = TemplateImage.FromStream(source, 30, 15, cancellationToken: TestContext.Current.CancellationToken); Assert.True(source.CanRead);
        WordDocument document = WordDocument.Create(); document.AddImage(image, TestContext.Current.CancellationToken); Assert.Equal(pixels, ImagePart(Save(document)));
        source.Position = 1;
        Assert.Throws<InvalidDataException>(() => TemplateImage.FromStream(source, 30, 15, maximumBytes: 1, cancellationToken: TestContext.Current.CancellationToken)); Assert.True(source.CanRead);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); source.Position = 1;
        Assert.Throws<OperationCanceledException>(() => TemplateImage.FromStream(source, 30, 15, cancellationToken: cancelled.Token)); Assert.Equal(1, source.Position);
    }
    [Fact]
    public void AppendPreservesDefaultDestinationGeometryAndUnresolvedSourceReferences()
    {
        WordDocument target = WordDocument.Create(Policy()); target.AddParagraph("Destination", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("Missing", 0, 0, 11, TestContext.Current.CancellationToken);
        WordDocument source = Load(Docx("<w:p>" + Field("REF Missing", "source cache") + "</w:p>" + Page));
        target.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(target); Schema(word);
        Assert.Equal("source cache", Part(word, "word/document.xml").Descendants(W + "fldSimple").Single().Value);
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages); Assert.InRange(pdf.GetPage(1).Width, 595.27, 595.28); Assert.Equal(400, pdf.GetPage(2).Width, 2);
    }

    [Fact]
    public void NestedControlsSharingARowAreDiagnosedOnEditingLoad()
    {
        byte[] input = Docx("<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Outer", Control("repeat:Inner", "<w:tr>" + Cell(P("Content")) + "</w:tr>")) + "</w:tbl>");
        WordDocument document = Load(input); Assert.Contains(document.Diagnostics, d => d.Code == "Template.NestedRowControls");
        using var stream = new MemoryStream(input);
        Assert.Throws<NotSupportedException>(() => WordDocument.Load(stream, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void MultirowRepeatControlsAreExplicitlyRejected()
    {
        string rows = "<w:tr>" + Cell(P("One")) + "</w:tr><w:tr>" + Cell(P("Two")) + "</w:tr>";
        WordDocument document = Load(Docx("<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Items", rows) + "</w:tbl>"));
        byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.Merge(new { Items = OneItem }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }
    [Theory]
    [InlineData("\\# &quot;F2&quot;", "text")]
    [InlineData("\\@ &quot;yyyy&quot;", 42)]
    public void FormattingSwitchesRejectIncompatibleScalarTypes(string format, object value)
    {
        WordDocument document = Load(Docx("<w:p>" + Field("MERGEFIELD Value " + format, "cached") + "</w:p>")); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.Merge(new { Value = value }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void CallerGetterFailuresDoNotExposeValuesOrChangeTheDocument()
    {
        WordDocument document = Load(Docx("<w:p>" + Field("MERGEFIELD Value", "cached") + "</w:p>")); byte[] before = Save(document);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => document.Merge(new FailingData("private-value"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private-value", error.ToString(), StringComparison.Ordinal); Assert.Equal(before, Save(document));
    }
    private sealed class FailingData
    {
        private readonly string message;
        internal FailingData(string message) { this.message = message; }
        public string Value => throw new InvalidOperationException(message);
    }

    [Fact]
    public void ReusedScalarExpansionIsAdmittedBeforeCommit()
    {
        byte[] input = Docx("<w:p>" + string.Concat(Enumerable.Repeat(Field("MERGEFIELD Name", "cached"), 10)) + "</w:p>");
        using var stream = new MemoryStream(input);
        WordDocument document = WordDocument.Load(stream, DocumentFormat.Docx, out _, Policy(5000), TestContext.Current.CancellationToken); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.Merge(new { Name = new string('x', 1000) }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }
    [Fact]
    public void UnexecutedMergeCachesKeepMixedFormattingUntilBinding()
    {
        WordDocument document = Load(Docx("<w:p><w:fldSimple w:instr='MERGEFIELD Name'><w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>Italic</w:t></w:r></w:fldSimple></w:p>"));
        byte[] before = Save(document); Schema(before); XDocument cached = Part(before, "word/document.xml");
        Assert.NotEmpty(cached.Descendants(W + "b")); Assert.NotEmpty(cached.Descendants(W + "i"));
        document.Merge(new { Name = "Bound" }, cancellationToken: TestContext.Current.CancellationToken);
        XDocument filled = Part(Save(document), "word/document.xml");
        Assert.Equal("Bound", filled.Root!.Value); Assert.NotEmpty(filled.Descendants(W + "b")); Assert.Empty(filled.Descendants(W + "fldSimple"));
    }
    [Theory]
    [InlineData(CompositionStyle.KeepSource)]
    [InlineData(CompositionStyle.UseDestination)]
    public void CollidingNamedStylesResolveAccordingToTheCompositionPolicy(CompositionStyle policy)
    {
        WordDocument Styled(string text, string style) => Load(TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr><w:pStyle w:val='Shared'/></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>" + Page),
            styles: "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Shared'><w:name w:val='Shared'/><w:rPr><w:" + style + "/></w:rPr></w:style></w:styles>"));
        WordDocument target = Styled("Destination", "i"), source = Styled("Source", "b");
        target.Append(source, policy, TestContext.Current.CancellationToken);
        byte[] output = Save(target); Schema(output);
        XElement paragraph = Part(output, "word/document.xml").Descendants(W + "p").Single(p => p.Value == "Source");
        Assert.NotEmpty(paragraph.Descendants(W + (policy == CompositionStyle.KeepSource ? "b" : "i")));
    }

    [Fact]
    public void RepeatedNoteReferencesReceiveUniquePartIdsAndUpdatedLabels()
    {
        string body = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Items", "<w:tr>" + Cell("<w:p><w:r><w:t>Reference</w:t><w:footnoteReference w:id='7'/></w:r></w:p>") + "</w:tr>") + "</w:tbl>" + Page;
        WordDocument document = Load(TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new KeyValuePair<string, string>("word/footnotes.xml", "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'>" + Control("if:ShowNote", P("Note")) + "</w:footnote></w:footnotes>")));
        document.Merge(new { Items = ThreeItems, ShowNote = false }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output);
        Assert.Equal(3, Part(output, "word/document.xml").Descendants(W + "footnoteReference").Select(r => (string?)r.Attribute(W + "id")).Distinct().Count());
        Assert.Equal(3, Part(output, "word/footnotes.xml").Descendants(W + "footnote").Count(n => n.Attribute(W + "type") == null));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Reference1", text, StringComparison.Ordinal); Assert.Contains("Reference2", text, StringComparison.Ordinal); Assert.Contains("Reference3", text, StringComparison.Ordinal);
    }
    [Fact]
    public void OrdinaryConversionStillUsesCachedMergeFields()
    {
        byte[] input = Docx("<w:p>" + Field("MERGEFIELD Name", "Cached name") + "</w:p>");
        byte[] pdf = new WordConverter().ConvertToPdf(input, out ConversionResult result, Policy());
        using PdfDocument parsed = PdfDocument.Open(pdf); Assert.Equal("Cached name", parsed.GetPage(1).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
    }

    [Fact]
    public void ScalarFieldsAndControlsInHeaderStoriesAreMerged()
    {
        string body = "<w:p>" + Field("MERGEFIELD Name", "cached") + "</w:p>" + Page.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + Control("merge:Name", P("cached")) + "</w:hdr>"));
        WordDocument document = Load(input);
        Assert.Equal(2, document.Merge(new { Name = "Hello" }, cancellationToken: TestContext.Current.CancellationToken).Changes);
        byte[] output = Save(document); Schema(output);
        Assert.Contains("Hello", Part(output, "word/header1.xml").Root!.Value, StringComparison.Ordinal);
        Assert.Empty(Part(output, "word/header1.xml").Descendants(W + "sdt"));
    }

    [Fact]
    public void CompositionKeepsSourceStoriesAndExplicitlyStopsDestinationInheritance()
    {
        string section = Page.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        WordDocument WithHeader(string text, string header) => Load(TestDocuments.Docx(TestDocuments.Body(P(text) + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>")));
        WordDocument document = WithHeader("Cover", "Cover header");
        document.Append(Load(Docx(P("No header") + Page)), cancellationToken: TestContext.Current.CancellationToken);
        document.Append(WithHeader("Appendix", "Appendix header"), cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output);
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.Equal(3, pdf.NumberOfPages); Assert.Contains("Cover header", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cover header", pdf.GetPage(2).Text, StringComparison.Ordinal); Assert.Contains("Appendix header", pdf.GetPage(3).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void NestedRowRegionsResolveItemScopeAndRespectDepth(int maximumDepth, bool succeeds)
    {
        string inner = "<w:tbl><w:tblGrid><w:gridCol w:w='2000'/></w:tblGrid>" + Control("repeat:Lines", "<w:tr>" + Cell("<w:p>" + Field("MERGEFIELD Description", "old") + "</w:p>") + "</w:tr>") + "</w:tbl>";
        string outer = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Orders", "<w:tr>" + Cell("<w:p>" + Field("MERGEFIELD Name", "old") + "</w:p>" + inner + P("End")) + "</w:tr>") + "</w:tbl>";
        WordDocument document = Load(Docx(outer + Page)); byte[] before = Save(document);
        var data = new { Orders = new[] { new { Name = "Order", Lines = new[] { new { Description = "One" }, new { Description = "Two" } } } } };
        if (!succeeds)
        { Assert.Throws<InvalidDataException>(() => document.Merge(data, new TemplateOptions { MaximumDepth = maximumDepth }, TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document)); return; }
        document.Merge(data, new TemplateOptions { MaximumDepth = maximumDepth }, TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output); XDocument xml = Part(output, "word/document.xml");
        Assert.Equal(3, xml.Descendants(W + "tr").Count()); Assert.Contains("OrderOneTwoEnd", xml.Root!.Value, StringComparison.Ordinal); Assert.Empty(xml.Descendants(W + "sdt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InlineConditionsEvaluateNestedMergeFieldsAndRemoveMarkers(bool show)
    {
        WordDocument document = Load(Docx("<w:p>" + Control("if:Show", Field("MERGEFIELD Name", "cached")) + "</w:p>"));
        document.Merge(new { Show = show, Name = "Visible" }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output); XDocument xml = Part(output, "word/document.xml");
        Assert.Equal(show ? "Visible" : "", document.Paragraphs[0].Text); Assert.Empty(xml.Descendants(W + "sdt")); Assert.Empty(xml.Descendants(W + "fldSimple"));
    }

    [Fact]
    public void RepeatingRowsKeepHorizontalSpansDecorationAndContinuingNumbering()
    {
        string row = "<w:tr><w:trPr><w:trHeight w:val='500'/><w:cantSplit/></w:trPr><w:tc><w:tcPr><w:gridSpan w:val='2'/><w:shd w:fill='FFFF00'/></w:tcPr>" + FidelityDocuments.ListParagraph("Item") + "</w:tc></w:tr>";
        WordDocument document = Load(FidelityDocuments.Docx(body: "<w:tbl><w:tblGrid><w:gridCol w:w='2000'/><w:gridCol w:w='2000'/></w:tblGrid>" + Control("repeat:Items", row) + "</w:tbl>" + Page, lists: true));
        document.Merge(new { Items = ThreeItems }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output); XDocument xml = Part(output, "word/document.xml");
        Assert.Equal(3, xml.Descendants(W + "gridSpan").Count()); Assert.Equal(3, xml.Descendants(W + "shd").Count()); Assert.Equal(3, xml.Descendants(W + "cantSplit").Count());
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal("3. Item4. Item5. Item", pdf.GetPage(1).Text);
    }

    [Fact]
    public void InvalidImageAndCrossRegionBookmarksFailAtomically()
    {
        WordDocument document = Load(Docx(Control("image:Logo", P("cached")))); byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.Merge(new { Logo = new TemplateImage(Encoding.UTF8.GetBytes("not an image"), 20, 10) }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
        string body = "<w:p><w:bookmarkStart w:id='1' w:name='Cross'/></w:p><w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>"
            + Control("repeat:Items", "<w:tr>" + Cell("<w:p><w:r><w:t>End</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>") + "</w:tr>") + "</w:tbl>";
        WordDocument crossing = Load(Docx(body)); byte[] original = Save(crossing);
        Assert.Throws<NotSupportedException>(() => crossing.Merge(new { Items = OneItem }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(original, Save(crossing));
    }

    [Fact]
    public void DuplicateTargetsDatesNullsAndRootFallbackHaveDeterministicBindings()
    {
        string text = Field("MERGEFIELD Name", "a") + Field("MERGEFIELD Name", "b") + Field("MERGEFIELD Date \\@ &quot;yyyy-MM-dd&quot;", "old") + Field("MERGEFIELD Empty", "old");
        WordDocument document = Load(Docx("<w:p>" + text + "</w:p>"));
        Assert.Equal(4, document.Merge(new { Name = "Twice", Date = new DateTime(2026, 10, 17), Empty = (string?)null }, cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal("TwiceTwice2026-10-17", document.Paragraphs[0].Text); Schema(Save(document));
    }

    [Fact]
    public void OutputLimitsEmptyCompositionAndAggregateInputAdmissionAreEnforced()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("Small", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, document.Append(WordDocument.Create(), cancellationToken: TestContext.Current.CancellationToken).Changes);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => document.Save(output, DocumentFormat.Docx, new ConversionOptions(maximumOutputBytes: 64), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        WordDocument source = WordDocument.Create(); source.AddParagraph(new string('x', 101), cancellationToken: TestContext.Current.CancellationToken);
        WordDocument target = WordDocument.Create(Policy(100)); Assert.Throws<InvalidDataException>(() => target.Append(source, cancellationToken: TestContext.Current.CancellationToken)); Assert.Empty(target.Paragraphs);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var input = new MemoryStream(Save(document));
        Assert.Throws<OperationCanceledException>(() => WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: cancelled.Token)); Assert.True(input.CanRead);
    }

    [Fact]
    public void StrictMissingFallbackAndUnrelatedControlTagsAreExplicit()
    {
        using var input = new MemoryStream(Docx("<w:p>" + Field("MERGEFIELD Name", "cached") + "</w:p>"));
        WordDocument strict = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => strict.Merge(new { }, new TemplateOptions { MissingValues = MissingTemplateValue.KeepCached }, TestContext.Current.CancellationToken)); Assert.Equal("cached", strict.Paragraphs[0].Text);
        WordDocument unsupported = Load(Docx(Control("execute:Code", P("cached")))); byte[] before = Save(unsupported);
        Assert.Equal(0, unsupported.Merge(new { Code = "ignored" }, cancellationToken: TestContext.Current.CancellationToken).Changes); Assert.Equal(before, Save(unsupported));
    }

    [Fact]
    public void LoadDiagnosticsKeepTheirCountsAndLocationsThroughEditsAndSave()
    {
        WordDocument document = Load(Docx("<w:p><w:ins><w:r><w:t>A</w:t></w:r></w:ins><w:ins><w:r><w:t>B</w:t></w:r></w:ins></w:p>"));
        ConversionDiagnostic original = Assert.Single(document.Diagnostics, d => d.Code == "Revisions.Accepted");
        document.AddParagraph("New", cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        ConversionDiagnostic copied = Assert.Single(result.Diagnostics, d => d.Code == original.Code);
        Assert.Equal(original.Count, copied.Count); Assert.Equal(original.Occurrences.Select(o => o.Location.Path), copied.Occurrences.Select(o => o.Location.Path));
    }
}
