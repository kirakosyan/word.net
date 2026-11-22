using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlWordVariantsTests
{
    private static readonly (string Kind, string Fixture)[] Resources =
    {
        ("styles", "styles"), ("settings", "settings"), ("fontTable", "font-table"), ("webSettings", "web-settings")
    };

    private static byte[] Authored(string name, bool inline = false, bool stripCharacterStyle = false, bool includeMainStyles = false)
    {
        XElement control = XElement.Parse(Fixture("OfficeWordVariants/" + name + ".xml"));
        if (stripCharacterStyle) control.Descendants(W + "rStyle").Remove();
        string body = inline ? new XElement(W + "p", control).ToString(SaveOptions.DisableFormatting) : control.ToString(SaveOptions.DisableFormatting);
        return Modify(Package(Document(body)), zip =>
        {
            if (includeMainStyles)
            {
                AddRelationship(zip, "word/_rels/document.xml.rels", "styles", "styles", "styles.xml");
                Put(zip, "word/styles.xml", Fixture("OfficeWordVariants/main-character-styles.xml"));
                Override(zip, "word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml");
            }
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
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyGlossaryKeepsWordResourcesWithoutResolvingUnusedRunDefaults(bool collection)
    {
        byte[] source = Modify(Authored("date-value"), zip => Put(zip, "word/glossary/document.xml",
            "<w:glossaryDocument xmlns:w='" + W + "'>" + (collection ? "<w:docParts/>" : "") + "</w:glossaryDocument>"));
        WordDocument document = Load(source, new ConversionOptions(FidelityMode.Strict));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "Docx.ThemeFont");
        DocumentContentControl date = Assert.Single(document.ContentControls);
        document.SetControlDate(date, ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Empty(Xml(saved, "word/glossary/document.xml").Root!.Elements());
        static string[] Relationships(byte[] package) => Xml(package, "word/glossary/_rels/document.xml.rels").Root!.Elements()
            .Select(r => $"{r.Attribute("Id")?.Value} {r.Attribute("Type")?.Value} {r.Attribute("Target")?.Value}").OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(Relationships(source), Relationships(saved));
        foreach (var resource in Resources)
        {
            string part = "word/glossary/" + resource.Kind + ".xml";
            Assert.Equal(Part(source, part), Part(saved, part));
        }
        DocumentContentControl reopened = Assert.Single(Load(saved).ContentControls);
        Assert.Equal(date.Id, reopened.Id); Assert.Equal("2/29/2024", reopened.DisplayText);
    }

    [Fact]
    public void SixWordGeneratedControlKindsKeepTheirValuesAndNamedPlaceholders()
    {
        XElement controls = XElement.Parse(Fixture("OfficeWordVariants/word-20625-six-controls.xml"));
        string body = string.Concat(controls.Elements().Select(p => p.ToString(SaveOptions.DisableFormatting)));
        string[] names = controls.Descendants(W + "placeholder").Elements(W + "docPart")
            .Select(p => (string)p.Attribute(W + "val")!).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(3, names.Length);
        byte[] input = Modify(WithGlossary(body), zip =>
        {
            const string part = "word/glossary/document.xml";
            XDocument glossary = Xml(zip, part);
            XElement entries = glossary.Root!.Element(W + "docParts")!;
            XElement prototype = entries.Elements().Single(); entries.RemoveNodes();
            foreach (string name in names)
            {
                XElement entry = new XElement(prototype);
                entry.Element(W + "docPartPr")!.Element(W + "name")!.SetAttributeValue(W + "val", name);
                entry.Descendants(W + "t").Single().Value = "Prompt " + Array.IndexOf(names, name);
                entries.Add(entry);
            }
            Put(zip, part, glossary.ToString());
        });
        Validate(input); WordDocument document = Load(input, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal(6, document.ContentControls.Select(control => control.Kind).Distinct().Count());
        DocumentContentControl ByTag(string tag) => document.ContentControls.Single(control => control.Tag == tag);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal("WordRich", ByTag("Rich").DisplayText); Assert.Equal("WordPlain", ByTag("Plain").DisplayText);
        Assert.True(ByTag("Checkbox").IsChecked); Assert.Equal("b", ByTag("Combo").SelectedValue);
        Assert.Equal("b", ByTag("Dropdown").SelectedValue); Assert.Equal("2024-02-29", ByTag("Date").DisplayText);
        document.ReplaceControlContent(ByTag("Rich"), new[] { new DocumentText("Rich edit") }, token);
        document.SetControlText(ByTag("Plain"), "Plain edit", token); document.SetControlChecked(ByTag("Checkbox"), false, token);
        document.SetControlSelection(ByTag("Dropdown"), "a", token); document.SetControlComboText(ByTag("Combo"), "Custom", token);
        document.ShowControlPlaceholder(ByTag("Date"), token);
        byte[] saved = Save(document); Validate(saved); WordDocument reloaded = Load(saved, new ConversionOptions(FidelityMode.Lenient));
        Assert.Equal(document.ContentControls.Select(c => (c.Id, c.Tag, c.Kind, c.DisplayText)),
            reloaded.ContentControls.Select(c => (c.Id, c.Tag, c.Kind, c.DisplayText)));
        Assert.Equal("Alpha", ByTag("Dropdown").DisplayText); Assert.Equal("Custom", ByTag("Combo").DisplayText);
        Assert.Equal("Prompt 2", ByTag("Date").DisplayText);
        Assert.Equal(controls.Descendants(W + "placeholder").Elements(W + "docPart").Select(p => (string?)p.Attribute(W + "val")),
            Xml(saved, "word/document.xml").Descendants(W + "placeholder").Elements(W + "docPart").Select(p => (string?)p.Attribute(W + "val")));
        Assert.Contains("RicheditPlainedit-AlphaCustomPrompt2", Compact(Pdf(reloaded)), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyGlossaryStillValidatesUnusedLocalStyleDefinitions()
    {
        byte[] source = Modify(Authored("date-value"), zip =>
        {
            Put(zip, "word/glossary/document.xml", "<w:glossaryDocument xmlns:w='" + W + "'/>");
            const string part = "word/glossary/styles.xml";
            XDocument styles = Xml(zip, part); XElement style = styles.Root!.Elements(W + "style").First();
            styles.Root.Add(new XElement(style)); Put(zip, part, styles.ToString());
        });
        Assert.Contains("Control.Malformed", Assert.Throws<InvalidDataException>(() => Load(source)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WordDerivedControlWithoutGlossaryRunDefaultsKeepsIndependentPlaceholders()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new ConversionOptions(FidelityMode.Lenient);
        byte[] input = Modify(Authored("plain-block"), zip =>
        {
            const string stylesPart = "word/glossary/styles.xml";
            XDocument styles = Xml(zip, stylesPart);
            styles.Descendants(W + "rPrDefault").Single().Remove();
            Put(zip, stylesPart, styles.ToString());
        });
        Validate(input);
        WordDocument fragment = Load(input, options);
        Assert.Empty(fragment.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(d => d.Code));
        byte[] original = Save(fragment);
        WordDocument target = WordDocument.Create(options);
        target.AddParagraph("One", cancellationToken: token);
        target.AddParagraph("Two", cancellationToken: token);
        target.AddBookmark("one", 0, 0, 3, token);
        target.AddBookmark("two", 1, 0, 3, token);
        target.InsertFragmentAtBookmark("one", fragment, token);
        target.InsertFragmentAtBookmark("two", fragment, token);
        Assert.Equal(2, target.ContentControls.Count);
        Assert.NotEqual(target.ContentControls[0].Id, target.ContentControls[1].Id);
        target.SetControlText(target.ContentControls[0], "First copy", token);
        target.ShowControlPlaceholder(target.ContentControls[1], token);

        byte[] saved = Save(target);
        Validate(saved);
        XElement[] controls = Xml(saved, "word/document.xml").Descendants(W + "sdt").ToArray();
        Assert.Equal(2, controls.Length);
        Assert.Equal("First copy", string.Concat(controls[0].Descendants(W + "t").Select(t => t.Value)));
        Assert.Equal("Click here to enter text.", string.Concat(controls[1].Descendants(W + "t").Select(t => t.Value)));
        Assert.Null(controls[0].Element(W + "sdtPr")!.Element(W + "showingPlcHdr"));
        Assert.NotNull(controls[1].Element(W + "sdtPr")!.Element(W + "showingPlcHdr"));
        foreach (var resource in Resources)
        {
            string path = "word/glossary/" + resource.Kind + ".xml";
            Assert.Equal(Part(input, path), Part(saved, path));
        }
        Assert.Equal("FirstcopyClickheretoentertext.", Compact(Pdf(target)));
        Assert.Equal(original, Save(fragment));
    }

    [Theory]
    [InlineData("date-placeholder", true, "Control.CharacterStyle")]
    [InlineData("plain-block", false, "Docx.ThemeFont")]
    public void WordDerivedControlWithLoadLossRejectsFragmentInsertionAtomically(string name, bool includeStyles, string expectedLoss)
    {
        var token = TestContext.Current.CancellationToken;
        var options = new ConversionOptions(FidelityMode.Lenient);
        byte[] input = Authored(name, includeMainStyles: includeStyles);
        Validate(input);
        WordDocument fragment = Load(input, options);
        Assert.Contains(fragment.Diagnostics, diagnostic => diagnostic.Code == expectedLoss && diagnostic.Severity == DiagnosticSeverity.Warning);
        WordDocument target = WordDocument.Create(options);
        target.AddParagraph("Slot", cancellationToken: token);
        target.AddBookmark("slot", 0, 0, 4, token);
        byte[] before = Save(target);
        byte[] original = Save(fragment);
        var failure = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("slot", fragment, token));
        Assert.StartsWith("Fragment.SourceLoss:", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, Save(target));
        Assert.Equal(original, Save(fragment));
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
    public void ResolvingTheCharacterStyleDoesNotInventAMissingWordPlaceholder(string name)
    {
        byte[] source = Authored(name, includeMainStyles: true); Validate(source);
        var error = Assert.Throws<NotSupportedException>(() => Load(source, new ConversionOptions(FidelityMode.Lenient)));
        Assert.Contains("placeholder reference", error.Message, StringComparison.Ordinal);
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

    [Fact]
    public void WordAuthoredDatePlaceholderWithLocalCharacterStyleRetainsItsResolvedAppearance()
    {
        using JsonDocument reference = JsonDocument.Parse(Fixture("OfficeWordVariants/date-style-reference.json"));
        string referenceText = reference.RootElement.GetProperty("text").GetString()!;
        string referenceColor = reference.RootElement.GetProperty("color").GetString()!;
        byte[] source = Authored("date-placeholder", includeMainStyles: true); Validate(source);
        using (var input = new MemoryStream(source))
            Assert.Contains("Control.CharacterStyle", Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(input, out _,
                new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        WordDocument document = Load(source, new ConversionOptions(FidelityMode.Lenient));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "Control.CharacterStyle");
        Assert.True(Assert.Single(document.ContentControls).ShowingPlaceholder);
        Assert.Equal(referenceText, Assert.Single(document.ContentControls).DisplayText);
        using (PdfDocument pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)))
        {
            Assert.Equal(referenceText, pdf.GetPage(1).Text);
            Assert.All(pdf.GetPage(1).Letters, letter =>
            {
                var color = letter.Color.ToRGBValues();
                Assert.Equal(Convert.ToInt32(referenceColor.Substring(0, 2), 16) / 255.0, color.r, 2);
                Assert.Equal(Convert.ToInt32(referenceColor.Substring(2, 2), 16) / 255.0, color.g, 2);
                Assert.Equal(Convert.ToInt32(referenceColor.Substring(4, 2), 16) / 255.0, color.b, 2);
            });
        }
        document.SetControlDate(Assert.Single(document.ContentControls), ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        byte[] saved = Save(document); Validate(saved);
        Assert.Empty(Xml(saved, "word/document.xml").Descendants(W + "rStyle"));
        Assert.Equal("808080", (string?)Xml(saved, "word/document.xml").Descendants(W + "color").Single().Attribute(W + "val"));
        Assert.Equal("2/29/2024", Assert.Single(Load(saved, new ConversionOptions(FidelityMode.Lenient)).ContentControls).DisplayText);
        document.ShowControlPlaceholder(Assert.Single(document.ContentControls), TestContext.Current.CancellationToken);
        Assert.Equal("Click here to enter a date.", Assert.Single(document.ContentControls).DisplayText);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("paragraph")]
    [InlineData("cycle")]
    [InlineData("active")]
    [InlineData("script")]
    public void CharacterStylesDoNotAdmitMissingCyclicOrUnqualifiedBehavior(string scenario)
    {
        byte[] source = Authored("date-placeholder", includeMainStyles: true);
        source = Modify(source, zip =>
        {
            XDocument styles = Xml(zip, "word/styles.xml"); XElement target = styles.Descendants(W + "style").Single(e => (string?)e.Attribute(W + "styleId") == "PlaceholderText");
            if (scenario == "missing") target.Remove();
            else if (scenario == "paragraph") target.SetAttributeValue(W + "type", "paragraph");
            else if (scenario == "cycle") target.Element(W + "basedOn")!.SetAttributeValue(W + "val", "PlaceholderText");
            else if (scenario == "active") target.Add(new XElement(W + "link", new XAttribute(W + "val", "unexpected")));
            else target.Element(W + "rPr")!.Add(new XElement(W + "szCs", new XAttribute(W + "val", "22")));
            Put(zip, "word/styles.xml", styles.ToString(SaveOptions.DisableFormatting));
        });
        Exception? error = Record.Exception(() => Load(source, new ConversionOptions(FidelityMode.Lenient)));
        Assert.True(error is InvalidDataException or NotSupportedException);
        Assert.Contains(scenario is "missing" or "paragraph" ? "Control.Unsupported" : "Control.Style", error!.Message, StringComparison.Ordinal);
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
