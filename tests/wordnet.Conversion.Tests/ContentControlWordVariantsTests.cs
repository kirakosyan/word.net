using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlWordVariantsTests
{
    private static readonly (string Kind, string Fixture)[] Resources =
    {
        ("styles", "styles"), ("settings", "settings"), ("fontTable", "font-table"), ("webSettings", "web-settings")
    };

    private static byte[] Authored(string name, bool inline = false, bool stripCharacterStyle = false)
    {
        XElement control = XElement.Parse(Fixture("OfficeWordVariants/" + name + ".xml"));
        if (stripCharacterStyle) control.Descendants(W + "rStyle").Remove();
        string body = inline ? new XElement(W + "p", control).ToString(SaveOptions.DisableFormatting) : control.ToString(SaveOptions.DisableFormatting);
        return Modify(Package(Document(body)), zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "glossary", "glossaryDocument", "glossary/document.xml");
            Put(zip, "word/glossary/document.xml", Fixture("OfficeWordVariants/glossary-document.xml"));
            Put(zip, "word/glossary/_rels/document.xml.rels", Fixture("OfficeWordVariants/glossary-relationships.xml"));
            Override(zip, "word/glossary/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml");
            foreach (var resource in Resources)
            {
                string part = "word/glossary/" + resource.Kind + ".xml";
                Put(zip, part, Fixture("OfficeWordVariants/glossary-" + resource.Fixture + ".xml"));
                Override(zip, part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + resource.Kind + "+xml");
            }
        });
    }

    private static byte[] Part(byte[] bytes, string path)
    {
        using var input = new MemoryStream(bytes); using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using Stream part = archive.GetEntry(path)!.Open(); using var output = new MemoryStream(); part.CopyTo(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData("rich-block", false, ContentControlKind.RichText, "This is an SdtBlock.  ")]
    [InlineData("rich-inline", true, ContentControlKind.RichText, "SdtRun")]
    [InlineData("plain-block", false, ContentControlKind.PlainText, "Text")]
    [InlineData("date-value", false, ContentControlKind.Date, "7/1/2008")]
    public void WordAuthoredControlKindsPreserveTheirLocalGlossaryThroughEdits(string name, bool inline, ContentControlKind kind, string initial)
    {
        byte[] source = Authored(name, inline); Validate(source);
        WordDocument document = Load(source, new ConversionOptions(FidelityMode.Lenient));
        DocumentContentControl original = Assert.Single(document.ContentControls);
        Assert.Equal(kind, original.Kind); Assert.Equal(initial, original.DisplayText);
        if (kind == ContentControlKind.PlainText) document.SetControlText(original, "Edited", TestContext.Current.CancellationToken);
        else if (kind == ContentControlKind.Date) document.SetControlDate(original, ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        else if (inline) document.ReplaceControlContent(original, new[] { new DocumentText("Edited") }, TestContext.Current.CancellationToken);
        else document.ReplaceControlParagraphs(original, original.Content, TestContext.Current.CancellationToken);
        string expected = kind == ContentControlKind.Date ? "2/29/2024" : kind == ContentControlKind.RichText && !inline ? initial : "Edited";
        Assert.Equal(expected, Assert.Single(document.ContentControls).DisplayText);
        if (kind != ContentControlKind.Date)
        {
            document.ShowControlPlaceholder(Assert.Single(document.ContentControls), TestContext.Current.CancellationToken);
            expected = "Click here to enter text.";
        }
        else
            Assert.Contains("Control.Placeholder", Assert.Throws<NotSupportedException>(() => document.ShowControlPlaceholder(
                Assert.Single(document.ContentControls), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        byte[] saved = Save(document); Validate(saved);
        foreach (var resource in Resources)
        {
            string part = "word/glossary/" + resource.Kind + ".xml";
            Assert.Equal(Part(source, part), Part(saved, part));
        }
        XElement sourceControl = Xml(source, "word/document.xml").Descendants(W + "sdtPr").Single();
        XElement savedControl = Xml(saved, "word/document.xml").Descendants(W + "sdtPr").Single();
        Assert.Equal((string?)sourceControl.Element(W + "id")?.Attribute(W + "val"), (string?)savedControl.Element(W + "id")?.Attribute(W + "val"));
        Assert.Equal((string?)sourceControl.Element(W + "placeholder")?.Element(W + "docPart")?.Attribute(W + "val"),
            (string?)savedControl.Element(W + "placeholder")?.Element(W + "docPart")?.Attribute(W + "val"));
        WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal(expected, Assert.Single(reopened.ContentControls).DisplayText);
        WordDocument display = reopened.CreateControlDisplayView(out _, TestContext.Current.CancellationToken);
        Assert.Contains(expected.TrimEnd(), Pdf(display), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("combo-dangling")]
    [InlineData("dropdown-dangling")]
    public void MissingWordAuthoredGlossaryEntriesRemainExplicitlyUnsupported(string name)
    {
        // The source references a name not in its local glossary. Remove only the
        // independently unsupported character-style reference to isolate that boundary.
        byte[] source = Authored(name, stripCharacterStyle: true); Validate(source);
        using var input = new MemoryStream(source);
        var error = Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(input, out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Contains("placeholder reference", error.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("combo-dangling")]
    [InlineData("dropdown-dangling")]
    [InlineData("date-placeholder")]
    public void AuthoredControlCharacterStyleReferencesAreStillExplicitlyUnsupported(string name)
    {
        byte[] source = Authored(name); Validate(source);
        using var input = new MemoryStream(source);
        var error = Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(input, out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.StartsWith("Control.Unsupported:", error.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void ExplicitNonTrueTypeFlagsStayInTheLocalFontCatalog(string value)
    {
        const string part = "word/glossary/fontTable.xml";
        byte[] source = Modify(Authored("plain-block"), zip =>
        {
            XDocument fonts = Xml(zip, part); fonts.Descendants(W + "notTrueType").Single().SetAttributeValue(W + "val", value);
            Put(zip, part, fonts.ToString(SaveOptions.DisableFormatting));
        });
        Validate(source);
        WordDocument document = Load(source, new ConversionOptions(FidelityMode.Lenient));
        document.SetControlText(Assert.Single(document.ContentControls), "Edited", TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Equal(Part(source, part), Part(saved, part));
        Assert.Equal(value, (string?)Xml(saved, part).Descendants(W + "notTrueType").Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData("invalid-value")]
    [InlineData("child")]
    [InlineData("external-attribute")]
    [InlineData("duplicate")]
    public void AuthoredNonTrueTypeFontMetadataDoesNotAdmitActiveOrMalformedContent(string scenario)
    {
        byte[] source = Modify(Authored("plain-block"), zip =>
        {
            const string path = "word/glossary/fontTable.xml";
            XDocument fonts = Xml(zip, path); XElement property = fonts.Descendants(W + "notTrueType").Single();
            if (scenario == "invalid-value") property.SetAttributeValue(W + "val", "maybe");
            else if (scenario == "child") property.Add(new XElement(W + "font"));
            else if (scenario == "external-attribute") property.SetAttributeValue(R + "id", "external");
            else property.AddAfterSelf(new XElement(property));
            Put(zip, path, fonts.ToString(SaveOptions.DisableFormatting));
        });
        using var input = new MemoryStream(source);
        Exception? error = Record.Exception(() => WordDocument.LoadWithControls(input, out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        if (scenario == "external-attribute") Assert.IsType<NotSupportedException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.True(input.CanRead);
    }
}
