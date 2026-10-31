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
    private static readonly string[] CrossTabCountSeparator = { "Count" };
    private const string CrossTabCellMargins = "<w:tcMar><w:top w:type='dxa' w:w='0'/><w:left w:type='dxa' w:w='0'/><w:bottom w:type='dxa' w:w='0'/><w:right w:type='dxa' w:w='240'/></w:tcMar>";
    private static string CrossTabCellOwner(string content, string layout = "fixed", string width = "4000", string cellProperties = "") =>
        "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='6000'/><w:tblLayout w:type='" + layout + "'/></w:tblPr>" +
        "<w:tblGrid><w:gridCol w:w='" + width + "'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr>" +
        "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='" + width + "'/><w:shd w:val='clear' w:fill='DDEEFF'/>" +
        CrossTabCellMargins + cellProperties + "</w:tcPr>" + content + "</w:tc>" +
        "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2000'/></w:tcPr>" + P("Neighbor") + "</w:tc></w:tr></w:tbl>";

    [Fact]
    public void CellBookmarkCrossTabRetainsCellGeometryAndSurroundingContent()
    {
        string keep = "<w:p><w:bookmarkStart w:id='2' w:name='keep'/><w:r><w:t>Inside before</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>";
        string body = P("Outside before") + CrossTabCellOwner(keep + Placeholder("place", 1) + P("Inside after")) + P("Outside after") + Page;
        WordDocument document = Load(Docx(body));
        DocumentEditResult edit = document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, includeTotals: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, edit.Changes); byte[] saved = Save(document); Schema(saved);
        var table = Assert.Single(Part(saved, "word/document.xml").Root!.Element(W + "body")!.Elements(W + "tbl"));
        var cell = table.Element(W + "tr")!.Elements(W + "tc").First();
        Assert.Equal("4000", (string?)cell.Element(W + "tcPr")!.Element(W + "tcW")!.Attribute(W + "w"));
        Assert.Equal("DDEEFF", (string?)cell.Element(W + "tcPr")!.Element(W + "shd")!.Attribute(W + "fill"));
        var nested = Assert.Single(cell.Elements(W + "tbl"));
        Assert.Empty(nested.Descendants(W + "tblHeader")); Assert.Contains(nested.Descendants(W + "tc"), item => item.Value == "7.5");
        Assert.Equal("Inside before", cell.Elements(W + "p").First().Value); Assert.Equal("Inside after", cell.Elements(W + "p").Last().Value);
        Assert.Contains(cell.Descendants(W + "bookmarkStart"), item => (string?)item.Attribute(W + "name") == "keep");
        Assert.Equal("Neighbor", table.Element(W + "tr")!.Elements(W + "tc").Last().Value);
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); string text = string.Concat(pdf.GetPages().Select(page => page.Text));
        Assert.True(text.IndexOf("Inside before", StringComparison.Ordinal) < text.IndexOf("North", StringComparison.Ordinal));
        Assert.True(text.IndexOf("North", StringComparison.Ordinal) < text.IndexOf("Inside after", StringComparison.Ordinal));
        Assert.Contains("7.5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CellCrossTabKeepsAllRowsAcrossPagesAndDoesNotRepeatNestedHeader()
    {
        WordDocument document = Load(Docx(CrossTabCellOwner(Placeholder("place", 1)) + Page));
        CrossTabRecord[] records = Enumerable.Range(0, 100).Select(i => new CrossTabRecord("Item" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), "Count", 1)).ToArray();
        document.InsertCrossTabAtCellBookmark("place", records, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Schema(saved); using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.True(pdf.NumberOfPages > 1); string text = string.Concat(pdf.GetPages().Select(page => page.Text));
        foreach (CrossTabRecord record in records) Assert.Equal(1, text.Split(new[] { record.Row }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split(CrossTabCountSeparator, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CellCrossTabRejectsAmbiguousGeometryBeforeReadingRecords()
    {
        string[] owners = {
            CrossTabCellOwner(Placeholder("place", 1), layout: "autofit"),
            CrossTabCellOwner(Placeholder("place", 1), cellProperties: "<w:gridSpan w:val='2'/>"),
            CrossTabCellOwner(Placeholder("place", 1), cellProperties: "<w:vMerge w:val='restart'/>"),
            CrossTabCellOwner(Placeholder("place", 1)).Replace(CrossTabCellMargins, ""),
            CrossTabCellOwner(Placeholder("place", 1)).Replace("<w:bottom w:type='dxa' w:w='0'/>", ""),
            CrossTabCellOwner(Placeholder("place", 1)).Replace("<w:left w:type='dxa' w:w='0'/>", "<w:start w:type='dxa' w:w='0'/>") ,
            CrossTabCellOwner(Placeholder("place", 1)).Replace("<w:left w:type='dxa' w:w='0'/>", "<w:left w:type='dxa' w:w='0'/><w:left w:type='dxa' w:w='80'/>") ,
            CrossTabCellOwner(Placeholder("place", 1)).Replace("<w:tr>", "<w:tr><w:trPr><w:trHeight w:val='300' w:hRule='exact'/></w:trPr>"),
            Region(CrossTabCellOwner(Placeholder("place", 1)), "Items"),
        };
        foreach (string owner in owners)
        {
            WordDocument document = Load(Docx(owner + Page)); byte[] before = Save(document); bool read = false;
            IEnumerable<CrossTabRecord> Records() { read = true; yield return new CrossTabRecord("A", "B", 1); }
            Assert.Throws<NotSupportedException>(() => document.InsertCrossTabAtCellBookmark("place", Records(), cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(read); Assert.Equal(before, Save(document));
        }
    }

    [Fact]
    public void CellCrossTabRejectsOverflowReferencesAndCancellationAtomically()
    {
        WordDocument document = Load(Docx(CrossTabCellOwner(Placeholder("place", 1), width: "900") + Page)); byte[] before = Save(document);
        var error = Assert.Throws<NotSupportedException>(() => document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Report.CrossTabLayout:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(document));
        document = Load(Docx(CrossTabCellOwner(Placeholder("place", 1)) + "<w:p>" + Field("REF place", "Insert") + "</w:p>" + Page)); before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
        string enclosed = "<w:p><w:bookmarkStart w:id='2' w:name='outer'/><w:r><w:t>Before</w:t></w:r></w:p>" + Placeholder("place", 1)
            + "<w:p><w:r><w:t>After</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>";
        document = Load(Docx(CrossTabCellOwner(enclosed) + Page)); before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, cancellationToken: cancel.Token)); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void EmptyCellCrossTabKeepsTheRequiredTrailingParagraph()
    {
        WordDocument document = Load(Docx(CrossTabCellOwner(Placeholder("place", 1)) + Page));
        document.InsertCrossTabAtCellBookmark("place", Array.Empty<CrossTabRecord>(), cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Schema(saved);
        var cell = Part(saved, "word/document.xml").Descendants(W + "tc").First();
        Assert.Equal(W + "p", cell.Elements().Last().Name);
        Assert.Equal("Row", cell.Descendants(W + "tbl").Single().Value);
    }

    [Fact]
    public void CellCrossTabUsesCompleteInheritedMarginsAndCancelsDuringEnumeration()
    {
        string inherited = CrossTabCellMargins.Replace("tcMar", "tblCellMar");
        string table = CrossTabCellOwner(Placeholder("place", 1)).Replace(CrossTabCellMargins, "").Replace("</w:tblPr>", inherited + "</w:tblPr>");
        WordDocument document = Load(Docx(table + Page)); byte[] before = Save(document);
        using var cancel = new CancellationTokenSource();
        IEnumerable<CrossTabRecord> Records() { yield return new CrossTabRecord("First", "Q", 1); cancel.Cancel(); yield return new CrossTabRecord("Second", "Q", 2); }
        Assert.Throws<OperationCanceledException>(() => document.InsertCrossTabAtCellBookmark("place", Records(), cancellationToken: cancel.Token));
        Assert.Equal(before, Save(document));
        document.InsertCrossTabAtCellBookmark("place", PlacedCrossTab, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Schema(saved);
        Assert.Contains(Part(saved, "word/document.xml").Descendants(W + "tc"), cell => cell.Value == "North");
    }
}
