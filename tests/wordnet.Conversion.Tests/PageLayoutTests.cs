using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class PageLayoutTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private const string Letter = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='1440' w:right='1200' w:bottom='1440' w:left='1440' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
    private const string Landscape = "<w:sectPr><w:pgSz w:w='15840' w:h='12240' w:orient='landscape'/><w:pgMar w:top='720' w:right='720' w:bottom='720' w:left='720' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";

    [Theory]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Xps)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Xps)]
    public void MixedSectionsPreservePhysicalPageGeometry(DocumentFormat source, DocumentFormat target)
    {
        byte[] input = Document(Paragraph("First", Letter) + Paragraph("Second") + Landscape);
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        byte[] output = Convert(input, source, target);
        if (target == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output);
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Equal(612, pdf.GetPage(1).Width); Assert.Equal(792, pdf.GetPage(1).Height);
            Assert.Equal(792, pdf.GetPage(2).Width); Assert.Equal(612, pdf.GetPage(2).Height);
            Assert.Equal("First", pdf.GetPage(1).Text); Assert.Equal("Second", pdf.GetPage(2).Text);
            Assert.Equal(72, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3);
            Assert.Equal(36, pdf.GetPage(2).Letters[0].StartBaseLine.X, 3);
        }
        else
        {
            using var zip = new ZipArchive(new MemoryStream(output));
            XDocument first = Read(zip, "Documents/1/Pages/1.fpage"), second = Read(zip, "Documents/1/Pages/2.fpage");
            Assert.Equal("816", (string?)first.Root!.Attribute("Width"));
            Assert.Equal("1056", (string?)first.Root!.Attribute("Height"));
            Assert.Equal("1056", (string?)second.Root!.Attribute("Width"));
            Assert.Equal("816", (string?)second.Root!.Attribute("Height"));
        }
    }

    [Fact]
    public void ImportsIndependentApachePoiParagraphAndSectionProperties()
    {
        byte[] output = Convert(ConversionAcceptanceTests.Fixture("poi-layout.doc"), DocumentFormat.Doc, DocumentFormat.Docx);
        using var zip = new ZipArchive(new MemoryStream(output));
        XDocument xml = Read(zip, "word/document.xml");
        XElement[] paragraphs = xml.Descendants(W + "body").Elements(W + "p").ToArray();
        Assert.Equal("Independent layout heading.", string.Concat(paragraphs[0].Descendants(W + "t").Select(t => t.Value)));
        XElement first = paragraphs[0].Element(W + "pPr")!;
        Assert.NotNull(first.Element(W + "keepNext")); Assert.NotNull(first.Element(W + "keepLines"));
        Assert.Equal("120", (string?)first.Element(W + "spacing")?.Attribute(W + "before"));
        Assert.Equal("240", (string?)first.Element(W + "spacing")?.Attribute(W + "after"));
        Assert.Equal("720", (string?)first.Element(W + "ind")?.Attribute(W + "left"));
        Assert.Equal("360", (string?)first.Element(W + "ind")?.Attribute(W + "right"));
        Assert.Equal("240", (string?)first.Element(W + "ind")?.Attribute(W + "hanging"));
        XElement margins = xml.Descendants(W + "pgMar").Single();
        Assert.Equal("1440", (string?)margins.Attribute(W + "left")); Assert.Equal("1200", (string?)margins.Attribute(W + "right"));
        Assert.Equal("1080", (string?)margins.Attribute(W + "top")); Assert.Equal("900", (string?)margins.Attribute(W + "bottom"));
    }

    [Theory]
    [InlineData(DocumentFormat.Docx, false)]
    [InlineData(DocumentFormat.Doc, false)]
    [InlineData(DocumentFormat.Docx, true)]
    [InlineData(DocumentFormat.Doc, true)]
    public void EmptyFinalSectionRetainsItsBlankPageAndGeometry(DocumentFormat source, bool implicitGeometry)
    {
        byte[] input = Document(Paragraph("First", Letter) + (implicitGeometry ? "" : Landscape));
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal("First", pdf.GetPage(1).Text); Assert.Empty(pdf.GetPage(2).Text);
        Assert.InRange(pdf.GetPage(2).Width, implicitGeometry ? 595.25 : 791.99, implicitGeometry ? 595.30 : 792.01);
        if (source == DocumentFormat.Doc)
        {
            using var zip = new ZipArchive(new MemoryStream(Convert(input, source, DocumentFormat.Docx)));
            Assert.Equal(2, Read(zip, "word/document.xml").Descendants(W + "sectPr").Count());
        }
    }

    [Fact]
    public void BinaryWordRetainsEditableSectionsAndParagraphProperties()
    {
        const string properties = "<w:keepNext/><w:keepLines/><w:pageBreakBefore/><w:spacing w:before='200' w:after='300' w:line='360' w:lineRule='auto'/><w:ind w:left='720' w:right='360' w:hanging='240'/>";
        byte[] input = Document(Paragraph("First", properties + Letter) + Paragraph("Second") + Landscape);
        byte[] doc = Convert(input, DocumentFormat.Docx, DocumentFormat.Doc, strict: true);
        byte[] restored = Convert(doc, DocumentFormat.Doc, DocumentFormat.Docx);
        using WordprocessingDocument package = WordprocessingDocument.Open(new MemoryStream(restored), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        using var zip = new ZipArchive(new MemoryStream(restored));
        XDocument xml = Read(zip, "word/document.xml");
        XElement[] sections = xml.Descendants(W + "sectPr").ToArray();
        Assert.Equal(2, sections.Length);
        Assert.Equal("12240", (string?)sections[0].Element(W + "pgSz")?.Attribute(W + "w"));
        Assert.Equal("landscape", (string?)sections[1].Element(W + "pgSz")?.Attribute(W + "orient"));
        XElement p = xml.Descendants(W + "pPr").First();
        Assert.NotNull(p.Element(W + "keepNext")); Assert.NotNull(p.Element(W + "keepLines")); Assert.NotNull(p.Element(W + "pageBreakBefore"));
        Assert.Equal("200", (string?)p.Element(W + "spacing")?.Attribute(W + "before"));
        Assert.Equal("300", (string?)p.Element(W + "spacing")?.Attribute(W + "after"));
        Assert.Equal("360", (string?)p.Element(W + "spacing")?.Attribute(W + "line"));
        Assert.Equal("240", (string?)p.Element(W + "ind")?.Attribute(W + "hanging"));
    }

    [Fact]
    public void SpacingAndIndentationResolveThroughDefaultsAndStyleOverrides()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:docDefaults><w:pPrDefault><w:pPr><w:spacing w:after='240'/><w:ind w:left='720'/></w:pPr></w:pPrDefault></w:docDefaults>"
            + "<w:style w:type='paragraph' w:styleId='Base'><w:pPr><w:spacing w:before='120'/></w:pPr></w:style>"
            + "<w:style w:type='paragraph' w:styleId='Child'><w:basedOn w:val='Base'/><w:pPr><w:ind w:firstLine='360'/></w:pPr></w:style></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Paragraph("Inherited", "<w:pStyle w:val='Child'/>")
            + Paragraph("Override", "<w:pStyle w:val='Child'/><w:spacing w:before='0' w:after='0'/><w:ind w:left='0' w:firstLine='0'/>") + Letter), styles: styles);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Docx, DocumentFormat.Pdf, strict: true));
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal(126, letters[0].StartBaseLine.X, 3);
        Assert.Equal(72, letters[9].StartBaseLine.X, 3);
        Assert.Equal(11 * 1.362 + 12, letters[0].StartBaseLine.Y - letters[9].StartBaseLine.Y, 3);
    }

    [Theory]
    [InlineData("w:line='480' w:lineRule='auto'", "w:lineRule='exact'", "480", "exact")]
    [InlineData("w:line='400' w:lineRule='exact'", "w:line='600'", "600", "exact")]
    [InlineData("w:lineRule='exact'", "w:line='600'", "600", "exact")]
    public void LineAmountAndRuleInheritIndependently(string inherited, string direct, string amount, string rule)
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Base'><w:pPr><w:spacing " + inherited + "/></w:pPr></w:style></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Paragraph("Two lines", "<w:pStyle w:val='Base'/><w:spacing " + direct + "/>")), styles: styles);
        byte[] doc = Convert(input, DocumentFormat.Docx, DocumentFormat.Doc, strict: true);
        using var zip = new ZipArchive(new MemoryStream(Convert(doc, DocumentFormat.Doc, DocumentFormat.Docx)));
        XElement spacing = Read(zip, "word/document.xml").Descendants(W + "spacing").Single();
        Assert.Equal(amount, (string?)spacing.Attribute(W + "line"));
        Assert.Equal(rule, (string?)spacing.Attribute(W + "lineRule"));
    }

    [Fact]
    public void MissingParagraphStyleRetainsDocumentLayoutDefaults()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:docDefaults><w:pPrDefault><w:pPr><w:ind w:left='720'/></w:pPr></w:pPrDefault></w:docDefaults></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Paragraph("Default", "<w:pStyle w:val='Missing'/>") + Letter), styles: styles);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Equal(108, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3);
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void ExplicitPageBreakBeforeStartsANewPage(DocumentFormat source)
    {
        byte[] input = Document(Paragraph("First") + Paragraph("Second", "<w:pageBreakBefore/>") + Letter);
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal("First", pdf.GetPage(1).Text); Assert.Equal("Second", pdf.GetPage(2).Text);
    }

    [Fact]
    public void KeepWithNextMovesHeadingWithFirstFollowingLine()
    {
        string small = Letter.Replace("w:h='15840'", "w:h='4320'").Replace("w:top='1440'", "w:top='720'").Replace("w:bottom='1440'", "w:bottom='720'");
        string body = string.Concat(Enumerable.Range(0, 5).Select(i => Paragraph("Filler" + i.ToString(CultureInfo.InvariantCulture))))
            + Paragraph("Heading", "<w:keepNext/>") + Paragraph("Following") + small;
        using PdfDocument pdf = PdfDocument.Open(Convert(ExplicitLayoutFixture.LegacyMetrics(Document(body)), DocumentFormat.Docx, DocumentFormat.Pdf, strict: true));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.DoesNotContain("Heading", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal("HeadingFollowing", pdf.GetPage(2).Text);
    }

    [Theory]
    [InlineData("w:w='0' w:h='15840'")]
    [InlineData("w:w='1000' w:h='15840'")]
    [InlineData("w:w='2147483647' w:h='15840'")]
    public void InvalidGeometryFailsBeforeWriting(string size)
    {
        byte[] bytes = Document(Paragraph("Body") + "<w:sectPr><w:pgSz " + size + "/></w:sectPr>");
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void KeepChainUsesEachParagraphsOwnAfterSpacing()
    {
        string small = Letter.Replace("w:h='15840'", "w:h='4320'").Replace("w:top='1440'", "w:top='720'").Replace("w:bottom='1440'", "w:bottom='720'");
        string body = P("Filler") + P("Filler") + P("Filler") + P("Filler")
            + Paragraph("One", "<w:keepNext/><w:spacing w:after='0'/>")
            + Paragraph("Two", "<w:keepNext/><w:spacing w:after='480'/>") + P("Three") + small;
        using PdfDocument pdf = PdfDocument.Open(Convert(ExplicitLayoutFixture.LegacyMetrics(Document(body)), DocumentFormat.Docx, DocumentFormat.Pdf, strict: true));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal("OneTwoThree", pdf.GetPage(2).Text);
        static string P(string text) => Paragraph(text);
    }

    [Fact]
    public void NegativeIndentCannotPlaceTextOutsidePhysicalPage()
    {
        using var input = new MemoryStream(Document(Paragraph("Outside", "<w:ind w:right='-2000'/>") + Letter));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Contains("Layout.Indent", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ReflowableOutputReportsSectionLoss()
    {
        using var input = new MemoryStream(Document(Paragraph("Body") + Letter)); using var output = new MemoryStream();
        Assert.Contains("Layout.Sections", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text)).Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    private static string Paragraph(string text, string properties = "") => "<w:p><w:pPr>" + properties + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";
    private static byte[] Document(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static XDocument Read(ZipArchive zip, string path) { using Stream input = zip.GetEntry(path)!.Open(); return XDocument.Load(input); }
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, bool strict = false)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, from, to, new ConversionOptions(strict ? FidelityMode.Strict : FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
}
