using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using global::DocumentFormat.OpenXml.Packaging;
using global::DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;
using W = global::DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

public sealed class ParagraphAlignmentTests
{
    private static readonly string[] ThreeAlignments = { "left", "center", "right" };
    private static readonly string[] CellAlignments = { "center", "right" };
    private static readonly string[] InheritedAlignments = { "right", "center", "left" };
    private static readonly string[] PoiText = { "Cell 1,1", "Cell 1,2", "Cell 1,3" };
    private static readonly string[] HtmlAlignments = { "text-align:left", "text-align:center", "text-align:right", "text-align:center", "text-align:right" };
    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    public void PreservesParagraphAndCellAlignmentAcrossEditableFormats(DocumentFormat intermediate)
    {
        string paragraphs = Paragraph("left") + Paragraph("center") + Paragraph("right");
        byte[] source = TestDocuments.Docx(TestDocuments.Body(paragraphs + "<w:tbl><w:tr><w:tc>" + Paragraph("center") + "</w:tc><w:tc>" + Paragraph("right") + "</w:tc></w:tr></w:tbl>"));
        byte[] exported = Convert(source, DocumentFormat.Docx, intermediate);
        byte[] restored = Convert(exported, intermediate, DocumentFormat.Docx);
        using WordprocessingDocument result = Open(restored);
        W.Body body = result.MainDocumentPart!.Document!.Body!;
        Assert.Equal(ThreeAlignments, body.Elements<W.Paragraph>().Take(3).Select(Alignment));
        Assert.Equal(CellAlignments, body.Descendants<W.TableCell>().Select(cell => Alignment(cell.Elements<W.Paragraph>().Single())));
        Assert.Equal(5, body.Descendants<W.Paragraph>().Count(p => p.InnerText == "Align"));
        Assert.Empty(new OpenXmlValidator().Validate(result, TestContext.Current.CancellationToken));

        if (intermediate == DocumentFormat.Epub)
        {
            using var archive = new ZipArchive(new MemoryStream(exported));
            using Stream stream = archive.GetEntry("EPUB/content.xhtml")!.Open();
            XElement[] html = XDocument.Load(stream).Descendants(XName.Get("p", "http://www.w3.org/1999/xhtml")).ToArray();
            Assert.Equal(HtmlAlignments, html.Select(p => (string?)p.Attribute("style")));
        }
    }

