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
    private static readonly string[] MixedBefore = { "Before" };
    private static readonly string[] MixedAfter = { "After" };
    private static readonly int[] MixedTwoItems = { 1, 2 };
    private static readonly int[] MixedOneItem = { 1 };
    private static readonly string[] MixedNestedExpected = { "Ada", "A1", "A2", "Bea", "B1" };
    private static string MixedTable(string cell) => "<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='4000' w:type='dxa'/></w:tcPr><w:p>" + cell + "</w:p></w:tc></w:tr></w:tbl>";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void MixedRegionRepeatsParagraphsAndTablesInRecordOrder(int count)
    {
        string body = P("Before") + Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + MixedTable(ReportFieldXml("Value")) + P("End")) + P("After") + Page;
        WordDocument document = Load(Docx(body)); byte[] preserved = Save(document); Schema(preserved); document = Load(preserved);
        var records = Enumerable.Range(0, count).Select(i => new { Name = "Name" + i, Value = "Value" + i }).ToArray();
        document.Merge(new { Items = records }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(document); Schema(word); var bodyXml = Part(word, "word/document.xml").Root!.Element(W + "body")!;
        var order = bodyXml.Elements().Where(e => e.Name == W + "p" || e.Name == W + "tbl").Select(e => e.Value).ToArray();
        var expected = MixedBefore.Concat(records.SelectMany(r => new[] { r.Name, r.Value, "End" })).Concat(MixedAfter); Assert.Equal(expected, order);
        Assert.Empty(bodyXml.Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        int position = 0; foreach (string value in expected) { int next = text.IndexOf(value, position, StringComparison.Ordinal); Assert.True(next >= position); position = next + value.Length; }
    }

    [Fact]
    public void MixedRegionProducesMultipleRealPagesInSourceOrder()
    {
        WordDocument document = Load(Docx(Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + MixedTable(ReportFieldXml("Value"))) + Page));
        var records = Enumerable.Range(0, 24).Select(i => new { Name = "Name" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Value = "Value" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) }).ToArray();
        document.Merge(new { Items = records }, cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Schema(word);
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.True(pdf.NumberOfPages > 1);
        string text = string.Concat(pdf.GetPages().Select(p => p.Text)); int position = 0;
        foreach (var record in records) foreach (string value in new[] { record.Name, record.Value }) { int next = text.IndexOf(value, position, StringComparison.Ordinal); Assert.True(next >= position); position = next + value.Length; }
    }

    [Fact]
    public void MixedRegionRemapsBookmarksAndRejectsCrossingReferencesAtomically()
    {
        string bookmark = "<w:bookmarkStart w:id='1' w:name='mark'/>" + ReportFieldXml("Name") + "<w:bookmarkEnd w:id='1'/>";
        WordDocument document = Load(Docx(Region("<w:p>" + bookmark + "</w:p>" + MixedTable(ReportFieldXml("Value")))));
        document.Merge(new { Items = new[] { new { Name = "Ada", Value = "One" }, new { Name = "Bea", Value = "Two" } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(document); Schema(word); var starts = Part(word, "word/document.xml").Descendants(W + "bookmarkStart").ToArray(); Assert.Equal(2, starts.Select(e => (string?)e.Attribute(W + "name")).Distinct().Count());
        string external = "<w:p><w:hyperlink w:anchor='mark'><w:r><w:t>Link</w:t></w:r></w:hyperlink></w:p>";
        document = Load(Docx(Region("<w:p>" + bookmark + "</w:p>" + MixedTable(ReportFieldXml("Value"))) + external));
        byte[] before = Save(document); Assert.Throws<NotSupportedException>(() => document.Merge(new { Items = new[] { new { Name = "Ada", Value = "One" } } }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void MixedRegionBoundsRowsAndRejectsNestedTablesWithoutPartialEdits()
    {
        WordDocument document = Load(Docx(Region(P("Item") + MixedTable("<w:r><w:t>Cell</w:t></w:r>")))); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.Merge(new { Items = MixedTwoItems }, new TemplateOptions { MaximumRows = 5 }, TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
        document.Merge(new { Items = MixedTwoItems }, new TemplateOptions { MaximumRows = 6 }, TestContext.Current.CancellationToken); Assert.Equal(4, document.Paragraphs.Count);
        string nested = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc>" + MixedTable("<w:r><w:t>Nested</w:t></w:r>") + "<w:p/></w:tc></w:tr></w:tbl>";
        document = Load(Docx(Region(P("Item") + nested))); Assert.Throws<NotSupportedException>(() => document.Merge(new { Items = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MixedRegionRemapsFootnotesAndNumberingIndependently()
    {
        string numbered = "<w:p><w:pPr><w:numPr><w:ilvl w:val='0'/><w:numId w:val='7'/></w:numPr></w:pPr>" + ReportFieldXml("Name") + "</w:p>";
        string numbering = "<w:numbering xmlns:w='" + W + "'><w:abstractNum w:abstractNumId='1'><w:lvl w:ilvl='0'><w:start w:val='1'/><w:numFmt w:val='decimal'/><w:lvlText w:val='%1.'/></w:lvl></w:abstractNum><w:num w:numId='7'><w:abstractNumId w:val='1'/></w:num></w:numbering>";
        string table = MixedTable(ReportFieldXml("Value"));
        var data = new { Items = new[] { new { Name = "Ada", Value = "One" }, new { Name = "Bea", Value = "Two" } } };
        WordDocument document = Load(TestDocuments.Docx(TestDocuments.Body(Region(numbered + table)), "<Relationship Id='numbers' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>", extra: new KeyValuePair<string, string>("word/numbering.xml", numbering)));
        document.Merge(data, cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Schema(word);
        Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "numId").Select(e => (string?)e.Attribute(W + "val")).Distinct().Count());
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'><w:p>" + ReportFieldXml("Name") + "</w:p></w:footnote></w:footnotes>";
        string paragraph = "<w:p><w:r><w:t>Item</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        document = Load(TestDocuments.Docx(TestDocuments.Body(Region(paragraph + table)), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes)));
        document.Merge(data, cancellationToken: TestContext.Current.CancellationToken); word = Save(document); Schema(word);
        Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "footnoteReference").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        string noteText = Part(word, "word/footnotes.xml").Root!.Value; Assert.Contains("Ada", noteText, StringComparison.Ordinal); Assert.Contains("Bea", noteText, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedRegionKeepsExistingNestedRowRepeats()
    {
        string row = "<w:tr>" + Cell("<w:p>" + ReportFieldXml("Code") + "</w:p>") + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Lines", row) + "</w:tbl>";
        WordDocument document = Load(Docx(Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + table)));
        var data = new { Items = new[] { new { Name = "Ada", Lines = new[] { new { Code = "A1" }, new { Code = "A2" } } }, new { Name = "Bea", Lines = new[] { new { Code = "B1" } } } } };
        document.Merge(data, cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Schema(word);
        Assert.Equal(MixedNestedExpected, document.Paragraphs.Select(p => p.Text)); Assert.Empty(Part(word, "word/document.xml").Descendants(W + "sdt"));
    }

    [Fact]
    public void MixedRegionExecutesInlineConditionsAndImages()
    {
        string run = "<w:r><w:t>Visible</w:t></w:r>";
        string paragraph = "<w:p>" + ReportFieldXml("Name") + Control("if:Show", run) + Control("image:Logo", run) + "</w:p>";
        WordDocument document = Load(Docx(Region(paragraph + MixedTable(ReportFieldXml("Value")))));
        byte[] image = FidelityDocuments.Fixture("color-grid.png"); var logo = new TemplateImage(image, 20, 10, "Logo");
        var data = new { Items = new[] { new { Name = "Ada", Value = "One", Show = true, Logo = logo }, new { Name = "Bea", Value = "Two", Show = false, Logo = logo } } };
        document.Merge(data, cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Schema(word);
        Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "drawing").Count()); Assert.Equal(image, ImagePart(word));
        string text = Part(word, "word/document.xml").Root!.Value; Assert.Contains("AdaVisible", text, StringComparison.Ordinal); Assert.DoesNotContain("BeaVisible", text, StringComparison.Ordinal);
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal(2, pdf.GetPages().Sum(page => page.GetImages().Count()));
    }

    [Fact]
    public void MixedRegionCancellationDuringCallerEnumerationIsAtomic()
    {
        WordDocument document = Load(Docx(Region(P("Item") + MixedTable("<w:r><w:t>Cell</w:t></w:r>")))); byte[] before = Save(document); var diagnostics = document.Diagnostics;
        using var cancel = new CancellationTokenSource();
        IEnumerable<object> Items() { yield return new { Name = "First" }; cancel.Cancel(); yield return new { Name = "Second" }; }
        Assert.Throws<OperationCanceledException>(() => document.Merge(new { Items = Items() }, cancellationToken: cancel.Token)); Assert.Equal(before, Save(document)); Assert.Same(diagnostics, document.Diagnostics);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MixedRegionRejectsCrossingFieldAndCounterReferences(int kind)
    {
        string bookmark = "<w:bookmarkStart w:id='1' w:name='mark'/><w:r><w:t>Item</w:t></w:r><w:bookmarkEnd w:id='1'/>";
        string inside = kind switch { 0 => bookmark, 1 => bookmark + Field("HYPERLINK \\l &quot;mark&quot;", "link"), _ => Field("SEQ Ticket", "1") };
        string outside = kind switch { 0 => "<w:p>" + Field("REF mark", "Item") + "</w:p>", 1 => "", _ => "<w:p>" + Field("SEQ Ticket", "2") + "</w:p>" };
        WordDocument document = Load(Docx(Region("<w:p>" + inside + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>")) + outside));
        byte[] before = Save(document); var failure = Assert.Throws<NotSupportedException>(() => document.Merge(new { Items = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.RepeatReferences:", failure.Message, StringComparison.Ordinal); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void MixedRegionRemapsRetainedTemplateControlIdsWithoutCollidingWithOutsideControls()
    {
        string nested = "<w:sdt><w:sdtPr><w:alias w:val='Nested'/><w:id w:val='7'/><w:tag w:val='merge:Optional'/></w:sdtPr><w:sdtContent><w:r><w:t>Cached</w:t></w:r></w:sdtContent></w:sdt>";
        string outside = "<w:p><w:sdt><w:sdtPr><w:id w:val='1'/><w:tag w:val='merge:Outside'/></w:sdtPr><w:sdtContent><w:r><w:t>Outside</w:t></w:r></w:sdtContent></w:sdt></w:p>";
        WordDocument document = Load(Docx(Region("<w:p>" + nested + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>")) + outside));
        byte[] before = Save(document); Schema(before); Assert.Contains(Part(before, "word/document.xml").Descendants(W + "id"), e => (string?)e.Attribute(W + "val") == "7");
        document.Merge(new { Items = MixedTwoItems }, new TemplateOptions { MissingValues = MissingTemplateValue.KeepCached }, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Schema(word); var controls = Part(word, "word/document.xml").Descendants(W + "sdtPr").ToArray();
        Assert.Equal(3, controls.Length); Assert.Equal(3, controls.Select(e => (string?)e.Element(W + "id")?.Attribute(W + "val")).Distinct().Count());
        Assert.Equal(2, controls.Count(e => (string?)e.Element(W + "alias")?.Attribute(W + "val") == "Nested"));
        Assert.Contains(controls, e => (string?)e.Element(W + "id")?.Attribute(W + "val") == "1");
    }

    [Fact]
    public void CompositionRemapsTemplateControlIdsAndRejectsAmbiguousNestedControlMetadata()
    {
        string inline = "<w:p><w:sdt><w:sdtPr><w:alias w:val='A'/><w:id w:val='7'/><w:tag w:val='merge:Value'/></w:sdtPr><w:sdtContent><w:r><w:t>Cached</w:t></w:r></w:sdtContent></w:sdt></w:p>";
        WordDocument destination = Load(Docx(inline)); WordDocument source = Load(Docx(inline)); destination.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = Save(destination); Schema(word); Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "sdtPr").Select(e => (string?)e.Element(W + "id")?.Attribute(W + "val")).Distinct().Count());
        string unsupported = "<w:sdt><w:sdtPr><w:tag w:val='merge:Value'/><w:picture/></w:sdtPr><w:sdtContent><w:r><w:t>Cached</w:t></w:r></w:sdtContent></w:sdt>";
        WordDocument invalid = Load(Docx(Region("<w:p>" + unsupported + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>"))));
        Assert.Contains(invalid.Diagnostics, d => d.Code == "Template.InactiveControl");
        Assert.Throws<NotSupportedException>(() => invalid.Merge(new { Items = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MixedRegionRejectsSectionBoundariesAndRetainedObjects()
    {
        string sectionParagraph = "<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>Section</w:t></w:r></w:p>";
        WordDocument section = Load(Docx(Region(sectionParagraph + MixedTable("<w:r><w:t>Cell</w:t></w:r>"))));
        Assert.Throws<NotSupportedException>(() => section.Merge(new { Items = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
        byte[] chart = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "column.docx"));
        byte[] mixed = ControlTestDocuments.Modify(chart, zip =>
        {
            var xml = ControlTestDocuments.Xml(zip, "word/document.xml"); var body = xml.Root!.Element(W + "body")!;
            var chartParagraph = body.Elements(W + "p").First(p => p.Descendants().Any(e => e.Name.LocalName == "chart"));
            var table = System.Xml.Linq.XElement.Parse("<root xmlns:w='" + W + "'>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>") + "</root>").Elements().Single();
            chartParagraph.ReplaceWith(new System.Xml.Linq.XElement(W + "sdt", new System.Xml.Linq.XElement(W + "sdtPr", new System.Xml.Linq.XElement(W + "tag", new System.Xml.Linq.XAttribute(W + "val", "repeat:Items"))), new System.Xml.Linq.XElement(W + "sdtContent", new System.Xml.Linq.XElement(chartParagraph), table)));
            ControlTestDocuments.Put(zip, "word/document.xml", xml.ToString());
        });
        var failure = Assert.Throws<NotSupportedException>(() => Load(mixed));
        Assert.StartsWith("Object.Owner:", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedRegionBoundsCallerImageExpansionBeforeCommit()
    {
        string paragraph = "<w:p>" + Control("image:Logo", "<w:r><w:t>Logo</w:t></w:r>") + "</w:p>";
        byte[] input = Docx(Region(paragraph + MixedTable("<w:r><w:t>Cell</w:t></w:r>")));
        using var stream = new MemoryStream(input); var options = new ConversionOptions(FidelityMode.Lenient, maximumExpandedBytes: 8192) { Clock = () => new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero) };
        WordDocument document = WordDocument.Load(stream, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken); byte[] before = Save(document);
        var logo = new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 20, 10); var items = Enumerable.Range(0, 100).Select(_ => new { Logo = logo }).ToArray();
        Assert.Throws<InvalidDataException>(() => document.Merge(new { Items = items }, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(before, Save(document));
    }

    [Fact]
    public void EmptyMixedRegionCanRemoveAllBodyContent()
    {
        WordDocument document = Load(Docx(Region(P("Item") + MixedTable("<w:r><w:t>Cell</w:t></w:r>"))));
        document.Merge(new { Items = Array.Empty<object>() }, cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Schema(word);
        Assert.Empty(Part(word, "word/document.xml").Descendants(W + "sdt")); Assert.Empty(document.Paragraphs);
    }

    [Fact]
    public void UntaggedNestedControlsLoadInStrictModeButCannotExecute()
    {
        string nested = "<w:sdt><w:sdtPr/><w:sdtContent><w:r><w:t>Cached</w:t></w:r></w:sdtContent></w:sdt>";
        byte[] input = Docx(Region("<w:p>" + nested + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>")));
        using var stream = new MemoryStream(input);
        WordDocument doc = WordDocument.Load(stream, DocumentFormat.Docx, out var result, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Template.InactiveControl" && d.Severity == DiagnosticSeverity.Info);
        Assert.Throws<NotSupportedException>(() => doc.Merge(new { Items = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
        using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Empty(Part(output.ToArray(), "word/document.xml").Descendants(W + "tag"));
    }
}
