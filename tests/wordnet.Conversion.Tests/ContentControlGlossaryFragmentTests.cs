using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlGlossaryFragmentTests
{
    private const string Placeholder = "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
    private const string GuidValue = "{12345678-1234-1234-1234-123456789012}";
    private static readonly string[] ResourceKinds = { "styles", "settings", "fontTable", "webSettings" };
    private static readonly string[] Slots = { "One", "Two" };
    private static string Bookmark(string name, int id) => "<w:p><w:bookmarkStart w:id='" + id + "' w:name='" + name + "'/>" + Run("Insert") + "<w:bookmarkEnd w:id='" + id + "'/></w:p>";
    private static byte[] WithGuid(string body, string prompt) => Modify(WithGlossary(body, prompt), zip =>
    {
        XDocument glossary = Xml(zip, "word/glossary/document.xml");
        glossary.Descendants(W + "docPartPr").Single().Add(new XElement(W + "guid", new XAttribute(W + "val", GuidValue)));
        Put(zip, "word/glossary/document.xml", glossary.ToString());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlossaryFragmentsRenameEntriesAndKeepIndependentPlaceholderValues(bool cell)
    {
        string host = "<w:p>" + Control(1, "<w:text/>", "Host", Placeholder + "<w:tag w:val='host'/>") + "</w:p>";
        string slots = Bookmark("one", 1) + Bookmark("two", 2);
        if (cell) slots = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4000'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='4000'/></w:tcPr>" + slots + "</w:tc></w:tr></w:tbl>";
        WordDocument target = Load(WithGuid(host + slots, "Host"), new ConversionOptions(FidelityMode.Lenient));
        string fragment = "<w:p>" + Control(1, "<w:text/>", "Choose", Placeholder + "<w:tag w:val='source'/>") + "</w:p>";
        WordDocument source = Load(WithGuid(fragment, "Choose"), new ConversionOptions(FidelityMode.Lenient)); byte[] before = Save(source);
        if (cell) { target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken); target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken); }
        else { target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken); }
        byte[] inserted = Save(target); Validate(inserted);
        XDocument glossary = Xml(inserted, "word/glossary/document.xml");
        string[] names = glossary.Descendants(W + "docPartPr").Select(p => (string)p.Element(W + "name")!.Attribute(W + "val")!).ToArray();
        string[] guids = glossary.Descendants(W + "docPartPr").Select(p => (string)p.Element(W + "guid")!.Attribute(W + "val")!).ToArray();
        Assert.Equal(3, names.Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.Equal(3, guids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Prompt", names[0]); Assert.Equal(GuidValue, guids[0]);
        XDocument main = Xml(inserted, "word/document.xml");
        Assert.Equal(names, main.Descendants(W + "placeholder").Select(p => (string)p.Element(W + "docPart")!.Attribute(W + "val")!));
        target.SetControlText(target.ContentControls.First(c => c.Tag == "source"), "Edited", TestContext.Current.CancellationToken);
        target.ShowControlPlaceholder(target.ContentControls.First(c => c.Tag == "source"), TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved); WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal("Host", reopened.ContentControls.Single(c => c.Tag == "host").DisplayText);
        Assert.All(reopened.ContentControls.Where(c => c.Tag == "source"), c => { Assert.Equal("Choose", c.DisplayText); Assert.True(c.ShowingPlaceholder); });
        Assert.Equal(before, Save(source));
    }

    [Fact]
    public void ANewGlossaryGetsAnIsolatedPackagePartAndRemainsEditable()
    {
        string fragment = "<w:p>" + Control(1, "<w:text/>", "Choose", Placeholder) + "</w:p>";
        WordDocument source = Load(WithGuid(fragment, "Choose")); var target = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken); target.AddBookmark("one", 0, 0, 6, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); byte[] saved = Save(target); Validate(saved);
        Assert.NotNull(Xml(saved, "word/glossary/wordnet-fragment-1.xml").Root); WordDocument reopened = Load(saved);
        reopened.SetControlText(reopened.ContentControls[0], "New", TestContext.Current.CancellationToken);
        reopened.ShowControlPlaceholder(reopened.ContentControls[0], TestContext.Current.CancellationToken);
        Assert.Equal("Choose", reopened.ContentControls[0].DisplayText); Validate(Save(reopened));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryAdmittedControlKindKeepsItsCopiedGlossaryPlaceholder(int kind)
    {
        string type = kind switch
        {
            0 => "<w:text/>",
            1 => Checkbox(),
            2 => Choices,
            3 => Choices.Replace("dropDownList", "comboBox"),
            4 => "<w:date><w:dateFormat w:val='yyyy-MM-dd'/></w:date>",
            _ => "<w:richText/>"
        };
        string prompt = kind == 1 ? "-" : "Choose";
        string fragment = kind == 5 ? Control(1, type, prompt, Placeholder, block: true)
            : "<w:p>" + Control(1, type, prompt, Placeholder) + "</w:p>";
        WordDocument source = Load(WithGuid(fragment, prompt)); byte[] before = Save(source);
        var target = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken); target.AddBookmark("one", 0, 0, 6, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        switch (kind)
        {
            case 0: target.SetControlText(target.ContentControls[0], "Edited", TestContext.Current.CancellationToken); break;
            case 1: target.SetControlChecked(target.ContentControls[0], true, TestContext.Current.CancellationToken); break;
            case 2: case 3: target.SetControlSelection(target.ContentControls[0], "b", TestContext.Current.CancellationToken); break;
            case 4: target.SetControlDate(target.ContentControls[0], ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken); break;
            default: target.ReplaceControlParagraphs(target.ContentControls[0], new[] { new ContentControlParagraph(new[] { new DocumentText("Edited") }) }, TestContext.Current.CancellationToken); break;
        }
        target.ShowControlPlaceholder(target.ContentControls[0], TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved); WordDocument reopened = Load(saved);
        Assert.Equal(prompt, reopened.ContentControls[0].DisplayText); Assert.True(reopened.ContentControls[0].ShowingPlaceholder); Assert.Equal(before, Save(source));
    }

    [Fact]
    public void IdenticalLocalGlossaryResourcesAreCopiedOnceAcrossRepeatedInsertions()
    {
        string fragment = "<w:p>" + Control(1, "<w:text/>", "Choose", Placeholder) + "</w:p>";
        byte[] input = Modify(WithGuid(fragment, "Choose"), zip =>
        {
            foreach (string kind in ResourceKinds)
            {
                string part = "word/glossary/" + kind + ".xml";
                AddRelationship(zip, "word/glossary/_rels/document.xml.rels", "source-" + kind, kind, kind + ".xml");
                Put(zip, part, new XElement(W + (kind == "fontTable" ? "fonts" : kind)).ToString());
                Override(zip, part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + kind + "+xml");
            }
        });
        WordDocument source = Load(input); byte[] before = Save(source);
        var target = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient)); target.AddParagraphs(Slots, cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("one", 0, 0, 3, TestContext.Current.CancellationToken); target.AddBookmark("two", 1, 0, 3, TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken);
        byte[] saved = Save(target); Validate(saved);
        using var sourceZip = new ZipArchive(new MemoryStream(before)); using var outputZip = new ZipArchive(new MemoryStream(saved));
        foreach (string kind in ResourceKinds)
        {
            string part = "word/glossary/" + kind + ".xml";
            using Stream a = sourceZip.GetEntry(part)!.Open(); using Stream b = outputZip.GetEntry(part)!.Open();
            using var x = new MemoryStream(); using var y = new MemoryStream(); a.CopyTo(x); b.CopyTo(y); Assert.Equal(x.ToArray(), y.ToArray());
        }
        XDocument relationships = Xml(saved, "word/glossary/_rels/wordnet-fragment-1.xml.rels");
        Assert.Equal(4, relationships.Root!.Elements().Count());
        Assert.Equal(4, relationships.Root.Elements().Select(e => (string?)e.Attribute("Id")).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, Load(saved).ContentControls.Count); Assert.Equal(before, Save(source));
    }

    [Fact]
    public void CombinedGlossaryEntryLimitFailsWithoutMutatingEitherSession()
    {
        string host = "<w:p>" + Control(1, "<w:text/>", "Host", Placeholder) + "</w:p>" + Bookmark("one", 1);
        byte[] full = Modify(WithGlossary(host, "Host"), zip =>
        {
            XDocument xml = Xml(zip, "word/glossary/document.xml"); XElement entries = xml.Descendants(W + "docParts").Single(); XElement original = entries.Element(W + "docPart")!;
            for (int i = 1; i < 128; i++)
            {
                var copy = new XElement(original); copy.Element(W + "docPartPr")!.Element(W + "name")!.SetAttributeValue(W + "val", "Other" + i); entries.Add(copy);
            }
            Put(zip, "word/glossary/document.xml", xml.ToString());
        });
        WordDocument target = Load(full); WordDocument source = Load(WithGlossary("<w:p>" + Control(2, "<w:text/>", "Choose", Placeholder) + "</w:p>"));
        byte[] beforeTarget = Save(target), beforeSource = Save(source);
        var error = Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.Controls:", error.Message, StringComparison.Ordinal); Assert.Equal(beforeTarget, Save(target)); Assert.Equal(beforeSource, Save(source));
    }
}
