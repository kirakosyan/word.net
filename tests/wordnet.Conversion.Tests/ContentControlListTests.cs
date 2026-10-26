using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlListTests
{
    private static readonly string[] ExpectedValues = { "a", "", "b<" };
    private static readonly string[] ExpectedLabels = { "Display A", "Empty", "B & <" };
    [Fact]
    public void OrderedItemsStoredValuesFreeTextAndCellOwnersRemainEditable()
    {
        byte[] source = Package(Fixture("choices.xml")); Validate(source); WordDocument document = Load(source);
        var dropdown = document.ContentControls.Single(c => c.Id == 10); var combo = document.ContentControls.Single(c => c.Id == 11);
        Assert.Equal(ExpectedValues, dropdown.Items.Select(i => i.Value)); Assert.Equal(ExpectedLabels, dropdown.Items.Select(i => i.Label));
        Assert.Equal("a", dropdown.SelectedValue); Assert.Equal("custom", combo.FreeText); Assert.Equal(ContentControlValueState.FreeText, combo.State);
        document.SetControlSelection(dropdown, "", TestContext.Current.CancellationToken);
        var emptyValue = document.ContentControls.Single(c => c.Id == 10); Assert.Equal("", emptyValue.SelectedValue); Assert.Equal("Empty", emptyValue.DisplayText); Assert.Equal(ContentControlValueState.Value, emptyValue.State);
        document.SetControlComboText(document.ContentControls.Single(c => c.Id == 11), "literal <& Unicode Ω", TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word);
        var xml = Xml(word, "word/document.xml"); var items = xml.Descendants(W + "dropDownList").Single().Elements(W + "listItem").ToArray();
        Assert.Equal(ExpectedValues, items.Select(i => (string?)i.Attribute(W + "value")));
        Assert.Equal("999", (string?)xml.Descendants(W + "dropDownList").Single().Attribute(W + "lastValue"));
        Assert.Equal("Emptyliteral<&UnicodeΩ", Compact(Pdf(document))); Assert.Equal("Emptyliteral<&UnicodeΩ", Compact(Xps(document)));
        WordDocument reloaded = Load(word); Assert.Equal("", reloaded.ContentControls.Single(c => c.Id == 10).SelectedValue); Assert.Equal("literal <& Unicode Ω", reloaded.ContentControls.Single(c => c.Id == 11).FreeText);
        document.SetControlSelection(document.ContentControls.Single(c => c.Id == 10), null, TestContext.Current.CancellationToken);
        var cleared = document.ContentControls.Single(c => c.Id == 10); Assert.Null(cleared.SelectedValue); Assert.False(cleared.ShowingPlaceholder); Assert.Equal(ContentControlValueState.Empty, cleared.State);
    }
    [Fact]
    public void InvalidSelectionAndAmbiguousComboTextAreAtomic()
    {
        WordDocument document = Load(Package(Fixture("choices.xml"))); var handle = document.ContentControls.Single(c => c.Id == 10);
        string before = Xml(Save(document), "word/document.xml").ToString();
        Assert.Throws<ArgumentException>(() => document.SetControlSelection(handle, "A", TestContext.Current.CancellationToken));
        Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString());
        document.SetControlSelection(handle, "b<", TestContext.Current.CancellationToken);
        var combo = document.ContentControls.Single(c => c.Id == 11);
        Assert.Throws<ArgumentException>(() => document.SetControlComboText(combo, "One", TestContext.Current.CancellationToken));
        document.SetControlSelection(combo, "1", TestContext.Current.CancellationToken); Assert.Equal("One", document.ContentControls.Single(c => c.Id == 11).DisplayText);
        document.SetControlComboText(document.ContentControls.Single(c => c.Id == 11), "", TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlValueState.Empty, document.ContentControls.Single(c => c.Id == 11).State);
    }
    [Theory]
    [InlineData("<w:listItem w:displayText='Alpha' w:value='a'/><w:listItem w:displayText='Other' w:value='a'/>")]
    [InlineData("<w:listItem w:displayText='Same' w:value='a'/><w:listItem w:displayText='Same' w:value='b'/>")]
    [InlineData("<w:listItem w:displayText='' w:value='a'/>")]
    [InlineData("<w:listItem w:value=''/>")]
    [InlineData("<w:listItem w:displayText='Alpha'/>")]
    public void DuplicateMissingAndEmptyItemIdentityIsRejected(string items)
    {
        string control = Control(1, "<w:dropDownList>" + items + "</w:dropDownList>", "Alpha");
        Assert.Throws<System.IO.InvalidDataException>(() => LoadBody("<w:p>" + control + "</w:p>", new ConversionOptions(FidelityMode.Lenient)));
    }
    [Fact]
    public void UnmatchedDropdownCacheIsUnresolvedAndNeverSelectsItemZero()
    {
        WordDocument document = LoadBody("<w:p>" + Control(1, Choices, "Nonmember") + "</w:p>");
        var snapshot = Assert.Single(document.ContentControls); Assert.Null(snapshot.SelectedValue); Assert.Equal(ContentControlValueState.Unresolved, snapshot.State);
        Assert.Contains(document.Diagnostics, d => d.Code == "Control.ChoiceUnresolved"); Assert.Equal("Nonmember", Pdf(document));
        byte[] word = Save(document); Validate(word); Assert.Equal("Nonmember", Assert.Single(Load(word).ContentControls).DisplayText);
        document.SetControlSelection(snapshot, "b", TestContext.Current.CancellationToken); Assert.Equal("Beta", Assert.Single(document.ContentControls).DisplayText);
    }
    [Fact]
    public void OmittedLabelsUseValuesAndAnEmptyListCanStayEmpty()
    {
        string items = "<w:dropDownList><w:listItem w:value='Only'/></w:dropDownList>";
        WordDocument document = LoadBody("<w:p>" + Control(1, items, "Only") + Control(2, "<w:dropDownList/>", "") + "</w:p>");
        Assert.Null(document.ContentControls[0].Items[0].DisplayText); Assert.Equal("Only", document.ContentControls[0].SelectedValue);
        Assert.Empty(document.ContentControls[1].Items); Assert.Equal(ContentControlValueState.Empty, document.ContentControls[1].State); Validate(Save(document));
    }
    [Fact]
    public void LocalGlossaryAndCacheOnlyPlaceholdersSurviveExplicitSelectionAndReset()
    {
        string metadata = "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
        WordDocument document = Load(WithGlossary("<w:p>" + Control(1, Choices, "Choose", metadata) + "</w:p>"));
        var placeholder = Assert.Single(document.ContentControls); Assert.True(placeholder.ShowingPlaceholder); Assert.Null(placeholder.SelectedValue); Assert.Equal("Choose", Pdf(document));
        document.SetControlSelection(placeholder, "a", TestContext.Current.CancellationToken); Assert.Equal("Alpha", Pdf(document));
        byte[] word = Save(document); Validate(word); Assert.Equal("Choose", Xml(word, "word/glossary/document.xml").Descendants(W + "t").Single().Value);
        Assert.Contains(Xml(word, "word/_rels/document.xml.rels").Root!.Elements(), r => ((string?)r.Attribute("Type"))?.EndsWith("/glossaryDocument", StringComparison.Ordinal) == true);
        document.ShowControlPlaceholder(Assert.Single(document.ContentControls), TestContext.Current.CancellationToken); Assert.True(Assert.Single(document.ContentControls).ShowingPlaceholder); Assert.Equal("Choose", Pdf(document));
        Validate(Save(document)); Assert.True(Assert.Single(Load(Save(document)).ContentControls).ShowingPlaceholder);
        WordDocument cached = LoadBody("<w:p>" + Control(1, Choices, "Cached prompt", "<w:showingPlcHdr/>") + "</w:p>");
        cached.SetControlSelection(Assert.Single(cached.ContentControls), "b", TestContext.Current.CancellationToken);
        cached.ShowControlPlaceholder(Assert.Single(cached.ContentControls), TestContext.Current.CancellationToken); Assert.Equal("Cached prompt", Assert.Single(cached.ContentControls).DisplayText);
    }
    [Fact]
    public void MultipleInlineEditsDoNotUseStaleRunSliceOffsets()
    {
        string firstRuns = Run("Al") + Run("pha");
        WordDocument document = LoadBody("<w:p>" + Control(1, Choices, "", content: firstRuns) + Run("|") + Control(2, Choices, "Alpha") + "</w:p>");
        document.SetControlSelection(document.ContentControls[0], "b", TestContext.Current.CancellationToken);
        document.SetControlSelection(document.ContentControls[1], "b", TestContext.Current.CancellationToken);
        Assert.Equal("Beta|Beta", Pdf(document)); Validate(Save(document));
    }
}
