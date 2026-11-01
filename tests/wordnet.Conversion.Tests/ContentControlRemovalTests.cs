using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlRemovalTests
{
    private static DocumentContentControl ById(WordDocument document, int id) => document.ContentControls.Single(control => control.Id == id);

    [Theory]
    [InlineData(true, "Before one / two after")]
    [InlineData(false, "Before  / two after")]
    public void InlineRemovalSeparatesWrapperAndContentAndInvalidatesOldHandles(bool preserve, string expected)
    {
        string body = "<w:p>" + Run("Before ") + Control(1, "<w:text/>", "one") + Run(" / ") + Control(2, "<w:text/>", "two") + Run(" after") + "</w:p>";
        WordDocument document = LoadBody(body); DocumentContentControl old = ById(document, 2);
        Assert.Equal(1, document.RemoveControl(ById(document, 1), preserve, TestContext.Current.CancellationToken).Changes);
        Assert.Equal(expected, Assert.Single(document.Paragraphs).Text);
        Assert.Throws<ArgumentException>(() => document.RemoveControl(old, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("two", Assert.Single(document.ContentControls).DisplayText);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("2", (string?)Assert.Single(Xml(saved, "word/document.xml").Descendants(W + "sdtPr")).Element(W + "id")!.Attribute(W + "val"));
        Assert.Equal(expected, Assert.Single(Load(saved).Paragraphs).Text);
        Assert.Equal(Compact(expected), Compact(Pdf(document)));
    }

    [Theory]
    [InlineData("rich")]
    [InlineData("plain")]
    [InlineData("checkbox")]
    [InlineData("dropdown")]
    [InlineData("combo")]
    [InlineData("date")]
    public void EveryAdmittedKindCanLoseItsWrapperWhileKeepingItsText(string kind)
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
        string text = kind switch { "checkbox" => "-", "dropdown" or "combo" => "Alpha", "date" => "2000-01-01", _ => "text" };
        WordDocument document = LoadBody("<w:p>" + Control(1, type, text) + "</w:p>");
        string expected = Assert.Single(document.ContentControls).DisplayText;
        document.RemoveControl(Assert.Single(document.ContentControls), cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal(expected, Assert.Single(document.Paragraphs).Text);
        Assert.Empty(Xml(saved, "word/document.xml").Descendants(W + "sdt"));
        Assert.Equal(Compact(expected), Compact(Pdf(document)));
    }

    [Theory]
    [InlineData("unlocked", true, true)]
    [InlineData("sdtLocked", true, false)]
    [InlineData("contentLocked", true, true)]
    [InlineData("sdtContentLocked", true, false)]
    [InlineData("unlocked", false, true)]
    [InlineData("sdtLocked", false, false)]
    [InlineData("contentLocked", false, false)]
    [InlineData("sdtContentLocked", false, false)]
    public void WrapperAndContentLocksAreRespected(string lockValue, bool preserve, bool allowed)
    {
        string metadata = "<w:lock w:val='" + lockValue + "'/>";
        WordDocument document = LoadBody("<w:p>" + Control(1, "<w:text/>", "safe", metadata) + "</w:p>"); byte[] before = Save(document);
        DocumentContentControl handle = Assert.Single(document.ContentControls);
        if (!allowed)
        {
            Assert.Contains("Control.Locked", Assert.Throws<InvalidOperationException>(() => document.RemoveControl(handle, preserve, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
            Assert.Equal(before, Save(document)); Assert.Single(document.ContentControls);
        }
        else
        {
            document.RemoveControl(handle, preserve, TestContext.Current.CancellationToken);
            Assert.Empty(document.ContentControls); Assert.Equal(preserve ? "safe" : "", Assert.Single(document.Paragraphs).Text);
            byte[] saved = Save(document); Validate(saved); Assert.Empty(Xml(saved, "word/document.xml").Descendants(W + "sdt"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockRemovalInACellKeepsValidCellStructure(bool preserve)
    {
        string table = "<w:tbl><w:tblPr><w:tblW w:w='4000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc>"
            + Control(5, "<w:richText/>", "Cell content", block: true) + "</w:tc></w:tr></w:tbl>";
        WordDocument document = LoadBody(table); document.RemoveControl(Assert.Single(document.ContentControls), preserve, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved); XDocument xml = Xml(saved, "word/document.xml");
        XElement cell = Assert.Single(xml.Descendants(W + "tc")); Assert.Single(cell.Elements(W + "p")); Assert.Empty(cell.Descendants(W + "sdt"));
        Assert.Equal(preserve ? "Cell content" : "", string.Concat(cell.Descendants(W + "t").Select(element => element.Value)));
        Assert.Equal(preserve ? "Cellcontent" : "", Compact(Pdf(document)));
    }

    [Fact]
    public void RemovingANestedChildRefreshesItsParentAndCannotBypassLocks()
    {
        string child = Control(2, "<w:text/>", "child");
        string parent = Control(1, "<w:richText/>", "", block: true, content: Run("Before ") + child + Run(" after"));
        WordDocument document = LoadBody(parent); byte[] before = Save(document);
        Assert.Throws<NotSupportedException>(() => document.RemoveControl(ById(document, 1), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(document));
        document.RemoveControl(ById(document, 2), preserveContent: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Before  after", Assert.Single(document.ContentControls).DisplayText); Validate(Save(document));
        string lockedParent = Control(1, "<w:richText/>", "", "<w:lock w:val='contentLocked'/>", block: true, content: child);
        WordDocument locked = LoadBody(lockedParent); byte[] lockedBefore = Save(locked);
        Assert.Throws<InvalidOperationException>(() => locked.RemoveControl(ById(locked, 2), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(lockedBefore, Save(locked));
    }

    [Fact]
    public void DirtyBindingsAndLastResourceOwnerRequireAnExplicitDecision()
    {
        string bound = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + "</w:p>";
        WordDocument document = Load(WithStore(bound)); document.SetControlText(ById(document, 1), "changed", TestContext.Current.CancellationToken);
        Assert.Contains("Control.BindingDirty", Assert.Throws<InvalidOperationException>(() => document.RemoveControl(ById(document, 1), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        document.DetachControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] before = Save(document);
        Assert.Contains("Control.Resources", Assert.Throws<NotSupportedException>(() => document.RemoveControl(ById(document, 1), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(document));
        Assert.Equal("Ada & Co", (string?)Xml(before, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
    }

    [Fact]
    public void RemovingOneBindingRetainsTheSharedLocalStoreAndOtherControls()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached", Binding("/p:invoice/p:customer/@name")) + Run(" / ")
            + Control(2, "<w:text/>", "other") + "</w:p>";
        WordDocument document = Load(WithStore(body)); document.RemoveControl(ById(document, 1), cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal("cached / other", Assert.Single(document.Paragraphs).Text);
        Assert.Equal("Ada & Co", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        Assert.Equal(2, Assert.Single(Load(saved).ContentControls).Id);
        Assert.Throws<NotSupportedException>(() => document.RemoveControl(ById(document, 2), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(saved, Save(document));
    }

    [Fact]
    public void LastGlossaryOwnerCannotBeRemovedWhileItsLocalResourcesRemain()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "cached") + "</w:p>";
        WordDocument document = Load(WithGlossary(body)); byte[] before = Save(document);
        Assert.Contains("Control.Resources", Assert.Throws<NotSupportedException>(() => document.RemoveControl(
            ById(document, 1), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(document)); Assert.Single(document.ContentControls);
    }

    [Fact]
    public void PreservedRichTextKeepsTheFormattingOfItsRuns()
    {
        string runs = "<w:r><w:rPr><w:b/><w:color w:val='2266CC'/></w:rPr><w:t>Bold</w:t></w:r>" + Run(" plain");
        WordDocument document = LoadBody("<w:p>" + Control(1, "<w:richText/>", "", content: runs) + "</w:p>");
        document.RemoveControl(Assert.Single(document.ContentControls), cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        XElement[] savedRuns = Xml(saved, "word/document.xml").Descendants(W + "r").ToArray();
        Assert.NotNull(savedRuns[0].Element(W + "rPr")!.Element(W + "b"));
        Assert.Equal("2266CC", (string?)savedRuns[0].Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val"));
        Assert.Equal("Bold plain", Assert.Single(document.Paragraphs).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemovingASharedStoryControlUpdatesBothPagesWithoutDuplicatingOwners(bool preserve)
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/><w:type w:val='nextPage'/></w:sectPr>";
        string body = "<w:p><w:pPr>" + section + "</w:pPr>" + Run("First") + "</w:p><w:p>" + Run("Second") + "</w:p><w:sectPr/>";
        byte[] source = Modify(Package(Document(body)), zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "header", "header", "header1.xml");
            Put(zip, "word/header1.xml", "<w:hdr xmlns:w='" + W + "'>" + Control(3, "<w:text/>", "Header", block: true) + "</w:hdr>");
            Override(zip, "word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml");
        });
        WordDocument document = Load(source); Assert.Single(document.ContentControls);
        document.RemoveControl(Assert.Single(document.ContentControls), preserve, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Empty(Xml(saved, "word/header1.xml").Descendants(W + "sdt"));
        using PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); Assert.Equal(2, pdf.NumberOfPages);
        Assert.All(pdf.GetPages(), page => Assert.Equal(preserve, page.Text.Contains("Header")));
    }

    [Fact]
    public void CancellationAndForeignHandlesLeaveTheSourceAndHandlesUntouched()
    {
        string body = "<w:p>" + Control(1, "<w:text/>", "safe") + "</w:p>";
        WordDocument document = LoadBody(body), foreign = LoadBody(body); byte[] before = Save(document);
        Assert.Throws<ArgumentException>(() => document.RemoveControl(Assert.Single(foreign.ContentControls), cancellationToken: TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.RemoveControl(Assert.Single(document.ContentControls), cancellationToken: cancelled.Token));
        Assert.Equal(before, Save(document));
        document.RemoveControl(Assert.Single(document.ContentControls), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(document.ContentControls);
    }
}
