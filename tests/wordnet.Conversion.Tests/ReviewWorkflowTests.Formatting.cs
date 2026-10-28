using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    [Fact]
    public void FormatHistoryPreservesTabsAndLineBreaksAfterReload()
    {
        WordDocument old = Load(Input("<w:p><w:r><w:t>Name:</w:t><w:tab/><w:t>Value</w:t><w:br/><w:t>Next</w:t></w:r></w:p>"));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Name:</w:t><w:tab/><w:t>Value</w:t><w:br/><w:t>Next</w:t></w:r></w:p>"));
        WordDocument compared = DocumentComparison.Compare(old, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(compared); Schema(reviewed);
        WordDocument reloaded = Load(reviewed); Assert.Single(reloaded.Revisions);
        Assert.Equal("Name:\tValue\nNext", reloaded.Paragraphs[0].Text);
        Assert.Equal("Name:\tValue\nNext", reloaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
    }
    [Fact]
    public void FormatHistoryKeepsBothAppearancesAndExplicitViews()
    {
        string content = "<w:p><w:r><w:rPr><w:b/><w:color w:val='C00000'/><w:sz w:val='32'/><w:rPrChange w:id='12' w:author='Reviewer' w:date='2026-10-19T12:00:00Z'><w:rPr><w:i/><w:color w:val='0000FF'/><w:sz w:val='20'/></w:rPr></w:rPrChange></w:rPr><w:t>café</w:t></w:r></w:p>";
        WordDocument doc = Load(Input(content)); byte[] saved = Save(doc); Schema(saved);
        var change = Part(saved).Descendants(W + "rPrChange").Single();
        Assert.Equal("Reviewer", (string?)change.Attribute(W + "author"));
        Assert.NotNull(change.Element(W + "rPr")!.Element(W + "i"));
        Assert.Equal("0000FF", (string?)change.Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val"));
        Assert.Single(Load(saved).Revisions);
        WordDocument accepted = doc.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = doc.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        var a = Part(Save(accepted)).Descendants(W + "rPr").First(); var b = Part(Save(rejected)).Descendants(W + "rPr").First();
        Assert.NotNull(a.Element(W + "b")); Assert.Equal("0", (string?)a.Element(W + "i")!.Attribute(W + "val"));
        Assert.NotNull(b.Element(W + "i")); Assert.Equal("0", (string?)b.Element(W + "b")!.Attribute(W + "val"));
        using PdfDocument acceptedPdf = PdfDocument.Open(Save(accepted, DocumentFormat.Pdf));
        using PdfDocument rejectedPdf = PdfDocument.Open(Save(rejected, DocumentFormat.Pdf));
        Assert.Equal(16, acceptedPdf.GetPage(1).Letters[0].FontSize, 3);
        Assert.Equal(10, rejectedPdf.GetPage(1).Letters[0].FontSize, 3);
        Assert.Equal(1, rejectedPdf.GetPage(1).Letters[0].Color.ToRGBValues().b, 3);
        Assert.Equal(saved, Save(doc));
    }

    [Fact]
    public void ComparisonCreatesFormattingHistoryAcrossDifferentRunBoundaries()
    {
        WordDocument original = Load(Input("<w:p><w:r><w:t>café text</w:t></w:r></w:p>"));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>café</w:t></w:r><w:r><w:t xml:space='preserve'> text</w:t></w:r></w:p>"));
        byte[] before = Save(original); WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed); Assert.Single(Part(reviewed).Descendants(W + "rPrChange"));
        Assert.NotNull(Part(Save(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "b").FirstOrDefault());
        Assert.DoesNotContain(Part(Save(result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "b"), e => (string?)e.Attribute(W + "val") != "0");
        Assert.Equal(before, Save(original));
        DocumentReviewItem stale = result.Revisions.Single(); result.ApplyRevisions(result.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => result.ApplyRevisions(new[] { stale }, ReviewDecision.Accept, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FormattingHistoryRejectsUnsupportedChangesAndSurvivesComposition()
    {
        string good = "<w:p><w:r><w:rPr><w:b/><w:rPrChange w:id='1' w:author='Author'><w:rPr/></w:rPrChange></w:rPr><w:t>text</w:t></w:r></w:p>";
        WordDocument source = Load(Input(good)); WordDocument target = Load(Input("<w:p><w:r><w:t>first</w:t></w:r></w:p>"));
        target.Append(source, cancellationToken: TestContext.Current.CancellationToken); target.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(target); Schema(output); Assert.Equal(2, Load(output).Revisions.Count);
        Assert.Equal(2, Part(output).Descendants(W + "rPrChange").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        string unsupported = good.Replace("<w:b/>", "<w:b/><w:u w:val='single'/>");
        Assert.Throws<NotSupportedException>(() => Load(Input(unsupported)));
        string mixed = good.Replace("<w:r><w:rPr>", "<w:ins w:id='2' w:author='Author'><w:r><w:rPr>").Replace("</w:r></w:p>", "</w:r></w:ins></w:p>");
        Assert.Throws<NotSupportedException>(() => Load(Input(mixed)));
    }

    [Fact]
    public void FormatDecisionKeepsExplicitOffAgainstInheritedBoldAfterReload()
    {
        string body = "<w:p><w:pPr><w:pStyle w:val='Emphasis'/></w:pPr><w:r><w:rPr><w:b w:val='0'/><w:rPrChange w:id='1' w:author='Author'><w:rPr><w:b/></w:rPr></w:rPrChange></w:rPr><w:t>X</w:t></w:r></w:p>";
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Emphasis'><w:name w:val='Emphasis'/><w:rPr><w:b/></w:rPr></w:style></w:styles>";
        byte[] fixture = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>", extra: new KeyValuePair<string, string>("word/styles.xml", styles));
        WordDocument source = Load(fixture);
        WordDocument accepted = source.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = source.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(Load(Save(accepted)).Paragraphs[0].Runs[0].Formatting.Bold);
        Assert.True(Load(Save(rejected)).Paragraphs[0].Runs[0].Formatting.Bold);
        Assert.False(Load(Save(Load(Save(accepted)))).Paragraphs[0].Runs[0].Formatting.Bold);
    }
    [Fact]
    public void IntersectingCommentRequiresExplicitRemovalBeforeFormatDecision()
    {
        string body = "<w:p><w:commentRangeStart w:id='8'/><w:r><w:rPr><w:b/><w:rPrChange w:id='1' w:author='Author'><w:rPr/></w:rPrChange></w:rPr><w:t>text</w:t></w:r>"
            + "<w:commentRangeEnd w:id='8'/><w:r><w:commentReference w:id='8'/></w:r></w:p>";
        string comments = "<w:comments xmlns:w='" + W + "'><w:comment w:id='8' w:author='Author'>" + P("Review") + "</w:comment></w:comments>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new KeyValuePair<string, string>("word/comments.xml", comments));
        WordDocument document = Load(input); byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.ApplyRevisions(document.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document)); document.RemoveComments(document.Comments, TestContext.Current.CancellationToken);
        document.ApplyRevisions(document.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        Assert.Empty(document.Revisions); Schema(Save(document));
    }
}
