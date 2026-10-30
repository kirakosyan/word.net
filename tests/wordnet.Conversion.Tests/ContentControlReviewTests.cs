using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlReviewTests
{
    private static string Kind(int kind) => kind switch
    {
        0 => Checkbox(),
        1 => Choices,
        2 => Choices.Replace("dropDownList", "comboBox"),
        3 => "<w:date><w:dateFormat w:val='yyyy-MM-dd'/></w:date>",
        4 => "<w:richText/>",
        _ => "<w:text/>"
    };

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void PendingClosedBlockBookmarksCannotBeDroppedOrMovedInsideTypedOwners(int kind, bool cell)
    {
        string marked = "<w:bookmarkStart w:id='1' w:name='Adjacent'/><w:bookmarkEnd w:id='1'/>" + Control(1, Kind(kind), kind == 0 ? "-" : "Alpha", block: true);
        string body = cell ? "<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc>" + marked + "</w:tc></w:tr></w:tbl>" : marked;
        using var input = new MemoryStream(Package(Document(body)));
        var failure = Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(input, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.StartsWith("Control.ProtectedOwner:", failure.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void WordShapedEditorMarkupKeepsPrimaryAppearanceAndEditableValues(int kind)
    {
        XDocument xml = XDocument.Parse(Fixture("word-editor.xml"));
        XElement properties = xml.Descendants(W + "sdtPr").Single(); properties.Element(W + "richText")!.Remove();
        XElement temporary = XElement.Parse(Control(1, Kind(kind), "")); properties.Add(temporary.Element(W + "sdtPr")!.Elements().Where(e => e.Name != W + "id"));
        xml.Descendants(W + "t").Single().Value = kind == 0 ? "-" : "Alpha";
        byte[] source = Package(xml.ToString()); Validate(source);
        WordDocument document = Load(source);
        Assert.Contains(document.Diagnostics, d => d.Code == "Control.EditorMetadata" && d.Severity == DiagnosticSeverity.Info);
        Assert.DoesNotContain(document.Diagnostics, d => d.Code is "Docx.RunFormatting" or "Docx.Inline");
        DocumentContentControl handle = Assert.Single(document.ContentControls); var run = Assert.Single(Assert.Single(handle.Content).Runs);
        Assert.True(run.Formatting.Bold); Assert.Equal(14, run.Formatting.FontSize); Assert.Equal("Noto Sans", run.Formatting.FontFamily);
        switch (kind)
        {
            case 0: document.SetControlChecked(handle, true, TestContext.Current.CancellationToken); break;
            case 1: case 2: document.SetControlSelection(handle, "b", TestContext.Current.CancellationToken); break;
            case 3: document.SetControlDate(handle, ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken); break;
            case 4: document.ReplaceControlParagraphs(handle, handle.Content, TestContext.Current.CancellationToken); break;
            default: document.SetControlText(handle, "Edited", TestContext.Current.CancellationToken); break;
        }
        string expected = kind switch { 0 => "X", 1 or 2 => "Beta", 3 => "2024-02-29", 4 => "Alpha", _ => "Edited" };
        byte[] saved = Save(document); Validate(saved); Assert.Equal(expected, Assert.Single(Load(saved).ContentControls).DisplayText);
        Assert.Equal(expected, Pdf(document)); Assert.Equal(expected, Xps(document));
        XDocument result = Xml(saved, "word/document.xml");
        Assert.DoesNotContain(result.Descendants(), e => e.Name == W + "proofErr" || e.Name == W + "lastRenderedPageBreak" || e.Name == W + "szCs");
        Assert.All(result.Descendants(W + "rFonts"), e => Assert.Null(e.Attribute(W + "cs")));
    }

    [Theory]
    [InlineData("مرحبا")]
    [InlineData("漢字")]
    [InlineData("नमस्ते")]
    public void InactiveScriptSlotAdmissionCannotSilentlyFlattenActiveScriptFormatting(string text)
    {
        XDocument xml = XDocument.Parse(Fixture("word-editor.xml")); xml.Descendants(W + "t").Single().Value = text;
        var failure = Assert.Throws<NotSupportedException>(() => Load(Package(xml.ToString()), new ConversionOptions(FidelityMode.Lenient)));
        Assert.StartsWith("Control.ScriptFormatting:", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProofingLanguagesAloneDoNotRestrictOrdinaryUnicodeText()
    {
        string content = "<w:r><w:rPr><w:lang w:val='ru-RU'/></w:rPr><w:t>Привет</w:t></w:r>";
        WordDocument document = LoadBody(Control(1, "<w:richText/>", "", block: true, content: content));
        Assert.Equal("Привет", document.ContentControls[0].DisplayText); Validate(Save(document));
    }

    [Fact]
    public void EditorMetadataStillRejectsMalformedOrDuplicateValues()
    {
        foreach (string replacement in new[] { "<w:bCs w:val='maybe'/>", "<w:bCs/><w:bCs/>" })
        {
            string content = "<w:r><w:rPr>" + replacement + "</w:rPr><w:t>Text</w:t></w:r>";
            Assert.Throws<InvalidDataException>(() => LoadBody(Control(1, "<w:richText/>", "", block: true, content: content)));
        }
        XDocument xml = XDocument.Parse(Fixture("word-editor.xml")); xml.Descendants(W + "p").Single().SetAttributeValue(W14 + "paraId", "bad");
        Assert.Throws<InvalidDataException>(() => Load(Package(xml.ToString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedParagraphDirectionSurvivesSaveReloadAndSnapshotReplacement(bool rtl)
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Direction'><w:name w:val='Direction'/><w:pPr><w:bidi w:val='" + (rtl ? "1" : "0") + "'/></w:pPr></w:style></w:styles>";
        string paragraph = "<w:p><w:pPr><w:pStyle w:val='Direction'/></w:pPr>" + Run("Direction") + "</w:p>";
        string control = "<w:sdt><w:sdtPr><w:id w:val='1'/><w:richText/></w:sdtPr><w:sdtContent>" + paragraph + "</w:sdtContent></w:sdt>";
        byte[] source = Modify(Package(Document(control)), zip => { AddRelationship(zip, "word/_rels/document.xml.rels", "directionStyle", "styles", "styles.xml"); Put(zip, "word/styles.xml", styles); Override(zip, "word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"); });
        WordDocument document = Load(source); var handle = document.ContentControls[0]; document.ReplaceControlParagraphs(handle, handle.Content, TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word);
        Assert.Equal(rtl ? "1" : "0", (string?)Xml(word, "word/document.xml").Descendants(W + "bidi").First().Attribute(W + "val"));
        WordDocument reopened = Load(word); Assert.Equal("Direction", reopened.ContentControls[0].DisplayText); Validate(Save(reopened));
    }

    [Fact]
    public void DanglingGlossaryRelationshipsStillHaveAnExplicitRejection()
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        byte[] source = Modify(WithGlossary(control), zip =>
        {
            AddRelationship(zip, "word/glossary/_rels/document.xml.rels", "glossaryStyles", "styles", "styles.xml");
        });
        var failure = Assert.Throws<InvalidDataException>(() => Load(source, new ConversionOptions(FidelityMode.Lenient))); Assert.StartsWith("Binding.Store:", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "default")]
    [InlineData(1, "default")]
    [InlineData(2, "default")]
    [InlineData(3, "default")]
    [InlineData(4, "placeholder")]
    [InlineData(5, "placeholder")]
    public void StaticGlossaryCategoriesSurviveTypedEditsAndPlaceholderViews(int kind, string gallery)
    {
        string placeholder = kind == 0 ? "-" : "Choose";
        string metadata = "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
        string control = Control(1, Kind(kind), placeholder, metadata, block: true);
        byte[] source = WithCategory(control, placeholder, new XElement(W + "category",
            new XElement(W + "name", new XAttribute(W + "val", "General")), new XElement(W + "gallery", new XAttribute(W + "val", gallery))));
        Validate(source);
        WordDocument document = Load(source);
        DocumentContentControl handle = Assert.Single(document.ContentControls);
        Assert.True(handle.ShowingPlaceholder); Assert.Equal(placeholder, handle.DisplayText); Assert.Equal(placeholder, Pdf(document));
        switch (kind)
        {
            case 0: document.SetControlChecked(handle, true, TestContext.Current.CancellationToken); break;
            case 1: case 2: document.SetControlSelection(handle, "b", TestContext.Current.CancellationToken); break;
            case 3: document.SetControlDate(handle, ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken); break;
            case 4: document.ReplaceControlParagraphs(handle, handle.Content, TestContext.Current.CancellationToken); break;
            default: document.SetControlText(handle, "Edited", TestContext.Current.CancellationToken); break;
        }
        byte[] saved = Save(document); Validate(saved);
        XElement category = Assert.Single(Xml(saved, "word/glossary/document.xml").Descendants(W + "category"));
        Assert.Equal("General", (string?)category.Element(W + "name")!.Attribute(W + "val"));
        Assert.Equal(gallery, (string?)category.Element(W + "gallery")!.Attribute(W + "val"));
        WordDocument reopened = Load(saved); Assert.Equal(1, Assert.Single(reopened.ContentControls).Id);
        reopened.ShowControlPlaceholder(Assert.Single(reopened.ContentControls), TestContext.Current.CancellationToken);
        Assert.Equal(placeholder, Assert.Single(reopened.ContentControls).DisplayText); Assert.Equal(placeholder, Pdf(reopened));
        Validate(Save(reopened));
    }

    [Theory]
    [InlineData("<w:name w:val='General'/>")]
    [InlineData("<w:name w:val='General'/><w:name w:val='Other'/><w:gallery w:val='default'/>")]
    [InlineData("<w:name w:val='General'/><w:gallery w:val='default'/><w:extra/>")]
    [InlineData("<w:name w:val=''/><w:gallery w:val='default'/>")]
    public void InvalidGlossaryCategoriesAreRejected(string children)
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        XElement category = XElement.Parse("<w:category xmlns:w='" + W + "'>" + children + "</w:category>");
        Assert.Throws<InvalidDataException>(() => Load(WithCategory(control, "Choose", category)));
    }

    [Fact]
    public void NonPlaceholderGlossaryGalleriesAreRejected()
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        var category = new XElement(W + "category", new XElement(W + "name", new XAttribute(W + "val", "General")), new XElement(W + "gallery", new XAttribute(W + "val", "autoTxt")));
        Assert.Throws<NotSupportedException>(() => Load(WithCategory(control, "Choose", category)));
    }

    private static byte[] WithCategory(string control, string placeholder, XElement category) => Modify(WithGlossary(control, placeholder), zip =>
    {
        XDocument glossary = Xml(zip, "word/glossary/document.xml");
        glossary.Descendants(W + "docPartPr").Single().Element(W + "types")!.AddBeforeSelf(category);
        Put(zip, "word/glossary/document.xml", glossary.ToString());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextExportsAndDisplayViewsKeepUnicodeCheckboxesUntilFixedOutput(bool state)
    {
        string glyph = state ? "\u2612" : "\u2610";
        WordDocument document = LoadBody(Control(1, "<w14:checkbox><w14:checked w14:val='" + (state ? "1" : "0") + "'/></w14:checkbox>", glyph, block: true));
        var policy = new ConversionOptions(FidelityMode.BestEffort);
        Assert.Equal(glyph, Encoding.UTF8.GetString(Save(document, DocumentFormat.Text, policy)).Trim());
        Assert.Contains(glyph, Encoding.UTF8.GetString(Save(document, DocumentFormat.Markdown, policy)), StringComparison.Ordinal);
        Assert.Equal(glyph, new HtmlParser().ParseDocument(Encoding.UTF8.GetString(Save(document, DocumentFormat.Html, policy))).Body!.TextContent.Trim());
        WordDocument view = document.CreateControlDisplayView(out ConversionResult result, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Control.GlyphFallback"); Assert.Equal(glyph, view.Paragraphs[0].Text); Assert.Empty(view.ContentControls);
        Assert.Equal(glyph, Xml(Save(view), "word/document.xml").Descendants(W + "t").Single().Value);
        Assert.Equal(state ? "[x]" : "[]", Compact(Pdf(view))); Assert.Equal(state ? "[x]" : "[]", Compact(Xps(view))); Assert.Equal(glyph, view.Paragraphs[0].Text);
        WordDocument destination = WordDocument.Create(); destination.Append(view, cancellationToken: TestContext.Current.CancellationToken); Assert.Equal(state ? "[x]" : "[]", Compact(Pdf(destination)));
        view.ReplaceParagraph(0, new[] { new DocumentText("Caller edit") }, TestContext.Current.CancellationToken); Assert.Equal("Caller edit", Pdf(view));
    }

    [Theory]
    [InlineData("left", "A\tB")]
    [InlineData("both", "AB")]
    [InlineData("distribute", "AB")]
    public void UnsupportedManagedUnderlinesCanStillFlattenToEditableAndTextViews(string alignment, string text)
    {
        string content = "<w:r><w:rPr><w:u w:val='single'/></w:rPr><w:t>" + text + "</w:t></w:r>";
        WordDocument document = LoadBody("<w:p><w:pPr><w:jc w:val='" + alignment + "'/></w:pPr>" + Control(1, "<w:richText/>", "", content: content) + "</w:p>");
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken); Assert.Equal(text, view.Paragraphs[0].Text); Assert.Empty(view.ContentControls); Validate(Save(view));
        var policy = new ConversionOptions(FidelityMode.BestEffort);
        Assert.Equal(text, Encoding.UTF8.GetString(Save(document, DocumentFormat.Text, policy)).Trim());
        Assert.Contains("AB", Compact(Encoding.UTF8.GetString(Save(document, DocumentFormat.Markdown, policy))), StringComparison.Ordinal);
        Assert.Equal(Compact(text), Compact(new HtmlParser().ParseDocument(Encoding.UTF8.GetString(Save(document, DocumentFormat.Html, policy))).Body!.TextContent));
        using var output = new MemoryStream(); output.WriteByte(9); Assert.Throws<NotSupportedException>(() => view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 9 }, output.ToArray());
    }

    [Fact]
    public void SnapshotReusePreservesFailedOperationsAndInvalidatesOnlyCommittedEdits()
    {
        WordDocument document = Load(Package(Fixture("checkbox.xml"))); IReadOnlyList<DocumentContentControl> original = document.ContentControls;
        Assert.Same(original, document.ContentControls); Assert.Throws<ArgumentException>(() => document.SetControlSelection(original[0], "missing", TestContext.Current.CancellationToken)); Assert.Same(original, document.ContentControls);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); Assert.Throws<OperationCanceledException>(() => document.SetControlChecked(original[0], true, canceled.Token)); Assert.Same(original, document.ContentControls);
        Validate(Save(document)); Assert.Same(original, document.ContentControls);
        document.Title = "Metadata"; IReadOnlyList<DocumentContentControl> current = document.ContentControls; Assert.NotSame(original, current); Assert.Same(current, document.ContentControls);
        Assert.Throws<ArgumentException>(() => document.SetControlChecked(original[0], true, TestContext.Current.CancellationToken)); document.SetControlChecked(current[0], true, TestContext.Current.CancellationToken);
        Assert.False(original[0].IsChecked); Assert.True(document.ContentControls[0].IsChecked);
        var beforeNoOp = document.ContentControls[0]; Assert.False(document.RemoveCustomProperty("Absent", TestContext.Current.CancellationToken)); Assert.Throws<ArgumentException>(() => document.SetControlChecked(beforeNoOp, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ControlFreeLoadsRetainOrdinaryContentAndPermitNormalEditing()
    {
        string body = string.Concat(Enumerable.Range(0, 100).Select(i => "<w:p>" + Run("Paragraph " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Ω") + "</w:p>"));
        WordDocument document = LoadBody(body); Assert.Empty(document.ContentControls); Assert.Equal(100, document.Paragraphs.Count); Assert.DoesNotContain(document.Diagnostics, d => d.Code.StartsWith("Control.", StringComparison.Ordinal));
        document.AddParagraph("Editable", cancellationToken: TestContext.Current.CancellationToken); byte[] word = Save(document); Validate(word);
        Assert.Equal("Paragraph 0 Ω", Xml(word, "word/document.xml").Descendants(W + "p").First().Value); Assert.Equal("Editable", Xml(word, "word/document.xml").Descendants(W + "p").Last().Value);
    }

    [Fact]
    public void DeferredGlyphProvenanceStaysWithinTightenedSessionBudgets()
    {
        WordDocument document = LoadBody(Control(1, "<w14:checkbox/>", "\u2610", block: true)); WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(5);
        Assert.Throws<InvalidDataException>(() => view.Save(output, DocumentFormat.Pdf, new ConversionOptions(maximumCharacters: 1), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 5 }, output.ToArray());
        Assert.Equal("\u2610", view.Paragraphs[0].Text); Assert.Equal("[]", Compact(Pdf(view)));
    }

    [Fact]
    public void FragmentInsertionRetainsDisplayGlyphPreparationAndBudgets()
    {
        WordDocument document = LoadBody(Control(1, "<w14:checkbox/>", "\u2610", block: true));
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        WordDocument target = WordDocument.Create(); target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("place", 0, 0, 6, TestContext.Current.CancellationToken); target.InsertFragmentAtBookmark("place", view, TestContext.Current.CancellationToken);
        Assert.Equal("[]", Compact(Pdf(target)));
        using var output = new MemoryStream(); output.WriteByte(5);
        Assert.Throws<InvalidDataException>(() => target.Save(output, DocumentFormat.Pdf, new ConversionOptions(maximumCharacters: 1), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 5 }, output.ToArray());
    }
}
