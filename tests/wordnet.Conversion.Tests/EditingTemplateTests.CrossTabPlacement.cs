using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    private static readonly CrossTabRecord[] PlacedCrossTab =
    {
        new CrossTabRecord("North", "Q1", 2.5m), new CrossTabRecord("South", "Q2", 4m), new CrossTabRecord("North", "Q1", 1m)
    };

    [Fact]
    public void BodyBookmarkCrossTabKeepsSurroundingsOrderAndEditableTotals()
    {
        WordDocument document = Load(Docx(P("Before") + Placeholder("place", 1) + P("After") + Page));
        DocumentEditResult edit = document.InsertCrossTabAtBookmark("place", PlacedCrossTab, includeTotals: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, edit.Changes); byte[] saved = Save(document); Schema(saved);
        var body = Part(saved, "word/document.xml").Root!.Element(W + "body")!;
        Assert.Equal("Before", body.Elements(W + "p").First().Value); Assert.Equal("After", body.Elements(W + "p").Last().Value);
        var table = Assert.Single(body.Elements(W + "tbl")); Assert.NotNull(table.Descendants(W + "tblHeader").SingleOrDefault());
        Assert.Equal("Row", table.Descendants(W + "tc").First().Value); Assert.Contains(table.Descendants(W + "tc"), cell => cell.Value == "7.5");
        Assert.DoesNotContain(body.Descendants(W + "bookmarkStart"), item => (string?)item.Attribute(W + "name") == "place");
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(page => page.Text));
        Assert.True(text.IndexOf("Before", StringComparison.Ordinal) < text.IndexOf("North", StringComparison.Ordinal));
        Assert.True(text.IndexOf("North", StringComparison.Ordinal) < text.IndexOf("After", StringComparison.Ordinal));
        Assert.Contains("7.5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyCrossTabRepeatsItsHeaderAcrossPagesWithoutLosingRows()
    {
        WordDocument document = Load(Docx(Placeholder("place", 1) + Page));
        CrossTabRecord[] records = Enumerable.Range(0, 120).Select(i => new CrossTabRecord("Item" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), "Count", 1)).ToArray();
        document.InsertCrossTabAtBookmark("place", records, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Schema(saved); using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        var pages = pdf.GetPages().ToArray(); Assert.True(pages.Length > 1);
        Assert.All(pages, page => Assert.Contains("Row", page.Text, StringComparison.Ordinal));
        string text = string.Concat(pages.Select(page => page.Text));
        foreach (CrossTabRecord record in records) Assert.Equal(1, text.Split(new[] { record.Row }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void BodyCrossTabUsesThePlaceholderSectionWidth()
    {
        string narrowPage = Page.Replace("w:w='8000'", "w:w='3000'");
        string narrowFirst = Placeholder("place", 1).Replace("<w:p>", "<w:p><w:pPr>" + narrowPage + "</w:pPr>");
        WordDocument document = Load(Docx(narrowFirst + P("After") + Page)); byte[] before = Save(document);
        var records = new[] { new CrossTabRecord(new string('W', 25), "Q", 1) };
        var error = Assert.Throws<NotSupportedException>(() => document.InsertCrossTabAtBookmark("place", records, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Report.CrossTabLayout:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(document));
        string wideFirst = Placeholder("place", 1).Replace("<w:p>", "<w:p><w:pPr>" + Page + "</w:pPr>");
        WordDocument roomy = Load(Docx(wideFirst + P("After") + narrowPage));
        roomy.InsertCrossTabAtBookmark("place", records, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(roomy); Schema(saved);
        Assert.Equal("8000", (string?)Part(saved, "word/document.xml").Descendants(W + "sectPr").First().Element(W + "pgSz")!.Attribute(W + "w"));
    }

    [Fact]
    public void BodyCrossTabRejectsUnsafePlaceholdersReferencesAndReentrantData()
    {
        WordDocument target = Load(Docx(Placeholder("place", 1) + "<w:p>" + Field("REF place", "Insert") + "</w:p>")); byte[] before = Save(target);
        bool enumerated = false; IEnumerable<CrossTabRecord> Unread() { enumerated = true; yield return new CrossTabRecord("A", "B", 1); }
        var reference = Assert.Throws<NotSupportedException>(() => target.InsertCrossTabAtBookmark("place", Unread(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Report.CrossTabReference:", reference.Message, StringComparison.Ordinal); Assert.False(enumerated); Assert.Equal(before, Save(target));
        WordDocument partial = Load(Docx("<w:p><w:r><w:t>Prefix</w:t></w:r><w:bookmarkStart w:id='1' w:name='place'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>"));
        byte[] beforePartial = Save(partial); Assert.Throws<NotSupportedException>(() => partial.InsertCrossTabAtBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(beforePartial, Save(partial));
        WordDocument enclosed = Load(Docx("<w:p><w:bookmarkStart w:id='2' w:name='outer'/><w:r><w:t>Before</w:t></w:r></w:p>" + Placeholder("place", 1)
            + "<w:p><w:r><w:t>After</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>"));
        byte[] beforeEnclosed = Save(enclosed); Assert.Throws<NotSupportedException>(() => enclosed.InsertCrossTabAtBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(beforeEnclosed, Save(enclosed));
        WordDocument pageBreak = Load(Docx(Placeholder("place", 1).Replace("<w:p>", "<w:p><w:pPr><w:pageBreakBefore/></w:pPr>"))); byte[] beforePageBreak = Save(pageBreak);
        Assert.Throws<InvalidDataException>(() => pageBreak.InsertCrossTabAtBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(beforePageBreak, Save(pageBreak));
        WordDocument cell = Load(Docx(Table(Cell(Placeholder("place", 1))))); byte[] beforeCell = Save(cell);
        Assert.Throws<InvalidDataException>(() => cell.InsertCrossTabAtBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(beforeCell, Save(cell));
        WordDocument reentrant = Load(Docx(Placeholder("place", 1))); byte[] beforeReentrant = Save(reentrant);
        IEnumerable<CrossTabRecord> Records() { reentrant.AddParagraph("Unexpected", cancellationToken: TestContext.Current.CancellationToken); yield return new CrossTabRecord("A", "B", 1); }
        Assert.Throws<InvalidOperationException>(() => reentrant.InsertCrossTabAtBookmark("place", Records(), cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(beforeReentrant, Save(reentrant));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => reentrant.InsertCrossTabAtBookmark("place", PlacedCrossTab, cancellationToken: cancelled.Token)); Assert.Equal(beforeReentrant, Save(reentrant));
    }
}
