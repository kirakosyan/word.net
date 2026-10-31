using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlCreationTests
{
    private static readonly string[] FormParagraphs = { "Name: ", "Approved: ", "Choice: ", "Other: ", "Date: ", "Note: " };
    private static readonly string[][] CellRows = { new[] { "Cell" } };
    private static readonly ContentControlListItem[] Choices = { new ContentControlListItem("Alpha", "a"), new ContentControlListItem("Beta", "b") };
    private static DocumentContentControl ById(WordDocument document, int id) => document.ContentControls.Single(control => control.Id == id);

    [Fact]
    public void ProgrammaticFormCreatesAllSixKindsAndKeepsThemEditable()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraphs(FormParagraphs, cancellationToken: TestContext.Current.CancellationToken);
        var definitions = new[]
        {
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada", tag: "merge:Never"),
            new ContentControlDefinition(ContentControlKind.Checkbox, isChecked: true),
            new ContentControlDefinition(ContentControlKind.DropDownList, items: Choices, selectedValue: "b"),
            new ContentControlDefinition(ContentControlKind.ComboBox, items: Choices, initialText: "Manual"),
            new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))),
            new ContentControlDefinition(ContentControlKind.RichText, initialText: "Approved")
        };
        for (int index = 0; index < definitions.Length; index++)
        {
            DocumentContentControl control = document.InsertContentControl(index, FormParagraphs[index].Length, definitions[index], TestContext.Current.CancellationToken);
            Assert.Equal(index + 1, control.Id); Assert.Equal(definitions[index].Kind, control.Kind);
        }
        Assert.Equal("merge:Never", ById(document, 1).Tag); Assert.True(ById(document, 2).IsChecked);
        Assert.Equal("b", ById(document, 3).SelectedValue); Assert.Equal("Manual", ById(document, 4).FreeText);
        byte[] saved = Save(document); Validate(saved); XDocument xml = Xml(saved, "word/document.xml");
        Assert.Equal(6, xml.Descendants(W + "sdt").Count()); Assert.Single(xml.Descendants(W14 + "checkbox"));
        Assert.Contains("Name:AdaApproved:XChoice:BetaOther:ManualDate:2026-10-31Note:Approved", Compact(Pdf(document)), StringComparison.Ordinal);
        Assert.Contains("Name:AdaApproved:XChoice:BetaOther:ManualDate:2026-10-31Note:Approved", Compact(Xps(document)), StringComparison.Ordinal);
        WordDocument reloaded = Load(saved); Assert.Equal(6, reloaded.ContentControls.Count);
        reloaded.SetControlText(ById(reloaded, 1), "Grace", TestContext.Current.CancellationToken);
        reloaded.SetControlChecked(ById(reloaded, 2), false, TestContext.Current.CancellationToken);
        reloaded.SetControlSelection(ById(reloaded, 3), "a", TestContext.Current.CancellationToken);
        reloaded.SetControlComboText(ById(reloaded, 4), "New", TestContext.Current.CancellationToken);
        reloaded.SetControlDate(ById(reloaded, 5), ContentControlDateValue.FromInstant(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        reloaded.ReplaceControlContent(ById(reloaded, 6), new[] { new DocumentText("Done") }, TestContext.Current.CancellationToken);
        byte[] edited = Save(reloaded); Validate(edited); Assert.Equal("Grace", ById(Load(edited), 1).DisplayText);
        WordDocument view = reloaded.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Contains("Name:Grace", Compact(Pdf(view)), StringComparison.Ordinal);
    }

    [Fact]
    public void InsertionSplitsOnlyAtUnicodeScalarBoundariesAndKeepsOtherText()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("A\U0001F600B", cancellationToken: TestContext.Current.CancellationToken);
        byte[] before = Save(document);
        Assert.Throws<ArgumentException>(() => document.InsertContentControl(0, 2, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
        DocumentContentControl control = document.InsertContentControl(0, 3, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "X"), TestContext.Current.CancellationToken);
        Assert.Equal("A\U0001F600XB", Assert.Single(document.Paragraphs).Text); Assert.Equal("X", control.DisplayText);
        byte[] saved = Save(document); Validate(saved); Assert.Equal("A\U0001F600XB", Assert.Single(Load(saved).Paragraphs).Text);
    }

    [Fact]
    public void IdsAvoidExistingControlsAndHandlesBecomeStaleAfterInsertion()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "one") + "</w:p><w:p>" + Run("Second") + "</w:p><w:p>" + Run("Third") + "</w:p>";
        WordDocument document = LoadBody(body); DocumentContentControl old = ById(document, 1);
        DocumentContentControl added = document.InsertContentControl(1, 6, new ContentControlDefinition(ContentControlKind.PlainText, initialText: " new"), TestContext.Current.CancellationToken);
        Assert.Equal(2, added.Id); Assert.Throws<ArgumentException>(() => document.SetControlText(old, "stale", TestContext.Current.CancellationToken));
        Assert.Equal(3, document.InsertContentControl(2, 5, new ContentControlDefinition(ContentControlKind.RichText, initialText: "!"), TestContext.Current.CancellationToken).Id);
        Validate(Save(document));
    }

    [Fact]
    public void CreatedControlCanUseAnExistingLocalBindingOnlyWhenExplicitlyApplied()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p><w:p>" + Run("New: ") + "</w:p>";
        WordDocument document = Load(WithStore(body));
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal) { ["p"] = "urn:wordnet:controls" };
        var binding = new ContentControlBinding(StoreId, "/p:invoice/p:customer/@name", mapping);
        mapping["p"] = "urn:changed";
        DocumentContentControl created = document.InsertContentControl(1, 5, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "waiting", binding: binding), TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlBindingState.Unevaluated, created.BindingState); Assert.Equal("waiting", created.DisplayText);
        Assert.Equal("urn:wordnet:controls", created.Binding!.NamespaceMappings["p"]);
        document.ApplyControlBindings(new[] { created }, TestContext.Current.CancellationToken); Assert.Equal("Ada & Co", ById(document, 2).DisplayText);
        document.SetControlText(ById(document, 1), "Agreed", TestContext.Current.CancellationToken);
        document.SetControlText(ById(document, 2), "Agreed", TestContext.Current.CancellationToken);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("Agreed", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal("Agreed", ById(Load(saved), 2).DisplayText);
    }

    [Fact]
    public void BoundComboCannotUseFreeTextThatCollidesWithAStoredItemValue()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p><w:p>" + Run("New: ") + "</w:p>";
        WordDocument document = Load(WithStore(body)); byte[] before = Save(document);
        var binding = new ContentControlBinding(StoreId, "/p:invoice/p:customer/@name", new Dictionary<string, string> { ["p"] = "urn:wordnet:controls" });
        var definition = new ContentControlDefinition(ContentControlKind.ComboBox, initialText: "a", items: Choices, binding: binding);
        Assert.Throws<ArgumentException>(() => document.InsertContentControl(1, 5, definition, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document)); Assert.Single(document.ContentControls);
    }

    [Fact]
    public void BoundComboWithAnEmptyStoredValueCanStartWithoutASelection()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p><w:p>" + Run("New: ") + "</w:p>";
        WordDocument document = Load(WithStore(body));
        var binding = new ContentControlBinding(StoreId, "/p:invoice/p:customer/@name", new Dictionary<string, string> { ["p"] = "urn:wordnet:controls" });
        var definition = new ContentControlDefinition(ContentControlKind.ComboBox, items: new[] { new ContentControlListItem("Empty", "") }, binding: binding);
        DocumentContentControl created = document.InsertContentControl(1, 5, definition, TestContext.Current.CancellationToken);
        Assert.Null(created.SelectedValue); Assert.Null(created.FreeText); Assert.Equal(ContentControlValueState.Empty, created.State);
        Assert.Equal(ContentControlBindingState.Unevaluated, created.BindingState); Validate(Save(document));
    }

    [Fact]
    public void InvalidDefinitionsLocationsBindingsAndCancellationAreAtomic()
    {
        Assert.Throws<ArgumentException>(() => new ContentControlDefinition(ContentControlKind.RichText, items: Choices));
        Assert.Throws<ArgumentException>(() => new ContentControlDefinition(ContentControlKind.DropDownList, items: Choices, selectedValue: "missing"));
        Assert.Throws<ArgumentException>(() => new ContentControlDefinition(ContentControlKind.ComboBox, items: Choices, selectedValue: "a", initialText: "other"));
        Assert.Throws<ArgumentException>(() => new ContentControlDefinition(ContentControlKind.DropDownList, items: new[] { Choices[0], Choices[0] }));
        WordDocument document = WordDocument.Create(); document.AddParagraph("Safe", cancellationToken: TestContext.Current.CancellationToken); byte[] before = Save(document);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertContentControl(1, 0, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertContentControl(0, 5, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        var binding = new ContentControlBinding(StoreId, "//p:invoice", new Dictionary<string, string> { ["p"] = "urn:wordnet:controls" });
        Assert.Throws<InvalidDataException>(() => document.InsertContentControl(0, 0, new ContentControlDefinition(ContentControlKind.PlainText, binding: binding), TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.InsertContentControl(0, 0, new ContentControlDefinition(ContentControlKind.PlainText), cancelled.Token));
        Assert.Equal(before, Save(document)); Assert.Empty(document.ContentControls);
    }

    [Fact]
    public void ExistingProtectedOwnersCannotBeSplitOrBypassed()
    {
        string body = "<w:p>" + Run("Before ") + Control(1, "<w:text/>", "protected", "<w:lock w:val='contentLocked'/>") + Run(" after") + "</w:p>";
        WordDocument document = LoadBody(body); byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.InsertContentControl(0, 2, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document)); Assert.Single(document.ContentControls);
    }

    [Fact]
    public void SimpleCellCreationIsEditableButNestedAndMergedTablesRejectAtomically()
    {
        WordDocument simple = WordDocument.Create(); simple.AddTable(CellRows, cancellationToken: TestContext.Current.CancellationToken);
        DocumentContentControl created = simple.InsertContentControl(0, 4, new ContentControlDefinition(ContentControlKind.PlainText, initialText: " value"), TestContext.Current.CancellationToken);
        Assert.Equal(" value", created.DisplayText); byte[] saved = Save(simple); Validate(saved);
        Assert.Single(Xml(saved, "word/document.xml").Descendants(W + "tc").Single().Descendants(W + "sdt"));
        Assert.Equal("Cell value", Assert.Single(Load(saved).Paragraphs).Text);

        string nested = "<w:tbl><w:tr><w:tc><w:p>" + Run("Outer") + "</w:p><w:tbl><w:tr><w:tc><w:p>" + Run("Inner") + "</w:p></w:tc></w:tr></w:tbl></w:tc></w:tr></w:tbl>";
        WordDocument nestedDocument = LoadBody(nested); byte[] before = Save(nestedDocument);
        Assert.Throws<NotSupportedException>(() => nestedDocument.InsertContentControl(0, 0, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => nestedDocument.InsertContentControl(1, 0, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(nestedDocument));

        string merged = "<w:tbl><w:tblGrid><w:gridCol w:w='2000'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr><w:p>" + Run("Merged") + "</w:p></w:tc></w:tr></w:tbl>";
        WordDocument mergedDocument = LoadBody(merged); byte[] mergedBefore = Save(mergedDocument);
        Assert.Throws<NotSupportedException>(() => mergedDocument.InsertContentControl(0, 0, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Equal(mergedBefore, Save(mergedDocument));
    }

    [Fact]
    public void ARepeatRowCannotReceiveANewTypedControl()
    {
        string row = "<w:tr><w:tc><w:p>" + Run("Row") + "</w:p></w:tc></w:tr>";
        string repeat = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>" + row + "</w:sdtContent></w:sdt>";
        using var input = new MemoryStream(Package(Document("<w:tbl>" + repeat + "</w:tbl>")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.InsertContentControl(0, 0, new ContentControlDefinition(ContentControlKind.PlainText), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

    [Fact]
    public void InitialContentLockAppliesAfterTheInitialValueIsInstalled()
    {
        WordDocument document = WordDocument.Create(); document.AddParagraph("Value: ", cancellationToken: TestContext.Current.CancellationToken);
        DocumentContentControl created = document.InsertContentControl(0, 7,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "fixed", editLock: ContentControlLock.ContentLocked), TestContext.Current.CancellationToken);
        Assert.Equal("fixed", created.DisplayText); Assert.Equal(ContentControlLock.ContentLocked, created.Lock);
        Assert.Throws<InvalidOperationException>(() => document.SetControlText(created, "blocked", TestContext.Current.CancellationToken));
        byte[] saved = Save(document); Validate(saved); Assert.Equal(ContentControlLock.ContentLocked, Assert.Single(Load(saved).ContentControls).Lock);
    }

    [Fact]
    public void DefinitionsCopyItemsAndKeepEmptySelectionDistinctFromNoSelection()
    {
        var items = new List<ContentControlListItem> { new ContentControlListItem("Empty", ""), new ContentControlListItem("Other", "other") };
        var definition = new ContentControlDefinition(ContentControlKind.DropDownList, items: items, selectedValue: "");
        items.Clear();
        WordDocument document = WordDocument.Create(); document.AddParagraph("Choice: ", cancellationToken: TestContext.Current.CancellationToken);
        DocumentContentControl created = document.InsertContentControl(0, 8, definition, TestContext.Current.CancellationToken);
        Assert.Equal(2, created.Items.Count); Assert.Equal("", created.SelectedValue); Assert.Equal("Empty", created.DisplayText);
        byte[] saved = Save(document); Validate(saved); Assert.Equal("", Assert.Single(Load(saved).ContentControls).SelectedValue);
    }

    [Fact]
    public void MultilineAndDateOnlyDefinitionsUseTheSessionsExplicitPolicies()
    {
        WordDocument document = LoadBody("<w:p>" + Run("Text: ") + "</w:p><w:p>" + Run("Date: ") + "</w:p><w:p>" + Run("Other: ") + "</w:p>", mode: ContentControlDateMode.DateOnly);
        DocumentContentControl text = document.InsertContentControl(0, 6,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "one\ntwo", multiLine: true), TestContext.Current.CancellationToken);
        Assert.Equal("one\ntwo", text.DisplayText);
        DocumentContentControl date = document.InsertContentControl(1, 6,
            new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromDate(2026, 10, 31)), TestContext.Current.CancellationToken);
        Assert.Equal("2026-10-31", date.DisplayText);
        byte[] saved = Save(document); Validate(saved);
        WordDocument reloaded = Load(saved, mode: ContentControlDateMode.DateOnly);
        Assert.Equal("one\ntwo", ById(reloaded, 1).DisplayText); Assert.Equal("2026-10-31", ById(reloaded, 2).DisplayText);
        byte[] before = Save(reloaded);
        Assert.Throws<ArgumentException>(() => reloaded.InsertContentControl(2, 0,
            new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(reloaded));
    }

    [Fact]
    public void GlobalControlLimitPreventsInsertionWithoutChangingExistingOwners()
    {
        string controls = string.Concat(Enumerable.Range(0, 1024).Select(id => Control(id, "<w:text/>", "x", block: true)));
        WordDocument document = LoadBody(controls + "<w:p>" + Run("Target") + "</w:p>"); byte[] before = Save(document);
        Assert.Throws<InvalidDataException>(() => document.InsertContentControl(1024, 0,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "extra"), TestContext.Current.CancellationToken));
        Assert.Equal(1024, document.ContentControls.Count); Assert.Equal(before, Save(document));
    }
}
