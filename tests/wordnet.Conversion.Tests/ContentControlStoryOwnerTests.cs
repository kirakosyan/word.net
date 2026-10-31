using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlStoryOwnerTests
{
    private const string HeaderRef = "<w:headerReference w:type='default' r:id='header'/>";
    private const string FooterRef = "<w:footerReference w:type='default' r:id='footer'/>";
    private static readonly int[] StoryIds = { 10, 11 };
    private static DocumentContentControl ById(WordDocument document, int id) => document.ContentControls.Single(control => control.Id == id);
    private static byte[] WithStories(byte[] source, string? header = null, string? footer = null) => Modify(source, zip =>
    {
        foreach (var story in new[] { (Kind: "header", Root: "hdr", Body: header), (Kind: "footer", Root: "ftr", Body: footer) })
        {
            if (story.Body == null) continue;
            string part = "word/" + story.Kind + "1.xml";
            AddRelationship(zip, "word/_rels/document.xml.rels", story.Kind, story.Kind, story.Kind + "1.xml");
            Put(zip, part, "<w:" + story.Root + " xmlns:w='" + W + "' xmlns:w14='" + W14 + "' xmlns:mc='http://schemas.openxmlformats.org/markup-compatibility/2006' mc:Ignorable='w14'>" + story.Body + "</w:" + story.Root + ">");
            Override(zip, part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + story.Kind + "+xml");
        }
    });
    private static byte[] Source(string body, string? header = null, string? footer = null) => WithStories(
        Package(Document(body + "<w:sectPr>" + (header == null ? "" : HeaderRef) + (footer == null ? "" : FooterRef) + "</w:sectPr>")), header, footer);

    [Fact]
    public void SyntheticWordDerivedStoriesMatchIndependentPageOrderAndPlacement()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentControls", "StoryOwners");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, "stories.docx")); Validate(source);
        WordDocument document = Load(source); Assert.Equal(2, document.ContentControls.Count);
        using PdfDocument reference = PdfDocument.Open(File.ReadAllBytes(Path.Combine(directory, "stories.pdf")));
        using PdfDocument actual = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        Assert.Equal(2, reference.NumberOfPages); Assert.Equal(2, actual.NumberOfPages);
        for (int page = 1; page <= 2; page++)
        {
            var expected = reference.GetPage(page); var result = actual.GetPage(page);
            Assert.Equal(Compact(expected.Text), Compact(result.Text));
            foreach (var rendered in new[] { expected, result })
            {
                var words = rendered.GetWords().ToArray();
                double header = words.Single(word => word.Text == "Reference").BoundingBox.Bottom;
                double body = words.Single(word => word.Text == (page == 1 ? "First" : "Second")).BoundingBox.Bottom;
                double footer = words.Single(word => word.Text == "Footer").BoundingBox.Bottom;
                Assert.True(header > body && body > footer);
            }
        }
        byte[] saved = Save(document); Validate(saved); Assert.Equal(2, Load(saved).ContentControls.Count);
    }

    [Fact]
    public void LocalNamespaceDeclarationsAreNotParagraphLayoutProperties()
    {
        string header = "<w:p><w:pPr><w:spacing xmlns:local='urn:test' w:after='0'/></w:pPr>" + Control(2, "<w:text/>", "header") + "</w:p>";
        WordDocument document = Load(Source("<w:p>" + Run("Body") + "</w:p>", header));
        Assert.Equal("header", ById(document, 2).DisplayText); Validate(Save(document));
    }

    [Fact]
    public void HeaderAndFooterValuesRemainEditableAndDisplayInBothFixedFormats()
    {
        string header = "<w:p>" + Run("Account ") + Control(2, "<w:text/>", "old") + Run(" end") + "</w:p>";
        string footer = Control(3, Checkbox(), "-", block: true);
        byte[] source = Source("<w:p>" + Run("Body") + "</w:p>", header, footer); Validate(source);
        WordDocument document = Load(source);
        Assert.Equal("inline header/footer paragraph", ById(document, 2).Owner);
        Assert.Equal("block header/footer paragraphs", ById(document, 3).Owner);
        DocumentContentControl old = ById(document, 2);
        document.SetControlText(old, "new", TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => document.SetControlText(old, "stale", TestContext.Current.CancellationToken));
        document.SetControlChecked(ById(document, 3), true, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved); WordDocument loaded = Load(saved);
        Assert.Equal("new", ById(loaded, 2).DisplayText); Assert.True(ById(loaded, 3).IsChecked);
        Assert.Single(Xml(saved, "word/header1.xml").Descendants(W + "sdt"));
        Assert.Single(Xml(saved, "word/footer2.xml").Descendants(W + "sdt"));
        Assert.Contains("Accountnewend", Compact(Pdf(document)), StringComparison.Ordinal);
        Assert.Contains("Accountnewend", Compact(Xps(document)), StringComparison.Ordinal);
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Contains("Accountnewend", Compact(Pdf(view)), StringComparison.Ordinal);
        byte[] flattened = Save(view); Assert.Empty(Xml(flattened, "word/header1.xml").Descendants(W + "sdt"));
        Assert.Empty(Xml(flattened, "word/footer2.xml").Descendants(W + "sdt")); Assert.Equal(2, document.ContentControls.Count);
    }

    [Fact]
    public void ASharedInheritedHeaderHasOneOwnerAndEditsAppearOnBothPages()
    {
        string firstSection = "<w:sectPr>" + HeaderRef + "<w:type w:val='nextPage'/></w:sectPr>";
        string body = "<w:p><w:pPr>" + firstSection + "</w:pPr>" + Run("First") + "</w:p><w:p>" + Run("Second") + "</w:p><w:sectPr/>";
        byte[] source = WithStories(Package(Document(body)), "<w:p>" + Control(5, "<w:text/>", "Original") + "</w:p>");
        WordDocument document = Load(source); Assert.Single(document.ContentControls);
        document.SetControlText(ById(document, 5), "Updated", TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved); Assert.Single(Load(saved).ContentControls);
        Assert.Single(Xml(saved, "word/header1.xml").Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal(2, pdf.NumberOfPages);
        Assert.All(pdf.GetPages(), page => Assert.Contains("Updated", page.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("header", "default")]
    [InlineData("header", "first")]
    [InlineData("header", "even")]
    [InlineData("footer", "default")]
    [InlineData("footer", "first")]
    [InlineData("footer", "even")]
    public void TypedStoryVariantsDisplayOnTheirSelectedPages(string kind, string type)
    {
        string reference = "<w:" + kind + "Reference w:type='" + type + "' r:id='" + kind + "'/>";
        string section = "<w:sectPr>" + reference + (type == "first" ? "<w:titlePg/>" : "") + "</w:sectPr>";
        string body = "<w:p>" + Run("First") + "<w:r><w:br w:type='page'/></w:r>" + Run("Second") + "</w:p>" + section;
        string content = "<w:p>" + Control(8, "<w:text/>", "Selected") + "</w:p>";
        byte[] source = WithStories(Package(Document(body)), kind == "header" ? content : null, kind == "footer" ? content : null);
        if (type == "even") source = Modify(source, zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "settings", "settings", "settings.xml");
            Put(zip, "word/settings.xml", "<w:settings xmlns:w='" + W + "'><w:evenAndOddHeaders/></w:settings>");
            Override(zip, "word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml");
        });
        WordDocument document = Load(source); document.SetControlText(ById(document, 8), "Updated", TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved); Assert.Equal("Updated", ById(Load(saved), 8).DisplayText);
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal(type != "even", pdf.GetPage(1).Text.Contains("Updated"));
        Assert.Equal(type != "first", pdf.GetPage(2).Text.Contains("Updated"));
    }

    [Theory]
    [InlineData("rich", false)]
    [InlineData("rich", true)]
    [InlineData("plain", false)]
    [InlineData("plain", true)]
    [InlineData("checkbox", false)]
    [InlineData("checkbox", true)]
    [InlineData("dropdown", false)]
    [InlineData("dropdown", true)]
    [InlineData("combo", false)]
    [InlineData("combo", true)]
    [InlineData("date", false)]
    [InlineData("date", true)]
    public void EveryAdmittedKindEditsInPlainHeaderAndFooterOwners(string kind, bool block)
    {
        string type = kind switch
        {
            "rich" => "<w:richText/>",
            "plain" => "<w:text/>",
            "checkbox" => Checkbox(),
            "dropdown" => Choices,
            "combo" => "<w:comboBox><w:listItem w:displayText='Alpha' w:value='a'/></w:comboBox>",
            _ => "<w:date w:fullDate='2000-01-01T00:00:00Z'><w:dateFormat w:val='yyyy-MM-dd'/><w:lid w:val='en-US'/><w:calendar w:val='gregorian'/></w:date>"
        };
        string text = kind switch { "checkbox" => "-", "dropdown" or "combo" => "Alpha", "date" => "2000-01-01", _ => "old" };
        string Story(int id)
        {
            string control = Control(id, type, text, block: block);
            return block ? control : "<w:p>" + control + "</w:p>";
        }
        WordDocument document = Load(Source("<w:p>" + Run("Body") + "</w:p>", Story(10), Story(11)));
        foreach (int id in StoryIds)
        {
            DocumentContentControl control = ById(document, id);
            switch (kind)
            {
                case "rich" when block: document.ReplaceControlParagraphs(control, new[] { new ContentControlParagraph(new[] { new DocumentText("new") }) }, TestContext.Current.CancellationToken); break;
                case "rich": document.ReplaceControlContent(control, new[] { new DocumentText("new") }, TestContext.Current.CancellationToken); break;
                case "plain": document.SetControlText(control, "new", TestContext.Current.CancellationToken); break;
                case "checkbox": document.SetControlChecked(control, true, TestContext.Current.CancellationToken); break;
                case "dropdown": document.SetControlSelection(control, "b", TestContext.Current.CancellationToken); break;
                case "combo": document.SetControlComboText(control, "new", TestContext.Current.CancellationToken); break;
                default: document.SetControlDate(control, ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken); break;
            }
        }
        byte[] saved = Save(document); Validate(saved); WordDocument reloaded = Load(saved);
        string expected = kind switch { "checkbox" => "X", "dropdown" => "Beta", "date" => "2026-10-31", _ => "new" };
        Assert.Equal(expected, ById(reloaded, 10).DisplayText); Assert.Equal(expected, ById(reloaded, 11).DisplayText);
    }

    [Fact]
    public void BodyAndStoryIdsAreReservedTogetherBeforeAssigningMissingIds()
    {
        string body = "<w:p>" + Control(null, "<w:text/>", "body") + "</w:p>";
        string header = "<w:p>" + Control(1, "<w:text/>", "header") + "</w:p>";
        string footer = "<w:p>" + Control(null, "<w:text/>", "footer") + "</w:p>";
        WordDocument document = Load(Source(body, header, footer));
        Assert.Equal("body", ById(document, 2).DisplayText); Assert.Equal("header", ById(document, 1).DisplayText); Assert.Equal("footer", ById(document, 3).DisplayText);
        byte[] saved = Save(document); Validate(saved); Assert.Equal(3, Load(saved).ContentControls.Count);
        Assert.Throws<InvalidDataException>(() => Load(Source("<w:p>" + Control(1, "<w:text/>", "body") + "</w:p>", header)));
        Assert.Throws<InvalidDataException>(() => Load(Source("<w:p>" + Run("body") + "</w:p>", header, header)));
    }

    [Fact]
    public void StoryBindingIsExplicitAndRetainsAtomicWriteBack()
    {
        string body = "<w:p>" + Run("Body") + "</w:p><w:sectPr>" + HeaderRef + "</w:sectPr>";
        string header = "<w:p>" + Control(7, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p>";
        WordDocument document = Load(WithStories(WithStore(body), header));
        Assert.Equal("cached", ById(document, 7).DisplayText);
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal("Ada & Co", ById(document, 7).DisplayText);
        document.SetControlText(ById(document, 7), "New & Co", TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidOperationException>(() => document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("New & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal("New & Co", ById(Load(saved), 7).DisplayText);
    }

    [Fact]
    public void StoryAndBodyControlsSharingOneBindingMustAgreeAtomically()
    {
        const string path = "/p:invoice/p:customer/@name";
        string body = "<w:p>" + Control(1, "<w:text/>", "body", Binding(path)) + "</w:p><w:sectPr>" + HeaderRef + "</w:sectPr>";
        string header = "<w:p>" + Control(2, "<w:text/>", "header", Binding(path)) + "</w:p>";
        WordDocument document = Load(WithStories(WithStore(body), header));
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        document.SetControlText(ById(document, 1), "one", TestContext.Current.CancellationToken);
        document.SetControlText(ById(document, 2), "two", TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken));
        Assert.All(document.ContentControls, control => Assert.Equal(ContentControlBindingState.Dirty, control.BindingState));
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.All(document.ContentControls, control => Assert.Equal("Ada & Co", control.DisplayText));
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("Ada & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
    }

    [Fact]
    public void StoryLocksAndCancellationLeaveTheDocumentUntouched()
    {
        string header = "<w:p>" + Control(4, "<w:text/>", "safe", "<w:lock w:val='contentLocked'/>") + "</w:p>";
        string footer = "<w:p>" + Control(5, "<w:text/>", "editable") + "</w:p>";
        WordDocument document = Load(Source("<w:p>" + Run("Body") + "</w:p>", header, footer)); byte[] before = Save(document);
        Assert.Throws<InvalidOperationException>(() => document.SetControlText(ById(document, 4), "forbidden", TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.SetControlText(ById(document, 5), "cancelled", cancelled.Token));
        Assert.Equal(before, Save(document)); Assert.Equal("safe", ById(document, 4).DisplayText);
        document.SetControlText(ById(document, 5), "allowed", TestContext.Current.CancellationToken); Validate(Save(document));
    }

    [Fact]
    public void GlobalControlLimitIncludesDistinctStoryParts()
    {
        string body = string.Concat(Enumerable.Range(0, 1023).Select(id => Control(id, "<w:text/>", "x", block: true)));
        string header = "<w:p>" + Control(1023, "<w:text/>", "x") + "</w:p>";
        Assert.Equal(1024, Load(Source(body, header)).ContentControls.Count);
        string footer = "<w:p>" + Control(1024, "<w:text/>", "x") + "</w:p>";
        Assert.Throws<InvalidDataException>(() => Load(Source(body, header, footer)));
    }

    [Fact]
    public void StoryControlsCannotCrossBookmarksOrUseNestedOwners()
    {
        string control = Control(2, "<w:text/>", "inside");
        string crossing = "<w:p><w:bookmarkStart w:id='9' w:name='cross'/>" + control + "<w:bookmarkEnd w:id='9'/></w:p>";
        Assert.Throws<NotSupportedException>(() => Load(Source("<w:p>" + Run("Body") + "</w:p>", crossing)));
        string nested = Control(1, "<w:richText/>", "", block: true, content: control);
        Assert.Throws<NotSupportedException>(() => Load(Source("<w:p>" + Run("Body") + "</w:p>", nested)));
    }
}
