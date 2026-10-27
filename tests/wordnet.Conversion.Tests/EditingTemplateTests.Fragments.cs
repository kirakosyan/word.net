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
}