    [Fact]
    public void ImportsAlignmentFromIndependentApachePoiProducer()
    {
        using WordprocessingDocument result = Open(Convert(ConversionAcceptanceTests.Fixture("poi-alignment.doc"), DocumentFormat.Doc, DocumentFormat.Docx));
        W.Paragraph[] paragraphs = result.MainDocumentPart!.Document!.Body!.Descendants<W.TableRow>().First().Descendants<W.Paragraph>().ToArray();
        Assert.Equal(PoiText, paragraphs.Take(3).Select(p => p.InnerText));
        Assert.Equal(ThreeAlignments, paragraphs.Take(3).Select(Alignment));
        Assert.Empty(new OpenXmlValidator().Validate(result, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ResolvesDefaultsStyleInheritanceAndDirectOverrides()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:docDefaults><w:pPrDefault><w:pPr><w:jc w:val='right'/></w:pPr></w:pPrDefault></w:docDefaults>"
            + "<w:style w:type='paragraph' w:styleId='Base'><w:pPr><w:jc w:val='center'/></w:pPr></w:style>"
            + "<w:style w:type='paragraph' w:styleId='Child'><w:basedOn w:val='Base'/></w:style></w:styles>";
        string body = "<w:p><w:r><w:t>Default</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val='Child'/></w:pPr><w:r><w:t>Inherited</w:t></w:r></w:p>"
            + "<w:p><w:pPr><w:pStyle w:val='Child'/><w:jc w:val='left'/></w:pPr><w:r><w:t>Override</w:t></w:r></w:p>";
        byte[] binary = Convert(TestDocuments.Docx(TestDocuments.Body(body), styles: styles), DocumentFormat.Docx, DocumentFormat.Doc, new ConversionOptions());
        using WordprocessingDocument result = Open(Convert(binary, DocumentFormat.Doc, DocumentFormat.Docx));
        Assert.Equal(InheritedAlignments, result.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().Select(Alignment));
    }

    [Fact]
    public void MarkdownColumnAlignmentSurvivesWordAndReturnsAsPipeSyntax()
    {
        const string markdown = "| Left | Center | Right |\n| :--- | :---: | ---: |\n| A | B | C |\n";
        byte[] docx = Convert(Encoding.UTF8.GetBytes(markdown), DocumentFormat.Markdown, DocumentFormat.Docx, new ConversionOptions());
        using WordprocessingDocument result = Open(docx);
        Assert.All(result.MainDocumentPart!.Document!.Body!.Descendants<W.TableRow>(), row =>
            Assert.Equal(ThreeAlignments, row.Descendants<W.Paragraph>().Select(Alignment)));
        string restored = Encoding.UTF8.GetString(Convert(docx, DocumentFormat.Docx, DocumentFormat.Markdown, new ConversionOptions()));
        Assert.Contains("| --- | :---: | ---: |", restored, StringComparison.Ordinal);
        Assert.Empty(new OpenXmlValidator().Validate(result, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Xps)]
    [InlineData(DocumentFormat.Doc, DocumentFormat.Xps)]
    public void FixedPagesPositionLeftCenterAndRightText(DocumentFormat sourceFormat, DocumentFormat target)
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body(Paragraph("left") + Paragraph("center") + Paragraph("right")));
        if (sourceFormat == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, DocumentFormat.Doc);
        byte[] output = Convert(input, sourceFormat, target);
        if (target == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output);
            Assert.Equal(1, pdf.NumberOfPages);
            var lines = pdf.GetPage(1).Letters.GroupBy(l => l.StartBaseLine.Y).OrderByDescending(g => g.Key).ToArray();
            Assert.Equal(3, lines.Length);
            Assert.All(lines, line => Assert.Equal("Align", string.Concat(line.Select(l => l.Value))));
            double left = lines[0].Min(l => l.StartBaseLine.X), width = lines[0].Max(l => l.EndBaseLine.X) - left;
            Assert.Equal(48, left, 2);
            Assert.InRange(Math.Abs(pdf.GetPage(1).Width - 595.276), 0, 0.025); // DOC stores whole twips.
            Assert.Equal(pdf.GetPage(1).Width / 2, lines[1].Min(l => l.StartBaseLine.X) + width / 2, 2);
            Assert.Equal(pdf.GetPage(1).Width - 48, lines[2].Max(l => l.EndBaseLine.X), 2);
        }
        else
        {
            using var archive = new ZipArchive(new MemoryStream(output));
            using Stream stream = archive.GetEntry("Documents/1/Pages/1.fpage")!.Open();
            XElement[] glyphs = XDocument.Load(stream).Descendants(XName.Get("Glyphs", "http://schemas.microsoft.com/xps/2005/06")).ToArray();
            Assert.Equal(3, glyphs.Length);
            Assert.All(glyphs, glyph => Assert.Equal("Align", (string?)glyph.Attribute("UnicodeString")));
            double[] x = glyphs.Select(g => double.Parse((string)g.Attribute("OriginX")!, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(64, x[0], 2);
            Assert.True(x[1] > 200);
            Assert.Equal(x[1] - x[0], x[2] - x[1], 2);
        }
    }

    [Fact]
    public void PdfCentersEveryWrappedLineAcrossStyledRunsAndIgnoresTrailingSpaces()
    {
        string text = string.Concat(Enumerable.Repeat("words for wrapping ", 25));
        string paragraph = "<w:p><w:pPr><w:jc w:val='center'/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>Bold </w:t></w:r><w:r><w:t xml:space='preserve'>" + text + "  </w:t></w:r></w:p>";
        using PdfDocument pdf = PdfDocument.Open(Convert(TestDocuments.Docx(TestDocuments.Body(paragraph)), DocumentFormat.Docx, DocumentFormat.Pdf));
        var lines = pdf.GetPage(1).Letters.GroupBy(l => l.StartBaseLine.Y).ToArray();
        Assert.True(lines.Length >= 4);
        foreach (var line in lines)
        {
            var visible = line.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToArray();
            Assert.Equal(595.276 / 2, (visible.Min(l => l.StartBaseLine.X) + visible.Max(l => l.EndBaseLine.X)) / 2, 2);
        }
        Assert.Equal(ConversionAcceptanceTests.Normalize("Bold " + text), ConversionAcceptanceTests.Normalize(pdf.GetPage(1).Text));
    }

    [Theory]
    [InlineData(DocumentFormat.Text, "Text.Formatting")]
    [InlineData(DocumentFormat.Markdown, "Markdown.Alignment")]
    public void ProseAlignmentLossIsExplicitAndStrictOutputIsUntouched(DocumentFormat target, string code)
    {
        byte[] document = TestDocuments.Docx(TestDocuments.Body(Paragraph("center")));
        using var input = new MemoryStream(document); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, target));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; output.SetLength(0);
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, target, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
        Assert.Contains("Align", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("")]
    public void InvalidDocxAlignmentFailsBeforeWriting(string value)
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(Paragraph(value)))); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Doc, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("both")]
    [InlineData("distribute")]
    [InlineData("end")]
    public void SupportedJustificationIsPreservedAndDirectionDependentAlignmentIsDiagnosed(string value)
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(Paragraph(value)))); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Doc, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        if (value == "end") Assert.Contains(result.Diagnostics, d => d.Code == "Docx.Alignment");
        else Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Docx.Alignment");
    }

    [Fact]
    public void ImportsInheritedAlignmentFromIndependentlyAuthoredEpubMarkup()
    {
        using var memory = new MemoryStream(); byte[] original = ConversionAcceptanceTests.Fixture("plain.epub"); memory.Write(original, 0, original.Length);
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            archive.GetEntry("book/chapter.xhtml")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("book/chapter.xhtml").Open(), new UTF8Encoding(false));
            writer.Write("<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Alignment</title></head><body style='text-align:right'><p>Inherited</p><div align='center'><p>Center</p><p style='text-align:left'>Override</p></div></body></html>");
        }
        using WordprocessingDocument result = Open(Convert(memory.ToArray(), DocumentFormat.Epub, DocumentFormat.Docx, new ConversionOptions()));
        Assert.Equal(InheritedAlignments, result.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().Select(Alignment));
    }

    [Fact]
    public void ConflictingTableCellAlignmentReportsMarkdownLoss()
    {
        string table = "<w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc>" + Paragraph("center") + "</w:tc></w:tr><w:tr><w:tc>" + Paragraph("right") + "</w:tc></w:tr></w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(table))); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Markdown, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Markdown.TableAlignment");
        Assert.Contains("| :---: |", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void PdfAlignsTextWithinEachTableCell()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tr><w:tc>" + Paragraph("center") + "</w:tc><w:tc>" + Paragraph("right") + "</w:tc></w:tr></w:tbl>"));
        using PdfDocument pdf = PdfDocument.Open(Convert(input, DocumentFormat.Docx, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal("AlignAlign", string.Concat(letters.Select(l => l.Value)));
        double column = (595.276 - 96) / 2;
        Assert.Equal(48 + (column - 12) / 2, (letters[0].StartBaseLine.X + letters[4].EndBaseLine.X) / 2, 2);
        Assert.Equal(595.276 - 48 - 12, letters[9].EndBaseLine.X, 2);
    }

    [Fact]
    public void UnsupportedBodyCssCannotSilentlyPassStrictEpubImport()
    {
        using var memory = new MemoryStream(); byte[] original = ConversionAcceptanceTests.Fixture("plain.epub"); memory.Write(original, 0, original.Length);
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            archive.GetEntry("book/chapter.xhtml")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("book/chapter.xhtml").Open(), new UTF8Encoding(false));
            writer.Write("<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Style</title></head><body style='color:red;text-align : center'><p>Text</p></body></html>");
        }
        Assert.Contains("Ebook.Css", Assert.Throws<NotSupportedException>(() => Convert(memory.ToArray(), DocumentFormat.Epub, DocumentFormat.Docx, new ConversionOptions())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FalseTableHeaderDoesNotBecomeARepeatingOrBoldHeader()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tr><w:trPr><w:tblHeader w:val='false'/></w:trPr><w:tc>" + Paragraph("right") + "</w:tc></w:tr></w:tbl>"));
        using WordprocessingDocument result = Open(Convert(Convert(input, DocumentFormat.Docx, DocumentFormat.Doc), DocumentFormat.Doc, DocumentFormat.Docx));
        Assert.Empty(result.MainDocumentPart!.Document!.Descendants<W.TableHeader>());
        Assert.Empty(result.MainDocumentPart.Document.Descendants<W.Bold>());
    }

    private static string Paragraph(string alignment) => "<w:p><w:pPr><w:jc w:val='" + alignment + "'/></w:pPr><w:r><w:t>Align</w:t></w:r></w:p>";
    private static string Alignment(W.Paragraph paragraph) => paragraph.ParagraphProperties?.Justification?.Val?.InnerText ?? "left";
    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);
    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to, ConversionOptions? options = null)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, from, to, options ?? new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken); return output.ToArray();
    }
}
