using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlRichTextTests
{
    [Fact]
    public void FormattedSnapshotsAndBlockAppearanceAreDetachedAndSchemaValidated()
    {
        byte[] source = Package(Fixture("rich.xml")); Validate(source); WordDocument document = Load(source);
        var inline = document.ContentControls.Single(c => c.Id == 30); var block = document.ContentControls.Single(c => c.Id == 31);
        Assert.Equal(ContentControlKind.RichText, block.Kind); Assert.Equal(2, block.Content.Count); Assert.Equal("First", block.Content[0].Runs.Single().Text);
        Assert.Equal(ContentControlAlignment.Center, block.Content[0].Alignment); Assert.Equal(6, block.Content[0].SpaceBefore); Assert.Equal(4, block.Content[0].SpaceAfter);
        Assert.Equal(1.5, block.Content[0].LineSpacing); Assert.Equal("auto", block.Content[0].LineRule); Assert.True(block.Content[0].KeepWithNext);
        TextFormatting detached = inline.Content[0].Runs[0].Formatting; detached.Bold = false; detached.Color = "0000FF";
        Assert.True(inline.Content[0].Runs[0].Formatting.Bold); Assert.Equal("FF0000", inline.Content[0].Runs[0].Formatting.Color);
        var inputFormatting = new TextFormatting { Bold = true, Color = "0000FF", FontSize = 14 };
        var input = new DocumentText("Changed", inputFormatting); inputFormatting.Bold = false; inputFormatting.Color = "FF0000";
        document.ReplaceControlContent(inline, new[] { input, new DocumentText(" text", new TextFormatting { Italic = true }) }, TestContext.Current.CancellationToken);
        Assert.Equal("Bold Italic", inline.DisplayText); Assert.Equal("Changed text", document.ContentControls.Single(c => c.Id == 30).DisplayText);
        document.ReplaceControlParagraphs(document.ContentControls.Single(c => c.Id == 31), block.Content, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word); XDocument xml = Xml(word, "word/document.xml");
        Assert.Single(xml.Descendants(W + "bookmarkStart")); Assert.Single(xml.Descendants(W + "bookmarkEnd"));
        Assert.Equal("DOCPROPERTY Outside", (string?)Assert.Single(xml.Descendants(W + "fldSimple")).Attribute(W + "instr"));
        XElement wrapper = xml.Descendants(W + "sdt").Single(e => (string?)e.Element(W + "sdtPr")?.Element(W + "id")?.Attribute(W + "val") == "31");
        XElement firstProperties = wrapper.Descendants(W + "pPr").First(); Assert.Equal("120", (string?)firstProperties.Element(W + "spacing")!.Attribute(W + "before"));
        Assert.Equal("360", (string?)firstProperties.Element(W + "spacing")!.Attribute(W + "line")); Assert.Equal("FF0000", (string?)firstProperties.Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val"));
        Assert.Equal("BEFOREChangedtextCACHEDFirstSecond", Compact(Pdf(document))); Assert.Equal("BEFOREChangedtextCACHEDFirstSecond", Compact(Xps(document)));
        using var pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); var changed = pdf.GetPage(1).GetWords().Single(w => w.Text == "Changed");
        Assert.Equal(14, changed.Letters[0].FontSize, 3); Assert.Equal(1, changed.Letters[0].Color.ToRGBValues().b, 3); Assert.Contains("Bold", changed.Letters[0].FontName, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void ReplacementCreatesRealParagraphsAndPreservesControlIdentityAndSurroundingFields()
    {
        WordDocument document = Load(Package(Fixture("rich.xml")));
        document.ReplaceControlParagraphs(document.ContentControls.Single(c => c.Id == 31), new[]
        {
            new ContentControlParagraph(new[] { new DocumentText("Heading", new TextFormatting { FontSize = 18, Bold = true }) }, ContentControlAlignment.Right, headingLevel: 2, spaceBefore: 3, spaceAfter: 5),
            new ContentControlParagraph(new[] { new DocumentText("Body Ω", new TextFormatting { Underline = true }) })
        }, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word); var xml = Xml(word, "word/document.xml");
        Assert.Equal(2, xml.Descendants(W + "sdt").Count()); Assert.Equal("if:Literal", document.ContentControls.Single(c => c.Id == 30).Tag);
        Assert.Equal("Paragraphs", document.ContentControls.Single(c => c.Id == 31).Alias); Assert.Equal("Heading\nBody Ω", document.ContentControls.Single(c => c.Id == 31).DisplayText);
        Assert.Contains(xml.Descendants(W + "pStyle"), e => (string?)e.Attribute(W + "val") == "Heading2");
        Assert.Equal("outside", (string?)xml.Descendants(W + "bookmarkStart").Single().Attribute(W + "name"));
        Assert.Contains("HeadingBodyΩ", Compact(Pdf(document)), StringComparison.Ordinal);
    }
    [Fact]
    public void InlineEmptyAndBlockEmptyAreValuesRatherThanPlaceholders()
    {
        WordDocument document = Load(Package(Fixture("rich.xml")));
        document.ReplaceControlContent(document.ContentControls.Single(c => c.Id == 30), Array.Empty<DocumentText>(), TestContext.Current.CancellationToken);
        document.ReplaceControlParagraphs(document.ContentControls.Single(c => c.Id == 31), Array.Empty<ContentControlParagraph>(), TestContext.Current.CancellationToken);
        Assert.All(document.ContentControls, d => { Assert.Equal(ContentControlValueState.Empty, d.State); Assert.False(d.ShowingPlaceholder); });
        byte[] word = Save(document); Validate(word); Assert.Equal("BEFORECACHED", Compact(Pdf(document))); Assert.Equal(2, Load(word).ContentControls.Count);
    }
    [Fact]
    public void LateInvalidAndCanceledEnumerationDoNotCommitAnyReplacementOrInvalidateTheHandle()
    {
        WordDocument document = Load(Package(Fixture("rich.xml"))); var handle = document.ContentControls.Single(c => c.Id == 30);
        string before = Xml(Save(document), "word/document.xml").ToString();
        IEnumerable<DocumentText> Invalid()
        { yield return new DocumentText("First"); yield return new DocumentText("\f"); }
        Assert.Throws<NotSupportedException>(() => document.ReplaceControlContent(handle, Invalid(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString());
        using var token = new CancellationTokenSource();
        IEnumerable<DocumentText> Canceled()
        { yield return new DocumentText("First"); token.Cancel(); yield return new DocumentText("Second"); }
        Assert.Throws<OperationCanceledException>(() => document.ReplaceControlContent(handle, Canceled(), token.Token));
        Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString());
        document.ReplaceControlContent(handle, new[] { new DocumentText("Committed") }, TestContext.Current.CancellationToken); Assert.Equal("Committed", document.ContentControls.Single(c => c.Id == 30).DisplayText);
    }
    [Fact]
    public void CrossedBookmarksAndFieldsInsideControlsAreRejectedWhileAdjacentNestedFieldsRemainLive()
    {
        string simple = Control(1, "<w:richText/>", "Value");
        Assert.Throws<NotSupportedException>(() => LoadBody("<w:p><w:bookmarkStart w:id='1' w:name='cross'/>" + simple + "<w:bookmarkEnd w:id='1'/></w:p>"));
        string begin = "<w:r><w:fldChar w:fldCharType='begin'/></w:r>";
        Assert.Throws<NotSupportedException>(() => LoadBody("<w:p>" + begin + simple + "<w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>"));
        string nested = "<w:fldSimple w:instr='IF &quot;yes&quot; = &quot;yes&quot; &quot;True&quot; &quot;False&quot;'><w:r><w:t>cache</w:t></w:r></w:fldSimple>";
        WordDocument document = LoadBody("<w:p>" + nested + simple + "</w:p>");
        document.ReplaceControlContent(Assert.Single(document.ContentControls), new[] { new DocumentText("New") }, TestContext.Current.CancellationToken);
        Validate(Save(document)); Assert.Equal("TrueNew", Pdf(document)); Assert.Single(Xml(Save(document), "word/document.xml").Descendants(W + "fldSimple"));
    }
    [Fact]
    public void TypedDisplayViewRemovesOnlyTypedTagsAndLeavesAnExternalMergeFieldExecutable()
    {
        XDocument source = XDocument.Parse(Fixture("rich.xml")); source.Descendants(W + "fldSimple").Single().SetAttributeValue(W + "instr", "MERGEFIELD Outside");
        WordDocument document = Load(Package(source.ToString())); WordDocument display = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        display.Merge(new { Outside = "Merged", Literal = false }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("BEFOREBoldItalicMergedFirstSecond", Compact(Pdf(display))); Assert.Equal(2, document.ContentControls.Count); Assert.Empty(display.ContentControls);
        Assert.Equal("CACHED", Xml(Save(document), "word/document.xml").Descendants(W + "fldSimple").Single().Value);
    }
    [Fact]
    public void ParagraphInputsCopyCallerCollectionsAndFormattingBeforeReplacement()
    {
        var values = new List<DocumentText> { new DocumentText("Original", new TextFormatting { Color = "123456" }) };
        var paragraph = new ContentControlParagraph(values, ContentControlAlignment.Center); values.Clear();
        Assert.Equal("Original", Assert.Single(paragraph.Runs).Text);
        WordDocument document = Load(Package(Fixture("rich.xml"))); document.ReplaceControlParagraphs(document.ContentControls.Single(c => c.Id == 31), new[] { paragraph }, TestContext.Current.CancellationToken);
        paragraph.Runs[0].Formatting.Color = "FFFFFF";
        Assert.Equal("123456", document.ContentControls.Single(c => c.Id == 31).Content[0].Runs[0].Formatting.Color); Validate(Save(document));
    }
}
