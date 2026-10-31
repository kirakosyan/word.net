using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlBlockCreationTests
{
    private static readonly ContentControlListItem[] Choices = { new ContentControlListItem("Alpha", "a"), new ContentControlListItem("Beta", "b") };
    private static readonly string[] EmptyPair = { "", "" };
    private static readonly string[] Kept = { "Keep", "" };
    private static DocumentContentControl ById(WordDocument document, int id) => document.ContentControls.Single(control => control.Id == id);

    [Fact]
    public void EmptyBodyParagraphsBecomeEditableTypedBlockControls()
    {
        WordDocument document = WordDocument.Create();
        document.AddParagraphs(Enumerable.Repeat("", 6), cancellationToken: TestContext.Current.CancellationToken);
        var definitions = new[]
        {
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada"),
            new ContentControlDefinition(ContentControlKind.Checkbox, isChecked: true),
            new ContentControlDefinition(ContentControlKind.DropDownList, items: Choices, selectedValue: "b"),
            new ContentControlDefinition(ContentControlKind.ComboBox, items: Choices, initialText: "Manual"),
            new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))),
            new ContentControlDefinition(ContentControlKind.RichText, initialText: "Approved")
        };
        for (int index = 0; index < definitions.Length; index++)
        {
            DocumentContentControl created = document.InsertBlockContentControl(index, definitions[index], TestContext.Current.CancellationToken);
            Assert.Equal(index + 1, created.Id); Assert.Equal(definitions[index].Kind, created.Kind);
            Assert.Equal("block body/cell paragraphs", created.Owner);
        }
        byte[] saved = Save(document); Validate(saved);
        var body = Xml(saved, "word/document.xml").Root!.Element(W + "body")!;
        Assert.Equal(6, body.Elements(W + "sdt").Count()); Assert.Empty(body.Elements(W + "p"));
        Assert.All(body.Elements(W + "sdt"), item => Assert.Single(item.Element(W + "sdtContent")!.Elements(W + "p")));
        Assert.Contains("AdaXBetaManual2026-10-31Approved", Compact(Pdf(document)), StringComparison.Ordinal);
        Assert.Contains("AdaXBetaManual2026-10-31Approved", Compact(Xps(document)), StringComparison.Ordinal);
        WordDocument loaded = Load(saved); Assert.Equal(6, loaded.ContentControls.Count);
        loaded.SetControlText(ById(loaded, 1), "Grace", TestContext.Current.CancellationToken);
        loaded.SetControlChecked(ById(loaded, 2), false, TestContext.Current.CancellationToken);
        loaded.SetControlSelection(ById(loaded, 3), "a", TestContext.Current.CancellationToken);
        loaded.SetControlComboText(ById(loaded, 4), "New", TestContext.Current.CancellationToken);
        loaded.SetControlDate(ById(loaded, 5), ContentControlDateValue.FromInstant(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        loaded.ReplaceControlParagraphs(ById(loaded, 6), new[] { new ContentControlParagraph(new[] { new DocumentText("Done") }) }, TestContext.Current.CancellationToken);
        byte[] edited = Save(loaded); Validate(edited); Assert.Equal("Grace", ById(Load(edited), 1).DisplayText);
        WordDocument view = loaded.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Contains("Grace", Pdf(view), StringComparison.Ordinal);
    }

    [Fact]
    public void BlockCreationRejectsNonemptyStyledAndCellOwnersAtomically()
    {
        var definition = new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Value");
        WordDocument nonempty = WordDocument.Create(); nonempty.AddParagraph("Existing", cancellationToken: TestContext.Current.CancellationToken); byte[] before = Save(nonempty);
        Assert.Throws<NotSupportedException>(() => nonempty.InsertBlockContentControl(0, definition, TestContext.Current.CancellationToken)); Assert.Equal(before, Save(nonempty));
        WordDocument styled = WordDocument.Create(); styled.AddParagraph("", new TextFormatting { Bold = true }, cancellationToken: TestContext.Current.CancellationToken); byte[] styledBefore = Save(styled);
        Assert.Throws<NotSupportedException>(() => styled.InsertBlockContentControl(0, definition, TestContext.Current.CancellationToken)); Assert.Equal(styledBefore, Save(styled));
        WordDocument styledMark = LoadBody("<w:p><w:pPr><w:rPr><w:b/></w:rPr></w:pPr></w:p>"); byte[] markBefore = Save(styledMark);
        Assert.Throws<NotSupportedException>(() => styledMark.InsertBlockContentControl(0, definition, TestContext.Current.CancellationToken)); Assert.Equal(markBefore, Save(styledMark));
        WordDocument reset = LoadBody("<w:p><w:r><w:rPr><w:b w:val='0'/></w:rPr><w:t/></w:r></w:p>"); byte[] resetBefore = Save(reset);
        Assert.Throws<NotSupportedException>(() => reset.InsertBlockContentControl(0, definition, TestContext.Current.CancellationToken)); Assert.Equal(resetBefore, Save(reset));
        WordDocument cell = LoadBody("<w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"); byte[] cellBefore = Save(cell);
        Assert.Throws<NotSupportedException>(() => cell.InsertBlockContentControl(0, definition, TestContext.Current.CancellationToken)); Assert.Equal(cellBefore, Save(cell));
        Assert.Throws<ArgumentOutOfRangeException>(() => nonempty.InsertBlockContentControl(-1, definition, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => nonempty.InsertBlockContentControl(0, definition, cancelled.Token)); Assert.Equal(before, Save(nonempty));
    }

    [Fact]
    public void BlockWrapperRemovalPreservesOrExplicitlyDeletesContent()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraphs(EmptyPair, cancellationToken: TestContext.Current.CancellationToken);
        DocumentContentControl first = document.InsertBlockContentControl(0, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Keep"), TestContext.Current.CancellationToken);
        _ = document.InsertBlockContentControl(1, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Delete"), TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => document.RemoveControl(first, cancellationToken: TestContext.Current.CancellationToken));
        document.RemoveControl(ById(document, 1), cancellationToken: TestContext.Current.CancellationToken);
        document.RemoveControl(ById(document, 2), preserveContent: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(document.ContentControls); Assert.Equal(Kept, document.Paragraphs.Select(item => item.Text));
        byte[] saved = Save(document); Validate(saved); Assert.Empty(Xml(saved, "word/document.xml").Descendants(W + "sdt"));
    }

    [Fact]
    public void CreatedBlockUsesExplicitBindingAndUpdatesTheExistingLocalStore()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p><w:p/>";
        WordDocument document = Load(WithStore(body));
        var binding = new ContentControlBinding(StoreId, "/p:invoice/p:customer/@name", new Dictionary<string, string> { ["p"] = "urn:wordnet:controls" });
        DocumentContentControl created = document.InsertBlockContentControl(1, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "waiting", binding: binding), TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlBindingState.Unevaluated, created.BindingState);
        document.ApplyControlBindings(new[] { created }, TestContext.Current.CancellationToken); Assert.Equal("Ada & Co", ById(document, 2).DisplayText);
        document.SetControlText(ById(document, 2), "Agreed", TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken));
        document.SetControlText(ById(document, 1), "Agreed", TestContext.Current.CancellationToken);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("Agreed", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal("Agreed", ById(Load(saved), 2).DisplayText);
    }

    [Fact]
    public void CreatedBlockRemainsIndependentAfterFragmentComposition()
    {
        WordDocument source = WordDocument.Create(); source.AddParagraph("", cancellationToken: TestContext.Current.CancellationToken);
        _ = source.InsertBlockContentControl(0, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Source"), TestContext.Current.CancellationToken);
        WordDocument target = WordDocument.Create(); target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("place", 0, 0, 6, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("place", source, TestContext.Current.CancellationToken);
        target.SetControlText(Assert.Single(target.ContentControls), "Copy", TestContext.Current.CancellationToken);
        Assert.Equal("Source", Assert.Single(source.ContentControls).DisplayText); Assert.Equal("Copy", Assert.Single(target.ContentControls).DisplayText);
        byte[] saved = Save(target); Validate(saved); Assert.Equal("Copy", Assert.Single(Load(saved).ContentControls).DisplayText);
    }

    [Fact]
    public void BlockCreationInstallsValueBeforeItsContentLock()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("", cancellationToken: TestContext.Current.CancellationToken);
        DocumentContentControl created = document.InsertBlockContentControl(0,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "fixed", editLock: ContentControlLock.ContentLocked), TestContext.Current.CancellationToken);
        Assert.Equal("fixed", created.DisplayText); Assert.Equal(ContentControlLock.ContentLocked, created.Lock);
        Assert.Throws<InvalidOperationException>(() => document.SetControlText(created, "blocked", TestContext.Current.CancellationToken));
        byte[] saved = Save(document); Validate(saved); Assert.Equal(ContentControlLock.ContentLocked, Assert.Single(Load(saved).ContentControls).Lock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatedControlIdsAvoidSeparateTemplateOwners(bool block)
    {
        string template = "<w:sdt><w:sdtPr><w:id w:val='1'/><w:tag w:val='if:Show'/></w:sdtPr><w:sdtContent><w:p>" + Run("Condition") + "</w:p></w:sdtContent></w:sdt>";
        using var input = new MemoryStream(Package(Document(template + "<w:p/>")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        var definition = new ContentControlDefinition(ContentControlKind.PlainText, initialText: "New");
        DocumentContentControl created = block
            ? document.InsertBlockContentControl(1, definition, TestContext.Current.CancellationToken)
            : document.InsertContentControl(1, 0, definition, TestContext.Current.CancellationToken);
        Assert.Equal(2, created.Id);
        byte[] saved = Save(document); Validate(saved);
        string?[] ids = Xml(saved, "word/document.xml").Descendants(W + "sdtPr").Select(item => (string?)item.Element(W + "id")?.Attribute(W + "val")).ToArray();
        Assert.Equal(2, ids.Length); Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedTemplateWrappersCountTowardTheCreationLimit(bool block)
    {
        string controls = string.Concat(Enumerable.Range(1, 1024).Select(id => Control(id, "", "x", "<w:tag w:val='if:Show'/>", block: true)));
        using var input = new MemoryStream(Package(Document(controls + "<w:p/>")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        byte[] before = Save(document); var definition = new ContentControlDefinition(ContentControlKind.PlainText, initialText: "new");
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            if (block) _ = document.InsertBlockContentControl(1024, definition, TestContext.Current.CancellationToken);
            else _ = document.InsertContentControl(1024, 0, definition, TestContext.Current.CancellationToken);
        });
        Assert.StartsWith("Limit.Controls:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(document));
    }
}
