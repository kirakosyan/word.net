using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlGlossaryResourceTests
{
    private static readonly (string Kind, string Xml)[] Resources =
    {
        ("styles", "<w:styles xmlns:w='" + W + "'/>"),
        ("settings", "<w:settings xmlns:w='" + W + "'><w:zoom w:percent='100'/><w:proofState w:spelling='clean' w:grammar='dirty'/><w:rsids><w:rsidRoot w:val='1234ABCD'/><w:rsid w:val='12345678'/></w:rsids></w:settings>"),
        ("fontTable", "<w:fonts xmlns:w='" + W + "'><w:font w:name='Noto Sans'><w:charset w:val='00'/><w:family w:val='swiss'/><w:pitch w:val='variable'/><w:sig w:usb0='00000001' w:usb1='00000000' w:usb2='00000000' w:usb3='00000000' w:csb0='00000001' w:csb1='00000000'/></w:font></w:fonts>"),
        ("webSettings", "<w:webSettings xmlns:w='" + W + "'><w:optimizeForBrowser/><w:allowPNG/></w:webSettings>")
    };

    [Fact]
    public void InertLocalGlossaryResourcesPreserveBytesRelationshipsAndBindings()
    {
        string metadata = Binding("/p:invoice/p:customer/@name") + "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>";
        string control = Control(1, "<w:text/>", "Choose", metadata, block: true);
        byte[] source = WithResources(AddStore(WithGlossary(control), control));
        Validate(source);
        WordDocument document = Load(source);
        Assert.True(Assert.Single(document.ContentControls).ShowingPlaceholder);
        document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        Assert.Equal("Ada & Co", Assert.Single(document.ContentControls).DisplayText);
        document.SetControlText(Assert.Single(document.ContentControls), "Updated", TestContext.Current.CancellationToken);
        document.WriteBackControlBindings(document.ContentControls, TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        XDocument relations = Xml(saved, "word/glossary/_rels/document.xml.rels");
        foreach (var resource in Resources)
        {
            string part = "word/glossary/" + resource.Kind + ".xml";
            Assert.Equal(Part(source, part), Part(saved, part));
            XElement relation = Assert.Single(relations.Root!.Elements(), e => (string?)e.Attribute("Id") == "resource-" + resource.Kind);
            Assert.Equal(resource.Kind + ".xml", (string?)relation.Attribute("Target"));
            Assert.EndsWith("/" + resource.Kind, (string?)relation.Attribute("Type"), StringComparison.Ordinal);
        }
        Assert.Equal("Updated", (string?)Xml(saved, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
        WordDocument reopened = Load(saved); Assert.Equal("Updated", Assert.Single(reopened.ContentControls).DisplayText);
        reopened.ShowControlPlaceholder(Assert.Single(reopened.ContentControls), TestContext.Current.CancellationToken);
        Assert.Equal("Choose", Assert.Single(reopened.ContentControls).DisplayText); Assert.Equal("Choose", Pdf(reopened));
        reopened.DetachControlBindings(reopened.ContentControls, TestContext.Current.CancellationToken);
        byte[] detached = Save(reopened); Validate(detached);
        Assert.Equal("Updated", (string?)Xml(detached, "customXml/item1.xml").Descendants(XName.Get("customer", "urn:wordnet:controls")).Single().Attribute("name"));
    }

    [Fact]
    public void DefaultLoadingStillUsesTheOrdinaryCachedControlProfile()
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        using var input = new MemoryStream(WithResources(WithGlossary(control)));
        WordDocument ordinary = WordDocument.Load(input, DocumentFormat.Docx, out ConversionResult result, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.Equal("Choose", Assert.Single(ordinary.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.AdditionalParts");
        Assert.Empty(ordinary.ContentControls);
    }

    [Fact]
    public void ResourceLimitsAndCancellationLeaveCallerOutputUntouched()
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        WordDocument document = Load(WithResources(WithGlossary(control)));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => document.Save(output, DocumentFormat.Docx, new ConversionOptions(maximumOutputBytes: 128), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(output.CanWrite);
        using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.Save(output, DocumentFormat.Docx, cancellationToken: cancellation.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void WordAuthoredGlossaryResourcesAndPlaceholderSurviveEdits()
    {
        byte[] source = WordAuthoredGlossary(); Validate(source);
        WordDocument document = Load(source, new ConversionOptions(FidelityMode.Lenient));
        Assert.Contains(document.Diagnostics, d => d.Code == "Docx.ThemeFont");
        Assert.Equal("SDT  Run", Assert.Single(document.ContentControls).DisplayText);
        document.SetControlText(Assert.Single(document.ContentControls), "Edited", TestContext.Current.CancellationToken);
        document.ShowControlPlaceholder(Assert.Single(document.ContentControls), TestContext.Current.CancellationToken);
        Assert.Equal("Click here to enter text.", Assert.Single(document.ContentControls).DisplayText);
        byte[] saved = Save(document); Validate(saved);
        foreach (string name in new[] { "styles", "settings", "fontTable", "webSettings" })
            Assert.Equal(Part(source, "word/glossary/" + name + ".xml"), Part(saved, "word/glossary/" + name + ".xml"));
        var originalRelationships = Xml(source, "word/glossary/_rels/document.xml.rels").Root!.Elements()
            .Select(e => ((string?)e.Attribute("Id"), (string?)e.Attribute("Type"), (string?)e.Attribute("Target"))).OrderBy(e => e.Item1, StringComparer.Ordinal).ToArray();
        var savedRelationships = Xml(saved, "word/glossary/_rels/document.xml.rels").Root!.Elements()
            .Select(e => ((string?)e.Attribute("Id"), (string?)e.Attribute("Type"), (string?)e.Attribute("Target"))).OrderBy(e => e.Item1, StringComparer.Ordinal).ToArray();
        Assert.Equal(originalRelationships, savedRelationships);
        XElement sourceProperties = Xml(source, "word/glossary/document.xml").Descendants(W + "docPartPr").Single();
        XElement savedProperties = Xml(saved, "word/glossary/document.xml").Descendants(W + "docPartPr").Single();
        Assert.Equal((string?)sourceProperties.Element(W + "name")?.Attribute(W + "val"), (string?)savedProperties.Element(W + "name")?.Attribute(W + "val"));
        Assert.Equal((string?)sourceProperties.Element(W + "guid")?.Attribute(W + "val"), (string?)savedProperties.Element(W + "guid")?.Attribute(W + "val"));
        XElement color = Assert.Single(Xml(saved, "word/document.xml").Descendants(W + "color"));
        Assert.Equal("808080", (string?)color.Attribute(W + "val"));
        WordDocument display = document.CreateControlDisplayView(out ConversionResult displayResult, TestContext.Current.CancellationToken);
        Assert.Empty(display.ContentControls); Assert.Single(document.ContentControls);
        Assert.Contains(displayResult.Diagnostics, d => d.Code == "Control.OutputLoss");
        using (PdfDocument pdf = PdfDocument.Open(Save(display, DocumentFormat.Pdf)))
        {
            var letters = pdf.GetPage(1).Letters;
            Assert.Contains("Click here to enter text.", string.Concat(letters.Select(l => l.Value)), StringComparison.Ordinal);
            Assert.All(letters, letter =>
            {
                var rgb = letter.Color.ToRGBValues();
                Assert.Equal(128 / 255.0, rgb.r, 2); Assert.Equal(128 / 255.0, rgb.g, 2); Assert.Equal(128 / 255.0, rgb.b, 2);
            });
        }
        WordDocument reopened = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal("Click here to enter text.", Assert.Single(reopened.ContentControls).DisplayText);
    }

    [Fact]
    public void WordAuthoredGlossaryThemeFallbackStillRejectsInStrictMode()
    {
        var error = Assert.Throws<NotSupportedException>(() => Load(WordAuthoredGlossary(), new ConversionOptions(FidelityMode.Strict)));
        Assert.StartsWith("Docx.ThemeFont:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WordAuthoredGlossaryWithUnresolvedLoadLossCannotBeInsertedSilently()
    {
        WordDocument source = Load(WordAuthoredGlossary(), new ConversionOptions(FidelityMode.Lenient));
        Assert.Contains(source.Diagnostics, d => d.Code == "Docx.ThemeFont");
        var target = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("slot", 0, 0, 6, TestContext.Current.CancellationToken);
        byte[] beforeSource = Save(source), beforeTarget = Save(target);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("slot", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceLoss:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeSource, Save(source)); Assert.Equal(beforeTarget, Save(target));
    }

    private static byte[] WordAuthoredGlossary()
    {
        string control = Fixture("OfficeWordParts/control.xml");
        return Modify(Package(Document("<w:p>" + control + "</w:p>")), zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "glossary", "glossaryDocument", "glossary/document.xml");
            Put(zip, "word/glossary/document.xml", Fixture("OfficeWordParts/glossary-document.xml"));
            Put(zip, "word/glossary/_rels/document.xml.rels", Fixture("OfficeWordParts/glossary-relationships.xml"));
            Override(zip, "word/glossary/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml");
            foreach (var resource in new[] { ("styles", "styles"), ("settings", "settings"), ("fontTable", "font-table"), ("webSettings", "web-settings") })
            {
                string part = "word/glossary/" + resource.Item1 + ".xml";
                Put(zip, part, Fixture("OfficeWordParts/glossary-" + resource.Item2 + ".xml"));
                Override(zip, part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + resource.Item1 + "+xml");
            }
        });
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("missing-base")]
    [InlineData("wrong-base-kind")]
    [InlineData("duplicate-style")]
    [InlineData("duplicate-latent")]
    [InlineData("bad-theme-token")]
    [InlineData("external-attribute")]
    [InlineData("missing-character-style")]
    [InlineData("paragraph-mark-style")]
    public void WordAuthoredGlossaryRejectsUnsafeStyleGraphs(string scenario)
    {
        byte[] source = Modify(WordAuthoredGlossary(), zip =>
        {
            string path = scenario is "missing-character-style" or "paragraph-mark-style" ? "word/glossary/document.xml" : "word/glossary/styles.xml";
            XDocument xml = Xml(zip, path);
            if (scenario == "missing-character-style")
                xml.Descendants(W + "rStyle").Single().SetAttributeValue(W + "val", "NotPresent");
            else if (scenario == "paragraph-mark-style")
                xml.Descendants(W + "p").Single().AddFirst(new XElement(W + "pPr", new XElement(W + "rPr", new XElement(W + "rStyle", new XAttribute(W + "val", "PlaceholderText")))));
            else
            {
                XElement style = xml.Descendants(W + "style").Single(e => (string?)e.Attribute(W + "styleId") == "PlaceholderText");
                if (scenario is "cycle" or "missing-base" or "wrong-base-kind")
                    style.Element(W + "basedOn")!.SetAttributeValue(W + "val", scenario == "cycle" ? "PlaceholderText" : scenario == "missing-base" ? "NotPresent" : "Normal");
                else if (scenario == "duplicate-style") xml.Root!.Add(new XElement(style));
                else if (scenario == "duplicate-latent")
                {
                    XElement latent = xml.Descendants(W + "latentStyles").Single(); latent.Add(new XElement(latent.Elements(W + "lsdException").First()));
                }
                else if (scenario == "bad-theme-token") xml.Descendants(W + "rFonts").First().SetAttributeValue(W + "asciiTheme", "unsupported");
                else style.SetAttributeValue(R + "id", "external");
            }
            Put(zip, path, xml.ToString());
        });
        using var input = new MemoryStream(source);
        Exception? error = Record.Exception(() => WordDocument.LoadWithControls(input, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        if (scenario is "external-attribute" or "missing-character-style" or "paragraph-mark-style") Assert.IsType<NotSupportedException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("漢")]
    [InlineData("العربية")]
    public void GlossaryScriptFontSlotsRejectUnqualifiedPlaceholderText(string text)
    {
        byte[] source = Modify(WordAuthoredGlossary(), zip =>
        {
            const string path = "word/glossary/document.xml";
            XDocument xml = Xml(zip, path); xml.Descendants(W + "t").Single().Value = text; Put(zip, path, xml.ToString());
        });
        var error = Assert.Throws<NotSupportedException>(() => Load(source, new ConversionOptions(FidelityMode.Lenient)));
        Assert.StartsWith("Control.ScriptFormatting:", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("dangling")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("nested")]
    [InlineData("active-font")]
    [InlineData("nonempty-styles")]
    [InlineData("active-settings")]
    [InlineData("bad-font")]
    public void UnsafeOrUnadmittedGlossaryResourcesFailBeforePublishingASession(string scenario)
    {
        string control = Control(1, Choices, "Choose", "<w:placeholder><w:docPart w:val='Prompt'/></w:placeholder><w:showingPlcHdr/>", block: true);
        byte[] source = Modify(WithResources(WithGlossary(control)), zip =>
        {
            string rels = "word/glossary/_rels/document.xml.rels";
            if (scenario == "external")
            {
                XDocument xml = Xml(zip, rels); xml.Root!.Elements().First().SetAttributeValue("TargetMode", "External"); Put(zip, rels, xml.ToString());
            }
            else if (scenario == "dangling") zip.GetEntry("word/glossary/styles.xml")!.Delete();
            else if (scenario is "duplicate" or "unknown")
            {
                XDocument xml = Xml(zip, rels); xml.Root!.Elements().Last().Remove(); Put(zip, rels, xml.ToString());
                AddRelationship(zip, rels, scenario == "duplicate" ? "second-style" : "active", scenario == "duplicate" ? "styles" : "aFChunk", scenario == "duplicate" ? "styles.xml" : "active.xml");
            }
            else if (scenario == "nested") AddRelationship(zip, "word/glossary/_rels/styles.xml.rels", "external", "hyperlink", "https://example.invalid/", "External");
            else if (scenario == "active-font") Put(zip, "word/glossary/fontTable.xml", "<w:fonts xmlns:w='" + W + "' xmlns:r='" + R + "'><w:font w:name='Test'><w:embedRegular r:id='external'/></w:font></w:fonts>");
            else if (scenario == "nonempty-styles") Put(zip, "word/glossary/styles.xml", "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Normal'><w:link w:val='Other'/></w:style></w:styles>");
            else if (scenario == "active-settings") Put(zip, "word/glossary/settings.xml", "<w:settings xmlns:w='" + W + "' xmlns:r='" + R + "'><w:attachedTemplate r:id='external'/></w:settings>");
            else Put(zip, "word/glossary/fontTable.xml", "<w:fonts xmlns:w='" + W + "'><w:font w:name='Test'><w:charset w:val='XX'/></w:font></w:fonts>");
        });
        using var input = new MemoryStream(source);
        Exception? error = Record.Exception(() => WordDocument.LoadWithControls(input, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        if (scenario is "external" or "dangling" or "duplicate" or "bad-font") Assert.IsType<InvalidDataException>(error);
        else Assert.IsType<NotSupportedException>(error);
        Assert.True(input.CanRead);
    }

    private static byte[] WithResources(byte[] source) => Modify(source, zip =>
    {
        foreach (var resource in Resources)
        {
            string part = "word/glossary/" + resource.Kind + ".xml";
            AddRelationship(zip, "word/glossary/_rels/document.xml.rels", "resource-" + resource.Kind, resource.Kind, resource.Kind + ".xml");
            Put(zip, part, resource.Xml);
            Override(zip, part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + resource.Kind + "+xml");
        }
    });

    private static byte[] AddStore(byte[] source, string control)
    {
        byte[] store = WithStore(control);
        return Modify(source, zip =>
        {
            AddRelationship(zip, "word/_rels/document.xml.rels", "data", "customXml", "../customXml/item1.xml");
            AddRelationship(zip, "customXml/_rels/item1.xml.rels", "props", "customXmlProps", "itemProps1.xml");
            Put(zip, "customXml/item1.xml", Xml(store, "customXml/item1.xml").ToString());
            XDocument properties = Xml(store, "customXml/itemProps1.xml"); properties.Root!.SetAttributeValue(Ds + "itemID", StoreId);
            Put(zip, "customXml/itemProps1.xml", properties.ToString());
            Override(zip, "customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        });
    }

    private static byte[] Part(byte[] bytes, string path)
    {
        using var input = new MemoryStream(bytes); using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using Stream part = archive.GetEntry(path)!.Open(); using var output = new MemoryStream(); part.CopyTo(output);
        return output.ToArray();
    }
}
