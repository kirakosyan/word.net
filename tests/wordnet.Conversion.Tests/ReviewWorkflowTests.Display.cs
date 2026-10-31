using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    [Fact]
    public void ReviewDisplayExportsSearchableMarkedTextAndKeepsSourceHistory()
    {
        WordDocument source = Load(Input(Revisions)); byte[] before = Save(source); Schema(before);
        DocumentReviewDisplay display = source.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, display.Revisions.Count);
        Assert.Equal("D1", display.Revisions[0].Label); Assert.True(display.Revisions[0].IsDeletion);
        Assert.Equal("10", display.Revisions[0].Text); Assert.Equal("Original Author", display.Revisions[0].Author); Assert.Null(display.Revisions[0].Date);
        Assert.Equal("I2", display.Revisions[1].Label); Assert.False(display.Revisions[1].IsDeletion);
        Assert.Equal("20", display.Revisions[1].Text); Assert.Equal(Time, display.Revisions[1].Date);
        using var output = new MemoryStream(); output.WriteByte(42);
        display.SavePdf(output, TestContext.Current.CancellationToken); Assert.True(output.CanWrite); Assert.Equal(42, output.ToArray()[0]);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray().Skip(1).ToArray());
        string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("[D1:Original Author]10[/D1]", text, StringComparison.Ordinal);
        Assert.Contains("[I2:New Author@", text, StringComparison.Ordinal); Assert.Contains("20[/I2]", text, StringComparison.Ordinal);
        Assert.Contains("Revision legend", text, StringComparison.Ordinal); Assert.Contains("date not supplied", text, StringComparison.Ordinal);
        var page = pdf.GetPage(1);
        var red = page.Letters.Where(l => l.Color.ToRGBValues().r > 0.6 && l.Color.ToRGBValues().b < 0.2).ToArray();
        var blue = page.Letters.Where(l => l.Color.ToRGBValues().b > 0.5 && l.Color.ToRGBValues().r < 0.1).ToArray();
        Assert.Contains("10[/D1]", string.Concat(red.Select(l => l.Value)), StringComparison.Ordinal);
        Assert.Contains("20[/I2]", string.Concat(blue.Select(l => l.Value)), StringComparison.Ordinal);
        Assert.Contains(red, l => l.Value == "0" && l.FontName!.Contains("Bold"));
        Assert.Contains(blue, l => l.Value == "0" && l.FontName!.Contains("Italic"));
        Assert.All(red.Concat(blue), l => { Assert.InRange(l.StartBaseLine.X, 0, page.Width); Assert.InRange(l.StartBaseLine.Y, 0, page.Height); });
        Assert.True(red[0].StartBaseLine.Y > blue[0].StartBaseLine.Y ||
            (red[0].StartBaseLine.Y == blue[0].StartBaseLine.Y && red[0].StartBaseLine.X < blue[0].StartBaseLine.X));
        Assert.Equal(before, Save(source)); Assert.Equal(2, source.Revisions.Count);
        Assert.Equal("Total 20", source.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal("Total 10", source.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
    }

    [Fact]
    public void ReviewDisplaySvgHasMarkedGlyphsAndSnapshotSurvivesSourceDecisions()
    {
        WordDocument source = Load(Input(Revisions));
        DocumentReviewDisplay display = source.CreateReviewDisplay(new ReviewDisplayOptions { IncludeLegend = false }, TestContext.Current.CancellationToken);
        source.ApplyRevisions(source.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        var mapping = Assert.Throws<NotSupportedException>(() => display.RenderSvg(new PreviewOptions { Layout = new LayoutSnapshotOptions() }, TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.ReviewDisplay:", mapping.Message, StringComparison.Ordinal);
        DocumentPreview preview = display.RenderSvg(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(preview.Pages); byte[] bytes = preview.Pages[0].GetBytes();
        XDocument svg = XDocument.Parse(Encoding.UTF8.GetString(bytes)); XNamespace ns = "http://www.w3.org/2000/svg";
        string titles = string.Concat(svg.Descendants(ns + "title").Select(e => e.Value));
        Assert.Contains("10", titles, StringComparison.Ordinal); Assert.Contains("20", titles, StringComparison.Ordinal);
        Assert.Contains("D1", titles, StringComparison.Ordinal); Assert.Contains("I2", titles, StringComparison.Ordinal);
        Assert.DoesNotContain("Revision legend", titles, StringComparison.Ordinal);
        Assert.Contains(svg.Descendants(ns + "g"), e => (string?)e.Attribute("fill") == "#B00020");
        Assert.Contains(svg.Descendants(ns + "g"), e => (string?)e.Attribute("fill") == "#0062B1");
        string redText = string.Concat(svg.Descendants(ns + "g").Where(e => (string?)e.Attribute("fill") == "#B00020").Select(e => (string?)e.Element(ns + "title")));
        string blueText = string.Concat(svg.Descendants(ns + "g").Where(e => (string?)e.Attribute("fill") == "#0062B1").Select(e => (string?)e.Element(ns + "title")));
        Assert.Contains("10[/D1]", redText, StringComparison.Ordinal); Assert.Contains("20[/I2]", blueText, StringComparison.Ordinal);
        Assert.NotEmpty(svg.Descendants(ns + "path"));
        bytes[0] ^= 0xff; Assert.NotEqual(bytes, preview.Pages[0].GetBytes());
        Assert.Empty(source.Revisions); Assert.Equal("Total 20", source.Paragraphs[0].Text);
        Assert.Equal("10", display.Revisions[0].Text); Assert.Equal("20", display.Revisions[1].Text);
    }

    [Fact]
    public void ReviewDisplayHandlesCellTextAndMultiplePages()
    {
        string cell = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc>" + Revisions + "</w:tc></w:tr></w:tbl>";
        WordDocument table = Load(Input(cell)); byte[] before = Save(table);
        DocumentReviewDisplay cellDisplay = table.CreateReviewDisplay(new ReviewDisplayOptions { IncludeLegend = false }, TestContext.Current.CancellationToken);
        using var cellPdf = new MemoryStream(); cellDisplay.SavePdf(cellPdf, TestContext.Current.CancellationToken);
        using PdfDocument parsed = PdfDocument.Open(cellPdf.ToArray());
        Assert.Contains("10[/D1]", parsed.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("20[/I2]", parsed.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal(before, Save(table));

        string longText = string.Concat(Enumerable.Repeat("removed ", 1500));
        string history = "<w:p><w:del w:id='1' w:author='Reviewer'><w:r><w:delText xml:space='preserve'>" + longText + "</w:delText></w:r></w:del></w:p>";
        WordDocument longSource = Load(Input(history));
        DocumentReviewDisplay longDisplay = longSource.CreateReviewDisplay(new ReviewDisplayOptions { IncludeLegend = false }, TestContext.Current.CancellationToken);
        using var longPdf = new MemoryStream(); longDisplay.SavePdf(longPdf, TestContext.Current.CancellationToken);
        using PdfDocument pages = PdfDocument.Open(longPdf.ToArray()); Assert.True(pages.NumberOfPages > 1);
        string text = string.Concat(pages.GetPages().Select(p => p.Text));
        Assert.Equal(1500, (text.Length - text.Replace("removed", "").Length) / "removed".Length);
        Assert.Contains("[D1:Reviewer]", pages.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("[/D1]", pages.GetPage(pages.NumberOfPages).Text, StringComparison.Ordinal);
        Assert.All(pages.GetPages(), p => Assert.All(p.Letters, l =>
        { Assert.InRange(l.StartBaseLine.X, 0, p.Width); Assert.InRange(l.StartBaseLine.Y, 0, p.Height); }));
        Assert.Equal(longText, Assert.Single(longDisplay.Revisions).Text); Assert.Single(longSource.Revisions);
    }

    [Fact]
    public void ReviewDisplayRejectsUnsupportedProfilesAndLeavesOutputUntouched()
    {
        string format = "<w:p><w:r><w:rPr><w:b/><w:rPrChange w:id='12' w:author='Reviewer'><w:rPr/></w:rPrChange></w:rPr><w:t>text</w:t></w:r></w:p>";
        WordDocument formatHistory = Load(Input(format)); byte[] before = Save(formatHistory);
        var error = Assert.Throws<NotSupportedException>(() => formatHistory.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Review.DisplayProfile:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(formatHistory));
        WordDocument ordinary = Load(Input(Revisions)); DocumentReviewDisplay display = ordinary.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => display.RenderSvg(new PreviewOptions { Format = PreviewFormat.Png }, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => display.SavePdf(output, cancellation.Token)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.True(output.CanWrite); Assert.Equal(2, ordinary.Revisions.Count);
    }

    [Fact]
    public void ReviewDisplayRejectsCommentsAndStoryRevisionsWithoutChangingSource()
    {
        string body = "<w:p><w:commentRangeStart w:id='7'/><w:r><w:t>Text</w:t></w:r><w:commentRangeEnd w:id='7'/>"
            + "<w:r><w:commentReference w:id='7'/></w:r></w:p>";
        string comments = "<w:comments xmlns:w='" + W + "'><w:comment w:id='7' w:author='Reviewer'>" + P("Comment") + "</w:comment></w:comments>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/comments.xml", comments));
        WordDocument commented = Load(input); byte[] before = Save(commented);
        Assert.Throws<NotSupportedException>(() => commented.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(commented)); Assert.Single(commented.Comments);

        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        string header = "<w:hdr xmlns:w='" + W + "'><w:p><w:ins w:id='1' w:author='Reviewer'><w:r><w:t>Header</w:t></w:r></w:ins></w:p></w:hdr>";
        input = TestDocuments.Docx(TestDocuments.Body(P("Body") + section), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/header.xml", header));
        WordDocument story = Load(input); before = Save(story); Assert.Single(story.Revisions);
        Assert.Throws<NotSupportedException>(() => story.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(story)); Assert.Single(story.Revisions);
    }

    [Fact]
    public void ReviewDisplayRejectsTemplateOwnersAndPriorSourceLoss()
    {
        string control = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>" + P("Cached") + "</w:sdtContent></w:sdt>";
        WordDocument templated = Load(Input(Revisions + control));
        var error = Assert.Throws<NotSupportedException>(() => templated.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Review.DisplayProfile:", error.Message, StringComparison.Ordinal); Assert.Equal(2, templated.Revisions.Count);

        string lossy = "<w:tbl><w:tblPr><w:tblInd w:w='10' w:type='pct'/></w:tblPr><w:tr><w:tc>" + P("Cell") + "</w:tc></w:tr></w:tbl>";
        WordDocument source = Load(Input(Revisions + lossy));
        Assert.Contains(source.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
        error = Assert.Throws<NotSupportedException>(() => source.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Review.DisplaySourceLoss:", error.Message, StringComparison.Ordinal); Assert.Equal(2, source.Revisions.Count);
    }

    [Fact]
    public void ReviewDisplayProfileChangesFailBeforeWriting()
    {
        var options = Policy(); using var input = new MemoryStream(Input(Revisions));
        WordDocument source = WordDocument.Load(input, DocumentFormat.Docx, new ReviewLoadOptions(), out _, options, TestContext.Current.CancellationToken);
        DocumentReviewDisplay display = source.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        options.Pdf.Accessibility = PdfAccessibility.Tagged;
        Assert.Throws<NotSupportedException>(() => display.SavePdf(output, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        options.Pdf.Accessibility = PdfAccessibility.None; options.Typography.Enabled = true;
        Assert.Throws<NotSupportedException>(() => display.SavePdf(output, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.Throws<NotSupportedException>(() => source.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, source.Revisions.Count); Assert.True(input.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void ReviewDisplayHonorsBudgetAndEscapesAttributionMarkers()
    {
        WordDocument constrained = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 200));
        constrained.AddParagraph("abc", cancellationToken: TestContext.Current.CancellationToken);
        DocumentReviewRange range = constrained.GetReviewRange(0, 1, 1, TestContext.Current.CancellationToken);
        constrained.RecordReplacement(range, "X", new string('A', 30), Time, TestContext.Current.CancellationToken);
        byte[] before = Save(constrained);
        Assert.Throws<InvalidDataException>(() => constrained.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(constrained)); Assert.Equal(2, constrained.Revisions.Count);

        WordDocument source = WordDocument.Create(); source.AddParagraph("abc", cancellationToken: TestContext.Current.CancellationToken);
        range = source.GetReviewRange(0, 1, 1, TestContext.Current.CancellationToken);
        source.RecordReplacement(range, "X", "A]B\nC", Time, TestContext.Current.CancellationToken);
        DocumentReviewDisplay display = source.CreateReviewDisplay(new ReviewDisplayOptions { IncludeLegend = false }, TestContext.Current.CancellationToken);
        Assert.Equal("A]B\nC", display.Revisions[0].Author);
        using var output = new MemoryStream(); display.SavePdf(output, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Contains("A\\]B\\u000AC", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }
}
