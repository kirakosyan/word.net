using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    [Theory]
    [InlineData(" here")]
    [InlineData(" here!")]
    public void ComparisonRejectsFormattingBoundaryChangesOnEqualText(string tail)
    {
        string Runs(string bold, string plain) => "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>" + bold + "</w:t></w:r><w:r><w:t xml:space='preserve'>" + plain + "</w:t></w:r></w:p>";
        WordDocument original = Load(Input(Runs("Important", " notice here")));
        WordDocument revised = Load(Input(Runs("Important notice", tail)));
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time), Policy(), TestContext.Current.CancellationToken));
    }
    [Fact]
    public void ComparisonTokenAllowanceChargesOnlyChangedParagraphs()
    {
        string unchanged = P(string.Concat(Enumerable.Repeat("unchanged text ", 1000)));
        WordDocument original = Load(Input(unchanged + P("old"))), revised = Load(Input(unchanged + P("new")));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Author", Time) { MaximumTokens = 2 }, Policy(), TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Revisions.Count);
        Assert.Equal("new", result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[1].Text);
    }
    [Fact]
    public void RevisionAuthorIdentityEnrichmentDoesNotRejectOrdinaryHistory()
    {
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body(Revisions),
            "<Relationship Id='people' Type='http://schemas.microsoft.com/office/2011/relationships/people' Target='people.xml'/>",
            extra: new KeyValuePair<string, string>("word/people.xml", "<w15:people xmlns:w15='http://schemas.microsoft.com/office/word/2012/wordml'><w15:person w15:author='Original Author'/></w15:people>"));
        WordDocument doc = Load(bytes); Assert.Equal(2, doc.Revisions.Count);
        Assert.Contains(doc.Diagnostics, d => d.Code == "Review.AuthorIdentitiesIgnored"); Schema(Save(doc));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberedCommentParagraphsResolveDefinitionsAfterNumberingLoad(bool linkedStyle)
    {
        string properties = "<w:numPr><w:ilvl w:val='0'/><w:numId w:val='1'/></w:numPr>";
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='List'><w:name w:val='List'/><w:pPr>" + properties + "</w:pPr></w:style></w:styles>";
        string comment = "<w:comments xmlns:w='" + W + "'><w:comment w:id='1' w:author='Author'><w:p><w:pPr>"
            + (linkedStyle ? "<w:pStyle w:val='List'/>" : properties) + "</w:pPr><w:r><w:t>Bulleted comment</w:t></w:r></w:p></w:comment></w:comments>";
        string body = "<w:p><w:commentRangeStart w:id='1'/><w:r><w:t>Body</w:t></w:r><w:commentRangeEnd w:id='1'/><w:r><w:commentReference w:id='1'/></w:r></w:p>";
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body(body),
            "<Relationship Id='comments' Type='" + TestDocuments.R + "/comments' Target='comments.xml'/><Relationship Id='numbers' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>",
            linkedStyle ? styles : null, extra: new KeyValuePair<string, string>("word/comments.xml", comment));
        using var stream = new MemoryStream(); stream.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/numbering.xml").Open(), new UTF8Encoding(false)))
            writer.Write("<w:numbering xmlns:w='" + W + "'><w:abstractNum w:abstractNumId='0'><w:lvl w:ilvl='0'><w:start w:val='1'/><w:numFmt w:val='bullet'/><w:lvlText w:val='•'/></w:lvl></w:abstractNum><w:num w:numId='1'><w:abstractNumId w:val='0'/></w:num></w:numbering>");
        WordDocument doc = Load(stream.ToArray()); Assert.Equal("Bulleted comment", Assert.Single(doc.Comments).Text);
        byte[] saved = Save(doc); Schema(saved);
        Assert.Single(Part(saved, "word/comments.xml").Descendants(W + "numPr"));
    }
}
