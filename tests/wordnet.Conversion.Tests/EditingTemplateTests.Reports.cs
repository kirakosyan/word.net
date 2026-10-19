using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    private static readonly string[] ReportExpected0 = new[] { "Before", "Ada", "Tail", "Bea", "Tail", "After" };
    private static readonly string[] ReportExpected1 = new[] { "Before", "After" };
    private static readonly string[] ReportExpected2 = new[] { "Ada", "Bea" };
    private static readonly string[] ReportExpected3 = new[] { "Immediate payment", "120.0" };
    private static string Region(string content, string path = "Items") => "<w:sdt><w:sdtPr><w:tag w:val='repeat:" + path + "'/></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";
    private static string ReportFieldXml(string path) => "<w:fldSimple w:instr='MERGEFIELD " + path + "'><w:r><w:rPr><w:b/></w:rPr><w:t>cached</w:t></w:r></w:fldSimple>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodyRepeatKeepsParagraphBoundariesNestedDataAndFormatting(bool reload)
    {
        WordDocument doc = Load(Docx(P("Before") + Region("<w:p>" + ReportFieldXml("Person.Name") + "</w:p>" + P("Tail")) + P("After") + Page));
        if (reload) doc = Load(Save(doc));
        doc.Merge(new { Items = new[] { new { Person = new { Name = "Ada" } }, new { Person = new { Name = "Bea" } } } }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReportExpected0, doc.Paragraphs.Select(p => p.Text));
        Assert.True(doc.Paragraphs[1].Runs.Single().Formatting.Bold); Assert.True(doc.Paragraphs[3].Runs.Single().Formatting.Bold);
        byte[] saved = Save(doc); Schema(saved); Assert.Empty(Part(saved, "word/document.xml").Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(doc, DocumentFormat.Pdf));
        Assert.Single(pdf.GetPages()); Assert.Equal("BeforeAdaTailBeaTailAfter", pdf.GetPage(1).Text);
    }

    [Fact]
    public void EmptyBodyRepeatRemovesTheEntireRegion()
    {
        WordDocument doc = Load(Docx(P("Before") + Region(P("A") + P("B")) + P("After")));
        doc.Merge(new { Items = Array.Empty<object>() }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReportExpected1, doc.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void BodyRepeatRemapsClosedBookmarksAndListIdentities()
    {
        string paragraph = "<w:p><w:bookmarkStart w:id='1' w:name='mark'/>" + ReportFieldXml("Name") + "<w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument doc = Load(Docx(Region(paragraph)));
        doc.Merge(new { Items = new[] { new { Name = "Ada" }, new { Name = "Bea" } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(doc); Schema(saved);
        XElement xml = Part(saved, "word/document.xml").Root!;
        Assert.Equal(2, xml.Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "name")).Distinct().Count());
        Assert.Equal(2, xml.Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        Assert.Equal(ReportExpected2, doc.Paragraphs.Select(p => p.Text));
    }
    [Fact]
    public void BodyRepeatAllocatesIndependentNumberingAndFootnoteIds()
    {
        string numbered = "<w:p><w:pPr><w:numPr><w:ilvl w:val='0'/><w:numId w:val='7'/></w:numPr></w:pPr>" + ReportFieldXml("Name") + "</w:p>";
        string numbering = "<w:numbering xmlns:w='" + W + "'><w:abstractNum w:abstractNumId='1'><w:lvl w:ilvl='0'><w:start w:val='1'/><w:numFmt w:val='decimal'/><w:lvlText w:val='%1.'/></w:lvl></w:abstractNum><w:num w:numId='7'><w:abstractNumId w:val='1'/></w:num></w:numbering>";
        WordDocument doc = Load(TestDocuments.Docx(TestDocuments.Body(Region(numbered)), "<Relationship Id='numbers' Type='" + TestDocuments.R + "/numbering' Target='numbering.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/numbering.xml", numbering)));
        var data = new { Items = new[] { new { Name = "Ada" }, new { Name = "Bea" } } };
        doc.Merge(data, cancellationToken: TestContext.Current.CancellationToken); byte[] saved = Save(doc); Schema(saved);
        Assert.Equal(2, Part(saved, "word/document.xml").Descendants(W + "numId").Select(e => (string?)e.Attribute(W + "val")).Distinct().Count());
        string note = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'><w:p>" + ReportFieldXml("Name") + "</w:p></w:footnote></w:footnotes>";
        string paragraph = "<w:p><w:r><w:t>Item</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        doc = Load(TestDocuments.Docx(TestDocuments.Body(Region(paragraph)), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", note)));
        doc.Merge(data, cancellationToken: TestContext.Current.CancellationToken); saved = Save(doc); Schema(saved);
        Assert.Equal(2, Part(saved, "word/document.xml").Descendants(W + "footnoteReference").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        string text = Part(saved, "word/footnotes.xml").Root!.Value;
        Assert.Contains("Ada", text, StringComparison.Ordinal); Assert.Contains("Bea", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossRegionBookmarksAndExpansionLimitsLeaveSessionUnchanged()
    {
        WordDocument doc = Load(Docx(Region("<w:p><w:bookmarkStart w:id='1' w:name='mark'/><w:r><w:t>A</w:t></w:r></w:p>")
            + "<w:p><w:bookmarkEnd w:id='1'/></w:p>"));
        byte[] before = Save(doc); var diagnostics = doc.Diagnostics;
        Assert.Throws<NotSupportedException>(() => doc.Merge(new { Items = new[] { new { } } }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc)); Assert.Same(diagnostics, doc.Diagnostics);
        doc = Load(Docx(Region(P("A") + P("B")))); before = Save(doc); diagnostics = doc.Diagnostics;
        Assert.Throws<InvalidDataException>(() => doc.Merge(new { Items = new[] { new { }, new { } } }, new TemplateOptions { MaximumRows = 3 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc)); Assert.Same(diagnostics, doc.Diagnostics);
        doc.Merge(new { Items = new[] { new { }, new { } } }, new TemplateOptions { MaximumRows = 4 }, TestContext.Current.CancellationToken);
        Assert.Equal(4, doc.Paragraphs.Count);
    }

    [Fact]
    public void InvalidNestedRegionsCannotBeSavedIntoExecutableTemplates()
    {
        WordDocument doc = Load(Docx(Region(Region(P("A")))));
        using var output = new MemoryStream(new byte[] { 1, 2, 3, 4 }, writable: true);
        Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.ToArray());
        Assert.Throws<NotSupportedException>(() => doc.Merge(new { Items = Array.Empty<object>() }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReportPredicatesDriveExistingIfControlsAndDecimalMergeFields()
    {
        string condition = "<w:sdt><w:sdtPr><w:tag w:val='if:PayNow'/></w:sdtPr><w:sdtContent>" + P("Immediate payment") + "</w:sdtContent></w:sdt>";
        WordDocument doc = Load(Docx(condition + "<w:p>" + ReportFieldXml("Total") + "</w:p>"));
        ReportData data = ReportData.Prepare(new { Net = 100m }, new ReportPlan(fields: new[] {
            new ReportField("Total", ReportExpression.Binary(ReportOperation.Multiply, ReportExpression.Path("Net"), ReportExpression.Literal(1.2m))),
            new ReportField("PayNow", ReportExpression.Binary(ReportOperation.Greater, ReportExpression.Path("Total"), ReportExpression.Literal(110m))) }), cancellationToken: TestContext.Current.CancellationToken);
        doc.Merge(data.Values, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ReportExpected3, doc.Paragraphs.Select(p => p.Text));
        Schema(Save(doc)); using PdfDocument pdf = PdfDocument.Open(Save(doc, DocumentFormat.Pdf));
        Assert.Contains("Immediate payment", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("120.0", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }
}
