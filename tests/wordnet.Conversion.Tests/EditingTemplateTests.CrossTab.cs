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
    private static readonly string[] CrossTabExpected = { "Row", "N", "S", "Total", "A", "5", "0", "5", "B", "0", "4", "4", "Total", "5", "4", "9" };
    private static readonly string[] CrossTabCounts = { "Row", "X", "Y", "A", "2", "0", "B", "0", "1" };
    private static readonly string[] CrossTabUnicode = { "Row", "Å", "É", "2" };

    [Fact]
    public void CrossTabProducesHandCalculatedEditableCellsAndPdf()
    {
        WordDocument doc = WordDocument.Create();
        doc.AddCrossTab(new[] { new CrossTabRecord("A", "N", 2m), new CrossTabRecord("A", "N", 3m), new CrossTabRecord("B", "S", 4m), new CrossTabRecord("A", "S", null) }, includeTotals: true, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(doc); Schema(word);
        Assert.Equal(CrossTabExpected, Part(word, "word/document.xml").Descendants(W + "tc").Select(e => e.Value));
        using PdfDocument pdf = PdfDocument.Open(Save(doc, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text)); int offset = 0;
        foreach (string value in CrossTabExpected) { int at = text.IndexOf(value, offset, StringComparison.Ordinal); Assert.True(at >= offset); offset = at + value.Length; }
        var words = pdf.GetPage(1).GetWords().ToArray();
        var row = words.First(w => w.Text == "Row").BoundingBox; var north = words.First(w => w.Text == "N").BoundingBox;
        var south = words.First(w => w.Text == "S").BoundingBox; var total = words.First(w => w.Text == "Total").BoundingBox;
        Assert.True(row.Right < north.Left && north.Right < south.Left && south.Right < total.Left && total.Right <= pdf.GetPage(1).Width);
    }

    [Fact]
    public void CrossTabCountAndEmptyInputHaveDefinedValues()
    {
        WordDocument doc = WordDocument.Create();
        doc.AddCrossTab(new[] { new CrossTabRecord("A", "X"), new CrossTabRecord("A", "X", 10), new CrossTabRecord("B", "Y") }, CrossTabAggregation.Count, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(CrossTabCounts, Part(Save(doc), "word/document.xml").Descendants(W + "tc").Select(e => e.Value));
        doc = WordDocument.Create(); doc.AddCrossTab(Array.Empty<CrossTabRecord>(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Row", Assert.Single(Part(Save(doc), "word/document.xml").Descendants(W + "tc")).Value);
        doc = WordDocument.Create(); doc.AddCrossTab(new[] { new CrossTabRecord("É", "Å", 2) }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(CrossTabUnicode, Part(Save(doc), "word/document.xml").Descendants(W + "tc").Select(e => e.Value));
    }

    [Fact]
    public void CrossTabRejectsInvalidInputOverflowAndNonFittingLayoutAtomically()
    {
        WordDocument doc = WordDocument.Create(); doc.AddParagraph("Before", cancellationToken: TestContext.Current.CancellationToken); byte[] before = Save(doc);
        Assert.Throws<InvalidDataException>(() => doc.AddCrossTab(new[] { new CrossTabRecord("", "X") }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => doc.AddCrossTab(new[] { new CrossTabRecord("Total", "X") }, includeTotals: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<OverflowException>(() => doc.AddCrossTab(new[] { new CrossTabRecord("A", "X", decimal.MaxValue), new CrossTabRecord("A", "X", 1m) }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => doc.AddCrossTab(new[] { new CrossTabRecord(new string('W', 60), "X", 1m) }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => doc.AddCrossTab(Enumerable.Range(0, 65).Select(i => new CrossTabRecord("A", i.ToString(System.Globalization.CultureInfo.InvariantCulture))), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => doc.AddCrossTab(Enumerable.Range(0, 160).SelectMany(row => Enumerable.Range(0, 63).Select(column => new CrossTabRecord(row.ToString(System.Globalization.CultureInfo.InvariantCulture), column.ToString(System.Globalization.CultureInfo.InvariantCulture)))), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
    }

    [Fact]
    public void CrossTabCancellationDuringInputLeavesDocumentUnchanged()
    {
        WordDocument doc = WordDocument.Create(); byte[] before = Save(doc); using var cancel = new CancellationTokenSource();
        IEnumerable<CrossTabRecord> Records() { yield return new CrossTabRecord("A", "X", 1); cancel.Cancel(); yield return new CrossTabRecord("B", "Y", 2); }
        Assert.Throws<OperationCanceledException>(() => doc.AddCrossTab(Records(), cancellationToken: cancel.Token)); Assert.Equal(before, Save(doc));
    }
}
