using System;
using System.IO;
using System.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlBoundaryTests
{
    [Theory]
    [InlineData("maybe", "0058")]
    [InlineData("1", "D800")]
    [InlineData("1", "110000")]
    [InlineData("1", "ZZZZ")]
    [InlineData("1", "0000")]
    [InlineData("1", " 058")]
    [InlineData("1", "FFFE")]
    [InlineData("1", "007F")]
    public void InvalidCheckboxStateAndGlyphsFailInEveryFidelityMode(string state, string glyph)
    { Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, Checkbox(state, glyph), "X", block: true), new ConversionOptions(FidelityMode.Lenient))); }
    [Fact]
    public void DuplicateIdsFailAndMultipleCharactersCannotMasqueradeAsACheckbox()
    {
        string control = Control(1, Checkbox(), "-");
        Assert.Throws<InvalidDataException>(() => LoadBody("<w:p>" + control + control + "</w:p>"));
        Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, Checkbox(), "two", block: true)));
    }
    [Theory]
    [InlineData("<w:temporary/>")]
    [InlineData("<w:picture/>")]
    [InlineData("<w:unknown/>")]
    [InlineData("<w:lock w:val='unknown'/>")]
    public void UnknownBehaviorAndInvalidLocksNeverBecomeLenientPreservation(string metadata)
    {
        Exception? error = Record.Exception(() => LoadBody(Control(1, Checkbox(), "-", metadata, block: true), new ConversionOptions(FidelityMode.Lenient)));
        Assert.True(error is NotSupportedException or InvalidDataException); Assert.StartsWith("Control.", error!.Message, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("<w:tbl/>")]
    [InlineData("<w:sdt><w:sdtPr/><w:sdtContent><w:r><w:t>Nested</w:t></w:r></w:sdtContent></w:sdt>")]
    [InlineData("<w:ins w:id='1' w:author='A'><w:r><w:t>Revision</w:t></w:r></w:ins>")]
    [InlineData("<w:r><w:drawing/></w:r>")]
    [InlineData("<w:bookmarkStart w:id='1' w:name='inside'/><w:r><w:t>Text</w:t></w:r><w:bookmarkEnd w:id='1'/>")]
    [InlineData("<w:fldSimple w:instr='PAGE'><w:r><w:t>1</w:t></w:r></w:fldSimple>")]
    [InlineData("<w:r><w:br w:type='page'/></w:r>")]
    public void RichControlsRejectStructuralHistoryObjectAndBoundaryContent(string content)
    { Assert.Throws<NotSupportedException>(() => LoadBody("<w:p>" + Control(1, "<w:richText/>", "", content: content) + "</w:p>", new ConversionOptions(FidelityMode.Lenient))); }
    [Fact]
    public void ScalarControlsRequireOneFormattingSetAndAtMostOneParagraph()
    {
        string runs = Run("Al") + "<w:r><w:rPr><w:b/></w:rPr><w:t>pha</w:t></w:r>";
        Assert.Throws<NotSupportedException>(() => LoadBody("<w:p>" + Control(1, Choices, "", content: runs) + "</w:p>"));
        string block = "<w:sdt><w:sdtPr><w:id w:val='1'/>" + Choices + "</w:sdtPr><w:sdtContent><w:p>" + Run("Alpha") + "</w:p><w:p>" + Run("Other") + "</w:p></w:sdtContent></w:sdt>";
        Assert.Throws<NotSupportedException>(() => LoadBody(block));
    }
    [Fact]
    public void TableRowAndHeaderStoryOwnersAreExplicitlyRejected()
    {
        string row = "<w:tbl><w:sdt><w:sdtPr><w:id w:val='1'/><w:richText/></w:sdtPr><w:sdtContent><w:tr><w:tc><w:p>" + Run("Text") + "</w:p></w:tc></w:tr></w:sdtContent></w:sdt></w:tbl>";
        Assert.Throws<NotSupportedException>(() => LoadBody(row));
        byte[] source = Modify(Package(Document("<w:p>" + Run("Body") + "</w:p><w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>")), zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "header", "header", "header1.xml");
            Put(zip, "word/header1.xml", "<w:hdr xmlns:w='" + W + "' xmlns:w14='" + W14 + "'><w:p>" + Control(1, Checkbox(), "-") + "</w:p></w:hdr>");
            Override(zip, "word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml");
        });
        Assert.Throws<NotSupportedException>(() => Load(source));
    }
    [Fact]
    public void ControlCountAndPerListItemLimitsHaveRealBoundaryCases()
    {
        string allowed = string.Concat(Enumerable.Range(0, 1024).Select(id => Control(id, "<w14:checkbox/>", "\u2610", block: true)));
        WordDocument document = LoadBody(allowed); Assert.Equal(1024, document.ContentControls.Count);
        byte[] word = Save(document); Assert.Equal(1024, Xml(word, "word/document.xml").Descendants(W + "sdt").Count()); Validate(word);
        Assert.Throws<InvalidDataException>(() => LoadBody(allowed + Control(1024, Checkbox(), "-", block: true)));
        string items = string.Concat(Enumerable.Range(0, 256).Select(i => "<w:listItem w:displayText='Label" + i + "' w:value='v" + i + "'/>"));
        Assert.Equal(256, Assert.Single(LoadBody(Control(1, "<w:dropDownList>" + items + "</w:dropDownList>", "Label0", block: true)).ContentControls).Items.Count);
        Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, "<w:dropDownList>" + items + "<w:listItem w:value='extra'/></w:dropDownList>", "Label0", block: true)));
    }
    [Fact]
    public void AggregateItemsAndMetadataStringsAreBounded()
    {
        string items = string.Concat(Enumerable.Range(0, 256).Select(i => "<w:listItem w:value='v" + i + "'/>"));
        string controls = string.Concat(Enumerable.Range(0, 16).Select(i => Control(i, "<w:dropDownList>" + items + "</w:dropDownList>", "v0", block: true)));
        Assert.Equal(4096, LoadBody(controls).ContentControls.Sum(c => c.Items.Count));
        Assert.Throws<InvalidDataException>(() => LoadBody(controls + Control(16, "<w:dropDownList>" + items + "</w:dropDownList>", "v0", block: true)));
        Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, Checkbox(), "-", "<w:tag w:val='" + new string('x', 257) + "'/>", block: true)));
        Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, "<w:dropDownList><w:listItem w:value='" + new string('x', 1025) + "'/></w:dropDownList>", "", block: true)));
    }
    [Fact]
    public void ReplacementExpansionAndTightenedSaveBoundsLeaveTheOriginalAndDestinationUntouched()
    {
        WordDocument document = LoadBody("<w:p>" + Control(1, "<w:richText/>", "Keep") + "</w:p>", new ConversionOptions(maximumCharacters: 2048));
        var handle = Assert.Single(document.ContentControls); string before = Xml(Save(document), "word/document.xml").ToString();
        Assert.Throws<InvalidDataException>(() => document.ReplaceControlContent(handle, new[] { new DocumentText(new string('x', 2049)) }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString());
        using var output = new MemoryStream(); output.WriteByte(5);
        Assert.Throws<InvalidDataException>(() => document.Save(output, DocumentFormat.Docx, new ConversionOptions(maximumArchiveEntries: 1), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 5 }, output.ToArray());
        document.ReplaceControlContent(handle, new[] { new DocumentText("Accepted") }, TestContext.Current.CancellationToken); Assert.Equal("Accepted", Assert.Single(document.ContentControls).DisplayText);
    }
    [Fact]
    public void MissingOrRichGlossaryEntriesFailInsteadOfDroppingThePlaceholderRelationship()
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>");
        Assert.Throws<NotSupportedException>(() => LoadBody("<w:p>" + control + "</w:p>"));
        byte[] source = Modify(WithGlossary("<w:p>" + control + "</w:p>"), zip =>
        {
            var xml = Xml(zip, "word/glossary/document.xml"); xml.Descendants(W + "docPartBody").Single().Add(new System.Xml.Linq.XElement(W + "tbl")); Put(zip, "word/glossary/document.xml", xml.ToString());
        });
        Assert.Throws<NotSupportedException>(() => Load(source));
    }
    [Fact]
    public void NativeAndConformanceCombinationsStayUnsupportedBeforeOutputCopy()
    {
        WordDocument document = Load(Package(Fixture("checkbox.xml")));
        foreach (var policy in new[] { new ConversionOptions { Typography = new TypographyOptions { Enabled = true } }, new ConversionOptions { Pdf = new PdfOptions { Accessibility = PdfAccessibility.Tagged } }, new ConversionOptions { Pdf = new PdfOptions { Conformance = PdfConformance.PdfA2b } } })
        {
            using var output = new MemoryStream(); output.WriteByte(3); Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, policy, TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 3 }, output.ToArray());
        }
    }
    [Fact]
    public void MalformedTextAndInstructionsOutsideTextNodesCannotBeSilentlyDropped()
    {
        string[] runs = { "<w:r>Lost<w:t>Kept</w:t></w:r>", "<w:r><?control hidden?><w:t>Kept</w:t></w:r>" };
        foreach (string content in runs)
            Assert.Throws<InvalidDataException>(() => LoadBody("<w:p>" + Control(1, "<w:richText/>", "", content: content) + "</w:p>"));
        string paragraph = "<w:sdt><w:sdtPr><w:id w:val='1'/><w:richText/></w:sdtPr><w:sdtContent><w:p>Lost" + Run("Kept") + "</w:p></w:sdtContent></w:sdt>";
        Assert.Throws<InvalidDataException>(() => LoadBody(paragraph));
    }
    [Fact]
    public void InlineOwnersRejectUnsupportedInheritedParagraphFormatting()
    {
        string body = "<w:p><w:pPr><w:ind w:left='720'/></w:pPr>" + Control(1, "<w:richText/>", "Kept") + "</w:p>";
        Assert.Throws<NotSupportedException>(() => LoadBody(body, new ConversionOptions(FidelityMode.Lenient)));
    }

}
