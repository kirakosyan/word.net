using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfAccessibleTextBoxTests
{
    private static string P(string text) => TestDocuments.TextParagraph(text);
    internal static string Box(string content) => "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' simplePos='0' relativeHeight='0' behindDoc='0' locked='0' layoutInCell='1' allowOverlap='1'>"
        + "<wp:simplePos x='0' y='0'/>"
        + "<wp:positionH relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom='paragraph'><wp:posOffset>0</wp:posOffset></wp:positionV>"
        + "<wp:extent cx='2540000' cy='1016000'/><wp:wrapTopAndBottom/><wp:docPr id='1' name='Box'/>"
        + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'>"
        + "<wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:prstGeom prst='rect'><a:avLst/></a:prstGeom><a:noFill/></wps:spPr>"
        + "<wps:txbx><w:txbxContent>" + content + "</w:txbxContent></wps:txbx><wps:bodyPr><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
    private static byte[] Input(string box, bool breakBeforeBox = false)
    {
        byte[] original = TestDocuments.Docx(TestDocuments.Body(P("Before box")
            + (breakBeforeBox ? "<w:p><w:r><w:br w:type='page'/></w:r></w:p>" : "") + box + P("After box")), TestDocuments.LinkRelationship("https://example.test/details"));
        using var input = new ZipArchive(new MemoryStream(original)); using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (ZipArchiveEntry entry in input.Entries)
            {
                ZipArchiveEntry copy = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using Stream source = entry.Open(); using Stream destination = copy.Open(); source.CopyTo(destination);
            }
        return output.ToArray();
    }
    private static WordDocument Source(string box) => Load(Input(box));
    private static WordDocument Load(byte[] bytes)
    {
        using (var source = WordprocessingDocument.Open(new MemoryStream(bytes), false))
            Assert.Empty(new OpenXmlValidator().Validate(source, TestContext.Current.CancellationToken));
        using var input = new MemoryStream(bytes); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.Title = "Box reading order"; return document;
    }
    private static PdfOptions Accessible() => new PdfOptions { Accessibility = PdfAccessibility.PdfUa1, Language = "en", TextBoxReadingOrder = PdfTextBoxReadingOrder.AnchorOrder };
    private static ConversionOptions Options(PdfOptions pdf) => new ConversionOptions { Pdf = pdf };

    [Fact]
    public void ExplicitAnchorOrderIncludesTextBoxParagraphsAndLinkInTheStructureTree()
    {
        string content = P("Box first") + "<w:p><w:hyperlink r:id='link'><w:r><w:t>Details</w:t></w:r></w:hyperlink></w:p>" + P("Box last");
        byte[] input = Input(Box(content)); TypographyTests.Save("accessible-boxes-source.docx", input);
        WordDocument document = Load(input);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, Options(Accessible()), TestContext.Current.CancellationToken);
        byte[] bytes = output.ToArray(); using PdfDocument pdf = PdfDocument.Open(bytes);
        string text = string.Concat(pdf.GetPages().Select(page => page.Text));
        Assert.Contains("Before box", text, StringComparison.Ordinal); Assert.Contains("Box first", text, StringComparison.Ordinal);
        Assert.Contains("Details", text, StringComparison.Ordinal); Assert.Contains("Box last", text, StringComparison.Ordinal); Assert.Contains("After box", text, StringComparison.Ordinal);
        string raw = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/S /Div ", raw, StringComparison.Ordinal); Assert.Contains("/S /Link ", raw, StringComparison.Ordinal);
        Assert.Contains("/OBJR", raw, StringComparison.Ordinal); Assert.Contains("/StructParents", raw, StringComparison.Ordinal);
        TypographyTests.Save("accessible-boxes.pdf", bytes);
        using var ordinary = new MemoryStream(); document.Save(ordinary, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        TypographyTests.Save("accessible-boxes-negative.pdf", ordinary.ToArray());
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TypographyTests.Save("accessible-boxes-saved.docx", saved.ToArray());
        Assert.Contains("txbxContent", P1FieldTests.Xml(saved.ToArray(), "word/document.xml").ToString(), StringComparison.Ordinal);
        using var source = WordprocessingDocument.Open(new MemoryStream(saved.ToArray()), false);
        Assert.Empty(new OpenXmlValidator().Validate(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AnchorOrderKeepsTagsAcrossPagesAndAfterPageSelection()
    {
        string content = P("Box first") + "<w:p><w:hyperlink r:id='link'><w:r><w:t>Details</w:t></w:r></w:hyperlink></w:p>" + P("Box last");
        WordDocument document = Load(Input(Box(content), breakBeforeBox: true));
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, Options(Accessible()), TestContext.Current.CancellationToken);
        using (PdfDocument pdf = PdfDocument.Open(output.ToArray()))
        {
            Assert.Equal(2, pdf.NumberOfPages); Assert.Contains("Before box", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("Box first", pdf.GetPage(2).Text, StringComparison.Ordinal); Assert.Contains("After box", pdf.GetPage(2).Text, StringComparison.Ordinal);
        }
        TypographyTests.Save("accessible-boxes-multipage.pdf", output.ToArray());
        PdfOptions selected = Accessible(); selected.Pages.Add(new PageRange(2, 2));
        using var selection = new MemoryStream(); document.Save(selection, DocumentFormat.Pdf, Options(selected), TestContext.Current.CancellationToken);
        using (PdfDocument pdf = PdfDocument.Open(selection.ToArray()))
        {
            Assert.Equal(1, pdf.NumberOfPages); Assert.DoesNotContain("Before box", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("Details", pdf.GetPage(1).Text, StringComparison.Ordinal);
        }
        TypographyTests.Save("accessible-boxes-selection.pdf", selection.ToArray());
    }

    [Fact]
    public void SeparateBoxesFollowTheirExplicitSourceAnchorOrder()
    {
        string first = Box(P("First box")); string second = Box(P("Second box")).Replace("id='1' name='Box'", "id='2' name='Box'");
        WordDocument document = Load(Input(first + second));
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, Options(Accessible()), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(output.ToArray()); string text = string.Concat(pdf.GetPages().Select(page => page.Text));
        Assert.True(text.IndexOf("Before box", StringComparison.Ordinal) < text.IndexOf("First box", StringComparison.Ordinal));
        Assert.True(text.IndexOf("First box", StringComparison.Ordinal) < text.IndexOf("Second box", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Second box", StringComparison.Ordinal) < text.IndexOf("After box", StringComparison.Ordinal));
        TypographyTests.Save("accessible-boxes-two.pdf", output.ToArray());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("wrap")]
    [InlineData("mixed")]
    [InlineData("alternative")]
    [InlineData("title")]
    [InlineData("native")]
    [InlineData("rotation")]
    [InlineData("autofit")]
    public void UnqualifiedBoxesAndMissingReadingOrderFailBeforeOutput(string reason)
    {
        string box = Box(P("Box text"));
        if (reason == "wrap") box = box.Replace("<wp:wrapTopAndBottom/>", "<wp:wrapNone/>");
        if (reason == "mixed") box = box.Replace("<w:p><w:r><w:drawing>", "<w:p><w:r><w:t>Mixed</w:t></w:r><w:r><w:drawing>");
        if (reason == "alternative") box = box.Replace("name='Box'", "name='Box' descr='Different alternative'");
        if (reason == "title") box = box.Replace("name='Box'", "name='Box' title='A title'");
        if (reason == "rotation") box = box.Replace("<wps:spPr><a:prstGeom", "<wps:spPr><a:xfrm rot='5400000'><a:off x='0' y='0'/><a:ext cx='2540000' cy='1016000'/></a:xfrm><a:prstGeom");
        if (reason == "autofit") box = box.Replace("<a:noAutofit/>", "<a:normAutofit/>");
        WordDocument document = Source(box); var pdf = Accessible();
        if (reason == "default") pdf.TextBoxReadingOrder = PdfTextBoxReadingOrder.Reject;
        var options = Options(pdf); if (reason == "native") { options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; }
        using var output = new MemoryStream(); output.WriteByte(9);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        if (reason == "default") Assert.StartsWith("Pdf.SemanticFeature:", error.Message, StringComparison.Ordinal);
        if (reason is "wrap" or "mixed") Assert.StartsWith("Pdf.TextBoxReadingOrder:", error.Message, StringComparison.Ordinal);
        if (reason is "alternative" or "title") Assert.StartsWith("Shape.TextBoxAlternative:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 9 }, output.ToArray());
    }

    [Fact]
    public void TextBoxAlternativeDescriptionAndTitleRemainInEditableDocx()
    {
        string box = Box(P("Box text")).Replace("name='Box'", "name='Box' title='Callout title' descr='Callout description'");
        WordDocument document = Source(box); using var output = new MemoryStream();
        document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        System.Xml.Linq.XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        var properties = Assert.Single(P1FieldTests.Xml(output.ToArray(), "word/document.xml").Descendants(wp + "docPr"));
        Assert.Equal("Callout title", (string?)properties.Attribute("title"));
        Assert.Equal("Callout description", (string?)properties.Attribute("descr"));
        using var pdf = new MemoryStream(); ConversionResult result = document.Save(pdf, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        ConversionDiagnostic loss = Assert.Single(result.Diagnostics, item => item.Code == "Shape.TextBoxAlternative");
        Assert.DoesNotContain("Callout description", loss.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingOrderOptionsAreSnapshottedAndUnknownValuesReject()
    {
        PdfOptions options = Accessible(); PdfOptions snapshot = options.Snapshot(); options.TextBoxReadingOrder = PdfTextBoxReadingOrder.Reject;
        Assert.Equal(PdfTextBoxReadingOrder.AnchorOrder, snapshot.TextBoxReadingOrder);
        options.TextBoxReadingOrder = (PdfTextBoxReadingOrder)99;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Snapshot());
    }

    [Fact]
    public void OversizedTextBoxAlternativeMetadataRejectsBeforeUse()
    {
        string box = Box(P("Box text")).Replace("name='Box'", "name='Box' descr='" + new string('x', 1025) + "'");
        byte[] input = Input(box); using var source = new MemoryStream(input);
        var error = Assert.Throws<InvalidDataException>(() => WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.TextBoxAlternative:", error.Message, StringComparison.Ordinal);
    }
}
