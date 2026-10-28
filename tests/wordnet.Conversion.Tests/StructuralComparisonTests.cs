using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class StructuralComparisonTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly ComparisonOptions Attribution = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private static string P(string text) => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";
    private static WordDocument Load(string body) => WordDocument.Load(new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body))), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
    private static byte[] Save(WordDocument doc) { using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static byte[] Pdf(WordDocument doc) { using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
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
    [Fact]
    public void UniqueParagraphMoveUsesPairedRangesAndAtomicDecisions()
    {
        WordDocument original = Load(P("A") + P("Move me") + P("B") + P("C"));
        WordDocument revised = Load(P("A") + P("B") + P("C") + P("Move me"));
        WordDocument result = DocumentComparison.Compare(original, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        XElement body = Body(bytes); XElement from = Assert.Single(body.Elements(W + "moveFromRangeStart")); XElement to = Assert.Single(body.Elements(W + "moveToRangeStart"));
        Assert.Equal((string?)from.Attribute(W + "name"), (string?)to.Attribute(W + "name"));
        Assert.Single(body.Descendants(W + "moveFrom"), e => e.Parent?.Name == W + "p");
        Assert.Single(body.Descendants(W + "moveTo"), e => e.Parent?.Name == W + "p");
        WordDocument loaded = WordDocument.Load(new MemoryStream(bytes), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(original), Visible(loaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(revised), Visible(loaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        WordDocument rejected = WordDocument.Load(new MemoryStream(bytes), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        rejected.ApplyRevisions(new[] { rejected.Revisions.Single(item => item.Structure.Length == 0 && item.IsDeletion) }, ReviewDecision.Reject, TestContext.Current.CancellationToken);
        Assert.Empty(rejected.Revisions); Assert.Equal(Visible(original), Visible(rejected));
        loaded.ApplyRevisions(new[] { loaded.Revisions[0] }, ReviewDecision.Accept, TestContext.Current.CancellationToken);
        Assert.Empty(loaded.Revisions); Assert.Equal(Visible(revised), Visible(loaded));
        WordDocument composed = WordDocument.Create();
        composed.Append(result, cancellationToken: TestContext.Current.CancellationToken);
        composed.Append(result, cancellationToken: TestContext.Current.CancellationToken);
        byte[] composedBytes = Save(composed);
        using (var composedPackage = WordprocessingDocument.Open(new MemoryStream(composedBytes), false))
            Assert.Empty(new OpenXmlValidator().Validate(composedPackage, TestContext.Current.CancellationToken));
        Assert.Equal(Visible(revised) + Visible(revised), Visible(composed.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, Body(composedBytes).Elements(W + "moveFromRangeStart").Select(e => (string?)e.Attribute(W + "name")).Distinct().Count());
        WordDocument duplicateOld = Load(P("A") + P("Move") + P("B") + P("Move") + P("C"));
        WordDocument duplicateNew = Load(P("A") + P("B") + P("Move") + P("C") + P("Move"));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(duplicateOld, duplicateNew, Attribution, cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void DuplicateParagraphEditedBeforeAnotherOccurrenceIsNotAMove()
    {
        WordDocument old = Load(P("Yes") + P("Question 2") + P("Yes"));
        WordDocument revised = Load(P("No") + P("Question 2") + P("Yes"));
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(old), Visible(result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.DoesNotContain(result.Revisions, item => item.Structure == "paragraph");
    }
    [Fact]
    public void UniqueMoveCanAccompanyAnUnrelatedParagraphEdit()
    {
        WordDocument old = Load(P("One") + P("Two") + P("Three") + P("Four") + P("Old"));
        WordDocument revised = Load(P("Two") + P("Three") + P("One") + P("Four") + P("New"));
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(old), Visible(result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        byte[] bytes = Save(result); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }
    [Fact]
    public void ReorderedEmptyParagraphUsesOrdinaryStructuralRevisions()
    {
        WordDocument old = Load(P("") + P("A") + P("B") + P("C"));
        WordDocument revised = Load(P("A") + P("B") + P("C") + P(""));
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        var accepted = result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs;
        var rejected = result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs;
        Assert.Equal("A|B|C|", string.Join("|", accepted.Select(paragraph => paragraph.Text)));
        Assert.Equal("|A|B|C", string.Join("|", rejected.Select(paragraph => paragraph.Text)));
    }
    [Fact]
    public void SmallStructuralComparisonDoesNotChargePackageBytesAsSteps()
    {
        WordDocument old = Load(P("A") + P("B"));
        WordDocument revised = Load(P("A") + P("Added") + P("B"));
        var options = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)) { MaximumSteps = 5000 };
        WordDocument result = DocumentComparison.Compare(old, revised, options, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
    }
    [Fact]
    public void MatchingTextComparisonDoesNotImposeTheStructuralSectionLimit()
    {
        const string section = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/></w:sectPr>";
        string body = string.Concat(Enumerable.Range(0, 70).Select(i => P("Paragraph " + i).Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>")));
        WordDocument old = Load(body + section), revised = Load(body.Replace("Paragraph 42", "Changed 42") + section);
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
    }
    [Fact]
    public void MatchingLargeDocumentsUseTheConfiguredWorkBudget()
    {
        string body = string.Concat(Enumerable.Range(0, 6000).Select(i => P("Paragraph " + i)));
        WordDocument old = Load(body), revised = Load(body.Replace("Paragraph 4200", "Changed 4200"));
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
    }
    [Fact]
    public void MovedParagraphAtSectionEndKeepsTheSectionBoundary()
    {
        const string section = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/></w:sectPr>";
        string Boundary(string text) => P(text).Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>");
        WordDocument old = Load(P("Move") + P("A") + Boundary("B") + P("Next section") + section);
        WordDocument revised = Load(P("A") + P("B") + Boundary("Move") + P("Next section") + section);
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        WordDocument reloaded = WordDocument.Load(new MemoryStream(bytes), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(revised), Visible(reloaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(old), Visible(reloaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, Body(Save(reloaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "sectPr").Count());
    }
    [Fact]
    public void IndependentlyAuthoredMovePairAndMalformedRangesAreValidated()
    {
        const string metadata = " w:author='Author' w:date='2026-10-25T12:00:00Z'";
        string Range(string side, int range, int mark, int text) => "<w:move" + side + "RangeStart w:id='" + range + "' w:name='moveA'" + metadata + "/>"
            + "<w:p><w:pPr><w:rPr><w:move" + side + " w:id='" + mark + "'" + metadata + "/></w:rPr></w:pPr>"
            + "<w:move" + side + " w:id='" + text + "'" + metadata + "><w:r><w:t>Move</w:t></w:r></w:move" + side + "></w:p>"
            + "<w:move" + side + "RangeEnd w:id='" + range + "'/>";
        string content = P("A") + Range("From", 1, 2, 3) + P("B") + P("C") + Range("To", 4, 5, 6);
        WordDocument Read(string body) => WordDocument.Load(new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body))), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument document = Read(content);
        Assert.Equal("AMoveBC", Visible(document.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal("ABCMove", Visible(document.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidDataException>(() => Read(content.Replace("moveToRangeStart w:id='4' w:name='moveA'", "moveToRangeStart w:id='4' w:name='other'")));
        Assert.Throws<InvalidDataException>(() => Read(content.Replace("moveToRangeEnd w:id='4'", "moveToRangeEnd w:id='99'")));
        string changedParagraph = P("A") + Range("From", 1, 2, 3) + P("B") + P("C")
            + Range("To", 4, 5, 6).Replace("<w:pPr>", "<w:pPr><w:jc w:val='center'/>");
        Assert.Throws<InvalidDataException>(() => Read(changedParagraph));
    }
    [Theory]
    [InlineData("continuous")]
    [InlineData("oddPage")]
    [InlineData("evenPage")]
    public void StructuralChangesStayWithinMatchingSections(string breakType)
    {
        string start = breakType == "continuous" ? "" : " w:start='3'";
        string section = "<w:sectPr><w:type w:val='" + breakType + "'/><w:pgSz w:w='12240' w:h='15840'/><w:pgNumType" + start + " w:fmt='lowerRoman'/></w:sectPr>";
        string first = P("First").Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>");
        WordDocument old = Load(first + P("Last") + section), revised = Load(first + P("Inserted") + P("Last") + section);
        WordDocument compared = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(compared); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        WordDocument accepted = compared.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = compared.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Visible(revised), Visible(accepted)); Assert.Equal(Visible(old), Visible(rejected));
        Assert.Equal(2, Body(Save(accepted)).Descendants(W + "sectPr").Count());
        Assert.Equal(2, Body(Save(rejected)).Descendants(W + "sectPr").Count());
        XElement[] numbering = Body(Save(accepted)).Descendants(W + "pgNumType").ToArray(); Assert.Equal(2, numbering.Length);
        Assert.All(numbering, item => { Assert.Equal("lowerRoman", (string?)item.Attribute(W + "fmt")); Assert.Equal(breakType == "continuous" ? null : "3", (string?)item.Attribute(W + "start")); });
        using PdfDocument oldPdf = PdfDocument.Open(Pdf(old)), newPdf = PdfDocument.Open(Pdf(revised));
        using PdfDocument acceptedPdf = PdfDocument.Open(Pdf(accepted)), rejectedPdf = PdfDocument.Open(Pdf(rejected));
        Assert.Equal(oldPdf.NumberOfPages, rejectedPdf.NumberOfPages); Assert.Equal(newPdf.NumberOfPages, acceptedPdf.NumberOfPages);
        Assert.Equal(string.Concat(oldPdf.GetPages().Select(p => p.Text)), string.Concat(rejectedPdf.GetPages().Select(p => p.Text)));
        Assert.Equal(string.Concat(newPdf.GetPages().Select(p => p.Text)), string.Concat(acceptedPdf.GetPages().Select(p => p.Text)));
        WordDocument differentGeometry = Load(first + P("Inserted") + P("Last") + section.Replace("12240", "10000"));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(old, differentGeometry, Attribution, cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void CrossSectionMovesAndExcessiveSectionCountsAreRejected()
    {
        const string section = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/></w:sectPr>";
        string boundary = P("Boundary").Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>");
        WordDocument old = Load(P("Move") + boundary + P("Last") + section), revised = Load(boundary + P("Last") + P("Move") + section);
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken));
        string many = string.Concat(Enumerable.Range(0, 65).Select(i => P("P" + i).Replace("<w:p>", "<w:p><w:pPr>" + section + "</w:pPr>")));
        WordDocument left = Load(many + section), right = Load(many + P("Added") + section);
        Assert.Throws<InvalidDataException>(() => DocumentComparison.Compare(left, right, Attribution, cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void EmptySectionAndRowsRetainTheirSectionBoundaries()
    {
        const string section = "<w:sectPr><w:type w:val='continuous'/><w:pgSz w:w='12240' w:h='15840'/></w:sectPr>";
        string empty = "<w:p><w:pPr>" + section + "</w:pPr></w:p>";
        WordDocument old = Load(empty + Table("Heading", "Old") + section), revised = Load(empty + Table("Heading", "New", "Extra") + section);
        WordDocument result = DocumentComparison.Compare(old, revised, Attribution, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.Equal(Visible(old), Visible(result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Visible(revised), Visible(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(2, Body(Save(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "sectPr").Count());
    }
    private static string Table(params string[] rows) => "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + string.Concat(rows.Select((text, i) => "<w:tr>" + (i == 0 ? "<w:trPr><w:tblHeader/></w:trPr>" : "") + "<w:tc>" + P(text) + "</w:tc></w:tr>")) + "</w:tbl>";
}
