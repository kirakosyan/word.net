using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    private static readonly string[] FragmentExpected = { "Before", "Fragment", "Cell", "", "Middle", "Fragment", "Cell", "", "After" };
    private static readonly string[] HtmlFragmentExpected = { "Hello", "Cell" };
    private static readonly string[][] FragmentCells = { new[] { "Cell" } };
    private static readonly string[][] NestedFragmentCells = { new[] { "NestedValue" } };
    private static readonly string[] HostGrid = { "4000", "2000" };
    private static readonly string[] RepeatedCellText = { "Clause", "Clause" };
    private static string Placeholder(string name, int id) => "<w:p><w:bookmarkStart w:id='" + id + "' w:name='" + name + "'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='" + id + "'/></w:p>";

    [Fact]
    public void FragmentsInsertInOrderAndLeaveTheSourceUnchanged()
    {
        WordDocument target = Load(Docx(P("Before") + Placeholder("one", 1) + P("Middle") + Placeholder("two", 2) + P("After")));
        WordDocument fragment = WordDocument.Create(); fragment.AddParagraph("Fragment", cancellationToken: TestContext.Current.CancellationToken); fragment.AddBookmark("marker", 0, 0, 8, TestContext.Current.CancellationToken);
        fragment.AddTable(FragmentCells, cancellationToken: TestContext.Current.CancellationToken); fragment.AddImage(new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 20, 10, "Logo"), TestContext.Current.CancellationToken);
        byte[] sourceBefore = Save(fragment); target.InsertFragmentAtBookmark("one", fragment, TestContext.Current.CancellationToken); target.InsertFragmentAtBookmark("two", fragment, TestContext.Current.CancellationToken);
        Assert.Equal(sourceBefore, Save(fragment)); Assert.Equal(FragmentExpected, target.Paragraphs.Select(p => p.Text));
        byte[] word = Save(target); Schema(word); var bookmarks = Part(word, "word/document.xml").Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "name")).ToArray();
        Assert.Equal(2, bookmarks.Length); Assert.Equal(2, bookmarks.Distinct().Count());
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text)); Assert.Contains("BeforeFragmentCellMiddleFragmentCellAfter", text, StringComparison.Ordinal);
        Assert.Equal(2, pdf.GetPages().Sum(p => p.GetImages().Count()));
    }

    [Fact]
    public void SafeHtmlFragmentInsertsAndLeavesCallerStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<html><body><p>Hello</p><table><tr><td>Cell</td></tr></table></body></html>"));
        WordDocument source = WordDocument.Load(stream, DocumentFormat.Html, out _, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument target = Load(Docx(Placeholder("place", 1))); target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken);
        Assert.True(stream.CanRead); byte[] word = Save(target); Schema(word);
        Assert.Equal(HtmlFragmentExpected, target.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void FragmentRejectsUnsafeSourcesAndUnresolvedReferencesAtomically()
    {
        WordDocument target = Load(Docx(Placeholder("place", 1))); byte[] before = Save(target);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("<html><body><p>Safe</p><script>alert(1)</script></body></html>"));
        WordDocument active = WordDocument.Load(input, DocumentFormat.Html, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("place", active, TestContext.Current.CancellationToken)); Assert.True(input.CanRead);
        using var imageInput = new MemoryStream(Encoding.UTF8.GetBytes("<html><body><img src='https://invalid.example/missing.png'/></body></html>"));
        WordDocument missingImage = WordDocument.Load(imageInput, DocumentFormat.Html, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("place", missingImage, TestContext.Current.CancellationToken)); Assert.True(imageInput.CanRead);
        WordDocument imageSource = WordDocument.Create(); imageSource.AddImage(new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 20, 10, "Logo"), TestContext.Current.CancellationToken);
        byte[] external = ControlTestDocuments.Modify(Save(imageSource), zip =>
        {
            var rels = ControlTestDocuments.Xml(zip, "word/_rels/document.xml.rels");
            var image = rels.Root!.Elements().First(e => ((string?)e.Attribute("Type"))?.EndsWith("/image", StringComparison.Ordinal) == true);
            image.SetAttributeValue("TargetMode", "External"); image.SetAttributeValue("Target", "https://invalid.example/image.png");
            ControlTestDocuments.Put(zip, "word/_rels/document.xml.rels", rels.ToString());
        });
        using var externalInput = new MemoryStream(external);
        WordDocument externalImage = WordDocument.Load(externalInput, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("place", externalImage, TestContext.Current.CancellationToken)); Assert.True(externalInput.CanRead);
        WordDocument broken = Load(Docx("<w:p><w:hyperlink w:anchor='absent'><w:r><w:t>Link</w:t></w:r></w:hyperlink></w:p>"));
        var unresolved = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("place", broken, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Reference:", unresolved.Message, StringComparison.Ordinal);
        WordDocument fieldTarget = Load(Docx(Placeholder("place", 1) + "<w:p>" + ReportFieldXml("Value") + "</w:p>"));
        WordDocument source = WordDocument.Create(); source.AddParagraph("Fragment", cancellationToken: TestContext.Current.CancellationToken);
        fieldTarget.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken); fieldTarget.Merge(new { Value = "Resolved" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(fieldTarget.Paragraphs, p => p.Text == "Resolved");
        WordDocument referenceTarget = Load(Docx(Placeholder("place", 1) + "<w:p>" + Field("REF place", "Insert") + "</w:p>")); byte[] targetBefore = Save(referenceTarget);
        var failure = Assert.Throws<NotSupportedException>(() => referenceTarget.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Reference:", failure.Message, StringComparison.Ordinal); Assert.Equal(targetBefore, Save(referenceTarget));
        Assert.Equal(before, Save(target));
    }

    [Fact]
    public void FragmentRespectsDestinationBudgetAndCancellation()
    {
        WordDocument target = WordDocument.Create(new ConversionOptions(maximumCharacters: 128));
        target.AddParagraph("Here", cancellationToken: TestContext.Current.CancellationToken); target.AddBookmark("place", 0, 0, 4, TestContext.Current.CancellationToken);
        WordDocument source = WordDocument.Create(); source.AddParagraph(new string('X', 300), cancellationToken: TestContext.Current.CancellationToken);
        byte[] before = Save(target); byte[] sourceBefore = Save(source);
        Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => target.InsertFragmentAtBookmark("place", source, cancel.Token));
        Assert.Equal(before, Save(target)); Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void CellFragmentPreservesHostGridAndRendersNestedContent()
    {
        string cell = "<w:tc><w:tcPr><w:tcW w:w='4000' w:type='dxa'/></w:tcPr>" + P("CellBefore") + Placeholder("place", 1) + "</w:tc>";
        string other = "<w:tc><w:tcPr><w:tcW w:w='2000' w:type='dxa'/></w:tcPr>" + P("RightCell") + "</w:tc>";
        string table = "<w:tbl><w:tblPr><w:tblW w:w='6000' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='4000'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr>" + cell + other + "</w:tr></w:tbl>";
        WordDocument target = Load(Docx(P("BodyBefore") + table + P("BodyAfter")));
        WordDocument fragment = WordDocument.Create();
        fragment.AddParagraph("ClauseOne", cancellationToken: TestContext.Current.CancellationToken);
        fragment.AddBookmark("internal", 0, 0, 9, TestContext.Current.CancellationToken);
        fragment.AddParagraph("ClauseTwo", cancellationToken: TestContext.Current.CancellationToken);
        fragment.AddTable(NestedFragmentCells, cancellationToken: TestContext.Current.CancellationToken);
        byte[] sourceBefore = Save(fragment), targetBefore = Save(target);
        Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtBookmark("place", fragment, TestContext.Current.CancellationToken));
        Assert.Equal(targetBefore, Save(target));
        target.InsertFragmentAtTableCellBookmark("place", fragment, TestContext.Current.CancellationToken);
        Assert.Equal(sourceBefore, Save(fragment));
        byte[] word = Save(target); Schema(word);
        var host = Part(word, "word/document.xml").Descendants(W + "tbl").First();
        Assert.Equal(HostGrid, host.Element(W + "tblGrid")!.Elements(W + "gridCol").Select(x => (string?)x.Attribute(W + "w")));
        var cells = host.Element(W + "tr")!.Elements(W + "tc").ToArray();
        Assert.Equal("4000", (string?)cells[0].Element(W + "tcPr")!.Element(W + "tcW")!.Attribute(W + "w"));
        Assert.Equal("2000", (string?)cells[1].Element(W + "tcPr")!.Element(W + "tcW")!.Attribute(W + "w"));
        Assert.Equal(W + "p", cells[0].Elements().Last().Name); Assert.Single(cells[0].Elements(W + "tbl"));
        var bookmark = Assert.Single(cells[0].Descendants(W + "bookmarkStart"));
        Assert.StartsWith("wn", (string?)bookmark.Attribute(W + "name"), StringComparison.Ordinal);
        Assert.Equal((string?)bookmark.Attribute(W + "id"), (string?)Assert.Single(cells[0].Descendants(W + "bookmarkEnd")).Attribute(W + "id"));
        Assert.Equal("RightCell", cells[1].Value);
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        var words = pdf.GetPage(1).GetWords().ToArray();
        foreach (string text in new[] { "BodyBefore", "CellBefore", "ClauseOne", "ClauseTwo", "NestedValue", "RightCell", "BodyAfter" })
            Assert.Contains(words, w => w.Text == text);
        Assert.True(words.Single(w => w.Text == "RightCell").BoundingBox.Left > words.Single(w => w.Text == "ClauseOne").BoundingBox.Left);
    }

    [Fact]
    public void RepeatedCellFragmentsKeepIndependentBookmarkIdentities()
    {
        WordDocument target = Load(Docx(Table(Cell(Placeholder("one", 1) + Placeholder("two", 2)))));
        WordDocument source = WordDocument.Create(); source.AddParagraph("Clause", cancellationToken: TestContext.Current.CancellationToken);
        source.AddBookmark("inside", 0, 0, 6, TestContext.Current.CancellationToken);
        byte[] before = Save(source);
        target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken);
        target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken);
        Assert.Equal(before, Save(source));
        byte[] saved = Save(target); Schema(saved);
        string?[] names = Part(saved, "word/document.xml").Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "name")).ToArray();
        Assert.Equal(2, names.Length); Assert.Equal(2, names.Distinct().Count());
        Assert.Equal(RepeatedCellText, target.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void CellFragmentPreservesInlineImageAndFollowingSibling()
    {
        WordDocument target = Load(Docx(Table(Cell(Placeholder("place", 1) + P("AfterCell")))));
        WordDocument source = WordDocument.Create();
        byte[] image = FidelityDocuments.Fixture("color-grid.png");
        source.AddImage(new TemplateImage(image, 20, 10, "Logo"), TestContext.Current.CancellationToken);
        target.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Schema(saved); Assert.Equal(image, ImagePart(saved));
        var cell = Part(saved, "word/document.xml").Descendants(W + "tc").First();
        Assert.Equal("AfterCell", cell.Elements(W + "p").Last().Value);
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        Assert.Single(pdf.GetPage(1).GetImages()); Assert.Contains("AfterCell", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedFragmentsPreserveNotesWithIndependentIdentities(bool cell)
    {
        string body = "<w:p><w:r><w:t>Clause</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'>" + P("NoteText") + "</w:footnote></w:footnotes>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", notes));
        WordDocument source = Load(input); byte[] before = Save(source);
        string placeholders = Placeholder("one", 1) + Placeholder("two", 2);
        WordDocument target = Load(Docx(cell ? Table(Cell(placeholders)) : placeholders));
        if (cell)
        {
            target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken);
            target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken);
        }
        else
        {
            target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
            target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken);
        }
        Assert.Equal(before, Save(source)); byte[] saved = Save(target); Schema(saved);
        string?[] ids = Part(saved, "word/document.xml").Descendants(W + "footnoteReference").Select(e => (string?)e.Attribute(W + "id")).ToArray();
        Assert.Equal(2, ids.Length); Assert.Equal(2, ids.Distinct().Count());
        var copiedNotes = Part(saved, "word/footnotes.xml").Descendants(W + "footnote").Where(e => ids.Contains((string?)e.Attribute(W + "id"))).ToArray();
        Assert.Equal(2, copiedNotes.Length); Assert.All(copiedNotes, n => Assert.Contains("NoteText", n.Value, StringComparison.Ordinal));
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.GetPages().SelectMany(p => p.GetWords()).Count(w => w.Text == "NoteText"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FragmentStylePolicyDefaultsToSourceAndCanUsePlaceholderAppearance(bool cell)
    {
        string placeholder = "<w:p><w:pPr><w:jc w:val='center'/></w:pPr><w:bookmarkStart w:id='1' w:name='place'/>"
            + "<w:r><w:rPr><w:i/><w:color w:val='0000FF'/></w:rPr><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        byte[] destination = Docx(cell ? Table(Cell(placeholder)) : placeholder);
        WordDocument source = Load(Docx("<w:p><w:r><w:rPr><w:b/><w:color w:val='FF0000'/></w:rPr><w:t>Clause</w:t></w:r></w:p>"));
        byte[] sourceBefore = Save(source); WordDocument keep = Load(destination), restyled = Load(destination);
        if (cell)
        {
            keep.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken);
            restyled.InsertFragmentAtTableCellBookmarkWithOptions("place", source, new FragmentInsertionOptions { Style = CompositionStyle.UseDestination }, TestContext.Current.CancellationToken);
        }
        else
        {
            keep.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken);
            restyled.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Style = CompositionStyle.UseDestination }, TestContext.Current.CancellationToken);
        }
        Assert.Equal(sourceBefore, Save(source)); byte[] kept = Save(keep), changed = Save(restyled); Schema(kept); Schema(changed);
        var oldRun = Part(kept, "word/document.xml").Descendants(W + "r").Single(r => r.Value == "Clause");
        var newRun = Part(changed, "word/document.xml").Descendants(W + "r").Single(r => r.Value == "Clause");
        Assert.Equal("FF0000", (string?)oldRun.Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val"));
        Assert.NotNull(oldRun.Element(W + "rPr")!.Element(W + "b"));
        Assert.Equal("0000FF", (string?)newRun.Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val"));
        Assert.NotNull(newRun.Element(W + "rPr")!.Element(W + "i")); Assert.Null(newRun.Element(W + "rPr")!.Element(W + "b"));
        Assert.Equal("center", (string?)newRun.Parent!.Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val"));
        using PdfDocument keptPdf = PdfDocument.Open(Save(keep, DocumentFormat.Pdf));
        using PdfDocument changedPdf = PdfDocument.Open(Save(restyled, DocumentFormat.Pdf));
        var originalColor = keptPdf.GetPage(1).Letters[0].Color.ToRGBValues();
        var changedColor = changedPdf.GetPage(1).Letters[0].Color.ToRGBValues();
        Assert.Equal(1, originalColor.r, 3); Assert.Equal(0, originalColor.b, 3);
        Assert.Equal(0, changedColor.r, 3); Assert.Equal(1, changedColor.b, 3);
    }

    [Fact]
    public void DestinationStyleRejectsCodeBlocksWithoutMutatingSessions()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("```\ncode sample\n```\n"));
        WordDocument source = WordDocument.Load(input, DocumentFormat.Markdown, out _, Policy(), TestContext.Current.CancellationToken);
        WordDocument target = Load(Docx(Placeholder("place", 1))); byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source,
            new FragmentInsertionOptions { Style = CompositionStyle.UseDestination }, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Structure:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source)); Assert.True(input.CanRead);
    }

    [Fact]
    public void DestinationStyleRejectsCodeFormattingInsideFieldCache()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='character' w:styleId='WordnetCode'><w:name w:val='Code'/></w:style></w:styles>";
        string field = "<w:p><w:fldSimple w:instr='MERGEFIELD Name'><w:r><w:t>Plain</w:t></w:r>"
            + "<w:r><w:rPr><w:rStyle w:val='WordnetCode'/></w:rPr><w:t>Code</w:t></w:r></w:fldSimple></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(field), "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/styles.xml", styles));
        WordDocument source = Load(input), target = Load(Docx(Placeholder("place", 1)));
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var options = new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults, Style = CompositionStyle.UseDestination };
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Structure:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("nested")]
    [InlineData("body")]
    [InlineData("crossing")]
    public void CellFragmentRejectsUnsupportedOwnersWithoutMutatingEitherSession(string scenario)
    {
        string placeholder = scenario == "crossing"
            ? "<w:p><w:bookmarkStart w:id='1' w:name='place'/><w:bookmarkStart w:id='2' w:name='other'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/><w:bookmarkEnd w:id='2'/></w:p>"
            : Placeholder("place", 1);
        string contents = scenario == "nested" ? Table(Cell(placeholder)) + P("") : placeholder;
        string properties = scenario == "merged" ? "<w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" : "";
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/><w:gridCol w:w='2000'/></w:tblGrid>"
            + "<w:tr><w:tc>" + properties + contents + "</w:tc></w:tr></w:tbl>";
        WordDocument target = Load(Docx(scenario == "body" ? placeholder : table));
        WordDocument source = WordDocument.Create(); source.AddParagraph("Clause", cancellationToken: TestContext.Current.CancellationToken);
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        Exception? error = Record.Exception(() => target.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.NotNull(error);
        if (scenario == "crossing") Assert.IsType<NotSupportedException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.StartsWith("Fragment.Placeholder:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Fact]
    public void CellFragmentRespectsReferencesAndCancellationAtomically()
    {
        WordDocument target = Load(Docx(Table(Cell(Placeholder("place", 1))) + "<w:p>" + Field("REF place", "Insert") + "</w:p>"));
        WordDocument source = WordDocument.Create(); source.AddParagraph("Clause", cancellationToken: TestContext.Current.CancellationToken);
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Reference:", error.Message, StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => target.InsertFragmentAtTableCellBookmark("place", source, cancellation.Token));
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Fact]
    public void AmbiguousNamesAcrossBodyAndCellAreRejectedDuringLoading()
    {
        using var input = new MemoryStream(Docx(Placeholder("place", 1) + Table(Cell(Placeholder("place", 2)))));
        var error = Assert.Throws<InvalidDataException>(() => WordDocument.Load(input, DocumentFormat.Docx, out _, Policy(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Docx.Bookmark:", error.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
    }

    [Fact]
    public void CellFragmentHonorsDestinationCharacterBudget()
    {
        WordDocument target = WordDocument.Create(new ConversionOptions(maximumCharacters: 128));
        target.AddTable(FragmentCells, cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("place", 0, 0, 4, TestContext.Current.CancellationToken);
        WordDocument source = WordDocument.Create(); source.AddParagraph(new string('X', 300), cancellationToken: TestContext.Current.CancellationToken);
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Fact]
    public void FragmentBookmarkNestingHonorsDestinationDepthBound()
    {
        string nested = "<w:p><w:bookmarkStart w:id='1' w:name='a'/><w:bookmarkStart w:id='2' w:name='b'/>"
            + "<w:bookmarkStart w:id='3' w:name='c'/><w:r><w:t>Nested</w:t></w:r>"
            + "<w:bookmarkEnd w:id='3'/><w:bookmarkEnd w:id='2'/><w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument source = Load(Docx(nested));
        WordDocument target = WordDocument.Create(new ConversionOptions(maximumNestingDepth: 2));
        target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("place", 0, 0, 6, TestContext.Current.CancellationToken);
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var error = Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.NestingDepth:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FieldFragmentsRequireExplicitCachePolicyAndRemapLocalReferences(bool cell, bool useDestinationStyle)
    {
        string sourceContent = "<w:p><w:bookmarkStart w:id='2' w:name='local'/><w:r><w:t>AnchorText</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>"
            + "<w:p>" + Field("REF local", "OldReference") + "</w:p><w:p>" + Field("PAGEREF local", "99") + "</w:p>"
            + "<w:p>" + Field("MERGEFIELD Customer", "CachedCustomer") + "</w:p>";
        WordDocument source = Load(Docx(sourceContent)); byte[] sourceBefore = Save(source);
        WordDocument target = Load(Docx(cell ? Table(Cell(Placeholder("place", 1))) : Placeholder("place", 1)));
        byte[] targetBefore = Save(target);
        if (cell)
            Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtTableCellBookmark("place", source, TestContext.Current.CancellationToken));
        else
            Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken));
        Assert.Equal(targetBefore, Save(target));
        var policy = new FragmentInsertionOptions
        {
            Fields = FragmentFieldHandling.AcceptCachedResults,
            Style = useDestinationStyle ? CompositionStyle.UseDestination : CompositionStyle.KeepSource
        };
        DocumentEditResult result = cell
            ? target.InsertFragmentAtTableCellBookmarkWithOptions("place", source, policy, TestContext.Current.CancellationToken)
            : target.InsertFragmentAtBookmarkWithOptions("place", source, policy, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Fragment.CachedFields"); Assert.Equal(sourceBefore, Save(source));
        byte[] saved = Save(target); Schema(saved); var xml = Part(saved, "word/document.xml");
        string name = (string)Assert.Single(xml.Descendants(W + "bookmarkStart")).Attribute(W + "name")!;
        var reference = Assert.Single(xml.Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.StartsWith("REF ", StringComparison.Ordinal) == true);
        Assert.Equal("REF " + name, (string?)reference.Attribute(W + "instr")); Assert.Equal("AnchorText", reference.Value);
        var pageReference = Assert.Single(xml.Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.StartsWith("PAGEREF ", StringComparison.Ordinal) == true);
        Assert.Equal("PAGEREF " + name, (string?)pageReference.Attribute(W + "instr"));
        var merge = Assert.Single(xml.Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.StartsWith("MERGEFIELD ", StringComparison.Ordinal) == true);
        Assert.Equal("CachedCustomer", merge.Value);
        target.Merge(new { Customer = "Ada" }, cancellationToken: TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        Assert.Contains("AnchorText", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("Ada", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("1", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedFieldFragmentsIsolateSequenceCounters()
    {
        WordDocument target = Load(Docx(Placeholder("one", 1) + Placeholder("two", 2) + "<w:p>" + Field("SEQ Number", "0") + "</w:p>"));
        WordDocument source = Load(Docx("<w:p>" + Field("SEQ Number", "0") + "</w:p>"));
        var policy = new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults };
        target.InsertFragmentAtBookmarkWithOptions("one", source, policy, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmarkWithOptions("two", source, policy, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Schema(saved);
        var fields = Part(saved, "word/document.xml").Descendants(W + "fldSimple").ToArray();
        Assert.Equal(3, fields.Length);
        Assert.Equal(3, fields.Select(e => (string?)e.Attribute(W + "instr")).Distinct().Count());
        Assert.All(fields, e => Assert.Equal("1", e.Value));
    }

    [Fact]
    public void FragmentPageAndClockFieldsUseDestinationLayoutAndClock()
    {
        string sourceBody = "<w:p>" + Field("PAGE", "old-page") + "</w:p><w:p>" + Field("NUMPAGES", "old-pages")
            + "</w:p><w:p>" + Field("SECTIONPAGES", "old-section") + "</w:p><w:p>" + Field("DATE", "old-date")
            + "</w:p><w:p>" + Field("TIME", "old-time") + "</w:p>";
        WordDocument source = Load(Docx(sourceBody)); byte[] before = Save(source);
        var conversion = Policy(); conversion.Clock = () => new DateTimeOffset(2026, 10, 30, 12, 34, 56, TimeSpan.Zero);
        using var input = new MemoryStream(Docx(Placeholder("place", 1)));
        WordDocument target = WordDocument.Load(input, DocumentFormat.Docx, out _, conversion, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults }, TestContext.Current.CancellationToken);
        Assert.Equal(before, Save(source)); byte[] saved = Save(target); Schema(saved);
        Assert.Equal(5, Part(saved, "word/document.xml").Descendants(W + "fldSimple").Count());
        using PdfDocument pdf = PdfDocument.Open(Save(target, DocumentFormat.Pdf));
        string text = pdf.GetPage(1).Text;
        Assert.Contains("2026-10-30", text, StringComparison.Ordinal); Assert.Contains("12:34:56", text, StringComparison.Ordinal);
        Assert.Equal(3, pdf.GetPage(1).GetWords().Count(w => w.Text == "1"));
    }

    [Theory]
    [InlineData("wn1_b0")]
    [InlineData("WN1_B0")]
    public void FragmentBookmarksCannotCapturePreviouslyUnresolvedDestinationReferences(string targetName)
    {
        string body = Placeholder("place", 1) + "<w:p>" + Field("REF " + targetName, "StillUnresolved") + "</w:p>"
            + "<w:p><w:hyperlink w:anchor='" + targetName + "'><w:r><w:t>UnresolvedLink</w:t></w:r></w:hyperlink></w:p>";
        WordDocument target = Load(Docx(body)), source = WordDocument.Create();
        source.AddParagraph("Incoming", cancellationToken: TestContext.Current.CancellationToken);
        source.AddBookmark("local", 0, 0, 8, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Schema(saved); var xml = Part(saved, "word/document.xml");
        string? bookmark = (string?)Assert.Single(xml.Descendants(W + "bookmarkStart")).Attribute(W + "name");
        Assert.False(string.Equals("wn1_b0", bookmark, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("StillUnresolved", Assert.Single(xml.Descendants(W + "fldSimple")).Value);
        Assert.Equal(targetName, (string?)Assert.Single(xml.Descendants(W + "hyperlink")).Attribute(W + "anchor"));
    }

    [Fact]
    public void FieldFragmentsRejectCrossingBookmarkRangesAtomically()
    {
        string crossing = "<w:p><w:bookmarkStart w:id='1' w:name='a'/><w:bookmarkStart w:id='2' w:name='b'/>"
            + "<w:r><w:t>Anchor</w:t></w:r><w:bookmarkEnd w:id='1'/><w:bookmarkEnd w:id='2'/></w:p><w:p>" + Field("REF a", "Cached") + "</w:p>";
        WordDocument target = Load(Docx(Placeholder("place", 3))), source = Load(Docx(crossing));
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults }, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Reference:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }

    [Theory]
    [InlineData("REF outside", "Fragment.Reference:")]
    [InlineData("PAGEREF outside", "Fragment.Reference:")]
    [InlineData("TITLE", "Fragment.Structure:")]
    [InlineData("MERGEFIELD", "Fragment.Structure:")]
    public void FieldFragmentsRejectUnresolvedOrUnsupportedInstructionsWithoutMutation(string instruction, string expectedCode)
    {
        WordDocument target = Load(Docx(Placeholder("place", 1) + Placeholder("outside", 2)));
        WordDocument source = Load(Docx("<w:p>" + Field(instruction, "Cached") + "</w:p>"));
        byte[] targetBefore = Save(target), sourceBefore = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults }, TestContext.Current.CancellationToken));
        Assert.StartsWith(expectedCode, error.Message, StringComparison.Ordinal);
        Assert.Equal(targetBefore, Save(target)); Assert.Equal(sourceBefore, Save(source));
    }

    [Theory]
    [InlineData("REF local \\x")]
    [InlineData("REF local \\h")]
    [InlineData("SEQ Number \\* Unsupported")]
    [InlineData("PAGE \\* ROMAN")]
    [InlineData("MERGEFIELD Customer \\x nope")]
    public void FieldFragmentsRejectUnsupportedSwitchesBeforeInsertion(string instruction)
    {
        WordDocument target = Load(Docx(Placeholder("place", 1)));
        WordDocument source = Load(Docx("<w:p><w:bookmarkStart w:id='2' w:name='local'/><w:r><w:t>Anchor</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>"
            + "<w:p>" + Field(instruction, "Cached") + "</w:p>"));
        byte[] targetBefore = Save(target), sourceBefore = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults }, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Structure:", error.Message, StringComparison.Ordinal);
        Assert.Equal(targetBefore, Save(target)); Assert.Equal(sourceBefore, Save(source));
        Assert.Throws<ArgumentOutOfRangeException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, new FragmentInsertionOptions { Fields = (FragmentFieldHandling)42 }, TestContext.Current.CancellationToken));
        Assert.Equal(targetBefore, Save(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FragmentRejectsFieldsAlreadyFlattenedBySourceLoading(bool cell)
    {
        WordDocument source = Load(Docx("<w:p>" + Field("UNKNOWNFIELD", "Cached") + "</w:p>"));
        Assert.Contains(source.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
        WordDocument target = Load(Docx(cell ? Table(Cell(Placeholder("place", 1))) : Placeholder("place", 1)));
        byte[] targetBefore = Save(target), sourceBefore = Save(source);
        var policy = new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults };
        var error = cell
            ? Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtTableCellBookmarkWithOptions("place", source, policy, TestContext.Current.CancellationToken))
            : Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("place", source, policy, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceLoss:", error.Message, StringComparison.Ordinal);
        Assert.Equal(targetBefore, Save(target)); Assert.Equal(sourceBefore, Save(source));
    }
}
