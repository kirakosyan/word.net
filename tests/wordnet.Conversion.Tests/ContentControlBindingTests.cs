using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlBindingTests
{
    private static readonly string[] AppliedValues = { "X", "Ada & Co", "Beta", "<merge:Never> & literal", "2024-02-29", "No namespace" };
    private static string Plain(int id, string path, string cache = "old", string metadata = "", bool multiLine = false) =>
        "<w:p>" + Control(id, multiLine ? "<w:text w:multiLine='1'/>" : "<w:text/>", cache, Binding(path) + metadata) + "</w:p>";
    private static string CalendarType(string storage = "date", string value = "2000-01-01T00:00:00Z") =>
        "<w:date w:fullDate='" + value + "'><w:dateFormat w:val='yyyy-MM-dd'/><w:lid w:val='en-US'/><w:storeMappedDataAs w:val='" + storage + "'/><w:calendar w:val='gregorian'/></w:date>";
    [Fact]
    public void LocalGraphsArePreservedWithoutImplicitApplyThenAllScalarKindsApplyExplicitly()
    {
        string body = "<w:p>" + Control(1, Checkbox(), "-", Binding("/p:invoice/p:approved")) + "</w:p>"
            + Plain(2, "/p:invoice/p:customer/@name")
            + "<w:p>" + Control(3, Choices, "Alpha", Binding("/p:invoice/p:choice")) + "</w:p>"
            + "<w:p>" + Control(4, "<w:comboBox><w:listItem w:displayText='Alpha' w:value='a'/></w:comboBox>", "old", Binding("/p:invoice/p:text")) + "</w:p>"
            + Control(5, CalendarType(), "2000-01-01", Binding("/p:invoice/p:date"), block: true)
            + Plain(6, "/p:invoice/plain");
        WordDocument document = Load(WithStore(body), mode: ContentControlDateMode.DateOnly);
        Assert.False(document.ContentControls[0].IsChecked); Assert.Equal("old", document.ContentControls[1].DisplayText);
        Assert.All(document.ContentControls, c => Assert.Equal(ContentControlBindingState.Unevaluated, c.BindingState));
        using var untouched = new MemoryStream(); ConversionResult initial = document.Save(untouched, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(initial.Diagnostics, d => d.Code == "Control.BindingNotApplied"); Assert.Equal("Ada & Co", (string?)Xml(untouched.ToArray(), "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal("old", Xml(untouched.ToArray(), "word/document.xml").Descendants(W + "sdt").Single(e => (string?)e.Element(W + "sdtPr")?.Element(W + "id")?.Attribute(W + "val") == "2").Element(W + "sdtContent")!.Value);
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal(AppliedValues, document.ContentControls.Select(c => c.DisplayText)); Assert.All(document.ContentControls, c => Assert.Equal(ContentControlBindingState.Synchronized, c.BindingState));
        Assert.Equal("b", document.ContentControls.Single(c => c.Id == 3).SelectedValue); Assert.Equal("p", document.ContentControls[0].Binding!.NamespaceMappings.Keys.Single());
        Assert.Equal(StoreId, document.ContentControls[0].Binding!.StoreItemId);
        byte[] word = Save(document); Validate(word);
        Assert.Equal(StoreId, (string?)Xml(word, "customXml/itemProps1.xml").Root!.Attribute(Ds + "itemID")); Assert.Contains(document.Diagnostics, d => d.Code == "Control.GuidNormalized");
        XElement relationship = Assert.Single(Xml(word, "customXml/_rels/item1.xml.rels").Root!.Elements()); Assert.EndsWith("/customXmlProps", (string?)relationship.Attribute("Type"), StringComparison.Ordinal); Assert.Equal("/customXml/itemProps1.xml", (string?)relationship.Attribute("Target"));
        Assert.Equal("b", (string?)Xml(word, "word/document.xml").Descendants(W + "dropDownList").Single().Attribute(W + "lastValue"));
        Assert.Equal("XAda&CoBeta<merge:Never>&literal2024-02-29Nonamespace", Compact(Pdf(document))); Assert.Equal("XAda&CoBeta<merge:Never>&literal2024-02-29Nonamespace", Compact(Xps(document)));
        Assert.Equal(AppliedValues, Load(word, mode: ContentControlDateMode.DateOnly).ContentControls.Select(c => c.DisplayText));
    }
    [Fact]
    public void DirtySaveRequiresAnExplicitDecisionAndWriteBackEscapesLiteralAttributeData()
    {
        WordDocument document = Load(WithStore(Plain(1, "/p:invoice/p:customer/@name")));
        document.SetControlText(Assert.Single(document.ContentControls), "Uncommitted XML", TestContext.Current.CancellationToken);
        Assert.Equal(ContentControlBindingState.Dirty, Assert.Single(document.ContentControls).BindingState);
        using var rejected = new MemoryStream(); rejected.WriteByte(42);
        Assert.Throws<InvalidOperationException>(() => document.Save(rejected, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, rejected.ToArray());
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Equal("Ada & Co", Assert.Single(document.ContentControls).DisplayText);
        const string literal = "<b> & merge:Never";
        document.SetControlText(Assert.Single(document.ContentControls), literal, TestContext.Current.CancellationToken); document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word); var xml = Xml(word, "customXml/item1.xml");
        Assert.Equal(literal, (string?)xml.Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name")); Assert.Empty(xml.Descendants("b"));
        Assert.Contains("&lt;b&gt; &amp; merge:Never", xml.ToString(), StringComparison.Ordinal); Assert.Equal(literal, Assert.Single(Load(word).ContentControls).DisplayText);
        Assert.Equal("<b>&merge:Never", Compact(Pdf(document)));
    }
    [Fact]
    public void DetachmentRemovesTheMappingWithoutDeletingLocalXmlAndReloadsStrictly()
    {
        WordDocument document = Load(WithStore(Plain(1, "/p:invoice/p:customer/@name")));
        document.SetControlText(Assert.Single(document.ContentControls), "Detached", TestContext.Current.CancellationToken);
        document.DetachControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Null(Assert.Single(document.ContentControls).Binding);
        byte[] word = Save(document); Validate(word); Assert.Empty(Xml(word, "word/document.xml").Descendants(W + "dataBinding"));
        Assert.Equal("Ada & Co", (string?)Xml(word, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        WordDocument reloaded = Load(word); Assert.Equal("Detached", Assert.Single(reloaded.ContentControls).DisplayText); Assert.Equal(ContentControlBindingState.None, Assert.Single(reloaded.ContentControls).BindingState);
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(Save(view))); Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("customXml/", StringComparison.Ordinal));
    }
    [Fact]
    public void SharedTargetWriteBackRequiresEveryControlAndRejectsDivergentValuesAtomically()
    {
        string body = Plain(1, "/p:invoice/p:customer/@name") + Plain(2, "/p:invoice/p:customer/@name");
        WordDocument document = Load(WithStore(body)); document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        document.SetControlText(document.ContentControls[0], "One", TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(new[] { document.ContentControls[0] }, TestContext.Current.CancellationToken));
        document.SetControlText(document.ContentControls[1], "Two", TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(document.ContentControls.Reverse(), TestContext.Current.CancellationToken));
        Assert.All(document.ContentControls, c => Assert.Equal(ContentControlBindingState.Dirty, c.BindingState));
        // XML was not partially overwritten by the failed group: explicit Apply still returns the authored value.
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.All(document.ContentControls, c => Assert.Equal("Ada & Co", c.DisplayText));
        document.SetControlText(document.ContentControls[0], "Agreed", TestContext.Current.CancellationToken); document.SetControlText(document.ContentControls[1], "Agreed", TestContext.Current.CancellationToken);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Validate(Save(document));
        Assert.Equal("Agreed", (string?)Xml(Save(document), "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
    }
    [Fact]
    public void MultiControlApplyRefreshesInlineSliceLocationsAndLateFailuresRollBackAllOwners()
    {
        string first = Control(1, "<w:text/>", "", Binding("/p:invoice/p:customer/@name"), content: Run("old ") + Run("cache"));
        string second = Control(2, "<w:text/>", "old", Binding("/p:invoice/p:customer/@name"));
        WordDocument document = Load(WithStore("<w:p>" + first + Run("|") + second + "</w:p>"));
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Equal("Ada&Co|Ada&Co", Compact(Pdf(document))); Validate(Save(document));
        string failing = "<w:p>" + Control(2, Checkbox(), "-", Binding("/p:invoice/p:customer/@name")) + "</w:p>";
        WordDocument invalid = Load(WithStore(Plain(1, "/p:invoice/p:customer/@name") + failing)); var handles = invalid.ContentControls;
        Assert.Throws<InvalidDataException>(() => invalid.ApplyControlBindings(handles, TestContext.Current.CancellationToken));
        Assert.Equal("old", invalid.ContentControls[0].DisplayText); Assert.False(invalid.ContentControls[1].IsChecked);
        invalid.ApplyControlBindings(new[] { handles[0] }, TestContext.Current.CancellationToken); Assert.Equal("Ada & Co", invalid.ContentControls[0].DisplayText);
    }
    [Theory]
    [InlineData("unlocked", true, true)]
    [InlineData("sdtLocked", true, false)]
    [InlineData("contentLocked", false, false)]
    [InlineData("sdtContentLocked", false, false)]
    public void BindingDirectionsRespectLocksAndForeignOrStaleHandles(string value, bool mayEdit, bool mayDetach)
    {
        WordDocument document = Load(WithStore(Plain(1, "/p:invoice/p:customer/@name", metadata: "<w:lock w:val='" + value + "'/>")));
        if (mayEdit)
        { document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken); }
        else
        { Assert.Throws<InvalidOperationException>(() => document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken)); Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken)); }
        var handle = Assert.Single(document.ContentControls);
        if (mayDetach) document.DetachControlBindings(new[] { handle }, TestContext.Current.CancellationToken);
        else Assert.Throws<InvalidOperationException>(() => document.DetachControlBindings(new[] { handle }, TestContext.Current.CancellationToken));
        Assert.Equal(mayEdit ? "Ada & Co" : "old", Assert.Single(document.ContentControls).DisplayText); Validate(Save(document));
    }
    [Theory]
    [InlineData("//p:invoice/p:approved")]
    [InlineData("/p:invoice//p:approved")]
    [InlineData("/p:invoice/p:approved[2]")]
    [InlineData("/p:invoice/*")]
    [InlineData("/p:invoice/../p:approved")]
    [InlineData("/p:invoice/p:approved | /p:invoice/p:text")]
    [InlineData("/p:invoice/p:approved/text()")]
    [InlineData("/q:invoice/p:approved")]
    [InlineData("/p:invoice/@xmlns")]
    [InlineData("/p:invoice/p:missing")]
    [InlineData("/p:invoice/p:customer/@missing")]
    [InlineData("/p:invoice")]
    public void ArbitraryOrUnresolvedPathsAreRejectedWithoutXPathExecution(string path)
    { Assert.Throws<InvalidDataException>(() => Load(WithStore(Plain(1, path)), new ConversionOptions(FidelityMode.Lenient))); }
    [Fact]
    public void RedundantFirstPredicatesDoNotHideAmbiguousChildrenAndPrefixesMustBeExplicit()
    {
        string body = Plain(1, "/p:invoice[1]/p:customer[1]/@name"); WordDocument admitted = Load(WithStore(body)); admitted.ApplyControlBindings(admitted.ContentControls, TestContext.Current.CancellationToken); Assert.Equal("Ada & Co", Assert.Single(admitted.ContentControls).DisplayText);
        string duplicates = "<p:invoice xmlns:p='urn:wordnet:controls'><p:approved>true</p:approved><p:approved>false</p:approved></p:invoice>";
        Assert.Throws<InvalidDataException>(() => Load(WithStore(Plain(1, "/p:invoice/p:approved[1]"), duplicates)));
        string missing = "<w:p>" + Control(1, "<w:text/>", "old", Binding("/p:invoice/p:text", mappings: null)) + "</w:p>";
        Assert.Throws<InvalidDataException>(() => Load(WithStore(missing)));
        string duplicatePrefixes = "<w:p>" + Control(1, "<w:text/>", "old", Binding("/p:invoice/p:text", mappings: "xmlns:p='urn:wordnet:controls' xmlns:p='urn:other'")) + "</w:p>";
        Assert.Throws<InvalidDataException>(() => Load(WithStore(duplicatePrefixes)));
    }
    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void BooleanBindingsUseOnlyXmlSchemaLexicalValuesAndCanonicalWriteBack(string lexical, bool expected)
    {
        string xml = "<p:invoice xmlns:p='urn:wordnet:controls'><p:approved>" + lexical + "</p:approved></p:invoice>";
        WordDocument document = Load(WithStore("<w:p>" + Control(1, Checkbox(), "-", Binding("/p:invoice/p:approved")) + "</w:p>", xml));
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Equal(expected, Assert.Single(document.ContentControls).IsChecked);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Equal(expected ? "true" : "false", Xml(Save(document), "customXml/item1.xml").Root!.Value); Validate(Save(document));
    }
    [Fact]
    public void DateBindingModesAndPlainTextMultilineAreExplicit()
    {
        string body = Control(1, CalendarType("dateTime"), "2000-01-01", Binding("/p:invoice/p:instant"), block: true) + Plain(2, "/p:invoice/p:text", multiLine: true);
        WordDocument document = Load(WithStore(body)); document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal("2025-12-31", document.ContentControls[0].DisplayText); Assert.Equal(new DateTimeOffset(2025, 12, 31, 22, 30, 0, TimeSpan.Zero), document.ContentControls[0].DateValue!.Value);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal("2025-12-31T22:30:00Z", Xml(Save(document), "customXml/item1.xml").Descendants(XName.Get("instant", "urn:wordnet:controls")).Single().Value);
        document.SetControlText(document.ContentControls[1], "Line1\nLine2 <xml>", TestContext.Current.CancellationToken); document.WriteBackControlBindings(new[] { document.ContentControls[1] }, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word); Assert.Equal("Line1\nLine2 <xml>", Load(word).ContentControls[1].DisplayText); Assert.Single(Xml(word, "word/document.xml").Descendants(W + "br"));
        WordDocument singleLine = LoadBody("<w:p>" + Control(1, "<w:text/>", "old") + "</w:p>"); Assert.Throws<ArgumentException>(() => singleLine.SetControlText(singleLine.ContentControls[0], "not\nallowed", TestContext.Current.CancellationToken));
        WordDocument textDate = Load(WithStore(Control(1, CalendarType("text"), "2000-01-01", Binding("/p:invoice/p:date"), block: true)));
        Assert.Throws<NotSupportedException>(() => textDate.ApplyControlBindings(textDate.ContentControls, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => textDate.WriteBackControlBindings(textDate.ContentControls, TestContext.Current.CancellationToken));
    }
    [Fact]
    public void RichTextBindingsAndStructuredXmlTargetsAreRejectedRatherThanInterpretedAsMarkup()
    {
        string rich = "<w:p>" + Control(1, "<w:richText/>", "Text", Binding("/p:invoice/p:text")) + "</w:p>";
        Assert.Throws<NotSupportedException>(() => Load(WithStore(rich)));
        Assert.Throws<InvalidDataException>(() => Load(WithStore(Plain(1, "/p:invoice"))));
        WordDocument document = Load(WithStore(Plain(1, "/p:invoice/p:text"))); document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal("<merge:Never> & literal", Assert.Single(document.ContentControls).DisplayText); byte[] word = Save(document); Validate(word);
        Assert.Empty(Xml(word, "word/document.xml").Descendants(W + "fldSimple")); Assert.Equal("<merge:Never> & literal", Xml(word, "word/document.xml").Descendants(W + "sdtContent").Single().Value);
    }
    [Fact]
    public void ExternalUnsafeAndDuplicateStoreGraphsFailInLenientMode()
    {
        byte[] safe = WithStore(Plain(1, "/p:invoice/p:text"));
        byte[] external = Modify(safe, zip =>
        { var rels = Xml(zip, "customXml/_rels/item1.xml.rels"); rels.Root!.Elements().Single().SetAttributeValue("TargetMode", "External"); rels.Root.Elements().Single().SetAttributeValue("Target", "https://example.invalid/props"); Put(zip, "customXml/_rels/item1.xml.rels", rels.ToString()); });
        Assert.Throws<InvalidDataException>(() => Load(external, new ConversionOptions(FidelityMode.Lenient)));
        byte[] escape = Modify(safe, zip =>
        { var rels = Xml(zip, "word/_rels/document.xml.rels"); rels.Root!.Elements().Single().SetAttributeValue("Target", "../../../outside.xml"); Put(zip, "word/_rels/document.xml.rels", rels.ToString()); });
        Assert.Throws<InvalidDataException>(() => Load(escape));
        byte[] missing = Modify(safe, zip => zip.GetEntry("customXml/itemProps1.xml")!.Delete()); Assert.Throws<InvalidDataException>(() => Load(missing));
        byte[] duplicate = Modify(safe, zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "second", "customXml", "../customXml/item2.xml"); AddRelationship(zip, "customXml/_rels/item2.xml.rels", "props", "customXmlProps", "itemProps2.xml");
            Put(zip, "customXml/item2.xml", Fixture("store.xml")); Put(zip, "customXml/itemProps2.xml", Xml(zip, "customXml/itemProps1.xml").ToString()); Override(zip, "customXml/itemProps2.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        });
        Assert.Throws<InvalidDataException>(() => Load(duplicate));
    }
    [Theory]
    [InlineData("<!DOCTYPE data [<!ENTITY xxe SYSTEM 'https://example.invalid/data'>]><data>&xxe;</data>")]
    [InlineData("<?xml-stylesheet href='https://example.invalid/style'?><data>literal</data>")]
    public void DtdEntitiesAndProcessingInstructionsAreNeverFetched(string xml)
    { Assert.Throws<InvalidDataException>(() => Load(WithStore("<w:p>" + Control(1, "<w:text/>", "old", Binding("/data", mappings: "")) + "</w:p>", xml), new ConversionOptions(FidelityMode.Lenient))); }
    [Fact]
    public void BindingSelectionCancellationAndDuplicateHandlesAreAtomic()
    {
        WordDocument document = Load(WithStore(Plain(1, "/p:invoice/p:text"))); var original = document.ContentControls;
        Assert.Throws<ArgumentException>(() => document.ApplyControlBindings(new[] { original[0], original[0] }, TestContext.Current.CancellationToken));
        using var token = new CancellationTokenSource();
        IEnumerable<DocumentContentControl> Canceled() { yield return original[0]; token.Cancel(); }
        Assert.Throws<OperationCanceledException>(() => document.ApplyControlBindings(Canceled(), token.Token)); Assert.Equal("old", document.ContentControls[0].DisplayText);
        document.ApplyControlBindings(original, TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => document.WriteBackControlBindings(original, TestContext.Current.CancellationToken));
        WordDocument other = Load(WithStore(Plain(1, "/p:invoice/p:text"))); Assert.Throws<ArgumentException>(() => other.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken));
    }
    [Fact]
    public void PathDepthLengthNamespaceAndAggregateXmlNodeLimitsAreEnforced()
    {
        Assert.Throws<InvalidDataException>(() => Load(WithStore(Plain(1, "/" + new string('x', 1024)))));
        Assert.Throws<InvalidDataException>(() => Load(WithStore(Plain(1, "/" + string.Join("/", Enumerable.Repeat("root", 17))))));
        string mappings = string.Join(" ", Enumerable.Range(0, 17).Select(i => "xmlns:p" + i + "='urn:n" + i + "'"));
        string invalid = "<w:p>" + Control(1, "<w:text/>", "old", Binding("/root/@v", mappings: mappings)) + "</w:p>";
        Assert.Throws<InvalidDataException>(() => Load(WithStore(invalid, "<root v='ok'/>")));
        string huge = "<root value='ok'>" + string.Concat(Enumerable.Repeat("<x/>", 100001)) + "</root>";
        string body = "<w:p>" + Control(1, "<w:text/>", "old", Binding("/root/@value", mappings: "")) + "</w:p>";
        Assert.Throws<InvalidDataException>(() => Load(WithStore(body, huge)));
        WordDocument bounded = Load(WithStore(body, "<root value='ok'>" + string.Concat(Enumerable.Repeat("<x/>", 1000)) + "</root>")); bounded.ApplyControlBindings(bounded.ContentControls, TestContext.Current.CancellationToken); Assert.Equal("ok", bounded.ContentControls[0].DisplayText);
    }
}
