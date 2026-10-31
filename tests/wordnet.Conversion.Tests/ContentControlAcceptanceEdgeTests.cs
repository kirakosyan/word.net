using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlAcceptanceEdgeTests
{
    private static readonly char[] SpaceComma = { ' ', ',' };
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ContentLocksProtectEveryTypedValueMutation(int kind)
    {
        string type = kind switch { 0 => Checkbox(), 1 => Choices, 2 => "<w:comboBox/>", 3 => "<w:date><w:dateFormat w:val='yyyy-MM-dd'/></w:date>", 4 => "<w:richText/>", _ => "<w:text/>" };
        WordDocument document = LoadBody("<w:p>" + Control(1, type, "?", "<w:lock w:val='contentLocked'/><w:showingPlcHdr/>") + "</w:p>");
        var handle = document.ContentControls[0]; string before = Xml(Save(document), "word/document.xml").ToString();
        void Edit()
        {
            switch (kind)
            {
                case 0: document.SetControlChecked(handle, true, TestContext.Current.CancellationToken); break;
                case 1: document.SetControlSelection(handle, "a", TestContext.Current.CancellationToken); break;
                case 2: document.SetControlComboText(handle, "literal", TestContext.Current.CancellationToken); break;
                case 3: document.SetControlDate(handle, ContentControlDateValue.FromInstant(DateTimeOffset.MinValue), TestContext.Current.CancellationToken); break;
                case 4: document.ReplaceControlContent(handle, new[] { new DocumentText("replacement") }, TestContext.Current.CancellationToken); break;
                default: document.SetControlText(handle, "replacement", TestContext.Current.CancellationToken); break;
            }
        }
        Assert.Throws<InvalidOperationException>(Edit); Assert.Throws<InvalidOperationException>(() => document.ShowControlPlaceholder(handle, TestContext.Current.CancellationToken));
        Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString()); Assert.True(document.ContentControls[0].ShowingPlaceholder);
    }
    [Fact]
    public void StaticUnderlineHasIndependentStrokeGeometryAndDoesNotPaintAdjacentText()
    {
        string decorated = "<w:r><w:rPr><w:color w:val='123456'/><w:sz w:val='40'/><w:u w:val='single'/></w:rPr><w:t>XYZ</w:t></w:r>";
        WordDocument document = LoadBody("<w:p>" + Run("Before ") + Control(1, "<w:richText/>", "", content: decorated) + Run(" after") + "</w:p>");
        byte[] pdfBytes = Save(document, DocumentFormat.Pdf); using var pdf = PdfDocument.Open(pdfBytes); Assert.Equal("BeforeXYZafter", Compact(pdf.GetPage(1).Text));
        var letters = pdf.GetPage(1).Letters.Where(l => l.Value is "X" or "Y" or "Z").ToArray(); Assert.Equal(3, letters.Length); Assert.All(letters, l => Assert.Equal(20, l.FontSize, 3));
        XDocument xps = Xml(Save(document, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        XElement[] lines = xps.Descendants().Where(e => e.Name.LocalName == "Path" && (string?)e.Attribute("Stroke") == "#FF123456").ToArray(); Assert.NotEmpty(lines);
        double start = letters[0].StartBaseLine.X, end = letters[letters.Length - 1].EndBaseLine.X;
        foreach (XElement line in lines)
        {
            Assert.Equal(1.25, double.Parse((string)line.Attribute("StrokeThickness")!, CultureInfo.InvariantCulture) * 0.75, 3);
            string[] values = ((string)line.Attribute("Data")!).Split(SpaceComma, StringSplitOptions.RemoveEmptyEntries);
            double x = double.Parse(values[1], CultureInfo.InvariantCulture) * 0.75, y = double.Parse(values[2], CultureInfo.InvariantCulture) * 0.75, width = double.Parse(values[4], CultureInfo.InvariantCulture) * 0.75;
            Assert.InRange(x, start - 0.01, end + 0.01); Assert.True(width > 0); Assert.True(x + width <= end + 0.02);
            double xpsBaseline = xps.Descendants().Where(e => e.Name.LocalName == "Glyphs" && (string?)e.Attribute("Fill") == "#FF123456").Select(e => double.Parse((string)e.Attribute("OriginY")!, CultureInfo.InvariantCulture)).First();
            Assert.Equal(xpsBaseline * 0.75 + 2, y, 3);
        }
        Validate(Save(document));
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken); Assert.Equal("BeforeXYZafter", Compact(Pdf(view)));
    }
    [Theory]
    [InlineData("left", "A\tB")]
    [InlineData("both", "AB")]
    [InlineData("distribute", "AB")]
    public void UnsupportedUnderlineCombinationsRejectStaticOutputExplicitly(string alignment, string text)
    {
        string run = "<w:r><w:rPr><w:u w:val='single'/></w:rPr><w:t>" + text + "</w:t></w:r>";
        string body = "<w:p><w:pPr><w:jc w:val='" + alignment + "'/></w:pPr>" + Control(1, "<w:richText/>", "", content: run) + "</w:p>";
        WordDocument document = LoadBody(body); Validate(Save(document)); using var output = new MemoryStream(); output.WriteByte(9);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 9 }, output.ToArray());
    }
    [Fact]
    public void NestedScalarTreesOutsideRichOwnersSurviveTypedReplacementAndSchemaValidation()
    {
        string Code(string text) => new XElement(W + "r", new XElement(W + "instrText", text)).ToString();
        string field = "<w:r><w:fldChar w:fldCharType='begin'/></w:r>" + Code("IF ") + P1FieldTests.Field("= 1 + 1", "2") + Code(" = 2 \"Yes\" \"No\"") + "<w:r><w:fldChar w:fldCharType='separate'/></w:r>" + Run("old") + "<w:r><w:fldChar w:fldCharType='end'/></w:r>";
        WordDocument document = LoadBody("<w:p>" + field + Control(1, "<w:richText/>", "Before") + "</w:p>");
        document.ReplaceControlContent(document.ContentControls[0], new[] { new DocumentText("After") }, TestContext.Current.CancellationToken);
        Assert.Equal("YesAfter", Pdf(document)); byte[] word = Save(document); Validate(word);
        XDocument xml = Xml(word, "word/document.xml");
        string instructions = string.Join(" ", xml.Descendants(W + "instrText").Select(e => e.Value).Concat(xml.Descendants(W + "fldSimple").Select(e => (string)e.Attribute(W + "instr")!))); Assert.Contains("= 1 + 1", instructions, StringComparison.Ordinal); Assert.DoesNotContain("\ufffc", instructions, StringComparison.Ordinal);
    }
    [Fact]
    public void DateLoadPolicyIsCapturedAndPlaceholdersDoNotExposeAnOldTypedValue()
    {
        string date = "<w:date w:fullDate='2024-02-29T00:00:00Z'><w:dateFormat w:val='yyyy-MM-dd'/></w:date>";
        using var input = new MemoryStream(Package(Document(Control(1, date, "Choose", "<w:showingPlcHdr/>", block: true))));
        var policy = new ContentControlLoadOptions { DateMode = ContentControlDateMode.DateOnly };
        WordDocument document = WordDocument.LoadWithControls(input, policy, out _, cancellationToken: TestContext.Current.CancellationToken); policy.DateMode = ContentControlDateMode.UtcInstant;
        Assert.Null(document.ContentControls[0].DateValue); document.SetControlDate(document.ContentControls[0], ContentControlDateValue.FromDate(2028, 2, 29), TestContext.Current.CancellationToken); Assert.Equal("2028-02-29", document.ContentControls[0].DisplayText); Validate(Save(document));
    }
    [Fact]
    public void XmlPathsAndNamespaceMapsHavePositiveAndNegativeBoundaryCases()
    {
        string[] names = Enumerable.Repeat(new string('r', 64), 15).Concat(new[] { new string('v', 48) }).ToArray();
        string path = "/" + string.Join("/", names); Assert.Equal(1024, path.Length);
        XElement data = new XElement(names[names.Length - 1], "endpoint"); for (int i = names.Length - 2; i >= 0; i--) data = new XElement(names[i], data);
        string Body(string xpath) => "<w:p>" + Control(1, "<w:text/>", "old", Binding(xpath, mappings: "")) + "</w:p>";
        WordDocument document = Load(WithStore(Body(path), data.ToString())); document.ApplyControlBindings(document.ContentControls, TestContext.Current.CancellationToken); Assert.Equal("endpoint", document.ContentControls[0].DisplayText); Validate(Save(document));
        Assert.Throws<InvalidDataException>(() => Load(WithStore(Body(path + "x"), data.ToString())));
        string mappings = string.Join(" ", Enumerable.Range(0, 16).Select(i => "xmlns:p" + i + "='urn:n" + i + "'"));
        string mapped = "<w:p>" + Control(1, "<w:text/>", "old", Binding("/p0:root/@value", mappings: mappings)) + "</w:p>";
        WordDocument admitted = Load(WithStore(mapped, "<p:root xmlns:p='urn:n0' value='mapped'/>")); admitted.ApplyControlBindings(admitted.ContentControls, TestContext.Current.CancellationToken); Assert.Equal(16, admitted.ContentControls[0].Binding!.NamespaceMappings.Count); Assert.Equal("mapped", admitted.ContentControls[0].DisplayText);
    }
    [Fact]
    public void LocalStoreCountIsBoundedWithoutDroppingUnboundAdmittedData()
    {
        byte[] Build(int count) => Modify(WithStore("<w:p>" + Control(1, "<w:text/>", "old", Binding("/p:invoice/p:text")) + "</w:p>"), zip =>
        {
            for (int i = 2; i <= count; i++)
            {
                string n = i.ToString(CultureInfo.InvariantCulture); string id = "{" + i.ToString("X8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000}";
                AddRelationship(zip, "word/_rels/document.xml.rels", "data" + n, "customXml", "../customXml/item" + n + ".xml"); AddRelationship(zip, "customXml/_rels/item" + n + ".xml.rels", "props", "customXmlProps", "itemProps" + n + ".xml");
                Put(zip, "customXml/item" + n + ".xml", "<root value='Store" + n + "'/>"); Put(zip, "customXml/itemProps" + n + ".xml", new XElement(Ds + "datastoreItem", new XAttribute(Ds + "itemID", id)).ToString()); Override(zip, "customXml/itemProps" + n + ".xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            }
        });
        WordDocument document = Load(Build(64)); byte[] word = Save(document); Validate(word);
        Assert.Equal(64, Xml(word, "word/_rels/document.xml.rels").Root!.Elements().Count(e => ((string?)e.Attribute("Type"))?.EndsWith("/customXml", StringComparison.Ordinal) == true));
        Assert.Equal("Store64", (string?)Xml(word, "customXml/item64.xml").Root!.Attribute("value")); Assert.Throws<InvalidDataException>(() => Load(Build(65)));
    }
    [Fact]
    public void RetainedObjectsAndRecordingCannotBypassTypedOwnerProtection()
    {
        byte[] chart = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "P2", "column.docx"));
        byte[] mixed = Modify(chart, zip => { XDocument xml = Xml(zip, "word/document.xml"); xml.Root!.Element(W + "body")!.AddFirst(XElement.Parse(Control(1, "<w:richText/>", "Control", block: true))); Put(zip, "word/document.xml", xml.ToString()); });
        WordDocument separate = Load(mixed); Assert.Single(separate.ContentControls); Assert.Equal(2, separate.Objects.Count);
        separate.ReplaceControlParagraphs(separate.ContentControls[0], new[] { new ContentControlParagraph(new[] { new DocumentText("Changed") }) }, TestContext.Current.CancellationToken);
        Assert.Equal(2, separate.Objects.Count); Validate(Save(separate));
        WordDocument document = LoadBody("<w:p>" + Run("Outside") + "</w:p>" + Control(1, Checkbox(), "-", block: true));
        var range = document.GetReviewRange(0, 0, 1, TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => document.RecordReplacement(range, "Changed", "Author", DateTimeOffset.MinValue, TestContext.Current.CancellationToken)); Assert.Equal("Outside", document.Paragraphs[0].Text); Assert.False(document.ContentControls[0].IsChecked);
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(document, document, new ComparisonOptions("Author", DateTimeOffset.MinValue), cancellationToken: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void DiagnosticsNeverIncludeControlOrXmlValues()
    {
        const string secret = "SensitiveValue-ABC"; string control = Control(null, "<w:dropDownList/>", secret, Binding("/root/@value", mappings: "") + "<w:alias w:val='SensitiveAlias'/><w:tag w:val='SensitiveTag'/>", block: true);
        WordDocument document = Load(WithStore(control, "<root value='SensitiveXml'/>")); using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Control.ChoiceUnresolved"); Assert.Contains(result.Diagnostics, d => d.Code == "Control.IdAssigned");
        Assert.All(result.Diagnostics, d => { Assert.DoesNotContain("Sensitive", d.Message, StringComparison.Ordinal); Assert.DoesNotContain(secret, d.Message, StringComparison.Ordinal); });
    }
}
