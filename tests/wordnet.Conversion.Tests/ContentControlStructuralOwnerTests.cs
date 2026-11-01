using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlStructuralOwnerTests
{
    private static string Cell(string text) => "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/></w:tcPr><w:p>" + Run(text) + "</w:p></w:tc>";
    private static string Wrapper(int id, string content, string metadata = "") => "<w:sdt><w:sdtPr><w:id w:val='" + id + "'/>" + metadata + "<w:richText/></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";
    private static string Table(string rows) => "<w:tbl><w:tblPr><w:tblW w:w='4800' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid>" + rows + "</w:tbl>";

    [Theory]
    [InlineData("row")]
    [InlineData("cell")]
    public void IndependentControlsMatchTextAndRelativeCellPositions(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "StructuralOwners");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".docx")); Validate(source);
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "reference.json")));
        JsonElement words = reference.RootElement.GetProperty("words");
        Assert.True(words.GetProperty("A").GetProperty("left").GetDouble() < words.GetProperty("B").GetProperty("left").GetDouble());
        Assert.True(words.GetProperty("A").GetProperty("top").GetDouble() < words.GetProperty("C").GetProperty("top").GetDouble());
        WordDocument document = Load(source);
        using PdfDocument actual = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.Equal(Compact(reference.RootElement.GetProperty("layoutText").GetString()!), Compact(actual.GetPage(1).Text));
        var actualWords = actual.GetPage(1).GetWords().ToDictionary(word => word.Text, StringComparer.Ordinal);
        Assert.True(actualWords["A"].BoundingBox.Left < actualWords["B"].BoundingBox.Left);
        Assert.True(actualWords["A"].BoundingBox.Bottom > actualWords["C"].BoundingBox.Bottom);
        byte[] saved = Save(document); Validate(saved); Assert.Single(Load(saved).ContentControls);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("cell")]
    public void OrdinaryLoadingKeepsCachedTextWithoutCreatingTypedOwners(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "StructuralOwners");
        using var source = File.OpenRead(Path.Combine(directory, name + ".docx"));
        WordDocument ordinary = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.True(source.CanRead); Assert.Empty(ordinary.ContentControls); Assert.Equal("ABCD", Compact(Pdf(ordinary)));
    }

    [Theory]
    [InlineData(true, "body table row", "A\nB")]
    [InlineData(false, "body table cell", "A")]
    public void SimpleStructuralWrapperRetainsIdentityContentAndStaticDisplay(bool rowOwner, string expectedOwner, string expectedText)
    {
        string first = rowOwner ? Wrapper(7, "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>")
            : "<w:tr>" + Wrapper(7, Cell("A")) + Cell("B") + "</w:tr>";
        byte[] source = Package(Document(Table(first + "<w:tr>" + Cell("C") + Cell("D") + "</w:tr>"))); Validate(source);
        WordDocument document = Load(source); DocumentContentControl original = Assert.Single(document.ContentControls);
        Assert.Equal(expectedOwner, original.Owner); Assert.Equal(expectedText, original.DisplayText); Assert.Null(original.ParentId);
        Assert.Equal("ABCD", Compact(Pdf(document))); Assert.Equal("ABCD", Compact(Xps(document)));
        document.Title = "Metadata only";
        Assert.Throws<ArgumentException>(() => document.RemoveControl(original, cancellationToken: TestContext.Current.CancellationToken));
        DocumentContentControl current = Assert.Single(document.ContentControls);
        Assert.Throws<NotSupportedException>(() => document.ReplaceControlParagraphs(current,
            new[] { new ContentControlParagraph(new[] { new DocumentText("replace") }) }, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.RemoveControl(current, cancellationToken: TestContext.Current.CancellationToken));
        byte[] saved = Save(document); Validate(saved); XDocument xml = Xml(saved, "word/document.xml");
        XElement wrapper = Assert.Single(xml.Descendants(W + "sdt"));
        Assert.Equal(rowOwner ? W + "tbl" : W + "tr", wrapper.Parent!.Name);
        Assert.Equal(rowOwner ? W + "tr" : W + "tc", wrapper.Element(W + "sdtContent")!.Elements().Single().Name);
        WordDocument reopened = Load(saved); Assert.Equal(expectedOwner, Assert.Single(reopened.ContentControls).Owner);
        Assert.Equal(expectedText, Assert.Single(reopened.ContentControls).DisplayText);
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Equal("ABCD", Compact(Pdf(view)));
        byte[] flat = Save(view); Validate(flat); Assert.Empty(Xml(flat, "word/document.xml").Descendants(W + "sdt"));
    }

    [Theory]
    [InlineData("<w:text/>", "", "Control.ProtectedOwner")]
    [InlineData("<w:richText/>", "<w:showingPlcHdr/>", "Control.ProtectedOwner")]
    [InlineData("<w:richText/>", "<w:temporary/>", "Control.Unsupported")]
    public void StructuralWrappersRejectUnsupportedKindsAndProperties(string kind, string metadata, string code)
    {
        string wrapper = "<w:sdt><w:sdtPr><w:id w:val='7'/>" + metadata + kind + "</w:sdtPr><w:sdtContent><w:tr>" + Cell("A") + "</w:tr></w:sdtContent></w:sdt>";
        Exception? error = Record.Exception(() => LoadBody(Table(wrapper)));
        Assert.True(error is NotSupportedException or InvalidDataException); Assert.Contains(code, error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedStructuralControlsAndMergedCellsRejectWithoutPublishingASession()
    {
        string nested = Wrapper(7, "<w:tr><w:tc><w:p>" + Control(8, "<w:text/>", "child") + "</w:p></w:tc></w:tr>");
        Assert.Throws<NotSupportedException>(() => LoadBody(Table(nested)));
        string merged = "<w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr><w:p>" + Run("A") + "</w:p></w:tc>";
        Assert.Throws<NotSupportedException>(() => LoadBody(Table("<w:tr>" + Wrapper(7, merged) + "</w:tr>")));
    }

    [Theory]
    [InlineData("<w:tcW w:type='auto' w:w='0'/>", "Control.ProtectedOwner")]
    [InlineData("<w:tcW w:type='nil' w:w='0'/>", "Control.ProtectedOwner")]
    [InlineData("<w:tcW w:w='2400'/>", "Control.ProtectedOwner")]
    [InlineData("<w:tcW w:type='dxa'/>", "Docx.Number")]
    [InlineData("<w:tcW w:type='dxa' w:w='0'/>", "Docx.Number")]
    [InlineData("<w:tcW w:type='pct' w:w='5001'/>", "Docx.Number")]
    public void StructuralCellsRejectUnpreservedOrInvalidWidths(string width, string code)
    {
        string cell = "<w:tc><w:tcPr>" + width + "</w:tcPr><w:p>" + Run("A") + "</w:p></w:tc>";
        Exception? error = Record.Exception(() => LoadBody(Table("<w:tr>" + Wrapper(7, cell) + Cell("B") + "</w:tr>")));
        Assert.True(error is NotSupportedException or InvalidDataException);
        Assert.Contains(code, error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuralCellPercentageWidthSurvivesSaving()
    {
        string cell = Cell("A").Replace("w:type='dxa' w:w='2400'", "w:type='pct' w:w='2500'");
        WordDocument document = LoadBody(Table("<w:tr>" + Wrapper(7, cell) + Cell("B") + "</w:tr>"));
        byte[] saved = Save(document); Validate(saved);
        XElement width = Assert.Single(Assert.Single(Xml(saved, "word/document.xml").Descendants(W + "sdt")).Descendants(W + "tcW"));
        Assert.Equal("pct", (string?)width.Attribute(W + "type"));
        Assert.Equal("2500", (string?)width.Attribute(W + "w"));
    }

    [Fact]
    public void AStructuralContentLockSurvivesMetadataEditsAndCannotBeBypassed()
    {
        string wrapper = Wrapper(7, "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>", "<w:lock w:val='contentLocked'/>");
        WordDocument document = LoadBody(Table(wrapper)); document.Title = "Metadata";
        DocumentContentControl control = Assert.Single(document.ContentControls); byte[] before = Save(document);
        Assert.Equal(ContentControlLock.ContentLocked, control.Lock);
        Assert.Throws<InvalidOperationException>(() => document.ReplaceControlParagraphs(control,
            new[] { new ContentControlParagraph(new[] { new DocumentText("changed") }) }, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.RemoveControl(control, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
        Assert.Equal(ContentControlLock.ContentLocked, Assert.Single(Load(before).ContentControls).Lock);
    }

    [Fact]
    public void StructuralIdsShareTheGlobalNamespaceWithOrdinaryControls()
    {
        string bodyControl = "<w:p>" + Control(1, "<w:text/>", "body") + "</w:p>";
        string row = Wrapper(7, "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>");
        string missing = row.Replace("<w:id w:val='7'/>", "");
        WordDocument document = LoadBody(bodyControl + Table(missing));
        Assert.Equal("body", document.ContentControls.Single(control => control.Id == 1).DisplayText);
        Assert.Equal("body table row", document.ContentControls.Single(control => control.Id == 2).Owner);
        byte[] saved = Save(document); Validate(saved); Assert.Equal(2, Load(saved).ContentControls.Count);
        Assert.Throws<InvalidDataException>(() => LoadBody(bodyControl + Table(row.Replace("w:val='7'", "w:val='1'"))));
    }

    [Fact]
    public void StructuralWrappersCannotBecomeInsertionOrBindingTargets()
    {
        byte[] source = Package(Document(Table(Wrapper(7, "<w:tr>" + Cell("A") + Cell("B") + "</w:tr>"))));
        WordDocument document = Load(source); byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.InsertContentControl(0, 0,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "bypass"), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }
}
