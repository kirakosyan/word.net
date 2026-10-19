using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly DateTimeOffset Time = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static ConversionOptions Policy() => new ConversionOptions(FidelityMode.Lenient);
    private static byte[] Input(string content) => TestDocuments.Docx(TestDocuments.Body(content));
    private static string P(string text) => TestDocuments.TextParagraph(text);
    private static WordDocument Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return WordDocument.Load(stream, DocumentFormat.Docx, new ReviewLoadOptions(), out _, Policy(), TestContext.Current.CancellationToken);
    }
    private static byte[] Save(WordDocument doc, DocumentFormat format = DocumentFormat.Docx)
    { using var output = new MemoryStream(); doc.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static XDocument Part(byte[] bytes, string name = "word/document.xml")
    { using var stream = new MemoryStream(bytes); using var archive = new ZipArchive(stream, ZipArchiveMode.Read); using Stream part = archive.GetEntry(name)!.Open(); return XDocument.Load(part); }
    private static void Schema(byte[] bytes)
    { using var stream = new MemoryStream(bytes); using var package = WordprocessingDocument.Open(stream, false); Assert.Empty(new OpenXmlValidator().Validate(package)); }
    private const string Revisions = "<w:p><w:r><w:t>Total </w:t></w:r><w:del w:id='3' w:author='Original Author'><w:r><w:rPr><w:b/></w:rPr><w:delText>10</w:delText></w:r></w:del>"
        + "<w:ins w:id='4' w:author='New Author' w:date='2026-10-19T12:00:00Z'><w:r><w:rPr><w:i/></w:rPr><w:t>20</w:t></w:r></w:ins></w:p>";
    [Fact]
    public void OptInPreservationKeepsRealDeletedTextAndMissingDatesThroughSchemaValidSave()
    {
        WordDocument doc = Load(Input(Revisions));
        Assert.Equal("Total 20", doc.Paragraphs[0].Text); Assert.Equal(2, doc.Revisions.Count);
        Assert.Null(doc.Revisions.Single(r => r.IsDeletion).Date);
        byte[] saved = Save(doc); Schema(saved); XDocument xml = Part(saved);
        Assert.Equal("10", xml.Descendants(W + "delText").Single().Value); Assert.Equal("20", xml.Descendants(W + "ins").Single().Value);
        Assert.Null(xml.Descendants(W + "del").Single().Attribute(W + "date"));
        Assert.Equal("Original Author", (string?)xml.Descendants(W + "del").Single().Attribute(W + "author"));
        Assert.Equal(2, Load(saved).Revisions.Count);
        Assert.Throws<NotSupportedException>(() => Save(doc, DocumentFormat.Pdf));
    }
    [Fact]
    public void PreservationRejectsMissingAuthorsAndRevisionsInsideRuns()
    {
        Assert.Throws<InvalidDataException>(() => Load(Input(Revisions.Replace(" w:author='Original Author'", ""))));
        Assert.Throws<NotSupportedException>(() => Load(Input("<w:p><w:r><w:ins w:id='1' w:author='Author'><w:r><w:t>X</w:t></w:r></w:ins></w:r></w:p>")));
        string comments = "<w:comments xmlns:w='" + W + "'><w:comment w:id='1'>" + P("Comment") + "</w:comment></w:comments>";
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body(P("Body")),
            "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new KeyValuePair<string, string>("word/comments.xml", comments));
        Assert.Throws<InvalidDataException>(() => Load(bytes));
    }
    [Theory]
    [InlineData(ReviewDecision.Accept, "Total 20")]
    [InlineData(ReviewDecision.Reject, "Total 10")]
    public void ExplicitViewsAndDecisionsMatchIndependentExpectedText(ReviewDecision decision, string expected)
    {
        WordDocument doc = Load(Input(Revisions)); byte[] before = Save(doc);
        WordDocument view = doc.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected, view.Paragraphs[0].Text); Assert.Empty(view.Revisions);
        using PdfDocument pdf = PdfDocument.Open(Save(view, DocumentFormat.Pdf)); Assert.Equal(expected, pdf.GetPage(1).Text);
        Assert.Equal(before, Save(doc));
        doc.ApplyRevisions(doc.Revisions, decision, TestContext.Current.CancellationToken);
        Assert.Equal(expected, doc.Paragraphs[0].Text); Assert.Empty(doc.Revisions); Schema(Save(doc));
        Assert.Empty(Part(Save(doc)).Descendants(W + "delText"));
    }
    [Fact]
    public void ReplacementRecordsCallerMetadataAndInvalidatesHandlesAtomically()
    {
        WordDocument doc = Load(Input(P("Total 10")));
        DocumentReviewRange range = doc.GetReviewRange(0, 6, 2, TestContext.Current.CancellationToken);
        doc.RecordReplacement(range, "20", "Caller Author", Time, TestContext.Current.CancellationToken);
        Assert.Equal("Total 20", doc.Paragraphs[0].Text); Assert.Equal(2, doc.Revisions.Count);
        Schema(Save(doc)); Assert.Equal("10", Part(Save(doc)).Descendants(W + "delText").Single().Value);
        Assert.All(doc.Revisions, r => { Assert.Equal("Caller Author", r.Author); Assert.Equal(Time, r.Date); });
        byte[] before = Save(doc); var diagnostics = doc.Diagnostics;
        Assert.Throws<ArgumentException>(() => doc.RecordReplacement(range, "30", "Caller Author", Time, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc)); Assert.Same(diagnostics, doc.Diagnostics);
        DocumentReviewItem stale = doc.Revisions[0];
        doc.ApplyRevisions(doc.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => doc.ApplyRevisions(new[] { stale }, ReviewDecision.Accept, TestContext.Current.CancellationToken));
    }
    [Fact]
    public void GraphemeSplitsForeignRangesAndCancellationCannotChangeSessions()
    {
        WordDocument doc = Load(Input(P("A😀e\u0301B"))), other = Load(Input(P("Other")));
        Assert.Throws<ArgumentException>(() => doc.GetReviewRange(0, 2, 1, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => doc.GetReviewRange(0, 3, 1, TestContext.Current.CancellationToken));
        DocumentReviewRange range = doc.GetReviewRange(0, 1, 4, TestContext.Current.CancellationToken); byte[] before = Save(doc);
        Assert.Throws<ArgumentException>(() => other.RecordReplacement(range, "Text", "Author", Time, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => doc.RecordReplacement(range, "Text", "Author", Time, cancellation.Token)); Assert.Equal(before, Save(doc));
    }
    [Fact]
    public void AppendRemapsHistoryIdsAndRetainsIndependentDecisions()
    {
        WordDocument doc = Load(Input(Revisions)); WordDocument source = Load(Input(Revisions));
        doc.Append(source, cancellationToken: TestContext.Current.CancellationToken); doc.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(6, doc.Revisions.Count); Assert.Equal(2, source.Revisions.Count);
        byte[] saved = Save(doc); Schema(saved);
        Assert.Equal(6, Part(saved).Descendants().Where(e => e.Name == W + "ins" || e.Name == W + "del").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        Assert.Equal(6, Load(saved).Revisions.Count);
    }
    [Fact]
    public void CrossingCommentsRetainMetadataAndMustBeRemovedBeforeConsumingTheirText()
    {
        string body = "<w:p><w:commentRangeStart w:id='7'/><w:r><w:t>A</w:t></w:r><w:commentRangeStart w:id='8'/>"
            + "<w:del w:id='3' w:author='Author'><w:r><w:delText>B</w:delText></w:r></w:del><w:commentRangeEnd w:id='7'/>"
            + "<w:r><w:commentReference w:id='7'/></w:r><w:r><w:t>C</w:t></w:r><w:commentRangeEnd w:id='8'/><w:r><w:commentReference w:id='8'/></w:r></w:p>";
        string comments = "<w:comments xmlns:w='" + W + "'><w:comment w:id='7' w:author='Author A' w:initials='AA'>" + P("Comment one")
            + "</w:comment><w:comment w:id='8' w:author='Author B' w:date='2026-10-19T12:00:00Z'>" + P("Comment two") + "</w:comment></w:comments>";
        byte[] fixture = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/>",
            extra: new KeyValuePair<string, string>("word/comments.xml", comments));
        WordDocument doc = Load(fixture); Assert.Equal(2, doc.Comments.Count); Assert.Equal("AA", doc.Comments[0].Initials); Assert.Null(doc.Comments[0].Date);
        Assert.Equal("Comment one", doc.Comments[0].Text); byte[] before = Save(doc); Schema(before);
        Assert.Equal(2, Load(before).Comments.Count);
        Assert.Throws<NotSupportedException>(() => doc.ApplyRevisions(doc.Revisions, ReviewDecision.Accept, TestContext.Current.CancellationToken)); Assert.Equal(before, Save(doc));
        WordDocument view = doc.CreateReviewView(ReviewDecision.Accept, true, TestContext.Current.CancellationToken);
        Assert.Equal("AC", view.Paragraphs[0].Text); Assert.Empty(view.Comments); Assert.Equal(before, Save(doc));
        doc.RemoveComments(doc.Comments, TestContext.Current.CancellationToken); doc.ApplyRevisions(doc.Revisions, ReviewDecision.Reject, TestContext.Current.CancellationToken);
        Assert.Equal("ABC", doc.Paragraphs[0].Text); Assert.Empty(doc.Comments); Schema(Save(doc));
        Assert.Empty(Part(Save(doc)).Descendants(W + "commentReference"));
    }
    [Theory]
    [InlineData(ComparisonGranularity.Word)]
    [InlineData(ComparisonGranularity.Grapheme)]
    public void ComparisonProducesDeterministicRealRevisionsAndBothExpectedViews(ComparisonGranularity granularity)
    {
        WordDocument original = Load(Input(P("Total 10, café 😀."))), revised = Load(Input(P("Total 20, café 😀!")));
        byte[] a = Save(original), b = Save(revised);
        var options = new ComparisonOptions("Comparison Author", Time) { Granularity = granularity };
        WordDocument compared = DocumentComparison.Compare(original, revised, options, Policy(), TestContext.Current.CancellationToken);
        Schema(Save(compared)); Assert.NotEmpty(compared.Revisions);
        Assert.Equal("Total 20, café 😀!", compared.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal("Total 10, café 😀.", compared.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal(a, Save(original)); Assert.Equal(b, Save(revised));
        Assert.Equal(Save(compared), Save(DocumentComparison.Compare(original, revised, options, Policy(), TestContext.Current.CancellationToken)));
    }
    [Fact]
    public void ComparisonRejectsHistoryStructureAndWorkExhaustionWithoutMutation()
    {
        WordDocument original = Load(Input(P("Original"))), revised = Load(Input(P("Revised"))); byte[] before = Save(original);
        Assert.Throws<InvalidDataException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time) { Granularity = ComparisonGranularity.Grapheme, MaximumSteps = 2 }, Policy(), TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, Load(Input(P("Extra") + P("Paragraph"))), new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, Load(Input(Revisions)), new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(original));
    }
    [Theory]
    [InlineData("a b a", "b a b")]
    [InlineData("e\u0301 👩‍👩‍👧‍👦", "e\u0301 👩‍👩‍👧‍👧")]
    [InlineData("שלום 10", "שלום 20")]
    public void ComparisonRetainsRepeatedTokensGraphemesAndLogicalRtlText(string oldText, string newText)
    {
        WordDocument original = Load(Input(P(oldText))), revised = Load(Input(P(newText)));
        WordDocument compared = DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Equal(newText, compared.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal(oldText, compared.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Schema(Save(compared));
    }
    [Fact]
    public void ComparisonWorksInMatchingCellsAndCoalescesEquivalentRunBoundaries()
    {
        WordDocument original = WordDocument.Create(Policy()), revised = WordDocument.Create(Policy());
        string oldValue = "10", newValue = "20";
        original.AddTable(new[] { new[] { "Total", oldValue } }, cancellationToken: TestContext.Current.CancellationToken);
        revised.AddTable(new[] { new[] { "Total", newValue } }, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument compared = DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Equal(newValue, compared.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[1].Text);
        Assert.Equal(oldValue, compared.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[1].Text);
        Schema(Save(compared));
        original = Load(Input("<w:p><w:r><w:t>Tot</w:t></w:r><w:r><w:t>al 10</w:t></w:r></w:p>")); revised = Load(Input(P("Total 20")));
        compared = DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Equal("Total 20", compared.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
    }
    [Fact]
    public void ComparisonPreservesMatchingHeaderStoriesAndRejectsChangesToThem()
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] Document(string text, string header) => TestDocuments.Docx(TestDocuments.Body(P(text) + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>"));
        WordDocument original = Load(Document("Total 10", "Stable header")), revised = Load(Document("Total 20", "Stable header"));
        WordDocument compared = DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken);
        Schema(Save(compared)); Assert.Equal("Stable header", Part(Save(compared), "word/header1.xml").Root!.Value);
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, Load(Document("Total 20", "Changed header")), new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData("<w:moveTo w:id='1' w:author='Author'><w:r><w:t>X</w:t></w:r></w:moveTo>")]
    [InlineData("<w:r><w:rPr><w:rPrChange w:id='1' w:author='Author'><w:rPr/></w:rPrChange></w:rPr><w:t>X</w:t></w:r>")]
    public void UnsupportedValidHistoryRejectsPreservation(string content)
    { Assert.Throws<NotSupportedException>(() => Load(Input("<w:p>" + content + "</w:p>"))); }
}
