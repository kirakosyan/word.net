using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlFragmentTests
{
    private static readonly string[] DefinitionLabels = { "Plain: ", "Check: ", "List: ", "Combo: ", "Date: ", "Rich: " };
    private static string Bookmark(string name, int id) => "<w:p><w:bookmarkStart w:id='" + id + "' w:name='" + name + "'/>" + Run("Insert") + "<w:bookmarkEnd w:id='" + id + "'/></w:p>";
    private static string TargetBody(bool cell)
    {
        string host = "<w:p>" + Control(1, "<w:text/>", "host", Binding("/p:invoice/p:customer/@name") + "<w:tag w:val='host'/>") + "</w:p>";
        string slots = Bookmark("one", 1) + Bookmark("two", 2);
        return host + (cell ? "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4000'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='4000'/></w:tcPr>" + slots + "</w:tc></w:tr></w:tbl>" : slots);
    }
    private static WordDocument Fragment()
    {
        string bound = Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name") + "<w:tag w:val='bound'/>");
        string child = Control(3, "<w:text/>", "child", "<w:tag w:val='child'/>");
        string parent = Control(2, "<w:richText/>", "", "<w:tag w:val='parent'/>", block: true, content: Run("Before ") + child + Run(" after"));
        return Load(WithStore("<w:p>" + bound + "</w:p>" + parent), new ConversionOptions(FidelityMode.Lenient));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedFragmentsCopyNestedOwnersAndIsolateLocalBindingStores(bool cell)
    {
        WordDocument source = Fragment(); byte[] sourceBefore = Save(source);
        byte[] hostBytes = WithStore(TargetBody(cell));
        WordDocument target = Load(hostBytes, new ConversionOptions(FidelityMode.Lenient));
        if (cell) target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken);
        else target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        DocumentContentControl stale = target.ContentControls.Single(control => control.Tag == "bound");
        if (cell) target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken);
        else target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken);
        Assert.Equal(sourceBefore, Save(source)); Assert.Equal(7, target.ContentControls.Count);
        Assert.Equal(7, target.ContentControls.Select(control => control.Id).Distinct().Count());
        Assert.Throws<ArgumentException>(() => target.SetControlText(stale, "stale", TestContext.Current.CancellationToken));
        DocumentContentControl[] parents = target.ContentControls.Where(control => control.Tag == "parent").ToArray();
        DocumentContentControl[] children = target.ContentControls.Where(control => control.Tag == "child").ToArray();
        Assert.Equal(2, parents.Length); Assert.Equal(parents.Select(control => (int?)control.Id), children.Select(control => control.ParentId));
        Assert.All(parents, control => Assert.Equal("Before child after", control.DisplayText));
        DocumentContentControl[] bound = target.ContentControls.Where(control => control.Tag == "bound").ToArray();
        Assert.All(bound, control => Assert.Equal(ContentControlBindingState.Unevaluated, control.BindingState));
        string[] stores = target.ContentControls.Where(control => control.Binding != null).Select(control => control.Binding!.StoreItemId).ToArray();
        Assert.Equal(3, stores.Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.Equal(StoreId, stores[0]);

        byte[] inserted = Save(target); Validate(inserted);
        WordDocument mirror = Load(hostBytes, new ConversionOptions(FidelityMode.Lenient));
        if (cell) { mirror.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken); mirror.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken); }
        else { mirror.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); mirror.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken); }
        Assert.Equal(inserted, Save(mirror));
        using (var zip = new ZipArchive(new MemoryStream(inserted)))
        {
            Assert.NotNull(zip.GetEntry("customXml/item1.xml"));
            Assert.NotNull(zip.GetEntry("customXml/wordnet-fragment-1.xml"));
            Assert.NotNull(zip.GetEntry("customXml/wordnet-fragment-2.xml"));
        }
        target.ApplyControlBindings(target.ContentControls.Where(control => control.Binding != null), TestContext.Current.CancellationToken);
        Assert.All(target.ContentControls.Where(control => control.Binding != null), control => Assert.Equal("Ada & Co", control.DisplayText));
        target.SetControlText(target.ContentControls.First(control => control.Tag == "bound"), "Edited & local", TestContext.Current.CancellationToken);
        target.WriteBackControlBindings(new[] { target.ContentControls.First(control => control.Tag == "bound") }, TestContext.Current.CancellationToken);
        Assert.Equal("Ada & Co", target.ContentControls.Last(control => control.Tag == "bound").DisplayText);
        byte[] saved = Save(target); Validate(saved);
        XName customer = XName.Get("customer", "urn:wordnet:controls");
        Assert.Equal("Ada & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(customer).Single().Attribute("name"));
        Assert.Equal("Edited & local", (string?)Xml(saved, "customXml/wordnet-fragment-1.xml").Descendants(customer).Single().Attribute("name"));
        Assert.Equal("Ada & Co", (string?)Xml(saved, "customXml/wordnet-fragment-2.xml").Descendants(customer).Single().Attribute("name"));
        WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal(7, reopened.ContentControls.Count); Assert.Equal(2, reopened.ContentControls.Count(control => control.ParentId.HasValue));
        Assert.Equal("Edited & local", reopened.ContentControls.First(control => control.Tag == "bound").DisplayText);
        Assert.Contains("Edited&local", Compact(Pdf(reopened)), StringComparison.Ordinal);
        Assert.Equal(sourceBefore, Save(source));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("cell")]
    public void StructuralFragmentsKeepWrapperInBodyButRejectNestedCellInsertion(string owner)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "StructuralOwners", owner + ".docx");
        WordDocument source = Load(File.ReadAllBytes(path), new ConversionOptions(FidelityMode.Lenient));
        WordDocument body = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        body.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        byte[] saved = Save(body); Validate(saved); WordDocument reloaded = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Single(reloaded.ContentControls, control => control.Owner == "body table " + owner);
        Assert.Equal(2, reloaded.ContentControls.Select(control => control.Id).Distinct().Count());
        WordDocument cell = Load(WithStore(TargetBody(true)), new ConversionOptions(FidelityMode.Lenient)); byte[] before = Save(cell), sourceBefore = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => cell.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.ControlOwner:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(cell)); Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void ProgrammaticallyCreatedKindsRemainEditableAfterFragmentInsertion()
    {
        WordDocument source = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        source.AddParagraphs(DefinitionLabels, cancellationToken: TestContext.Current.CancellationToken);
        var items = new[] { new ContentControlListItem("Alpha", "a"), new ContentControlListItem("Beta", "b") };
        var definitions = new[]
        {
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada", tag: "plain"),
            new ContentControlDefinition(ContentControlKind.Checkbox, isChecked: false, tag: "check"),
            new ContentControlDefinition(ContentControlKind.DropDownList, items: items, selectedValue: "a", tag: "list"),
            new ContentControlDefinition(ContentControlKind.ComboBox, items: items, initialText: "Manual", tag: "combo"),
            new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), tag: "date"),
            new ContentControlDefinition(ContentControlKind.RichText, initialText: "Note", tag: "rich")
        };
        for (int index = 0; index < definitions.Length; index++)
            source.InsertContentControl(index, source.Paragraphs[index].Text.Length, definitions[index], TestContext.Current.CancellationToken);
        byte[] sourceBefore = Save(source); WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        Assert.Equal(7, target.ContentControls.Count); Assert.Equal(7, target.ContentControls.Select(control => control.Id).Distinct().Count());
        DocumentContentControl Find(string tag) => target.ContentControls.Single(control => control.Tag == tag);
        target.SetControlText(Find("plain"), "Bea", TestContext.Current.CancellationToken);
        target.SetControlChecked(Find("check"), true, TestContext.Current.CancellationToken);
        target.SetControlSelection(Find("list"), "b", TestContext.Current.CancellationToken);
        target.SetControlComboText(Find("combo"), "Other", TestContext.Current.CancellationToken);
        target.SetControlDate(Find("date"), ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        target.ReplaceControlContent(Find("rich"), new[] { new DocumentText("Updated") }, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved); WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal("Bea", reopened.ContentControls.Single(control => control.Tag == "plain").DisplayText);
        Assert.True(reopened.ContentControls.Single(control => control.Tag == "check").IsChecked);
        Assert.Equal("Beta", reopened.ContentControls.Single(control => control.Tag == "list").DisplayText);
        Assert.Equal("Other", reopened.ContentControls.Single(control => control.Tag == "combo").DisplayText);
        Assert.Equal("2026-10-31", reopened.ContentControls.Single(control => control.Tag == "date").DisplayText);
        Assert.Equal("Updated", reopened.ContentControls.Single(control => control.Tag == "rich").DisplayText);
        Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void WordDerivedNestedFragmentPreservesItsChildrenAndStaticText()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "NestedOwners", "nested.docx");
        WordDocument source = Load(File.ReadAllBytes(path), new ConversionOptions(FidelityMode.Lenient)); byte[] sourceBefore = Save(source);
        WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved); WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal(4, reopened.ContentControls.Count); Assert.Equal(2, reopened.ContentControls.Count(control => control.ParentId.HasValue));
        Assert.Contains("ThisisanSdtBlock.SdtRunandPlain.", Compact(Pdf(reopened)), StringComparison.Ordinal);
        Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void ImportedChildCannotBypassItsParentContentLock()
    {
        string child = Control(2, "<w:text/>", "child", "<w:tag w:val='child'/>");
        string parent = Control(1, "<w:richText/>", "", "<w:lock w:val='contentLocked'/>", block: true, content: child);
        WordDocument source = LoadBody(parent); WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); byte[] before = Save(target);
        DocumentContentControl nested = target.ContentControls.Single(control => control.Tag == "child");
        var error = Assert.Throws<InvalidOperationException>(() => target.SetControlText(nested, "bypass", TestContext.Current.CancellationToken));
        Assert.StartsWith("Control.Locked:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(target));
    }

    [Fact]
    public void ImportedCachedPlaceholderCanBeRestoredWithoutAffectingItsSource()
    {
        string metadata = "<w:showingPlcHdr/><w:tag w:val='prompt'/>";
        WordDocument source = LoadBody("<w:p>" + Control(5, "<w:text/>", "Choose", metadata) + "</w:p>"); byte[] sourceBefore = Save(source);
        WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        target.SetControlText(target.ContentControls.Single(control => control.Tag == "prompt"), "Written", TestContext.Current.CancellationToken);
        target.ShowControlPlaceholder(target.ContentControls.Single(control => control.Tag == "prompt"), TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved);
        DocumentContentControl restored = Load(saved, new ConversionOptions(FidelityMode.Lenient)).ContentControls.Single(control => control.Tag == "prompt");
        Assert.True(restored.ShowingPlaceholder); Assert.Equal("Choose", restored.DisplayText); Assert.Equal(sourceBefore, Save(source));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuralTableOwnersProtectOtherCellBookmarks(bool rowControl)
    {
        string cell = "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2000'/></w:tcPr><w:p>" + Run("A") + "</w:p></w:tc>";
        string wrapped = Control(7, "<w:richText/>", "", content: rowControl ? "<w:tr>" + cell + cell + "</w:tr>" : cell);
        string destinationCell = "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2000'/></w:tcPr>" + Bookmark("one", 1) + "</w:tc>";
        string rows = rowControl ? wrapped + "<w:tr>" + destinationCell + cell + "</w:tr>" : "<w:tr>" + wrapped + destinationCell + "</w:tr>";
        string table = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4000'/></w:tblPr><w:tblGrid><w:gridCol w:w='2000'/><w:gridCol w:w='2000'/></w:tblGrid>" + rows + "</w:tbl>";
        byte[] input = Package(Document(table)); Validate(input); WordDocument target = Load(input, new ConversionOptions(FidelityMode.Lenient));
        WordDocument source = Fragment(); byte[] before = Save(target), sourceBefore = Save(source);
        var error = Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.Placeholder:", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(target)); Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void TypedFragmentsRejectReportOwnersAndRespectResourceLimits()
    {
        string report = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent><w:p>" + Run("Item") + "</w:p></w:sdtContent></w:sdt>";
        using var input = new MemoryStream(Package(Document(report + Bookmark("one", 1))));
        WordDocument target = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        WordDocument source = Fragment(); byte[] before = Save(target), sourceBefore = Save(source);
        var reportError = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceStructure:", reportError.Message, StringComparison.Ordinal); Assert.Equal(before, Save(target));
        WordDocument limited = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 128));
        limited.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        limited.AddBookmark("one", 0, 0, 6, TestContext.Current.CancellationToken); byte[] limitedBefore = Save(limited);
        Assert.Throws<InvalidDataException>(() => limited.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.Equal(limitedBefore, Save(limited)); Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void TypedFragmentResourcesRestylingAndCancellationRejectAtomically()
    {
        string metadata = "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
        WordDocument glossary = Load(WithGlossary("<w:p>" + Control(3, "<w:text/>", "Choose", metadata) + "</w:p>"));
        WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient));
        byte[] before = Save(target), beforeGlossary = Save(glossary);
        var resources = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("one", glossary, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.ControlResources:", resources.Message, StringComparison.Ordinal);
        WordDocument source = Fragment(); byte[] sourceBefore = Save(source);
        var options = new FragmentInsertionOptions { Style = CompositionStyle.UseDestination };
        var restyle = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("one", source, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Control.Restyle:", restyle.Message, StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => target.InsertFragmentAtBookmark("one", source, cancelled.Token));
        Assert.Equal(before, Save(target)); Assert.Equal(beforeGlossary, Save(glossary)); Assert.Equal(sourceBefore, Save(source));
    }

    [Fact]
    public void TypedFragmentDatePoliciesMustMatch()
    {
        string date = "<w:date w:fullDate='2024-02-29T00:00:00Z'><w:dateFormat w:val='yyyy-MM-dd'/><w:lid w:val='en-US'/><w:calendar w:val='gregorian'/></w:date>";
        WordDocument source = Load(Package(Document("<w:p>" + Control(1, date, "2024-02-29") + "</w:p>")), mode: ContentControlDateMode.DateOnly);
        WordDocument target = Load(WithStore(TargetBody(false)), new ConversionOptions(FidelityMode.Lenient)); byte[] before = Save(target), sourceBefore = Save(source);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.ControlDateMode:", error.Message, StringComparison.Ordinal); Assert.Equal(before, Save(target)); Assert.Equal(sourceBefore, Save(source));
    }
}
