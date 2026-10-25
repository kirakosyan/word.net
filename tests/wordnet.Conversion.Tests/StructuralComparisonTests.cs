using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace WordNet.Tests;

public sealed class StructuralComparisonTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly ComparisonOptions Attribution = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private static string P(string text) => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";
    private static WordDocument Load(string body) => WordDocument.Load(new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body))), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
    private static byte[] Save(WordDocument doc) { using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static XElement Body(byte[] bytes) { using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream xml = zip.GetEntry("word/document.xml")!.Open(); return XDocument.Load(xml).Root!.Element(W + "body")!; }
    private static string Visible(WordDocument document) => string.Concat(Body(Save(document)).Descendants(W + "t").Select(e => e.Value));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParagraphMarksAndTextRevisionsRecoverBothStructuresAfterReload(bool empty)
    {
        WordDocument original = Load(P("First") + P("Removed") + P("Last"));
        WordDocument revised = Load(P("First") + P(empty ? "" : "Inserted") + P("Other") + P("Last"));
        byte[] oldBytes = Save(original), newBytes = Save(revised);
        WordDocument review = DocumentComparison.Compare(original, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(review); using var package = WordprocessingDocument.Open(new MemoryStream(output), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        XElement body = Body(output); Assert.Contains(body.Descendants(W + "ins"), e => e.Parent?.Name == W + "rPr");
        Assert.Contains(body.Descendants(W + "del"), e => e.Parent?.Name == W + "rPr");
        string[] ids = body.Descendants().Where(e => e.Name == W + "ins" || e.Name == W + "del").Select(e => (string)e.Attribute(W + "id")!).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        WordDocument loaded = WordDocument.Load(new MemoryStream(output), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        foreach (WordDocument candidate in new[] { review, loaded })
        {
            Assert.Equal(Visible(original), Visible(candidate.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
            Assert.Equal(Visible(revised), Visible(candidate.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
            Assert.Equal(3, Body(Save(candidate.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken))).Elements(W + "p").Count());
            Assert.Equal(4, Body(Save(candidate.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken))).Elements(W + "p").Count());
        }
        Assert.Equal(oldBytes, Save(original)); Assert.Equal(newBytes, Save(revised));
        DocumentReviewItem[] handles = review.Revisions.ToArray(); review.ApplyRevisions(handles, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        Assert.Empty(review.Revisions); Assert.Throws<ArgumentException>(() => review.ApplyRevisions(handles, ReviewDecision.Reject, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SimpleRowsUseTrPrHistoryWithUnchangedHeaderAndRecoverBothRowSets()
    {
        WordDocument original = Load(Table("Heading", "A", "Removed", "Z")), revised = Load(Table("Heading", "A", "New", "Extra", "Z"));
        WordDocument review = DocumentComparison.Compare(original, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(review); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.All(Body(bytes).Descendants(W + "ins").Concat(Body(bytes).Descendants(W + "del")), marker => Assert.Equal(W + "trPr", marker.Parent!.Name));
        WordDocument loaded = WordDocument.Load(new MemoryStream(bytes), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(original), Visible(loaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(revised), Visible(loaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(3, loaded.Revisions.Count); Assert.All(loaded.Revisions, item => Assert.Equal("row", item.Structure));
    }

    [Fact]
    public void RepeatedContentUsesStableEarliestMatchesAndLimitsAreAtomic()
    {
        WordDocument left = Load(P("Repeat") + P("Repeat") + P("End")), right = Load(P("Repeat") + P("New") + P("Repeat") + P("End"));
        Assert.Equal(Save(DocumentComparison.Compare(left, right, Attribution, cancellationToken: TestContext.Current.CancellationToken)), Save(DocumentComparison.Compare(left, right, Attribution, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidDataException>(() => DocumentComparison.Compare(left, right, new ComparisonOptions("A", Attribution.Timestamp) { MaximumSteps = 1 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<OperationCanceledException>(() => DocumentComparison.Compare(left, right, Attribution, cancellationToken: new System.Threading.CancellationToken(true)));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(Load(P("A") + P("B")), Load(P("B") + P("A") + P("C")), Attribution, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(Load(P("A")), Load(P("A").Replace("<w:r>", "<w:r><w:rPr><w:b/></w:rPr>") + P("Extra")), Attribution, cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void AppendingStructuralHistoryRemapsBothMarkersAndTextIdentifiers()
    {
        WordDocument left = Load(P("First") + Table("Heading", "Old"));
        WordDocument right = Load(P("First") + P("Inserted") + Table("Heading", "New", "Extra"));
        WordDocument review = DocumentComparison.Compare(left, right, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument composed = WordDocument.Create(); composed.Append(review, cancellationToken: TestContext.Current.CancellationToken); composed.Append(review, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(review.Revisions.Count * 2, composed.Revisions.Count);
        byte[] bytes = Save(composed); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.Equal(Visible(left) + Visible(left), Visible(composed.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(right) + Visible(right), Visible(composed.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
    }
    [Fact]
    public void EmptyFinalSectionCannotHideAProtectedSectionDifference()
    {
        string section = "<w:sectPr><w:pgSz w:w='8000' w:h='6000'/></w:sectPr>";
        string body = P("First").Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>");
        WordDocument left = Load(body + section), right = Load(body + P("Inserted") + section.Replace("8000", "9000"));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(left, right, Attribution, cancellationToken: TestContext.Current.CancellationToken));
    }
    private static string Table(params string[] rows) => "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + string.Concat(rows.Select((text, i) => "<w:tr>" + (i == 0 ? "<w:trPr><w:tblHeader/></w:trPr>" : "") + "<w:tc>" + P(text) + "</w:tc></w:tr>")) + "</w:tbl>";
}
