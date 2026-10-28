using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

// OOXML inputs are authored directly, independently of the WordNet writer.
public sealed partial class EditingTemplateTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[][] SampleRows = { new[] { "Item", "Total" }, new[] { "Consulting", "42.00" } };
    private static readonly int[] ThreeItems = { 1, 2, 3 };
    private static readonly int[] OneItem = { 1 };
    private static readonly string[] LineSeparator = { "Line " };
    private const string Page = "<w:sectPr><w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
    private static ConversionOptions Policy(int characters = 4194304) => new ConversionOptions(FidelityMode.Lenient, maximumCharacters: characters)
    { Clock = () => new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void CreateEditAndSaveProducesEditableStructureAndRealPdf()
    {
        WordDocument document = WordDocument.Create(Policy()); document.Title = "Invoice"; document.Author = "Example";
        var formatting = new TextFormatting { Bold = true, Color = "336699", FontSize = 16 };
        document.AddParagraph("Invoice heading", formatting, 1, cancellationToken: TestContext.Current.CancellationToken); formatting.Bold = false;
        document.AddTable(SampleRows, true, cancellationToken: TestContext.Current.CancellationToken);
        byte[] image = FidelityDocuments.Fixture("color-grid.png"); document.AddImage(new TemplateImage(image, 60, 30, "Logo"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(document.Paragraphs[0].Runs[0].Formatting.Bold);
        document.Paragraphs[0].Runs[0].Formatting.Bold = false; Assert.True(document.Paragraphs[0].Runs[0].Formatting.Bold);
        byte[] word = Save(document); Schema(word);
        Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "tr").Count());
        Assert.Single(Part(word, "word/document.xml").Descendants(W + "drawing"));
        Assert.Contains("Invoice", Part(word, "docProps/core.xml").Root!.Value, StringComparison.Ordinal);
        Assert.Equal(image, ImagePart(word));
        byte[] pdf = Save(document, DocumentFormat.Pdf); using PdfDocument parsed = PdfDocument.Open(pdf);
        Assert.Contains("Invoice heading", parsed.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("Consulting", parsed.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal(60, Assert.Single(parsed.GetPage(1).GetImages()).BoundingBox.Width, 2);
        Assert.Equal(pdf, Save(document, DocumentFormat.Pdf));
        Assert.Equal(word, Save(document));
        WordDocument loaded = Load(word); loaded.ReplaceParagraph(1, new[] { new DocumentText("Changed cell") }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Changed cell", Part(Save(loaded), "word/document.xml").Root!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceAcrossStyledRunsPreservesPrefixSuffixAndFirstMatchAppearance()
    {
        WordDocument document = Load(Docx("<w:p><w:r><w:t>Prefix </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>{{Na</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>me}} suffix</w:t></w:r></w:p>"));
        Assert.Equal(1, document.Replace("{{Name}}", "Renée", cancellationToken: TestContext.Current.CancellationToken).Changes);
        byte[] output = Save(document); Schema(output);
        XElement[] runs = Part(output, "word/document.xml").Descendants(W + "r").ToArray();
        Assert.Equal("Prefix Renée suffix", string.Concat(runs.Select(r => r.Value)));
        Assert.NotNull(runs.Single(r => r.Value == "Renée").Element(W + "rPr")?.Element(W + "b"));
        Assert.NotNull(runs.Single(r => r.Value == " suffix").Element(W + "rPr")?.Element(W + "i"));
    }

    [Fact]
    public void ReplacementVisitsNestedCellsAndStoriesWithoutCrossingBoundaries()
    {
        string body = P("Hi ") + P("Name") + Table(Cell(P("Name") + Table(Cell(P("Name"))))) + Page.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P("Name") + "</w:hdr>"));
        WordDocument document = Load(input);
        Assert.Equal(0, document.Replace("Hi Name", "Wrong", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal(4, document.Replace("Name", "Reader", cancellationToken: TestContext.Current.CancellationToken).Changes);
        byte[] output = Save(document); Schema(output);
        Assert.Equal(3, Part(output, "word/document.xml").Descendants(W + "t").Count(t => t.Value == "Reader"));
        Assert.Contains("Reader", Part(output, "word/header1.xml").Root!.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(a+)+$", true)]
    [InlineData("[", false)]
    [InlineData("a*", false)]
    public void InvalidAndExpensiveRegexLeaveTheDocumentUnchanged(string pattern, bool timeout)
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph(timeout ? new string('a', 50000) + "!" : "bbb", cancellationToken: TestContext.Current.CancellationToken);
        string before = document.Paragraphs[0].Text;
        if (timeout) Assert.ThrowsAny<TimeoutException>(() => document.ReplaceRegex(pattern, "x", TimeSpan.FromMilliseconds(5), cancellationToken: TestContext.Current.CancellationToken));
        else Assert.ThrowsAny<ArgumentException>(() => document.ReplaceRegex(pattern, "x", TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, document.Paragraphs[0].Text);
    }

    [Fact]
    public void ReplacementIsNonoverlappingAndCannotSplitUnicodeOrStructuredRuns()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("aaaa", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, document.Replace("aa", "x", cancellationToken: TestContext.Current.CancellationToken).Changes); Assert.Equal("xx", document.Paragraphs[0].Text);
        document.AddParagraph("A😀B", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => document.ReplaceRegex("\\uD83D", "x", TimeSpan.FromMilliseconds(20), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("A😀B", document.Paragraphs[1].Text);
        WordDocument fields = Load(Docx("<w:p><w:r><w:t>Be</w:t></w:r>" + Field("PAGE", "1") + "<w:r><w:t>yond</w:t></w:r></w:p>"));
        Assert.Equal(0, fields.Replace("Be1yond", "No", cancellationToken: TestContext.Current.CancellationToken).Changes);
        document.AddBookmark("Boundary", 0, 0, 1, cancellationToken: TestContext.Current.CancellationToken); Assert.Equal(0, document.Replace("xx", "No", cancellationToken: TestContext.Current.CancellationToken).Changes);
    }

    [Fact]
    public void BookmarkEditsKeepBoundaryPairsAndSupportedContent()
    {
        WordDocument document = WordDocument.Create(Policy()); document.AddParagraph("Before Target After", cancellationToken: TestContext.Current.CancellationToken); document.AddBookmark("Target", 0, 7, 6, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Target", Assert.Single(document.Bookmarks).Text);
        document.ReplaceBookmark("Target", new[] { new DocumentText("New", new TextFormatting { Italic = true }) }, cancellationToken: TestContext.Current.CancellationToken);
        document.InsertAtBookmark("Target", new[] { new DocumentText(" end") }, true, cancellationToken: TestContext.Current.CancellationToken);
        document.InsertAtBookmark("Target", new[] { new DocumentText(new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 20, 10)) }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output);
        Assert.Equal("New end", Assert.Single(document.Bookmarks).Text);
        XDocument xml = Part(output, "word/document.xml"); Assert.Single(xml.Descendants(W + "bookmarkStart")); Assert.Single(xml.Descendants(W + "bookmarkEnd")); Assert.Single(xml.Descendants(W + "drawing"));
        WordDocument crossing = Load(Docx("<w:p><w:bookmarkStart w:id='1' w:name='Cross'/><w:r><w:t>A</w:t></w:r></w:p><w:p><w:r><w:t>B</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>"));
        byte[] before = Save(crossing); Assert.Throws<NotSupportedException>(() => crossing.ReplaceBookmark("Cross", new[] { new DocumentText("x") }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(crossing));
    }

    [Theory]
    [InlineData(CompositionStyle.KeepSource)]
    [InlineData(CompositionStyle.UseDestination)]
    public void CompositionRemapsListsBookmarksImagesAndFieldsWithoutMutatingSource(CompositionStyle style)
    {
        string body = "<w:p><w:bookmarkStart w:id='1' w:name='Same'/><w:r><w:rPr><w:b/></w:rPr><w:t>Source</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>"
            + FidelityDocuments.ListParagraph("Item") + "<w:p>" + Field("REF Same", "stale") + "</w:p>" + FidelityDocuments.ImageParagraph() + Page;
        WordDocument source = Load(FidelityDocuments.Docx("color-grid.png", body, true)); byte[] original = Save(source);
        WordDocument destination = Load(FidelityDocuments.Docx("color-grid-alpha.png", body.Replace("Source", "Destination").Replace("<w:b/>", "<w:i/>"), true));
        destination.Append(source, style, cancellationToken: TestContext.Current.CancellationToken); destination.Append(source, style, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(destination); Schema(output); Assert.Equal(original, Save(source));
        XDocument xml = Part(output, "word/document.xml");
        Assert.Equal(3, xml.Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "name")).Distinct().Count());
        Assert.Equal(3, Part(output, "word/numbering.xml").Descendants(W + "num").Count());
        Assert.Equal(3, xml.Descendants(W + "drawing").Count());
        using PdfDocument pdf = PdfDocument.Open(Save(destination, DocumentFormat.Pdf));
        Assert.Equal(3, pdf.NumberOfPages); Assert.Contains("Destination", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("Source", pdf.GetPage(2).Text, StringComparison.Ordinal);
        Assert.All(pdf.GetPages(), p => Assert.Contains("3.", p.Text, StringComparison.Ordinal));
        XElement appended = xml.Descendants(W + "p").First(p => p.Value == "Source");
        Assert.NotNull(appended.Descendants(W + (style == CompositionStyle.KeepSource ? "b" : "i")).SingleOrDefault());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    public void InvoiceTemplatesExpandRowsConditionsAndImagesIntoEditableWordAndPdf(int count, bool optional)
    {
        WordDocument document = Load(Invoice());
        var items = Enumerable.Range(0, count).Select(i => new { Description = "Line " + i.ToString(CultureInfo.InvariantCulture), Amount = 12.5m }).ToArray();
        DocumentEditResult result = document.Merge(new
        {
            Customer = new { Name = "Renée" },
            Items = items,
            HasTax = optional,
            Logo = new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 60, 30)
        }, new TemplateOptions { Culture = CultureInfo.GetCultureInfo("fr-FR") }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Changes >= 4);
        byte[] output = Save(document); Schema(output); XDocument xml = Part(output, "word/document.xml");
        Assert.Equal(count + 1, xml.Descendants(W + "tr").Count()); Assert.Empty(xml.Descendants(W + "sdt")); Assert.Empty(xml.Descendants(W + "fldSimple"));
        Assert.Contains("Renée", xml.Root!.Value, StringComparison.Ordinal); Assert.Equal(optional, xml.Root!.Value.Contains("Tax applies"));
        if (count > 0) Assert.Contains("12,50", xml.Root!.Value, StringComparison.Ordinal);
        Assert.Equal(FidelityDocuments.Fixture("color-grid.png"), ImagePart(output));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Renée", text, StringComparison.Ordinal); Assert.Equal(count, text.Split(LineSeparator, StringSplitOptions.None).Length - 1);
        Assert.Equal(60, Assert.Single(pdf.GetPages().SelectMany(p => p.GetImages())).BoundingBox.Width, 2);
    }

    [Fact]
    public void JsonAndComplexSplitFieldsUseExplicitCultureWithoutExecutingOtherFields()
    {
        string body = "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>MERGE</w:instrText></w:r><w:r><w:instrText>FIELD Amount \\# &quot;N2&quot;</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>cached</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>"
            + Control("merge:Customer.Name", P("cached")) + "<w:p>" + Field("PAGE", "7") + "</w:p>";
        WordDocument document = Load(Docx(body));
        Assert.Equal(2, document.MergeJson("{\"Amount\":1234.5,\"Customer\":{\"Name\":\"Zo\\u00eb\"}}", new TemplateOptions { Culture = CultureInfo.GetCultureInfo("de-DE") }, cancellationToken: TestContext.Current.CancellationToken).Changes);
        byte[] word = Save(document); Schema(word); XDocument xml = Part(word, "word/document.xml");
        Assert.Contains("1.234,50", xml.Root!.Value, StringComparison.Ordinal); Assert.Contains("Zoë", xml.Root!.Value, StringComparison.Ordinal);
        Assert.NotNull(xml.Descendants(W + "r").Single(r => r.Value == "1.234,50").Element(W + "rPr")?.Element(W + "b"));
        Assert.Single(xml.Descendants(W + "fldSimple"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Name\":1,\"Name\":2}")]
    [InlineData("{\"Name\":01}")]
    [InlineData("{\"Name\":\"\\uZZZZ\"}")]
    [InlineData("[]")]
    public void MissingAndMalformedJsonFailWithoutChangingTheSession(string json)
    {
        WordDocument document = Load(Docx("<w:p>" + Field("MERGEFIELD Name", "cached") + "</w:p>")); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.MergeJson(json, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }

    [Theory]
    [InlineData(MissingTemplateValue.KeepCached, "cached")]
    [InlineData(MissingTemplateValue.Empty, "")]
    public void MissingAndNullPoliciesHavePrivacyPreservingDiagnostics(MissingTemplateValue missing, string expected)
    {
        WordDocument document = Load(Docx("<w:p>" + Field("MERGEFIELD Secret", "cached") + "</w:p>"));
        DocumentEditResult result = document.Merge(new Dictionary<string, object?>(), new TemplateOptions { MissingValues = missing }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Template.MissingValue"); Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("Secret"));
        Assert.Equal(expected, document.Paragraphs[0].Text);
        document.Merge(new { Secret = (string?)null }, cancellationToken: TestContext.Current.CancellationToken); Assert.Empty(document.Paragraphs[0].Text);
    }

    [Fact]
    public void UnexecutedTemplateInstructionsAndControlsSurviveDocxSaveAndReload()
    {
        WordDocument loaded = Load(Invoice()); byte[] output = Save(loaded); Schema(output);
        WordDocument reloaded = Load(output);
        reloaded.Merge(new { Customer = new { Name = "New" }, Items = Array.Empty<object>(), HasTax = false, Logo = (TemplateImage?)null }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("New", Part(Save(reloaded), "word/document.xml").Root!.Value, StringComparison.Ordinal);
        Assert.Empty(Part(Save(reloaded), "word/document.xml").Descendants(W + "sdt"));
    }

    [Fact]
    public void LimitsCancellationAndReentrancyDoNotCommitPartialEdits()
    {
        WordDocument document = WordDocument.Create(Policy(100)); document.AddParagraph("original", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.Replace("original", new string('x', 101), cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal("original", document.Paragraphs[0].Text);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); Assert.Throws<OperationCanceledException>(() => document.AddParagraph("No", cancellationToken: cancel.Token));
        Assert.Throws<InvalidOperationException>(() => document.AddParagraph(Reenter(), cancellationToken: TestContext.Current.CancellationToken)); Assert.Single(document.Paragraphs);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => document.Save(output, DocumentFormat.Docx, cancellationToken: cancel.Token)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        WordDocument template = Load(Invoice()); byte[] before = Save(template);
        Assert.Throws<OperationCanceledException>(() => template.Merge(new { }, cancellationToken: cancel.Token)); Assert.Equal(before, Save(template));
        IEnumerable<DocumentText> Reenter() { document.AddParagraph("Nested", cancellationToken: TestContext.Current.CancellationToken); yield return new DocumentText("Outer"); }
    }

    [Fact]
    public async Task SessionOperationsSerializeAndSnapshotsRemainDetached()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("initial", cancellationToken: TestContext.Current.CancellationToken); var snapshot = document.Paragraphs;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => document.AddParagraph(i.ToString(CultureInfo.InvariantCulture), cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));
        Assert.Equal(13, document.Paragraphs.Count); Assert.Single(snapshot); Schema(Save(document));
        document.Append(document, cancellationToken: TestContext.Current.CancellationToken); Assert.Equal(26, document.Paragraphs.Count); Schema(Save(document));
    }

    [Fact]
    public void OriginalLoadLossesRemainVisibleAndStrictSaveLeavesOutputUntouched()
    {
        WordDocument document = Load(Docx("<w:p><w:del><w:r><w:delText>Deleted</w:delText></w:r></w:del><w:r><w:t>Kept</w:t></w:r></w:p>"));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Docx, new ConversionOptions(), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.Contains(document.Diagnostics, d => d.Code == "Revisions.Accepted"); Schema(Save(document));
    }

    [Theory]
    [InlineData("MERGEFIELD Name \\# &quot;D999999999&quot;")]
    [InlineData("MERGEFIELD Name \\execute code")]
    [InlineData("MERGEFIELD &quot;Unclosed")]
    public void MalformedMergeSyntaxAndUnboundedPrecisionAreRejected(string instruction)
    {
        WordDocument document = Load(Docx("<w:p>" + Field(instruction, "cached") + "</w:p>")); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.Merge(new { Name = 1 }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void RepeatedRowsRemapBookmarksAndEnforceExpansionAndMergeBounds()
    {
        string row = "<w:tr>" + Cell("<w:p><w:bookmarkStart w:id='1' w:name='Line'/><w:r><w:t>Bound</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>") + "</w:tr>";
        byte[] input = Docx("<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Items", row) + "</w:tbl>");
        WordDocument document = Load(input); document.Merge(new { Items = new[] { new { }, new { }, new { } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(document); Schema(word); Assert.Equal(3, document.Bookmarks.Select(b => b.Name).Distinct().Count());
        WordDocument limited = Load(input); byte[] before = Save(limited);
        Assert.Throws<InvalidDataException>(() => limited.Merge(new { Items = ThreeItems }, new TemplateOptions { MaximumRows = 2 }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(limited));
        WordDocument merged = Load(Docx("<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Items", row.Replace("<w:tc>", "<w:tc><w:tcPr><w:vMerge w:val='restart'/></w:tcPr>")) + "</w:tbl>"));
        Assert.Throws<NotSupportedException>(() => merged.Merge(new { Items = OneItem }, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static WordDocument Load(byte[] bytes) { using var stream = new MemoryStream(bytes); WordDocument document = WordDocument.Load(stream, DocumentFormat.Docx, out _, Policy(), TestContext.Current.CancellationToken); Assert.True(stream.CanRead); return document; }
    private static byte[] Save(WordDocument document, DocumentFormat format = DocumentFormat.Docx) { using var output = new MemoryStream(); document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); Assert.True(output.CanWrite); return output.ToArray(); }
    private static byte[] Docx(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string P(string text) => TestDocuments.TextParagraph(text);
    private static string Cell(string content) => "<w:tc>" + content + "</w:tc>";
    private static string Table(string cells) => "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr>" + cells + "</w:tr></w:tbl>";
    private static string Field(string instruction, string cached) => "<w:fldSimple w:instr='" + instruction + "'><w:r><w:t>" + cached + "</w:t></w:r></w:fldSimple>";
    private static string Control(string tag, string content) => "<w:sdt><w:sdtPr><w:tag w:val='" + tag + "'/></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";
    internal static byte[] Invoice() => Docx("<w:p>" + Field("MERGEFIELD Customer.Name", "Customer") + "</w:p>"
        + Control("image:Logo", P("Logo")) + "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell(P("Description")) + Cell(P("Amount")) + "</w:tr>"
        + Control("repeat:Items", "<w:tr>" + Cell("<w:p>" + Field("MERGEFIELD Description", "Description") + "</w:p>") + Cell("<w:p>" + Field("MERGEFIELD Amount \\# &quot;F2&quot;", "0.00") + "</w:p>") + "</w:tr>")
        + "</w:tbl>" + Control("if:HasTax", P("Tax applies")) + Page);
    private static XDocument Part(byte[] bytes, string name) { using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream part = zip.GetEntry(name)!.Open(); return XDocument.Load(part); }
    private static byte[] ImagePart(byte[] bytes) { using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream part = zip.Entries.First(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)).Open(); using var buffer = new MemoryStream(); part.CopyTo(buffer); return buffer.ToArray(); }
    private static void Schema(byte[] bytes) { using WordprocessingDocument word = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken)); }
}
