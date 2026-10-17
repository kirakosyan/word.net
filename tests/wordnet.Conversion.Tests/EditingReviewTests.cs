using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    private static readonly int[] TwoItems = { 1, 2 };
    private static readonly string[] OverflowBatch = { "abc", "def" };
    [Fact]
    public void RepeatedRowsKeepReferencesToDocumentBookmarksAndRemapLocalTargets()
    {
        string outside = "<w:p><w:bookmarkStart w:id='1' w:name='Terms'/><w:r><w:t>TermsValue</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        string inside = "<w:p><w:hyperlink w:anchor='Terms'><w:r><w:t>External</w:t></w:r></w:hyperlink>"
            + Field("REF Terms", "stale") + Field("PAGEREF Terms", "99")
            + "<w:bookmarkStart w:id='2' w:name='Local'/><w:r><w:t>LocalValue</w:t></w:r><w:bookmarkEnd w:id='2'/>"
            + "<w:hyperlink w:anchor='Local'><w:r><w:t>Inside</w:t></w:r></w:hyperlink></w:p>";
        WordDocument document = Load(Docx(outside + RepeatTable(inside)));
        document.Merge(new { Items = TwoItems }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output); XElement root = Part(output, "word/document.xml").Root!;
        Assert.Equal(2, root.Descendants(W + "hyperlink").Count(e => (string?)e.Attribute(W + "anchor") == "Terms"));
        string[] local = root.Descendants(W + "bookmarkStart").Select(e => (string)e.Attribute(W + "name")!).Where(n => n != "Terms").ToArray();
        Assert.Equal(2, local.Distinct().Count());
        Assert.All(local, name => Assert.Contains(root.Descendants(W + "hyperlink"), e => (string?)e.Attribute(W + "anchor") == name));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.DoesNotContain("stale", string.Concat(pdf.GetPages().Select(p => p.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void CellBlockControlsRetainConditionalAndScalarBehavior()
    {
        WordDocument document = Load(Docx(Table(Cell(Control("if:HasTax", P("Tax applies")) + Control("merge:Name", P("Placeholder"))))));
        document.Merge(new { HasTax = false, Name = "Customer" }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output);
        Assert.Equal("Customer", Part(output, "word/document.xml").Root!.Value);
        Assert.Empty(Part(output, "word/document.xml").Descendants(W + "sdt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnsupportedTableWrappersHaveExplicitLoadLosses(bool tagged)
    {
        string row = tagged ? "<w:tr>" + Control("if:Show", Cell(P("Text"))) + "</w:tr>"
            : "<w:customXml><w:tr>" + Cell(P("Text")) + "</w:tr></w:customXml>";
        byte[] input = Docx("<w:tbl>" + row + "</w:tbl>");
        WordDocument document = Load(input);
        Assert.Contains(document.Diagnostics, d => d.Code == (tagged ? "Template.TableControl" : "Docx.TableStructure"));
        using var source = new MemoryStream(input);
        Assert.Throws<NotSupportedException>(() => WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(), TestContext.Current.CancellationToken));
        if (tagged) Assert.Throws<NotSupportedException>(() => document.Merge(new { Show = false }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CellTextBoxesDoNotBecomeAdditionalCellParagraphsOnCloneOrMerge()
    {
        byte[] input = Docx(Table(Cell(BoxParagraph(P("BoxText")).Insert(5, "<w:r><w:t>A</w:t></w:r>")) + Cell(P("B"))));
        using var original = new MemoryStream(input); using var converted = new MemoryStream();
        new WordConverter().Convert(original, converted, DocumentFormat.Docx, DocumentFormat.Text, Policy(), TestContext.Current.CancellationToken);
        WordDocument document = Load(input);
        Assert.Equal(converted.ToArray(), Save(document, DocumentFormat.Text));
        document.Merge(new { }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(converted.ToArray(), Save(document, DocumentFormat.Text));
        Assert.DoesNotContain(document.Save(new MemoryStream(), DocumentFormat.Markdown, cancellationToken: TestContext.Current.CancellationToken).Diagnostics, d => d.Code == "Markdown.CellParagraphs");
        Schema(Save(document));
    }

    [Fact]
    public void InheritedHeaderListsRemainSharedAndMergeOnlyOnce()
    {
        string first = Page.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        string body = "<w:p><w:pPr>" + first + "</w:pPr><w:r><w:t>One</w:t></w:r></w:p>";
        body += string.Concat(Enumerable.Repeat("<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>Next</w:t></w:r></w:p>", 3)) + P("Final") + Page;
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'><w:p>" + Field("MERGEFIELD Missing", "cached") + "</w:p></w:hdr>"));
        WordDocument document = Load(input); byte[] output = Save(document); Schema(output);
        using (var zip = new ZipArchive(new MemoryStream(output))) Assert.Single(zip.Entries, e => e.FullName.StartsWith("word/header", StringComparison.Ordinal));
        DocumentEditResult result = document.Merge(new { }, new TemplateOptions { MissingValues = MissingTemplateValue.KeepCached }, TestContext.Current.CancellationToken);
        Assert.Equal(1, Assert.Single(result.Diagnostics, d => d.Code == "Template.MissingValue").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiBlockAndMultiRowControlBoundariesSurviveSaveReload(bool row)
    {
        string content = row ? "<w:tbl>" + Control("repeat:Items", "<w:tr>" + Cell(P("One")) + "</w:tr><w:tr>" + Cell(P("Two")) + "</w:tr>") + "</w:tbl>"
            : Control("merge:Address", P("One") + P("Two") + P("Three"));
        WordDocument document = Load(Docx(content));
        byte[] saved = Save(document); Schema(saved);
        Assert.Single(Part(saved, "word/document.xml").Descendants(W + "sdt"));
        WordDocument reloaded = Load(saved);
        Assert.Throws<NotSupportedException>(() => reloaded.Merge(new { Address = "Value", Items = OneItem }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WrappedSectionTailDoesNotAddAnEmptyParagraph()
    {
        string first = Control("if:Show", "<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>");
        byte[] saved = Save(Load(Docx(first + P("Second") + Page))); Schema(saved);
        XElement body = Part(saved, "word/document.xml").Root!.Element(W + "body")!;
        Assert.Equal(2, body.Descendants(W + "p").Count());
        Assert.NotNull(body.Element(W + "sdt")?.Descendants(W + "sectPr").SingleOrDefault());
    }

    [Fact]
    public void ScalarControlAcrossSectionsRemainsOneUnsupportedRegion()
    {
        string first = "<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>";
        WordDocument document = Load(Docx(Control("merge:Address", first + P("Second")) + Page));
        byte[] saved = Save(document); Schema(saved);
        XElement body = Part(saved, "word/document.xml").Root!.Element(W + "body")!;
        Assert.Single(body.Descendants(W + "sdt")); Assert.Equal(2, body.Descendants(W + "p").Count());
        Assert.Throws<NotSupportedException>(() => Load(saved).Merge(new { Address = "Value" }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EmptyRepeatWrapperIsDiagnosedAndCannotExecute()
    {
        WordDocument document = Load(Docx("<w:tbl>" + Control("repeat:Items", "") + "<w:tr>" + Cell(P("Body")) + "</w:tr></w:tbl>"));
        Assert.Contains(document.Diagnostics, d => d.Code == "Template.TableControl");
        Assert.Throws<NotSupportedException>(() => document.Merge(new { Items = OneItem }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(2111)]
    [InlineData(2216)]
    public void ScaledLoadedImageCanBeReadThroughParagraphSnapshots(int width)
    {
        // DOCX rejects these extents before scaling; EPUB admits finite point sizes.
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            void Text(string name, string text) => FidelityDocuments.Add(zip, name, Encoding.UTF8.GetBytes(text));
            Text("mimetype", "application/epub+zip");
            Text("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Text("book.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='text' href='chapter.xhtml' media-type='application/xhtml+xml'/><item id='image' href='grid.png' media-type='image/png'/></manifest><spine><itemref idref='text'/></spine></package>");
            Text("chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><body><p><img src='grid.png' style='width:" + width.ToString(CultureInfo.InvariantCulture) + "pt;height:100pt'/></p></body></html>");
            FidelityDocuments.Add(zip, "grid.png", FidelityDocuments.Fixture("color-grid.png"));
        }
        package.Position = 0;
        WordDocument document = WordDocument.Load(package, DocumentFormat.Epub, out _, Policy(), TestContext.Current.CancellationToken);
        TemplateImage image = Assert.Single(document.Paragraphs.SelectMany(p => p.Runs), r => r.Image != null).Image!;
        Assert.InRange(image.Width, 1583.99, 1584); Assert.True(image.Height > 0);
        Schema(Save(document));
    }

    [Theory]
    [InlineData("IF 1 = 1", false)]
    [InlineData("MERGEFIELD Name", false)]
    [InlineData("HYPERLINK &quot;https://example.com&quot;", true)]
    public void ControlsInsideSimpleAndComplexFieldResultsKeepCachedText(string instruction, bool complex)
    {
        string control = Control("merge:Inner", "<w:r><w:t>CachedText</w:t></w:r>");
        string field = complex ? "<w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>" + instruction + "</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r>" + control + "<w:r><w:fldChar w:fldCharType='end'/></w:r>"
            : "<w:fldSimple w:instr='" + instruction + "'>" + control + "</w:fldSimple>";
        WordDocument document = Load(Docx("<w:p>" + field + "</w:p>"));
        Assert.Equal("CachedText", Assert.Single(document.Paragraphs).Text);
        Assert.Contains("CachedText", Part(Save(document), "word/document.xml").Root!.Value, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, d => d.Code == "Template.FieldStructure");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedTemplateControlsCannotExecuteAfterFlattening(bool inline)
    {
        string inner = Control("merge:Rate", inline ? "<w:r><w:t>cached</w:t></w:r>" : P("cached"));
        string outer = Control("if:HasTax", inner);
        WordDocument document = Load(Docx(inline ? "<w:p>" + outer + "</w:p>" : outer));
        byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.Merge(new { HasTax = false, Rate = 5 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

    [Fact]
    public void RepeatedFootnotesUseItemScopeAndWrappedFirstParagraphKeepsItsMark()
    {
        string reference = "<w:p><w:r><w:t>Row</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'>" + Control("if:Show", "<w:p><w:pPr/><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>MERGEFIELD Description</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>cached</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>") + P("Tail") + "</w:footnote></w:footnotes>";
        WordDocument document = Load(TestDocuments.Docx(TestDocuments.Body(RepeatTable(reference)), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes)));
        byte[] template = Save(document); Schema(template);
        Assert.NotNull(Part(template, "word/footnotes.xml").Descendants(W + "sdtContent").Single().Descendants(W + "footnoteRef").SingleOrDefault());
        document.Merge(new { Description = "WrongRoot", Items = new[] { new { Description = "FirstItem", Show = true }, new { Description = "SecondItem", Show = true } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(document); Schema(output); string text = Part(output, "word/footnotes.xml").Root!.Value;
        Assert.Contains("FirstItem", text, StringComparison.Ordinal); Assert.Contains("SecondItem", text, StringComparison.Ordinal); Assert.DoesNotContain("WrongRoot", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "0%", "5%")]
    [InlineData(false, "0.0 '%'", "5.0 %")]
    [InlineData(true, "h:mm AM/PM", "3:05 PM")]
    [InlineData(true, "h:mm am/pm", "3:05 pm")]
    [InlineData(true, "'Due at' h:mm AM/PM", "Due at 3:05 PM")]
    public void MergeFormatsTranslateWordPercentMeridiemAndQuotedLiterals(bool date, string format, string expected)
    {
        WordDocument document = Load(Docx("<w:p>" + Field("MERGEFIELD Value " + (date ? "\\@" : "\\#") + " &quot;" + format.Replace("'", "&apos;") + "&quot;", "cached") + "</w:p>"));
        document.Merge(new Dictionary<string, object?> { ["Value"] = date ? (object)new DateTime(2026, 10, 17, 15, 5, 0) : 5 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(document.Paragraphs).Text); Schema(Save(document));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal(expected, pdf.GetPage(1).Text);
    }

    [Fact]
    public void LiteralReplacementTraversesManyStyledRunsWithoutQuadraticSlicing()
    {
        WordDocument document = WordDocument.Create(Policy());
        document.AddParagraph(Enumerable.Range(0, 30000).Select(i => new DocumentText("x", new TextFormatting { Bold = i % 2 == 0 })), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(30000, document.Replace("x", "y", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal(new string('y', 30000), Assert.Single(document.Paragraphs).Text);
        Assert.Equal(15000, Assert.Single(document.Paragraphs).Runs.Count(r => r.Formatting.Bold));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedValueTypeDataIsRejectedBeforeConditionsExecute(int kind)
    {
        object value = kind == 0 ? DayOfWeek.Sunday : kind == 1 ? (object)'x' : TimeSpan.Zero;
        WordDocument document = Load(Docx(Control("if:Status", P("Visible")))); byte[] before = Save(document);
        Assert.Throws<ArgumentException>(() => document.Merge(new { Status = value }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonTemplateControlsDoNotAcquireTemplateValidation(bool inline)
    {
        string contents = inline ? "<w:r><w:t>Text</w:t></w:r>" : P("Text");
        string control = "<w:sdt><w:sdtPr><w:tag w:val='" + new string('m', 300) + "'/><w:placeholder/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent>" + contents + "</w:sdtContent></w:sdt>";
        using var input = new MemoryStream(Docx(inline ? "<w:p>" + control + "</w:p>" : control));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out ConversionResult result, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Template.", StringComparison.Ordinal));
        Assert.Equal("Text", Assert.Single(document.Paragraphs).Text);
    }

    [Fact]
    public void ParagraphAndBookmarkEditsCannotDeleteOrSplitTemplateControls()
    {
        string range = "<w:bookmarkStart w:id='1' w:name='Name'/><w:r><w:t>cached</w:t></w:r><w:bookmarkEnd w:id='1'/>";
        WordDocument document = Load(Docx("<w:p>" + Control("merge:Name", range) + "</w:p>")); byte[] before = Save(document);
        var content = new[] { new DocumentText("New") };
        Assert.Throws<NotSupportedException>(() => document.ReplaceParagraph(0, content, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.InsertAtBookmark("Name", content, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.ReplaceBookmark("Name", content, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

    [Fact]
    public void BulkParagraphCreationIsAtomicAndSupportsLargeReports()
    {
        WordDocument document = WordDocument.Create(Policy()); int visited = 0;
        IEnumerable<string> Lines() { for (int i = 0; i < 20000; i++) { visited++; yield return "Report line " + i.ToString(CultureInfo.InvariantCulture); } }
        document.AddParagraphs(Lines(), new TextFormatting { Bold = true }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(20000, visited); Assert.Equal(20000, document.Paragraphs.Count);
        Assert.Equal("Report line 19999", document.Paragraphs[19999].Text);
        Assert.True(document.Paragraphs[0].Runs[0].Formatting.Bold);
        using var cancelled = new CancellationTokenSource();
        IEnumerable<string> Cancel() { yield return "Not committed"; cancelled.Cancel(); yield return "Cancelled"; }
        Assert.Throws<OperationCanceledException>(() => document.AddParagraphs(Cancel(), cancellationToken: cancelled.Token));
        Assert.Equal(20000, document.Paragraphs.Count);
        WordDocument limited = WordDocument.Create(new ConversionOptions(maximumCharacters: 5));
        Assert.Throws<InvalidDataException>(() => limited.AddParagraphs(OverflowBatch, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(limited.Paragraphs);
    }

    private static string RepeatTable(string paragraph) => "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Items", "<w:tr>" + Cell(paragraph) + "</w:tr>") + "</w:tbl>";
    private static string BoxParagraph(string contents) => "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0'><wp:positionH relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionH><wp:positionV relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionV><wp:extent cx='1270000' cy='1270000'/><wp:wrapNone/><wp:docPr id='1' name='Box'/><a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:prstGeom prst='rect'><a:avLst/></a:prstGeom></wps:spPr><wps:txbx><w:txbxContent>" + contents + "</w:txbxContent></wps:txbx><wps:bodyPr><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
}
