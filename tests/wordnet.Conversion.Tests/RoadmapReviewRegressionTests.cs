using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class RoadmapReviewRegressionTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private const string Mark = "w:id='1' w:author='Reviewer'";
    private static string P(string value) => "<w:p><w:r><w:t>" + value + "</w:t></w:r></w:p>";
    private static string Begin(string instruction) => "<w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText xml:space='preserve'>" + SecurityElement.Escape(instruction) + "</w:instrText></w:r>";
    private const string Separator = "<w:r><w:fldChar w:fldCharType='separate'/></w:r>";
    private const string End = "<w:r><w:fldChar w:fldCharType='end'/></w:r>";
    private static byte[] Save(WordDocument document)
    { using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static XElement Xml(byte[] bytes)
    { using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream part = zip.GetEntry("word/document.xml")!.Open(); return XElement.Load(part); }
    private static WordDocument Load(string body) => WordDocument.Load(new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body))), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("= ", " + 25", "145.00")]
    [InlineData("IF ", " = \"M\" \"Mr\" \"Ms\"", "Mr")]
    [InlineData("HYPERLINK ", "", "Link")]
    public void NestedInstructionsKeepCachedTextWithoutSynthesizingLiveFields(string prefix, string suffix, string cache)
    {
        foreach (bool simple in new[] { false, true })
        {
            string nested = simple ? "<w:fldSimple w:instr='REF Subtotal'><w:r><w:t>120</w:t></w:r></w:fldSimple>"
                : Begin("REF Subtotal") + Separator + "<w:r><w:t>120</w:t></w:r>" + End;
            byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p>" + Begin(prefix) + nested + "<w:r><w:instrText xml:space='preserve'>" + SecurityElement.Escape(suffix)
                + "</w:instrText></w:r>" + Separator + "<w:r><w:t>" + cache + "</w:t></w:r>" + End + "</w:p>"));
            foreach (DocumentFormat format in new[] { DocumentFormat.Docx, DocumentFormat.Pdf, DocumentFormat.Xps, DocumentFormat.Html })
            {
                using var source = new MemoryStream(input); using var output = new MemoryStream();
                WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out ConversionResult result, cancellationToken: TestContext.Current.CancellationToken);
                document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken);
                Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult" && d.Severity == DiagnosticSeverity.Info);
                if (format == DocumentFormat.Docx)
                {
                    XElement xml = Xml(output.ToArray()); Assert.Empty(xml.Descendants(W + "fldSimple")); Assert.Empty(xml.Descendants(W + "hyperlink"));
                    Assert.Equal(cache, string.Concat(xml.Descendants(W + "t").Select(e => e.Value)));
                }
                if (format == DocumentFormat.Pdf) { using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(cache, pdf.GetPage(1).Text); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictRetainsFormattedAndMultiparagraphScalarCaches(bool multipleParagraphs)
    {
        string body = "<w:p>" + Begin("= 2 + 3") + Separator + "<w:r><w:t>First</w:t></w:r>"
            + (multipleParagraphs ? "</w:p><w:p>" : "")
            + "<w:r><w:rPr><w:b/></w:rPr><w:t>Last</w:t></w:r>" + End + "</w:p>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body)));
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out ConversionResult result, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult" && d.Severity == DiagnosticSeverity.Info);
        XElement xml = Xml(Save(document)); Assert.Empty(xml.Descendants(W + "fldSimple"));
        Assert.Equal("FirstLast", string.Concat(xml.Descendants(W + "t").Select(e => e.Value)));
        Assert.Equal(multipleParagraphs ? 2 : 1, xml.Descendants(W + "p").Count());
        Assert.Contains(xml.Descendants(W + "r"), r => r.Value == "Last" && r.Element(W + "rPr")?.Element(W + "b") != null);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("FirstLast", pdf.GetPage(1).Text);
    }

    [Fact]
    public void NestedBinaryInstructionCannotTurnItsCached145Into25()
    {
        const string field = "\u0013= \u0013 REF Subtotal\u0014120\u0015 + 25\u0014145.00\u0015";
        string placeholder = new string(field.Select(c => c is '\u0013' or '\u0014' or '\u0015' ? '@' : c).ToArray());
        var model = new DocumentModel(); var paragraph = new ParagraphBlock(); paragraph.Runs.Add(new TextRun(placeholder)); model.Blocks.Add(paragraph);
        using var binary = new MemoryStream(); DocFormat.Write(model, binary, new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        using (RootStorage root = RootStorage.Open(binary, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = root.OpenStream("WordDocument"); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            byte[] data = buffer.ToArray(), needle = Encoding.Unicode.GetBytes(placeholder);
            int start = Enumerable.Range(0, data.Length - needle.Length + 1).Single(i => data.Skip(i).Take(needle.Length).SequenceEqual(needle));
            Encoding.Unicode.GetBytes(field).CopyTo(data, start); stream.Position = 0; stream.Write(data, 0, data.Length); root.Flush();
        }
        foreach (DocumentFormat format in new[] { DocumentFormat.Docx, DocumentFormat.Pdf })
        {
            binary.Position = 0; using var output = new MemoryStream();
            ConversionResult result = new WordConverter().Convert(binary, output, DocumentFormat.Doc, format, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
            Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
            if (format == DocumentFormat.Docx) { XElement xml = Xml(output.ToArray()); Assert.Empty(xml.Descendants(W + "fldSimple")); Assert.Equal("145.00", string.Concat(xml.Descendants(W + "t").Select(e => e.Value))); }
            else { using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("145.00", pdf.GetPage(1).Text); }
        }
    }

    [Fact]
    public void DefaultBestEffortStillConvertsInsertedParagraphAndRowMarks()
    {
        string paragraph = "<w:p><w:pPr><w:rPr><w:ins " + Mark + "/></w:rPr></w:pPr><w:ins w:id='2' w:author='Reviewer'><w:r><w:t>Paragraph</w:t></w:r></w:ins></w:p>";
        string table = "<w:tbl><w:tr><w:trPr><w:ins w:id='3' w:author='Reviewer'/></w:trPr><w:tc>" + P("Row") + "</w:tc></w:tr></w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph + table))); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Revisions.Accepted");
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("ParagraphRow", pdf.GetPage(1).Text);
    }

    [Theory]
    [InlineData(false, ReviewDecision.Accept)]
    [InlineData(false, ReviewDecision.Reject)]
    [InlineData(true, ReviewDecision.Accept)]
    [InlineData(true, ReviewDecision.Reject)]
    public void SelectingOnlyParagraphTextDecidesItsWholeStructuralChange(bool deletion, ReviewDecision decision)
    {
        WordDocument first = Load(P("First")), second = Load(P("First") + P("New"));
        WordDocument review = DocumentComparison.Compare(deletion ? second : first, deletion ? first : second, new ComparisonOptions("Reviewer", new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)), cancellationToken: TestContext.Current.CancellationToken);
        review.ApplyRevisions(new[] { Assert.Single(review.Revisions, r => r.Structure.Length == 0) }, decision, TestContext.Current.CancellationToken);
        Assert.Empty(review.Revisions);
        Assert.Equal(deletion == (decision == ReviewDecision.Accept) ? "First" : "FirstNew", string.Concat(Xml(Save(review)).Descendants(W + "t").Select(e => e.Value)));
        using var package = WordprocessingDocument.Open(new MemoryStream(Save(review)), false); Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumingTheLastTrackedRowRemovesItsTable(bool deletion)
    {
        string table = "<w:tbl><w:tr><w:trPr><w:" + (deletion ? "del" : "ins") + " " + Mark + "/></w:trPr><w:tc>" + P("Tracked row") + "</w:tc></w:tr></w:tbl>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table + P("After"))));
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        ReviewDecision decision = deletion ? ReviewDecision.Accept : ReviewDecision.Reject;
        Assert.Empty(Xml(Save(document.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "tbl"));
        document.ApplyRevisions(document.Revisions, decision, TestContext.Current.CancellationToken);
        Assert.Empty(document.Revisions); Assert.Empty(Xml(Save(document)).Descendants(W + "tbl"));
    }

    [Theory]
    [InlineData("header", "hdr")]
    [InlineData("footer", "ftr")]
    [InlineData("footnotes", "footnotes")]
    public void StoryParagraphMarksReportUnsupportedOwnership(string relation, string root)
    {
        string p = "<w:p><w:pPr><w:rPr><w:ins " + Mark + "/></w:rPr></w:pPr><w:r><w:t>Story</w:t></w:r></w:p>";
        string story = "<w:" + root + " xmlns:w='" + W + "'>" + (relation == "footnotes" ? "<w:footnote w:id='1'>" + p + "</w:footnote>" : p) + "</w:" + root + ">";
        string body = P("Body") + (relation == "footnotes" ? "" : "<w:sectPr><w:" + relation + "Reference w:type='default' r:id='story'/></w:sectPr>");
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='story' Type='" + TestDocuments.R + "/" + relation + "' Target='story.xml'/>", extra: new KeyValuePair<string, string>("word/story.xml", story)));
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => WordDocument.Load(source, DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Review.Unsupported:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeStructuralAlignmentPreservesCanonicalBlockOrder()
    {
        string[] paragraphs = Enumerable.Range(0, 1500).Select(i => P("Item " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        WordDocument original = Load(string.Concat(paragraphs)), revised = Load(string.Concat(paragraphs.Take(750)) + P("Inserted") + string.Concat(paragraphs.Skip(750)));
        WordDocument review = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, review.Revisions.Count);
        string[] actual = Xml(Save(review.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken))).Descendants(W + "t").Select(e => e.Value).ToArray();
        Assert.Equal(1501, actual.Length); Assert.Equal("Item 749", actual[749]); Assert.Equal("Inserted", actual[750]); Assert.Equal("Item 750", actual[751]);
    }
}
