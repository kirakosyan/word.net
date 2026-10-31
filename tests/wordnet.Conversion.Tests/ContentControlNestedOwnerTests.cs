using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlNestedOwnerTests
{
    private static readonly string[] InlineOrder = { "r", "sdt", "r" };
    private static readonly string[] ControlOrder = { "1", "2", "3", "4" };
    private static string Parent(string child, string metadata = "", string type = "<w:richText/>") =>
        Control(1, type, "", metadata, block: true, content: Run("Before ") + child + Run(" after"));
    private static DocumentContentControl ById(WordDocument document, int id) => document.ContentControls.Single(control => control.Id == id);

    [Fact]
    public void WordDerivedNestedFixtureMatchesIndependentTextOrderAndKeepsBothChildren()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "NestedOwners");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "nested.docx")); Validate(source);
        WordDocument document = Load(source); Assert.Equal(3, document.ContentControls.Count);
        Assert.Null(ById(document, 26134427).ParentId);
        Assert.Equal(26134427, ById(document, 25057961).ParentId); Assert.Equal(26134427, ById(document, 26000000).ParentId);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "nested.pdf")));
        using PdfDocument actual = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.Equal("ThisisanSdtBlock.SdtRunandPlain.", Compact(reference.GetPage(1).Text));
        Assert.Equal(Compact(reference.GetPage(1).Text), Compact(actual.GetPage(1).Text));
        foreach (PdfDocument pdf in new[] { reference, actual })
        {
            var words = pdf.GetPage(1).GetWords().ToArray(); Assert.True(words.Length >= 6);
            for (int index = 1; index < words.Length; index++)
                Assert.True(words[index - 1].BoundingBox.Left < words[index].BoundingBox.Left);
        }
        document.ReplaceControlContent(ById(document, 25057961), new[] { new DocumentText("Updated") }, TestContext.Current.CancellationToken);
        document.SetControlText(ById(document, 26000000), "Value", TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved); WordDocument reloaded = Load(saved);
        Assert.Equal("Updated", ById(reloaded, 25057961).DisplayText); Assert.Equal("Value", ById(reloaded, 26000000).DisplayText);
        Assert.Equal("ThisisanSdtBlock.UpdatedandValue.", Compact(Pdf(reloaded)));
    }

    [Fact]
    public void NestedInlineEditKeepsWrapperOrderIdentityAndDetachedDisplay()
    {
        string child = Control(2, "<w:text/>", "old");
        byte[] source = Package(Document(Parent(child))); Validate(source);
        WordDocument document = Load(source); DocumentContentControl parent = ById(document, 1), original = ById(document, 2);
        Assert.Null(parent.ParentId); Assert.Equal(1, original.ParentId); Assert.Equal("Before old after", parent.DisplayText);
        document.SetControlText(original, "new & literal", TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => document.SetControlText(original, "stale", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => document.ReplaceControlParagraphs(parent, Array.Empty<ContentControlParagraph>(), TestContext.Current.CancellationToken));
        Assert.Equal("Before new & literal after", ById(document, 1).DisplayText); Assert.Equal(1, ById(document, 2).ParentId);
        byte[] saved = Save(document); Validate(saved);
        XElement outer = Xml(saved, "word/document.xml").Descendants(W + "sdt").First();
        XElement paragraph = outer.Element(W + "sdtContent")!.Element(W + "p")!;
        Assert.Equal(InlineOrder, paragraph.Elements().Where(element => element.Name != W + "pPr").Select(element => element.Name.LocalName));
        Assert.Equal("2", (string?)paragraph.Element(W + "sdt")!.Element(W + "sdtPr")!.Element(W + "id")!.Attribute(W + "val"));
        WordDocument reloaded = Load(saved); Assert.Equal(1, ById(reloaded, 2).ParentId); Assert.Equal("new & literal", ById(reloaded, 2).DisplayText);
        Assert.Equal("Beforenew&literalafter", Compact(Pdf(document))); Assert.Equal("Beforenew&literalafter", Compact(Xps(document)));
        WordDocument view = document.CreateControlDisplayView(out ConversionResult result, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Control.OutputLoss");
        Assert.Equal("Beforenew&literalafter", Compact(Pdf(view))); Assert.Equal(2, document.ContentControls.Count);
    }

    [Theory]
    [InlineData("unlocked", true)]
    [InlineData("sdtLocked", true)]
    [InlineData("contentLocked", false)]
    [InlineData("sdtContentLocked", false)]
    public void ChildMutationHonorsTheParentsContentLock(string parentLock, bool editable)
    {
        string child = Control(2, Checkbox(), "-");
        WordDocument document = LoadBody(Parent(child, "<w:lock w:val='" + parentLock + "'/>")); byte[] before = Save(document);
        if (editable)
        {
            document.SetControlChecked(ById(document, 2), true, TestContext.Current.CancellationToken);
            Assert.True(ById(document, 2).IsChecked); Assert.Contains("X", Pdf(document), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Control.Locked", Assert.Throws<InvalidOperationException>(() => document.SetControlChecked(ById(document, 2), true,
                TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
            Assert.Equal(before, Save(document)); Assert.False(ById(document, 2).IsChecked);
        }
        Validate(Save(document));
    }

    [Fact]
    public void ParentReplacementAndChildOwnLockCannotBypassProtectedOwners()
    {
        string child = Control(2, "<w:text/>", "safe", "<w:lock w:val='contentLocked'/>");
        WordDocument document = LoadBody(Parent(child)); byte[] before = Save(document);
        Assert.Contains("Control.ProtectedOwner", Assert.Throws<NotSupportedException>(() => document.ReplaceControlParagraphs(ById(document, 1),
            new[] { new ContentControlParagraph(new[] { new DocumentText("erase") }) }, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Contains("Control.Locked", Assert.Throws<InvalidOperationException>(() => document.SetControlText(ById(document, 2), "erase",
            TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(document));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.ReplaceControlParagraphs(ById(document, 1), Array.Empty<ContentControlParagraph>(), cancellation.Token));
        Assert.Equal(before, Save(document));
    }

    [Fact]
    public void ParentValueStateFollowsTheCurrentNestedText()
    {
        string child = Control(2, "<w:text/>", "initial");
        WordDocument document = LoadBody(Control(1, "<w:richText/>", "", block: true, content: child));
        Assert.Equal(ContentControlValueState.Value, ById(document, 1).State);
        document.SetControlText(ById(document, 2), "", TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlValueState.Empty, ById(document, 1).State);
        document.SetControlText(ById(document, 2), "again", TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlValueState.Value, ById(document, 1).State); Validate(Save(document));
    }

    [Fact]
    public void NestedDateInSimpleCellRefreshesParentStateAndRemainsEditable()
    {
        string date = "<w:date w:fullDate='2000-01-01T00:00:00Z'><w:dateFormat w:val='yyyy-MM-dd'/><w:lid w:val='en-US'/><w:calendar w:val='gregorian'/></w:date>";
        string child = Control(2, date, "");
        string parent = Control(1, "<w:richText/>", "", block: true, content: child);
        string table = "<w:tbl><w:tblPr><w:tblW w:w='4000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>"
            + "<w:tr><w:tc><w:tcPr><w:tcW w:w='4000' w:type='dxa'/></w:tcPr>" + parent + "</w:tc></w:tr></w:tbl>";
        byte[] source = Package(Document(table)); Validate(source); WordDocument document = Load(source);
        Assert.Equal("2000-01-01", ById(document, 1).DisplayText); Assert.Equal(ContentControlValueState.Value, ById(document, 1).State);
        document.SetControlDate(ById(document, 2), ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        Assert.Equal("2026-10-31", ById(document, 1).DisplayText); Assert.Equal(1, ById(document, 2).ParentId);
        byte[] saved = Save(document); Validate(saved); Assert.Equal("2026-10-31", ById(Load(saved), 2).DisplayText);
        Assert.Contains("2026-10-31", Pdf(document), StringComparison.Ordinal);
    }

    [Fact]
    public void NestedChildCanRestoreItsLocalPlaceholderWithoutReplacingTheParent()
    {
        string metadata = "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
        string child = Control(2, "<w:text/>", "Choose", metadata);
        WordDocument document = Load(WithGlossary(Parent(child)));
        document.SetControlText(ById(document, 2), "written", TestContext.Current.CancellationToken);
        Assert.Equal("Before written after", ById(document, 1).DisplayText);
        document.ShowControlPlaceholder(ById(document, 2), TestContext.Current.CancellationToken);
        Assert.Equal("Before Choose after", ById(document, 1).DisplayText); Assert.True(ById(document, 2).ShowingPlaceholder);
        byte[] saved = Save(document); Validate(saved); Assert.True(ById(Load(saved), 2).ShowingPlaceholder);
    }

    [Fact]
    public void NestedChildBindingIsExplicitAndKeepsItsParentAndLocalStore()
    {
        string child = Control(2, "<w:text/>", "old", Binding("/p:invoice/p:customer/@name"));
        WordDocument document = Load(WithStore(Parent(child)));
        document.ApplyControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken);
        Assert.Equal("Before Ada & Co after", ById(document, 1).DisplayText);
        document.SetControlText(ById(document, 2), "New & Co", TestContext.Current.CancellationToken);
        using var blocked = new MemoryStream(); blocked.WriteByte(42);
        Assert.Throws<InvalidOperationException>(() => document.Save(blocked, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, blocked.ToArray());
        document.WriteBackControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("New & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal(1, ById(Load(saved), 2).ParentId);
    }

    [Fact]
    public void DivergentNestedBindingsRejectAtomicallyAndCanBeReapplied()
    {
        string first = Control(2, "<w:text/>", "one", Binding("/p:invoice/p:customer/@name"));
        string second = Control(3, "<w:text/>", "two", Binding("/p:invoice/p:customer/@name"));
        WordDocument document = Load(WithStore(Parent(first + Run(" / ") + second)));
        document.SetControlText(ById(document, 2), "first", TestContext.Current.CancellationToken);
        document.SetControlText(ById(document, 3), "second", TestContext.Current.CancellationToken);
        DocumentContentControl[] selected = document.ContentControls.Where(control => control.ParentId == 1).ToArray();
        Assert.Contains("Binding.Conflict", Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(selected,
            TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal("Before first / second after", ById(document, 1).DisplayText);
        Assert.All(selected, control => Assert.Equal(ContentControlBindingState.Dirty, control.BindingState));
        document.ApplyControlBindings(selected, TestContext.Current.CancellationToken);
        Assert.Equal("Before Ada & Co / Ada & Co after", ById(document, 1).DisplayText);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("Ada & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
    }

    [Fact]
    public void OrdinaryLoadingStillUsesCachedTextWithoutCreatingTypedOwners()
    {
        byte[] source = Package(Document(Parent(Control(2, "<w:text/>", "cached"))));
        using var input = new MemoryStream(source);
        WordDocument ordinary = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.Empty(ordinary.ContentControls);
        Assert.Equal("Before cached after", Assert.Single(ordinary.Paragraphs).Text);
    }

    [Fact]
    public void LockedParentAlsoBlocksNestedBindingOperations()
    {
        string child = Control(2, "<w:text/>", "old", Binding("/p:invoice/p:customer/@name"));
        WordDocument document = Load(WithStore(Parent(child, "<w:lock w:val='contentLocked'/>"))); byte[] before = Save(document);
        Assert.Throws<InvalidOperationException>(() => document.ApplyControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => document.DetachControlBindings(new[] { ById(document, 2) }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
    }

    [Fact]
    public void MultipleChildrenRetainTheirOrderAndSelectionIndependently()
    {
        string children = Control(2, Choices, "Alpha") + Run(" | ") + Control(3, "<w:comboBox><w:listItem w:value='a'/></w:comboBox>", "a")
            + Run(" | ") + Control(4, "<w:richText/>", "rich");
        WordDocument document = LoadBody(Parent(children));
        document.SetControlSelection(ById(document, 2), "b", TestContext.Current.CancellationToken);
        document.SetControlComboText(ById(document, 3), "free", TestContext.Current.CancellationToken);
        document.ReplaceControlContent(ById(document, 4), new[] { new DocumentText("revised") }, TestContext.Current.CancellationToken);
        Assert.Equal("Before Beta | free | revised after", ById(document, 1).DisplayText);
        Assert.All(document.ContentControls.Where(control => control.Id != 1), control => Assert.Equal(1, control.ParentId));
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal(ControlOrder, Xml(saved, "word/document.xml").Descendants(W + "sdtPr").Elements(W + "id").Select(id => (string?)id.Attribute(W + "val")));
        Assert.Equal("BeforeBeta|free|revisedafter", Compact(Pdf(Load(saved))));
    }

    [Fact]
    public void UnqualifiedParentAndDeeperOwnershipAreRejected()
    {
        string child = Control(2, "<w:text/>", "value");
        Assert.Throws<NotSupportedException>(() => LoadBody(Parent(child, "<w:showingPlcHdr/>")));
        Assert.Throws<NotSupportedException>(() => Load(WithStore(Parent(child, Binding("/p:invoice/p:text")))));
        Assert.Throws<NotSupportedException>(() => LoadBody(Parent(child, type: "<w:text/>")));
        string nestedInline = Control(2, "<w:richText/>", "", content: Control(3, "<w:text/>", "too deep"));
        Assert.Throws<NotSupportedException>(() => LoadBody(Parent(nestedInline)));
        Assert.Throws<InvalidDataException>(() => LoadBody(Parent(Control(1, "<w:text/>", "duplicate"))));
    }
}
