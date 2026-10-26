using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;
using Wp = global::DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace WordNet.Tests;

public sealed partial class DocumentFidelityTests
{
    [Fact]
    public void ImportsIndependentBinaryWordFontSizeAndIndexedColor()
    {
        using WordprocessingDocument output = Open(Convert(FidelityDocuments.Fixture("poi-appearance.doc"), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Run run = output.MainDocumentPart!.Document!.Descendants<W.Run>().First(r => r.InnerText.Contains("Independent"));
        Assert.Equal("36", run.RunProperties!.FontSize!.Val!.Value);
        Assert.Equal("0000FF", run.RunProperties.Color!.Val!.Value);
        Assert.Contains("Independent blue text.", output.MainDocumentPart.Document.InnerText, StringComparison.Ordinal);
        Validate(output);
    }

    [Fact]
    public void CharacterStylesInheritParagraphSizeAndCanResetColor()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val='20'/></w:rPr></w:rPrDefault></w:docDefaults>"
            + "<w:style w:type='paragraph' w:styleId='Base'><w:rPr><w:sz w:val='36'/><w:color w:val='336699'/></w:rPr></w:style>"
            + "<w:style w:type='character' w:styleId='Emphasis'><w:rPr><w:i/></w:rPr></w:style>"
            + "<w:style w:type='character' w:styleId='Automatic'><w:basedOn w:val='Emphasis'/><w:rPr><w:color w:val='auto'/></w:rPr></w:style></w:styles>";
        string body = "<w:p><w:pPr><w:pStyle w:val='Base'/></w:pPr><w:r><w:rPr><w:rStyle w:val='Emphasis'/></w:rPr><w:t>Inherited</w:t></w:r>"
            + "<w:r><w:rPr><w:rStyle w:val='Automatic'/></w:rPr><w:t>Reset</w:t></w:r></w:p>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), styles: styles);
        using PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Docx, DocumentFormat.Pdf));
        var inherited = pdf.GetPage(1).Letters[0];
        var reset = pdf.GetPage(1).Letters.First(l => l.Value == "R");
        Assert.Equal(18, inherited.FontSize, 3); Assert.Equal(18, reset.FontSize, 3);
        Assert.Equal(0.6, inherited.Color.ToRGBValues().b, 3); Assert.Equal(0, reset.Color.ToRGBValues().b, 3);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    public void MarkdownListsBecomeEditableWordNumbering(DocumentFormat target)
    {
        byte[] result = Convert(Encoding.UTF8.GetBytes("3. First\n   - Nested\n4. Second\n"), DocumentFormat.Markdown, target);
        if (target == DocumentFormat.Doc) result = Convert(result, target, DocumentFormat.Docx);
        using WordprocessingDocument output = Open(result);
        W.Paragraph[] paragraphs = output.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().Where(p => p.InnerText.Length > 0).ToArray();
        Assert.Equal(ListText, paragraphs.Select(p => p.InnerText));
        Assert.Equal(Levels, paragraphs.Select(p => p.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value));
        Assert.Contains(output.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Descendants<W.StartNumberingValue>(), s => s.Val!.Value == 3);
        Validate(output);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    public void IndependentEpubImportsInlineCssImageAndList(DocumentFormat target)
    {
        byte[] result = Convert(IllustratedEpub(), DocumentFormat.Epub, target);
        if (target == DocumentFormat.Doc) result = Convert(result, target, DocumentFormat.Docx);
        using WordprocessingDocument output = Open(result);
        W.Run run = output.MainDocumentPart!.Document!.Descendants<W.Run>().First(r => r.InnerText == "Styled");
        Assert.Equal("36", run.RunProperties!.FontSize!.Val!.Value); Assert.Equal("336699", run.RunProperties.Color!.Val!.Value);
        Assert.Equal(1524000L, Assert.Single(output.MainDocumentPart.Document.Descendants<Wp.Extent>()).Cx!.Value);
        Assert.Contains(output.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Descendants<W.StartNumberingValue>(), s => s.Val!.Value == 5);
        Assert.Single(output.MainDocumentPart.ImageParts); Validate(output);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    public void TableCellListsRetainEditableMarkerAndContent(DocumentFormat intermediate)
    {
        string body = "<w:tbl><w:tr><w:tc>" + FidelityDocuments.ListParagraph("Cell item") + "</w:tc></w:tr></w:tbl>";
        using WordprocessingDocument output = Open(Convert(Convert(FidelityDocuments.Docx(body: body, lists: true), DocumentFormat.Docx, intermediate), intermediate, DocumentFormat.Docx));
        W.Paragraph paragraph = Assert.Single(output.MainDocumentPart!.Document!.Descendants<W.TableCell>()).GetFirstChild<W.Paragraph>()!;
        Assert.Equal("Cell item", paragraph.InnerText); Assert.NotNull(paragraph.ParagraphProperties!.NumberingProperties);
        Validate(output);
    }

    [Fact]
    public void BinaryWordPreservesPreferredTableWidthSeparatelyFromCellWidths()
    {
        byte[] docx = FidelityDocuments.Docx(body: FidelityDocuments.Table.Replace("w:w='7200'", "w:w='8000'"));
        using WordprocessingDocument output = Open(Convert(Convert(docx, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Table table = Assert.Single(output.MainDocumentPart!.Document!.Descendants<W.Table>());
        Assert.Equal("8000", table.GetFirstChild<W.TableProperties>()!.TableWidth!.Width!.Value);
        Assert.Equal(Widths, table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Select(c => c.Width!.Value));
        Validate(output);
    }

    [Fact]
    public void ManyPaddedCellsSurviveBinaryWordIndirectPropertyStorage()
    {
        string cells = string.Concat(Enumerable.Range(0, 20).Select(i => "<w:tc><w:tcPr><w:tcW w:w='400' w:type='dxa'/><w:tcMar><w:left w:w='20' w:type='dxa'/></w:tcMar></w:tcPr>" + TestDocuments.TextParagraph("C" + i) + "</w:tc>"));
        byte[] docx = FidelityDocuments.Docx(body: "<w:tbl><w:tr>" + cells + "</w:tr></w:tbl>");
        using WordprocessingDocument output = Open(Convert(Convert(docx, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        W.TableCell[] actual = output.MainDocumentPart!.Document!.Descendants<W.TableCell>().ToArray();
        Assert.Equal(20, actual.Length); Assert.Equal("C19", actual[19].InnerText);
        Assert.All(actual, c => Assert.Equal("20", c.TableCellProperties!.GetFirstChild<W.TableCellMargin>()!.GetFirstChild<W.LeftMargin>()!.Width!.Value));
        Validate(output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsCorruptOrMissingImagePayloadBeforeWriting(bool missing)
    {
        byte[] input = Rewrite(FidelityDocuments.Docx("color-grid.png"), "word/media/color-grid.png", missing ? null : FidelityDocuments.Fixture("color-grid.png").Take(40).ToArray());
        Fails<InvalidDataException>(input, DocumentFormat.Docx);
    }

    [Fact]
    public void CroppedImageReportsLossAndStrictModeLeavesDestinationUntouched()
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph().Replace("<a:stretch>", "<a:srcRect l='10000'/><a:stretch>"));
        Fails<NotSupportedException>(input, DocumentFormat.Docx, new ConversionOptions());
        using var source = new MemoryStream(input); using var destination = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Epub, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Image.TransformOutput");
    }

    [Fact]
    public void InlineImagesPaginateWithoutClippingOrOverlap()
    {
        string body = string.Concat(Enumerable.Repeat(FidelityDocuments.ImageParagraph(1524000, 3810000), 3));
        using PdfDocument pdf = PdfDocument.Open(Convert(ExplicitLayoutFixture.LegacyMetrics(FidelityDocuments.Docx("color-grid.png", body)), DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages);
        var first = pdf.GetPage(1).GetImages().OrderByDescending(i => i.BoundingBox.Top).ToArray();
        Assert.Equal(2, first.Length); Assert.Single(pdf.GetPage(2).GetImages());
        Assert.True(first[0].BoundingBox.Bottom > first[1].BoundingBox.Top);
        Assert.All(pdf.GetPages().SelectMany(p => p.GetImages()), i => { Assert.Equal(300, i.BoundingBox.Height, 3); Assert.True(i.BoundingBox.Bottom >= 48); });
    }

    [Fact]
    public void ListStartOverrideIsRetained()
    {
        string numbering = FidelityDocuments.Numbering.Replace("<w:abstractNumId w:val='5'/>", "<w:abstractNumId w:val='5'/><w:lvlOverride w:ilvl='0'><w:startOverride w:val='8'/></w:lvlOverride>");
        using PdfDocument pdf = PdfDocument.Open(Convert(FidelityDocuments.Docx(body: FidelityDocuments.ListParagraph("Restarted"), lists: true, numbering: numbering), DocumentFormat.Docx, DocumentFormat.Pdf));
        Assert.Contains("8. Restarted", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "31d6cfe0d16ae931b73c59d7e0c089c0")]
    [InlineData("abc", "a448017aaf21d8525fc10ae87aa6729d")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "e33b4ddc9c38f2199c3e7b164fcc0536")]
    public void BinaryPictureIdentifierMatchesPublishedRfc1320Vectors(string input, string expected)
    {
        // OfficeArt requires this legacy identifier, not a security digest.
        Type type = typeof(WordConverter).Assembly.GetType("WordNet.Internal.ImageIdentifier", true)!;
        var method = type.GetMethod("Compute", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        byte[] digest = (byte[])method.Invoke(null, new object[] { Encoding.ASCII.GetBytes(input) })!;
        Assert.Equal(Enumerable.Range(0, 16).Select(i => System.Convert.ToByte(expected.Substring(i * 2, 2), 16)), digest);
    }

    private static byte[] Rewrite(byte[] package, string part, byte[]? replacement)
    {
        using var memory = new MemoryStream(); memory.Write(package, 0, package.Length);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        { zip.GetEntry(part)!.Delete(); if (replacement != null) FidelityDocuments.Add(zip, part, replacement); }
        return memory.ToArray();
    }

    private static byte[] IllustratedEpub()
    {
        // Independently authored EPUB package, not produced by wordnet's serializer.
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Text(string name, string value) => FidelityDocuments.Add(zip, name, Encoding.UTF8.GetBytes(value));
            Text("mimetype", "application/epub+zip");
            Text("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Text("book.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='remote' href='https://example.invalid/unused.png' media-type='image/png'/><item id='text' href='chapter.xhtml' media-type='application/xhtml+xml'/><item id='grid' href='grid.png' media-type='image/png'/></manifest><spine><itemref idref='text'/></spine></package>");
            Text("chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Independent</title></head><body><p style='font-size:24px;color:#336699'>Styled</p><p><img src='grid.png' width='160' height='80' alt='Grid'/></p><ol start='5'><li>Fifth</li></ol></body></html>");
            FidelityDocuments.Add(zip, "grid.png", FidelityDocuments.Fixture("color-grid.png"));
        }
        return memory.ToArray();
    }
}
