using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    [Fact]
    public void PlainHeaderHistoryRetainsAttributionAndCanBeDecided()
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        string header = "<w:hdr xmlns:w='" + W + "'><w:p><w:r><w:t>Total </w:t></w:r><w:del w:id='3' w:author='Header Author'><w:r><w:delText>old</w:delText></w:r></w:del><w:ins w:id='4' w:author='Header Author'><w:r><w:t>new</w:t></w:r></w:ins></w:p></w:hdr>";
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body(Revisions + section), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", header));
        WordDocument document = Load(bytes); Assert.Equal(4, document.Revisions.Count);
        byte[] output = Save(document); Schema(output); Assert.Equal(4, Load(output).Revisions.Count);
        Assert.Contains(document.Revisions, item => item.Author == "Header Author" && item.Text == "old");
        WordDocument accepted = document.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = document.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Total new", Part(Save(accepted), "word/header1.xml").Root!.Value);
        Assert.Equal("Total old", Part(Save(rejected), "word/header1.xml").Root!.Value);
        Assert.Equal(output, Save(document));
    }
    [Fact]
    public void ComparisonEditsAnInheritedSharedHeaderOnceAcrossSections()
    {
        string firstSection = "<w:sectPr><w:headerReference w:type='default' r:id='header'/><w:type w:val='nextPage'/></w:sectPr>";
        string first = "<w:p><w:pPr>" + firstSection + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>";
        byte[] Fixture(string header) => TestDocuments.Docx(TestDocuments.Body(first + P("Second") + "<w:sectPr/>"),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>"));
        WordDocument old = Load(Fixture("Account old")), revised = Load(Fixture("Account new"));
        byte[] before = Save(old); WordDocument result = DocumentComparison.Compare(old, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed); Assert.Equal(2, result.Revisions.Count);
        Assert.Equal(2, Part(reviewed, "word/header1.xml").Descendants().Count(e => e.Name == W + "ins" || e.Name == W + "del"));
        WordDocument loaded = Load(reviewed); Assert.Equal(2, loaded.Revisions.Count);
        WordDocument accepted = loaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = loaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Account new", Part(Save(accepted), "word/header1.xml").Root!.Value);
        Assert.Equal("Account old", Part(Save(rejected), "word/header1.xml").Root!.Value);
        using PdfDocument newPdf = PdfDocument.Open(Save(accepted, DocumentFormat.Pdf));
        using PdfDocument oldPdf = PdfDocument.Open(Save(rejected, DocumentFormat.Pdf));
        Assert.All(newPdf.GetPages(), page => Assert.Contains("Account new", page.Text));
        Assert.All(oldPdf.GetPages(), page => Assert.Contains("Account old", page.Text));
        Assert.Equal(before, Save(old));
    }
    [Fact]
    public void StoryAndStructuralBodyChangesUseOneReviewedResult()
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] Fixture(string body, string header) => TestDocuments.Docx(TestDocuments.Body(body + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>"));
        WordDocument old = Load(Fixture(P("First") + P("Last"), "Old header"));
        WordDocument revised = Load(Fixture(P("First") + P("Inserted") + P("Last"), "New header"));
        WordDocument result = DocumentComparison.Compare(old, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes);
        WordDocument accepted = Load(bytes).CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = Load(bytes).CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("New header", Part(Save(accepted), "word/header1.xml").Root!.Value);
        Assert.Equal("Old header", Part(Save(rejected), "word/header1.xml").Root!.Value);
        Assert.Equal("First|Inserted|Last", string.Join("|", accepted.Paragraphs.Select(p => p.Text)));
        Assert.Equal("First|Last", string.Join("|", rejected.Paragraphs.Select(p => p.Text)));
        Assert.Throws<System.IO.InvalidDataException>(() => DocumentComparison.Compare(old, revised, new ComparisonOptions("Reviewer", Time) { MaximumSteps = 1 }, Policy(), TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData("header", "default")]
    [InlineData("header", "first")]
    [InlineData("header", "even")]
    [InlineData("footer", "default")]
    [InlineData("footer", "first")]
    [InlineData("footer", "even")]
    public void PlainStoryVariantsSelectTheCorrectPageAfterDecision(string kind, string type)
    {
        string section = "<w:sectPr><w:" + kind + "Reference w:type='" + type + "' r:id='story'/>" + (type == "first" ? "<w:titlePg/>" : "") + "</w:sectPr>";
        string body = "<w:p><w:r><w:t>Page one</w:t><w:br w:type='page'/><w:t>Page two</w:t></w:r></w:p>";
        byte[] Fixture(string text)
        {
            byte[] bytes = TestDocuments.Docx(TestDocuments.Body(body + section),
                "<Relationship Id='story' Type='" + TestDocuments.R + "/" + kind + "' Target='" + kind + ".xml'/>" + (type == "even" ? "<Relationship Id='settings' Type='" + TestDocuments.R + "/settings' Target='settings.xml'/>" : ""),
                extra: new KeyValuePair<string, string>("word/" + kind + ".xml", "<w:" + (kind == "header" ? "hdr" : "ftr") + " xmlns:w='" + W + "'>" + P(text) + "</w:" + (kind == "header" ? "hdr" : "ftr") + ">"));
            if (type != "even") return bytes;
            using var memory = new MemoryStream(); memory.Write(bytes, 0, bytes.Length);
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
            using (var writer = new StreamWriter(zip.CreateEntry("word/settings.xml").Open(), new UTF8Encoding(false)))
                writer.Write("<w:settings xmlns:w='" + W + "'><w:evenAndOddHeaders/></w:settings>");
            return memory.ToArray();
        }
        WordDocument original = Load(Fixture("Old label")), revised = Load(Fixture("New label"));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed); WordDocument loaded = Load(reviewed);
        WordDocument accepted = loaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = loaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("New label", Part(Save(accepted), "word/" + kind + "1.xml").Root!.Value);
        Assert.Equal("Old label", Part(Save(rejected), "word/" + kind + "1.xml").Root!.Value);
        using PdfDocument pdf = PdfDocument.Open(Save(accepted, DocumentFormat.Pdf)); Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal(type != "even", pdf.GetPage(1).Text.Contains("New label"));
        Assert.Equal(type != "first", pdf.GetPage(2).Text.Contains("New label"));
    }
    [Fact]
    public void ComparisonRejectsSharedStoryTopologyChangesEvenWhenTextMatches()
    {
        string firstSection = "<w:sectPr><w:headerReference w:type='default' r:id='h1'/></w:sectPr>";
        string first = "<w:p><w:pPr>" + firstSection + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>";
        byte[] Fixture(bool separate)
        {
            string final = separate ? "<w:sectPr><w:headerReference w:type='default' r:id='h2'/></w:sectPr>" : "<w:sectPr/>";
            string relations = "<Relationship Id='h1' Type='" + TestDocuments.R + "/header' Target='header1.xml'/>"
                + (separate ? "<Relationship Id='h2' Type='" + TestDocuments.R + "/header' Target='header2.xml'/>" : "");
            string header = "<w:hdr xmlns:w='" + W + "'>" + P("Same") + "</w:hdr>";
            byte[] bytes = TestDocuments.Docx(TestDocuments.Body(first + P("Second") + final), relations, extra: new KeyValuePair<string, string>("word/header1.xml", header));
            if (!separate) return bytes;
            using var memory = new MemoryStream(); memory.Write(bytes, 0, bytes.Length);
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
            using (var writer = new StreamWriter(zip.CreateEntry("word/header2.xml").Open(), new UTF8Encoding(false))) writer.Write(header);
            return memory.ToArray();
        }
        WordDocument original = Load(Fixture(false)), revised = Load(Fixture(true)); byte[] before = Save(original);
        Assert.Throws<System.NotSupportedException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(original));
    }
}
