using System;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using WordNet.Examples;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    private static readonly string[] MasterDetailExpected = { "Ada", "A", "Ada", "Root", "B", "Ada", "Root", "Total 30", "Bea", "Total 0" };
    private static readonly string[] StatementExpected = { "Customer statement", "Ada", "Consulting", "20", "Support", "10", "30", "Bea", "Delivery", "15", "15" };
    private static readonly string[] ShadowExpected = { "parent", "child", "root-global" };
    private static readonly string[] DeepExpected = { "Ada", "Sales", "One", "Ada", "Root", "Two", "Ada", "Root", "End department", "Empty", "End department", "Other", "Extra", "End customer", "Bea", "End customer" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedMasterDetailUsesChildParentAndRootScopes(bool reload)
    {
        string child = Region("<w:p>" + ReportFieldXml("Code") + "</w:p>" + MixedTable(ReportFieldXml("Name")) + "<w:p>" + ReportFieldXml("Global") + "</w:p>", "Lines");
        string parent = Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + child + "<w:p><w:r><w:t xml:space='preserve'>Total </w:t></w:r>" + ReportFieldXml("Total") + "</w:p>", "Customers");
        WordDocument doc = Load(Docx(parent + Page));
        if (reload) { byte[] original = Save(doc); Schema(original); doc = Load(original); }
        var data = new { Global = "Root", Customers = new[] { new { Name = "Ada", Total = 30, Lines = new[] { new { Code = "A" }, new { Code = "B" } } }, new { Name = "Bea", Total = 0, Lines = new[] { new { Code = "unused" } }.Take(0).ToArray() } } };
        doc.Merge(data, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MasterDetailExpected, doc.Paragraphs.Select(p => p.Text));
        byte[] output = Save(doc); Schema(output); Assert.Empty(Part(output, "word/document.xml").Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(doc, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text)); int offset = 0;
        foreach (string expected in MasterDetailExpected) { int at = text.IndexOf(expected, offset, StringComparison.Ordinal); Assert.True(at >= offset); offset = at + expected.Length; }
    }

    [Fact]
    public void MixedMasterDetailEnforcesAggregateBudgetAtomically()
    {
        string child = Region(P("Line") + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Lines");
        WordDocument doc = Load(Docx(Region(P("Customer") + child, "Customers")));
        byte[] before = Save(doc);
        var data = new { Customers = new[] { new { Lines = new[] { 1, 2 } }, new { Lines = new[] { 3, 4 } } } };
        Assert.Throws<InvalidDataException>(() => doc.Merge(data, new TemplateOptions { MaximumRows = 12 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
    }

    [Fact]
    public void RunnableCustomerStatementHasIndependentTotalsAndOrder()
    {
        string output = Path.Combine(Path.GetTempPath(), "wordnet-statement-" + Guid.NewGuid().ToString("N"));
        try
        {
            P3Recipes.Run(output, Path.Combine(TestDocuments.FixtureDirectory, "Automation"), TestContext.Current.CancellationToken);
            byte[] word = File.ReadAllBytes(Path.Combine(output, "p3-statement.docx")); Schema(word);
            Assert.Equal(StatementExpected, Part(word, "word/document.xml").Descendants(W + "p").Select(p => p.Value));
            using PdfDocument pdf = PdfDocument.Open(Path.Combine(output, "p3-statement.pdf"));
            string text = string.Concat(pdf.GetPages().Select(p => p.Text)); int offset = 0;
            foreach (string value in StatementExpected) { int at = text.IndexOf(value, offset, StringComparison.Ordinal); Assert.True(at >= offset); offset = at + value.Length; }
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Fact]
    public void MixedMasterDetailClonesBookmarksAndNotesPerChild()
    {
        string paragraph = "<w:p><w:bookmarkStart w:id='1' w:name='mark'/><w:r><w:t>Child</w:t><w:footnoteReference w:id='7'/></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        string child = Region(paragraph + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Lines");
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'><w:p>" + ReportFieldXml("Name") + "</w:p></w:footnote></w:footnotes>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Region(P("Customer") + child, "Customers")), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", notes));
        WordDocument doc = Load(input); doc.Merge(new { Customers = new[] { new { Name = "Ada", Lines = MixedOneItem }, new { Name = "Bea", Lines = MixedOneItem } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(doc); Schema(output); var xml = Part(output, "word/document.xml");
        Assert.Equal(2, xml.Descendants(W + "bookmarkStart").Select(e => (string?)e.Attribute(W + "name")).Distinct().Count());
        Assert.Equal(2, xml.Descendants(W + "footnoteReference").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        string noteText = Part(output, "word/footnotes.xml").Root!.Value; Assert.Contains("Ada", noteText, StringComparison.Ordinal); Assert.Contains("Bea", noteText, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedMasterDetailPrefersChildValuesAndRemovesEmptyParents()
    {
        string child = Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + MixedTable(ReportFieldXml("Global")), "Lines");
        string template = Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + child, "Customers");
        WordDocument doc = Load(Docx(template));
        doc.Merge(new { Name = "root", Global = "root-global", Customers = new[] { new { Name = "parent", Lines = new[] { new { Name = "child" } } } } }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ShadowExpected, doc.Paragraphs.Select(p => p.Text));
        doc = Load(Docx(template)); doc.Merge(new { Customers = Array.Empty<object>() }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(doc.Paragraphs); Schema(Save(doc));
    }

    [Fact]
    public void MixedMasterDetailExpandsThreeLevelsAndSiblingRegions()
    {
        string deep = Region("<w:p>" + ReportFieldXml("Item") + "</w:p>" + MixedTable(ReportFieldXml("Name")) + "<w:p>" + ReportFieldXml("Global") + "</w:p>", "Units");
        string child = Region("<w:p>" + ReportFieldXml("Department") + "</w:p>" + deep + P("End department"), "Departments");
        string sibling = Region(P("Other") + MixedTable(ReportFieldXml("Label")), "Other");
        WordDocument doc = Load(Docx(Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + child + sibling + P("End customer"), "Customers") + Page));
        byte[] original = Save(doc); Schema(original);
        Assert.Equal(4, Part(original, "word/document.xml").Descendants(W + "tag").Count());
        doc = Load(original);
        var data = new
        {
            Global = "Root",
            Customers = new object[] {
            new { Name = "Ada", Departments = new object[] { new { Department = "Sales", Units = new[] { new { Item = "One" }, new { Item = "Two" } } }, new { Department = "Empty", Units = Array.Empty<object>() } }, Other = new[] { new { Label = "Extra" } } },
            new { Name = "Bea", Departments = Array.Empty<object>(), Other = Array.Empty<object>() }
        }
        };
        doc.Merge(data, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(DeepExpected, doc.Paragraphs.Select(p => p.Text));
        byte[] output = Save(doc); Schema(output); Assert.Empty(Part(output, "word/document.xml").Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(doc, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text)); int offset = 0;
        foreach (string value in DeepExpected) { int at = text.IndexOf(value, offset, StringComparison.Ordinal); Assert.True(at >= offset); offset = at + value.Length; }
    }

    [Fact]
    public void MixedMasterDetailEnforcesDeepLimitsAndReferenceBoundariesAtomically()
    {
        string bookmark = "<w:bookmarkStart w:id='7' w:name='deep'/><w:r><w:t>Deep</w:t></w:r><w:bookmarkEnd w:id='7'/>";
        string deep = Region("<w:p>" + bookmark + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Units");
        string child = Region(P("Department") + deep, "Departments");
        string parent = Region(P("Customer") + child, "Customers");
        var data = new { Customers = new[] { new { Departments = new[] { new { Units = MixedOneItem } } } } };
        WordDocument doc = Load(Docx(parent)); byte[] before = Save(doc);
        Assert.Throws<InvalidDataException>(() => doc.Merge(data, new TemplateOptions { MaximumRows = 1 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
        Assert.Throws<InvalidDataException>(() => doc.Merge(data, new TemplateOptions { MaximumDepth = 2 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
        doc.Merge(data, new TemplateOptions { MaximumDepth = 3 }, TestContext.Current.CancellationToken);
        Schema(Save(doc));
        string crossing = Region("<w:p>" + Field("REF deep", "Deep") + "</w:p>" + child, "Customers");
        doc = Load(Docx(crossing)); before = Save(doc);
        var error = Assert.Throws<NotSupportedException>(() => doc.Merge(data, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.RepeatReferences:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(doc));
    }

    [Fact]
    public void MixedMasterDetailSupportsFiveLevelsAndPreservesDeepIdsWhenCached()
    {
        string inner = Region("<w:p>" + ReportFieldXml("Value") + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "E");
        string four = Region(P("D") + inner, "D");
        string three = Region(P("C") + four, "C");
        string two = Region(P("B") + three, "B");
        string one = Region(P("A") + two, "A");
        WordDocument doc = Load(Docx(one)); byte[] before = Save(doc);
        Assert.Equal(5, Part(before, "word/document.xml").Descendants(W + "tag").Count());
        var data = new { A = new[] { new { B = new[] { new { C = new[] { new { D = new[] { new { E = new[] { new { Value = "Fifth" } } } } } } } } } } };
        Assert.Throws<InvalidDataException>(() => doc.Merge(data, new TemplateOptions { MaximumDepth = 4 }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
        doc.Merge(data, new TemplateOptions { MaximumDepth = 5 }, TestContext.Current.CancellationToken);
        Assert.Contains(doc.Paragraphs, p => p.Text == "Fifth"); Schema(Save(doc));

        string cached = one.Replace("w:val='repeat:B'", "w:val='repeat:B'/><w:id w:val='22'")
            .Replace("w:val='repeat:C'", "w:val='repeat:C'/><w:id w:val='33'")
            .Replace("w:val='repeat:D'", "w:val='repeat:D'/><w:id w:val='44'")
            .Replace("w:val='repeat:E'", "w:val='repeat:E'/><w:id w:val='55'");
        doc = Load(Docx(cached));
        doc.Merge(new { A = new[] { new { }, new { } } }, new TemplateOptions { MissingValues = MissingTemplateValue.KeepCached }, TestContext.Current.CancellationToken);
        byte[] output = Save(doc); Schema(output);
        var ids = Part(output, "word/document.xml").Descendants(W + "sdtPr").Select(p => (string?)p.Element(W + "id")?.Attribute(W + "val")).ToArray();
        Assert.Equal(8, ids.Length); Assert.DoesNotContain(ids, id => id == null); Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(2, Part(output, "word/document.xml").Descendants(W + "sdt").Count(e => e.Ancestors(W + "sdt").Count() == 3));
    }

    [Fact]
    public void MixedMasterDetailRemapsDeepNotesAndLooksUpEnclosingValues()
    {
        string note = "<w:p><w:r><w:footnoteReference w:id='7'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'><w:p>" + ReportFieldXml("Customer") + ReportFieldXml("Product") + "</w:p></w:footnote></w:footnotes>";
        string leaf = Region(note + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Units");
        string middle = Region(P("Group") + leaf, "Groups");
        string outer = Region(P("Customer") + middle, "Orders");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(outer), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", notes));
        WordDocument doc = Load(input);
        doc.Merge(new { Orders = new[] { new { Customer = "Ada", Groups = new[] { new { Units = new[] { new { Product = "A" }, new { Product = "B" } } } } } } }, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(doc); Schema(output);
        var references = Part(output, "word/document.xml").Descendants(W + "footnoteReference").Select(e => (string?)e.Attribute(W + "id")).ToArray();
        Assert.Equal(2, references.Length); Assert.Equal(2, references.Distinct().Count());
        string content = Part(output, "word/footnotes.xml").Root!.Value;
        Assert.Contains("AdaA", content, StringComparison.Ordinal); Assert.Contains("AdaB", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedMasterDetailRejectsRepeatedAncestorPathsAtomically()
    {
        string inner = Region(P("Inner") + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Same");
        string middle = Region(P("Middle") + inner, "Middle");
        WordDocument doc = Load(Docx(Region(P("Outer") + middle, "Same"))); byte[] before = Save(doc);
        Assert.Contains(doc.Diagnostics, diagnostic => diagnostic.Code == "Template.NestedBlockControls");
        Assert.Throws<NotSupportedException>(() => doc.Merge(new { Same = MixedOneItem }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(doc));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    public void MixedMasterDetailRejectsReferencesBetweenParentAndChild(int kind, int count)
    {
        string bookmark = "<w:bookmarkStart w:id='1' w:name='mark'/><w:r><w:t>Child</w:t></w:r><w:bookmarkEnd w:id='1'/>";
        string parent = kind switch { 0 => Field("REF mark", "Child"), 1 => "<w:hyperlink w:anchor='mark'><w:r><w:t>Link</w:t></w:r></w:hyperlink>", _ => Field("SEQ Figure", "1") };
        string child = kind == 2 ? Field("SEQ Figure", "2") : bookmark;
        WordDocument doc = Load(Docx(Region("<w:p>" + parent + "</w:p>" + Region("<w:p>" + child + "</w:p>" + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Lines"), "Customers")));
        byte[] before = Save(doc); var failure = Assert.Throws<NotSupportedException>(() => doc.Merge(new { Customers = new[] { new { Lines = Enumerable.Range(0, count).ToArray() } } }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.RepeatReferences:", failure.Message, StringComparison.Ordinal); Assert.Equal(before, Save(doc));
    }

    [Fact]
    public void MixedMasterDetailRejectsNotesSharedAcrossChildBoundary()
    {
        string reference = "<w:p><w:r><w:footnoteReference w:id='7'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:footnote></w:footnotes>";
        string child = Region(reference + MixedTable("<w:r><w:t>Cell</w:t></w:r>"), "Lines");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Region(reference + child, "Customers")), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", notes));
        WordDocument doc = Load(input); byte[] before = Save(doc);
        var failure = Assert.Throws<NotSupportedException>(() => doc.Merge(new { Customers = new[] { new { Lines = MixedOneItem } } }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.RepeatReferences:", failure.Message, StringComparison.Ordinal); Assert.Equal(before, Save(doc));
    }

    [Fact]
    public void ExistingNestedRowsKeepItemThenRootLookup()
    {
        string row = "<w:tr>" + Cell("<w:p>" + ReportFieldXml("Name") + "</w:p>") + "</w:tr>";
        string inner = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Lines", row) + "</w:tbl>";
        string outerRow = "<w:tr>" + Cell(inner + "<w:p/>") + "</w:tr>";
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + Control("repeat:Orders", outerRow) + "</w:tbl>";
        WordDocument doc = Load(Docx(table)); doc.Merge(new { Name = "Root", Orders = new[] { new { Name = "Order", Lines = MixedOneItem } } }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(doc.Paragraphs, p => p.Text == "Root"); Assert.DoesNotContain(doc.Paragraphs, p => p.Text == "Order");
    }
}
