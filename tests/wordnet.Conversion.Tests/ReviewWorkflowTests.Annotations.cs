using System;
using System.Collections.Generic;
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
    private static ReviewDisplayOptions Numbered() => new ReviewDisplayOptions { AnnotationPolicy = ReviewAnnotationPolicy.NumberedEndNotes, IncludeLegend = false };
    private static WordDocument Commented(string body, string comments)
    {
        byte[] fixture = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new KeyValuePair<string, string>("word/comments.xml", "<w:comments xmlns:w='" + W + "'>" + comments + "</w:comments>"));
        return Load(fixture);
    }
    private static string ReviewPdf(DocumentReviewDisplay display)
    {
        using var output = new MemoryStream(); display.SavePdf(output, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); return string.Concat(pdf.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void NumberedOverlappingCommentsRetainAnchorsAttributionAndHistory()
    {
        string body = "<w:p><w:commentRangeStart w:id='7'/><w:r><w:t>A</w:t></w:r><w:commentRangeStart w:id='8'/>"
            + "<w:del w:id='3' w:author='Author'><w:r><w:delText>B</w:delText></w:r></w:del><w:commentRangeEnd w:id='7'/>"
            + "<w:r><w:commentReference w:id='7'/></w:r><w:r><w:t>C</w:t></w:r><w:commentRangeEnd w:id='8'/><w:r><w:commentReference w:id='8'/></w:r></w:p>";
        string comments = "<w:comment w:id='7' w:author='Author A' w:initials='AA'>" + P("First comment") + P("Second paragraph")
            + "</w:comment><w:comment w:id='8' w:author='Author B' w:date='2026-10-19T12:00:00Z'>" + P("Another comment") + "</w:comment>";
        WordDocument source = Commented(body, comments); byte[] before = Save(source); Schema(before);
        DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        Assert.Equal(2, display.Annotations.Count); Assert.Equal("C1", display.Annotations[0].Label); Assert.Equal(ReviewDisplayAnnotationKind.Comment, display.Annotations[0].Kind);
        Assert.Equal("Author A", display.Annotations[0].Author); Assert.Equal("AA", display.Annotations[0].Initials);
        Assert.Null(display.Annotations[0].Date); Assert.Equal("First comment\nSecond paragraph", display.Annotations[0].Text);
        Assert.Equal(Time, display.Annotations[1].Date); Assert.Equal("Another comment", display.Annotations[1].Text);
        string text = ReviewPdf(display);
        Assert.Contains("[C1:start]A[C2:start][D1:Author]B[/D1][C1:end]C[C2:end]", text, StringComparison.Ordinal);
        foreach (string marker in new[] { "[C1:start]", "[C1:end]", "[C2:start]", "[C2:end]" })
            Assert.Equal(1, (text.Length - text.Replace(marker, "").Length) / marker.Length);
        Assert.Contains("C1: comment by Author A (AA) (date not supplied)", text, StringComparison.Ordinal);
        Assert.Contains("First comment", text, StringComparison.Ordinal); Assert.Contains("Second paragraph", text, StringComparison.Ordinal);
        Assert.Contains("C2: comment by Author B at", text, StringComparison.Ordinal); Assert.Contains("Another comment", text, StringComparison.Ordinal);
        DocumentPreview svg = display.RenderSvg(cancellationToken: TestContext.Current.CancellationToken);
        XNamespace ns = "http://www.w3.org/2000/svg";
        string titles = string.Concat(svg.Pages.SelectMany(p => XDocument.Parse(Encoding.UTF8.GetString(p.GetBytes())).Descendants(ns + "title")).Select(e => e.Value));
        Assert.Contains("C1:start", titles, StringComparison.Ordinal); Assert.Contains("C2:end", titles, StringComparison.Ordinal);
        Assert.Contains("First comment", titles, StringComparison.Ordinal); Assert.Contains("Another comment", titles, StringComparison.Ordinal);
        Assert.Equal(before, Save(source)); Assert.Equal(2, source.Comments.Count); Assert.Single(source.Revisions);
        Assert.Equal("AC", source.CreateReviewView(ReviewDecision.Accept, true, TestContext.Current.CancellationToken).Paragraphs[0].Text);
    }

    [Fact]
    public void NumberedFormatChangesKeepCurrentAppearanceAndDescribeBothStates()
    {
        string body = "<w:p><w:commentRangeStart w:id='8'/><w:r><w:rPr><w:b/><w:color w:val='C00000'/><w:sz w:val='32'/>"
            + "<w:rPrChange w:id='1' w:author='Author'><w:rPr><w:i/><w:color w:val='0000FF'/><w:sz w:val='20'/></w:rPr></w:rPrChange>"
            + "</w:rPr><w:t>text</w:t></w:r><w:commentRangeEnd w:id='8'/><w:r><w:commentReference w:id='8'/></w:r></w:p>";
        WordDocument source = Commented(body, "<w:comment w:id='8' w:author='Commenter'>" + P("Review this") + "</w:comment>");
        byte[] before = Save(source); Schema(before); DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        Assert.Equal(2, display.Annotations.Count);
        ReviewDisplayAnnotation change = display.Annotations.Single(a => a.Kind == ReviewDisplayAnnotationKind.Formatting);
        Assert.Equal("F1", change.Label); Assert.Equal("text", change.Text); Assert.Equal("Author", change.Author);
        Assert.Contains("bold=false, italic=true, size=10, color=0000FF", change.PreviousFormatting, StringComparison.Ordinal);
        Assert.Contains("bold=true, italic=false, size=16, color=C00000", change.CurrentFormatting, StringComparison.Ordinal);
        string text = ReviewPdf(display); Assert.Contains("[C1:start][F1]text[/F1][C1:end]", text, StringComparison.Ordinal);
        Assert.Contains("F1: formatting by Author", text, StringComparison.Ordinal); Assert.Contains("previous bold=false", text, StringComparison.Ordinal);
        Assert.Contains("current bold=true", text, StringComparison.Ordinal); Assert.Contains("Review this", text, StringComparison.Ordinal);
        using var output = new MemoryStream(); display.SavePdf(output, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
        Assert.Contains(page.Letters, l => l.Value == "x" && l.FontSize == 16 && l.FontName!.Contains("Bold") && l.Color.ToRGBValues().r > 0.7);
        Assert.All(page.Letters, l => { Assert.InRange(l.StartBaseLine.X, 0, page.Width); Assert.InRange(l.StartBaseLine.Y, 0, page.Height); });
        DocumentPreview svg = display.RenderSvg(cancellationToken: TestContext.Current.CancellationToken); XNamespace ns = "http://www.w3.org/2000/svg";
        string titles = string.Concat(svg.Pages.SelectMany(p => XDocument.Parse(Encoding.UTF8.GetString(p.GetBytes())).Descendants(ns + "title")).Select(e => e.Value));
        Assert.Contains("[F1]text[/F1]", titles, StringComparison.Ordinal);
        Assert.Equal(before, Save(source)); Assert.Single(source.Comments); Assert.Single(source.Revisions);
    }

    [Fact]
    public void NumberedWholeParagraphMovesPairBothEndpointsWithoutChangingTheSource()
    {
        WordDocument original = Load(Input(P("A") + P("Move me") + P("B") + P("C")));
        WordDocument revised = Load(Input(P("A") + P("B") + P("C") + P("Move me")));
        WordDocument source = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] before = Save(source); Schema(before); DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        ReviewDisplayAnnotation move = Assert.Single(display.Annotations); Assert.Equal(ReviewDisplayAnnotationKind.Move, move.Kind);
        Assert.Equal("M1", move.Label); Assert.Equal("Move me", move.Text); Assert.Equal("Reviewer", move.Author); Assert.Equal(Time, move.Date);
        string text = ReviewPdf(display); Assert.Contains("[M1:from]Move me[/M1]", text, StringComparison.Ordinal);
        Assert.Contains("[M1:to]Move me[/M1]", text, StringComparison.Ordinal); Assert.Contains("M1: move by Reviewer at", text, StringComparison.Ordinal);
        Assert.Equal(1, (text.Length - text.Replace("[M1:from]", "").Length) / "[M1:from]".Length);
        Assert.Equal(1, (text.Length - text.Replace("[M1:to]", "").Length) / "[M1:to]".Length);
        DocumentPreview svg = display.RenderSvg(cancellationToken: TestContext.Current.CancellationToken); XNamespace ns = "http://www.w3.org/2000/svg";
        string titles = string.Concat(svg.Pages.SelectMany(p => XDocument.Parse(Encoding.UTF8.GetString(p.GetBytes())).Descendants(ns + "title")).Select(e => e.Value));
        Assert.Contains("[M1:from]Move me[/M1]", titles, StringComparison.Ordinal); Assert.Contains("[M1:to]Move me[/M1]", titles, StringComparison.Ordinal);
        Assert.Equal(before, Save(source)); Assert.NotEmpty(source.Revisions);
        Assert.Equal(string.Concat(original.Paragraphs.Select(p => p.Text)), string.Concat(source.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
        Assert.Equal(string.Concat(revised.Paragraphs.Select(p => p.Text)), string.Concat(source.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
    }

    [Fact]
    public void NumberedCommentAndMoveRemainDistinctAfterComposition()
    {
        WordDocument original = Load(Input(P("A") + P("Move me") + P("B") + P("C")));
        WordDocument revised = Load(Input(P("A") + P("B") + P("C") + P("Move me")));
        WordDocument source = DocumentComparison.Compare(original, revised, new ComparisonOptions("Mover", Time), Policy(), TestContext.Current.CancellationToken);
        string body = "<w:p><w:commentRangeStart w:id='1'/><w:r><w:t>Anchor</w:t></w:r><w:commentRangeEnd w:id='1'/>"
            + "<w:r><w:commentReference w:id='1'/></w:r></w:p>";
        WordDocument comment = Commented(body, "<w:comment w:id='1' w:author='Commenter'>" + P("Note") + "</w:comment>");
        source.Append(comment, cancellationToken: TestContext.Current.CancellationToken); byte[] before = Save(source); Schema(before);
        DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        Assert.Equal(2, display.Annotations.Count); Assert.Single(display.Annotations, a => a.Kind == ReviewDisplayAnnotationKind.Move);
        Assert.Single(display.Annotations, a => a.Kind == ReviewDisplayAnnotationKind.Comment);
        string text = ReviewPdf(display);
        Assert.Contains("[M1:from]Move me[/M1]", text, StringComparison.Ordinal); Assert.Contains("[M1:to]Move me[/M1]", text, StringComparison.Ordinal);
        Assert.Contains("[C1:start]Anchor[C1:end]", text, StringComparison.Ordinal); Assert.Contains("C1: comment by Commenter", text, StringComparison.Ordinal);
        Assert.Contains("M1: move by Mover", text, StringComparison.Ordinal); Assert.Equal(before, Save(source)); Assert.Single(source.Comments);
    }

    [Fact]
    public void NumberedLongCommentsContinueAcrossPagesAndHonorLimitsAndCancellation()
    {
        string text = string.Concat(Enumerable.Repeat("anchor ", 1500)); string comment = string.Concat(Enumerable.Repeat("comment ", 1000));
        string body = "<w:p><w:commentRangeStart w:id='1'/><w:r><w:t xml:space='preserve'>" + text + "</w:t></w:r>"
            + "<w:commentRangeEnd w:id='1'/><w:r><w:commentReference w:id='1'/></w:r></w:p>";
        WordDocument source = Commented(body, "<w:comment w:id='1' w:author='Reviewer'>" + P(comment) + "</w:comment>");
        byte[] before = Save(source); DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); display.SavePdf(output, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(pdf.NumberOfPages > 2);
        string all = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("[C1:start]", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("[C1:end]", all, StringComparison.Ordinal);
        Assert.Equal(1500, (all.Length - all.Replace("anchor", "").Length) / "anchor".Length);
        Assert.Equal(1001, (all.Length - all.Replace("comment", "").Length) / "comment".Length);
        Assert.Equal(comment, Assert.Single(display.Annotations).Text); Assert.Equal(before, Save(source));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.CreateReviewDisplay(Numbered(), cancelled.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateReviewDisplay(new ReviewDisplayOptions { AnnotationPolicy = (ReviewAnnotationPolicy)999 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(source));
    }

    [Fact]
    public void NumberedAnnotationsStillRejectOtherStructuralHistory()
    {
        WordDocument original = Load(Input(P("First") + P("Last")));
        WordDocument revised = Load(Input(P("First") + P("Inserted") + P("Last")));
        WordDocument source = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] before = Save(source); Assert.Throws<NotSupportedException>(() => source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(source)); Assert.NotEmpty(source.Revisions);
    }

    [Fact]
    public void NumberedAnnotationsRejectExpansionBeforePublishingASnapshot()
    {
        string body = "<w:p><w:commentRangeStart w:id='1'/><w:r><w:t>Body</w:t></w:r><w:commentRangeEnd w:id='1'/>"
            + "<w:r><w:commentReference w:id='1'/></w:r></w:p>";
        string comment = "<w:comments xmlns:w='" + W + "'><w:comment w:id='1' w:author='Reviewer'>" + P(new string('x', 1100)) + "</w:comment></w:comments>";
        byte[] fixture = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new KeyValuePair<string, string>("word/comments.xml", comment));
        using var input = new MemoryStream(fixture);
        WordDocument source = WordDocument.Load(input, DocumentFormat.Docx, new ReviewLoadOptions(), out _,
            new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 1800), TestContext.Current.CancellationToken);
        byte[] before = Save(source);
        var error = Assert.Throws<InvalidDataException>(() => source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.ReviewDisplay:", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(source)); Assert.Single(source.Comments); Assert.True(input.CanRead);
    }

    [Fact]
    public void NumberedEmptyCommentAndAnchorRemainIdentifiable()
    {
        string body = "<w:p><w:commentRangeStart w:id='1'/><w:commentRangeEnd w:id='1'/><w:r><w:commentReference w:id='1'/></w:r>"
            + "<w:r><w:t>Next</w:t></w:r></w:p>";
        WordDocument source = Commented(body, "<w:comment w:id='1' w:author='Reviewer'>" + P("") + "</w:comment>");
        byte[] before = Save(source); Schema(before);
        DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        Assert.Equal("", Assert.Single(display.Annotations).Text);
        string text = ReviewPdf(display); Assert.Contains("[C1:start][C1:end]Next", text, StringComparison.Ordinal);
        Assert.Contains("C1: comment by Reviewer (date not supplied)", text, StringComparison.Ordinal);
        Assert.Equal(before, Save(source)); Assert.Single(source.Comments);
    }

    [Fact]
    public void NumberedCommentsInSimpleTableCellsRetainTheirAnchorAndText()
    {
        string paragraph = "<w:p><w:commentRangeStart w:id='1'/><w:r><w:t>Cell text</w:t></w:r><w:commentRangeEnd w:id='1'/>"
            + "<w:r><w:commentReference w:id='1'/></w:r></w:p>";
        string body = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc>" + paragraph + "</w:tc></w:tr></w:tbl>";
        WordDocument source = Commented(body, "<w:comment w:id='1' w:author='Reviewer'>" + P("Cell note") + "</w:comment>");
        byte[] before = Save(source); Schema(before);
        DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); display.SavePdf(output, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
        Assert.Contains("[C1:start]Cell text[C1:end]", page.Text, StringComparison.Ordinal); Assert.Contains("Cell note", page.Text, StringComparison.Ordinal);
        Assert.All(page.Letters, l => { Assert.InRange(l.StartBaseLine.X, 0, page.Width); Assert.InRange(l.StartBaseLine.Y, 0, page.Height); });
        Assert.Equal("Cell note", Assert.Single(display.Annotations).Text); Assert.Equal(before, Save(source)); Assert.Single(source.Comments);
    }

    [Fact]
    public void NumberedFormatSummaryRetainsTheExactFontSizes()
    {
        WordDocument original = WordDocument.Create(); original.AddParagraph("Text", new TextFormatting { FontSize = 10.12345 }, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument revised = WordDocument.Create(); revised.AddParagraph("Text", new TextFormatting { FontSize = 11.23456 }, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument source = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        DocumentReviewDisplay display = source.CreateReviewDisplay(Numbered(), TestContext.Current.CancellationToken);
        ReviewDisplayAnnotation format = Assert.Single(display.Annotations);
        Assert.Equal(ReviewDisplayAnnotationKind.Formatting, format.Kind);
        Assert.Contains("size=10.12345", format.PreviousFormatting, StringComparison.Ordinal);
        Assert.Contains("size=11.23456", format.CurrentFormatting, StringComparison.Ordinal);
        Assert.Single(source.Revisions);
    }
}
