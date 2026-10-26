using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static readonly string[] RefreshedWithoutPagination = { "Current", "1", "7" };
    private static readonly string[] RomanNoteMarker = { "[iii]" };
    [Theory]
    [InlineData(DocumentFormat.Html)]
    [InlineData(DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Text)]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    [InlineData(DocumentFormat.Doc)]
    public void ExplicitBreaksDegradeToLegalLineBreaks(DocumentFormat format)
    {
        byte[] input = Docx("<w:p><w:r><w:t>Before</w:t><w:br w:type='page'/><w:t>Middle</w:t><w:br w:type='column'/><w:t>After</w:t></w:r></w:p>");
        byte[] output = ExportMode(input, format, FidelityMode.Lenient, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Layout.ExplicitBreak");
        string text = OutputText(output, format);
        Assert.Contains("Before", text, StringComparison.Ordinal); Assert.Contains("Middle", text, StringComparison.Ordinal); Assert.Contains("After", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\f', text); Assert.DoesNotContain('\v', text);
        Fails<NotSupportedException>(input, format);
    }

    [Fact]
    public void PageNumberZeroIsRetainedAndRendered()
    {
        byte[] input = Docx("<w:p>" + Field("PAGE", "99") + "<w:r><w:br w:type='page'/></w:r>" + Field("PAGE", "99") + "</w:p>"
            + Page.Replace("<w:sectPr>", "<w:sectPr><w:pgNumType w:start='0'/>"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal("0", pdf.GetPage(1).Text); Assert.Equal("1", pdf.GetPage(2).Text);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Equal("0", (string?)Part(word, "word/document.xml").Descendants(W + "pgNumType").Single().Attribute(W + "start"));
    }

    [Fact]
    public void NegativeImageCropUsesAnExplicitFallback()
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph().Replace("<a:stretch>", "<a:srcRect l='-4000' t='-2000'/><a:stretch>"));
        Fails<NotSupportedException>(input, DocumentFormat.Pdf);
        using PdfDocument pdf = PdfDocument.Open(ExportMode(input, DocumentFormat.Pdf, FidelityMode.Lenient, out ConversionResult result));
        Assert.Single(pdf.GetPage(1).GetImages()); Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ImageCropPadding");
    }

    [Fact]
    public void BookmarksAcrossBodyTableAndCellBoundariesRemainResolvable()
    {
        string body = "<w:bookmarkStart w:id='1' w:name='BodyRange'/>" + P("Body") + "<w:bookmarkEnd w:id='1'/>"
            + Table(Cell("<w:bookmarkStart w:id='2' w:name='CellRange'/><w:p><w:r><w:t>Cell</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>"), 4000)
                .Replace("<w:tbl>", "<w:tbl><w:bookmarkStart w:id='3' w:name='TableRange'/>").Replace("</w:tbl>", "<w:bookmarkEnd w:id='3'/></w:tbl>")
            + "<w:p>" + Field("REF BodyRange", "stale") + Field("REF CellRange", "stale") + Field("REF TableRange", "stale") + "<w:bookmarkEnd w:id='99'/></w:p>";
        using PdfDocument pdf = PdfDocument.Open(Export(Docx(body + Page), DocumentFormat.Pdf, out ConversionResult result));
        Assert.Equal("BodyCellBodyCellCell", pdf.GetPage(1).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.BookmarkEndIgnored");
        byte[] word = Export(Docx(body + Page), DocumentFormat.Docx); Schema(word);
        Assert.Equal(3, Part(word, "word/document.xml").Descendants(W + "bookmarkStart").Count());
    }

    [Fact]
    public void DocxReferenceRefreshDoesNotInvokePagination()
    {
        string table = Table(Cell(P("Native exact row")), 4000).Replace("<w:tr>", "<w:tr><w:trPr><w:trHeight w:val='20' w:hRule='exact'/></w:trPr>");
        string body = "<w:p><w:bookmarkStart w:id='1' w:name='Title'/><w:r><w:t>Current</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>"
            + "<w:p>" + Field("REF Title", "stale") + Field("SEQ Figure", "99") + Field("PAGEREF Title", "7") + "</w:p>" + table
            + TextBox(P("Overflowing native box"), 8) + Page;
        byte[] word = Export(Docx(body), DocumentFormat.Docx, out ConversionResult result); Schema(word);
        Assert.Null(result.Metrics.PageCount);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Layout.", StringComparison.Ordinal) || d.Code.StartsWith("Font.", StringComparison.Ordinal));
        XElement[] fields = Part(word, "word/document.xml").Descendants(W + "fldSimple").ToArray();
        Assert.Equal(RefreshedWithoutPagination, fields.Select(f => f.Value));
        Assert.Equal("20", (string?)Part(word, "word/document.xml").Descendants(W + "trHeight").Single().Attribute(W + "val"));
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void LenientRowsBleedImagesAndTextBoxesRetainContent(DocumentFormat format)
    {
        string table = Table(Cell(P("Row")), 4000).Replace("<w:tr>", "<w:tr><w:trPr><w:trHeight w:val='280' w:hRule='exact'/></w:trPr>");
        string image = AnchorImage("none", false).Replace("relativeFrom='margin'", "relativeFrom='page'").Replace("<wp:posOffset>0</wp:posOffset>", "<wp:posOffset>-127000</wp:posOffset>");
        byte[] input = FidelityDocuments.Docx("color-grid.png", table + image + TextBox(P("First") + Table(Cell(P("Table text")), 1000), 15) + Page);
        byte[] output = ExportMode(input, format, FidelityMode.Lenient, out ConversionResult result);
        foreach (string code in new[] { "Layout.TableRowHeight", "Layout.AnchorBounds", "Layout.TextBoxContent", "Layout.TextBoxOverflow" })
            Assert.Contains(result.Diagnostics, d => d.Code == code);
        string text = OutputText(output, format); Assert.Contains("Row", text, StringComparison.Ordinal); Assert.Contains("First", text, StringComparison.Ordinal); Assert.Contains("Table text", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnchorBoundsAreCheckedAfterKeepGroupMoves()
    {
        string image = AnchorImage("none", false).Replace("relativeFrom='margin'", "relativeFrom='paragraph'")
            .Replace("<wp:positionH relativeFrom='paragraph'>", "<wp:positionH relativeFrom='margin'>");
        string filler = string.Concat(Enumerable.Repeat(P("Filler"), 9));
        string keep = "<w:p><w:pPr><w:keepNext/></w:pPr><w:r><w:t>Keep</w:t></w:r></w:p>";
        string following = image.Replace("<w:p>", "<w:p><w:pPr><w:keepLines/></w:pPr>").Replace("</w:p>", "<w:r><w:t>A</w:t><w:br/><w:t>B</w:t><w:br/><w:t>C</w:t><w:br/><w:t>D</w:t></w:r></w:p>");
        using PdfDocument pdf = PdfDocument.Open(Export(ExplicitLayoutFixture.LegacyMetrics(FidelityDocuments.Docx("color-grid.png", filler + keep + following + Page)), DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages); Assert.Single(pdf.GetPage(2).GetImages()); Assert.Contains("Keep", pdf.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Text)]
    [InlineData(DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Mobi)]
    [InlineData(DocumentFormat.Azw3)]
    [InlineData(DocumentFormat.Doc)]
    public void NoteFallbackUsesFinalLabelsAndKeepsTheFinalSection(DocumentFormat format)
    {
        byte[] input = Notes("<w:p><w:r><w:rPr><w:b/><w:vertAlign w:val='superscript'/></w:rPr><w:t>Body</w:t><w:footnoteReference w:id='7'/></w:r></w:p>", P("Note text"));
        input = ReplacePart(input, "word/document.xml", Part(input, "word/document.xml").ToString().Replace("</w:sectPr>", "<w:footnotePr><w:numFmt w:val='lowerRoman'/><w:numStart w:val='3'/></w:footnotePr></w:sectPr>"));
        byte[] output = ExportMode(input, format, FidelityMode.Lenient, out _);
        string text = OutputText(output, format).Replace("\\[", "[").Replace("\\]", "]"); Assert.Contains("[iii]", text, StringComparison.Ordinal); Assert.DoesNotContain("[1]", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split(RomanNoteMarker, StringSplitOptions.None).Length - 1);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken) { Destination = format };
        DocumentModel model = DocxReader.Read(new MemoryStream(input), context); FidelityChecks.Destination(model, format, context);
        Assert.All(model.Blocks, b => Assert.Same(model.FinalSection, b.Section));
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        XElement reference = Part(word, "word/document.xml").Descendants(W + "footnoteReference").Single().Parent!;
        Assert.NotNull(reference.Element(W + "rPr")?.Element(W + "b"));
        Assert.Equal("superscript", (string?)reference.Element(W + "rPr")?.Element(W + "vertAlign")?.Attribute(W + "val"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fixed")]
    [InlineData("autofit")]
    public void DocxPreservesExplicitAndImplicitTableLayout(string? layout)
    {
        string table = Table(Cell(P("Cell")), 4000);
        if (layout != null) table = table.Replace("<w:tbl>", "<w:tbl><w:tblPr><w:tblLayout w:type='" + layout + "'/></w:tblPr>");
        byte[] word = Export(Docx(table), DocumentFormat.Docx); Schema(word);
        Assert.Equal(layout, (string?)Part(word, "word/document.xml").Descendants(W + "tblLayout").SingleOrDefault()?.Attribute(W + "type"));
        using var output = new MemoryStream();
        new WordConverter().Convert(new MemoryStream(Encoding.UTF8.GetBytes("| A | B |\n|---|---|\n| C | D |")), output, DocumentFormat.Markdown, DocumentFormat.Docx);
        Schema(output.ToArray()); Assert.Empty(Part(output.ToArray(), "word/document.xml").Descendants(W + "tblLayout"));
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void DefaultWordTocRefreshesAcrossCachedParagraphs(DocumentFormat format)
    {
        string toc = "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> TOC \\o &quot;1-3&quot; \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r>"
            + "<w:hyperlink w:anchor='Old'><w:r><w:t>Old first</w:t></w:r>" + Field("PAGEREF Old", "9") + "</w:hyperlink></w:p>"
            + "<w:p><w:r><w:t>Old second</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>";
        string heading = "<w:p><w:pPr><w:outlineLvl w:val='0'/><w:pageBreakBefore/></w:pPr><w:r><w:t>Current heading</w:t></w:r></w:p>";
        string text = OutputText(ExportMode(Docx(toc + heading + Page), format, FidelityMode.Lenient, out ConversionResult result), format);
        Assert.DoesNotContain("Old", text, StringComparison.Ordinal); Assert.Contains("Current heading", text, StringComparison.Ordinal); Assert.Contains("2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
    }

    [Theory]
    [InlineData("\\n", "Heading")]
    [InlineData("\\p &quot;: &quot;", "Heading: 1")]
    public void TocDisplaySwitchesControlPageLabels(string switches, string expected)
    {
        string heading = "<w:p><w:pPr><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>Heading</w:t></w:r></w:p>";
        using PdfDocument pdf = PdfDocument.Open(Export(Docx("<w:p>" + Field("TOC \\o &quot;1-3&quot; \\z \\u " + switches, "stale") + "</w:p>" + heading + Page), DocumentFormat.Pdf));
        Assert.Equal(expected + "Heading", pdf.GetPage(1).Text);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Epub)]
    public void RichReferenceCachesKeepPicturesBookmarksAndFormatting(DocumentFormat format)
    {
        string imageRun = XElement.Parse(TestDocuments.Body(FidelityDocuments.ImageParagraph())).Descendants(W + "p").Single().Elements(W + "r").Single().ToString();
        string body = "<w:p><w:bookmarkStart w:id='1' w:name='Logo'/>" + imageRun + "<w:bookmarkEnd w:id='1'/></w:p>"
            + "<w:p><w:fldSimple w:instr='REF Logo \\h'>" + imageRun + "<w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:t>Plain</w:t></w:r></w:fldSimple></w:p>";
        byte[] output = ExportMode(FidelityDocuments.Docx("color-grid.png", body), format, FidelityMode.Lenient, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
        if (format == DocumentFormat.Pdf) { using PdfDocument pdf = PdfDocument.Open(output); Assert.Equal(2, pdf.GetPages().Sum(p => p.GetImages().Count())); }
        else if (format == DocumentFormat.Docx) { Schema(output); Assert.Equal(2, Part(output, "word/document.xml").Descendants(W + "drawing").Count()); Assert.NotEmpty(Part(output, "word/document.xml").Descendants(W + "b")); }
        else Assert.Equal(2, Part(output, "EPUB/content.xhtml").Descendants().Count(e => e.Name.LocalName == "img"));
        string text = OutputText(output, format); Assert.Contains("Bold", text, StringComparison.Ordinal); Assert.Contains("Plain", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Text)]
    [InlineData(DocumentFormat.Markdown)]
    [InlineData(DocumentFormat.Epub)]
    [InlineData(DocumentFormat.Doc)]
    public void FlattenedTextBoxesKeepNestedTableTextAndRunContent(DocumentFormat format)
    {
        string content = "<w:p><w:hyperlink w:anchor='Target'><w:r><w:rPr><w:b/></w:rPr><w:t>Linked</w:t></w:r></w:hyperlink></w:p>"
            + Table(Cell(P("Cell") + TextBox(P("Nested"), 30)), 2000);
        byte[] input = Docx(TextBox(content, 100));
        byte[] output = ExportMode(input, format, FidelityMode.Lenient, out ConversionResult result);
        string text = OutputText(output, format); Assert.Contains("Linked", text, StringComparison.Ordinal); Assert.Contains("Cell", text, StringComparison.Ordinal); Assert.Contains("Nested", text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == "Shape.TextBoxFlattened");
        if (format == DocumentFormat.Markdown) { Assert.Contains("**Linked**", text, StringComparison.Ordinal); Assert.Contains("#Target", text, StringComparison.Ordinal); }
    }

    [Theory]
    [InlineData(false, 72)]
    [InlineData(false, 144)]
    [InlineData(true, 72)]
    [InlineData(true, 144)]
    [InlineData(true, 72, "little")]
    [InlineData(true, 144, "little")]
    [InlineData(true, 72, "big")]
    [InlineData(true, 144, "big")]
    public void XpsImageCropsAreIndependentOfEmbeddedDensity(bool jpeg, int dpi, string? exif = null)
    {
        byte[] pixels = ImageWithDensity(jpeg, dpi, exif);
        byte[] input = ReplaceImage(FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph(1524000, 762000)
            .Replace("<a:stretch>", "<a:srcRect l='25000'/><a:stretch>") + Page), pixels);
        byte[] output = Export(input, DocumentFormat.Xps);
        XElement brush = Part(output, "Documents/1/Pages/1.fpage").Descendants().Single(e => e.Name.LocalName == "ImageBrush");
        Assert.Equal("Absolute", (string?)brush.Attribute("ViewboxUnits"));
        double[] viewbox = brush.Attribute("Viewbox")!.Value.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        Assert.InRange(viewbox[2], 80 * 0.75 * 96 / dpi - 0.01, 80 * 0.75 * 96 / dpi + 0.01);
        SaveBytes(output, "density-" + (exif ?? (jpeg ? "jpeg" : "png")) + "-" + dpi.ToString(CultureInfo.InvariantCulture) + ".xps");
    }

    private static byte[] ImageWithDensity(bool jpeg, int dpi, string? exif)
    {
        byte[] original = AsymmetricImage();
        if (jpeg)
        {
            using SKBitmap bitmap = SKBitmap.Decode(original); using SKData data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
            byte[] encoded = data.ToArray();
            // Independently authored JFIF APP0: density in dots per inch.
            byte[] header = { 0xff, 0xe0, 0, 16, 74, 70, 73, 70, 0, 1, 2, 1, 0, (byte)dpi, 0, (byte)dpi, 0, 0 };
            if (exif != null)
            {
                bool little = exif == "little";
                byte[] tiff = new byte[66]; tiff[0] = tiff[1] = (byte)(little ? 'I' : 'M');
                Short(2, 42); Long(4, 8); Short(8, 3);
                Short(10, 0x011a); Short(12, 5); Long(14, 1); Long(18, 50);
                Short(22, 0x011b); Short(24, 5); Long(26, 1); Long(30, 58);
                Short(34, 0x0128); Short(36, 3); Long(38, 1); Short(42, 2);
                Long(50, (uint)dpi); Long(54, 1); Long(58, (uint)dpi); Long(62, 1);
                header = new byte[] { 0xff, 0xe1, 0, 74, 69, 120, 105, 102, 0, 0 }.Concat(tiff).ToArray();
                void Short(int offset, int value) { tiff[offset] = (byte)(little ? value : value >> 8); tiff[offset + 1] = (byte)(little ? value >> 8 : value); }
                void Long(int offset, uint value) { Short(offset, (int)(little ? value & 0xffff : value >> 16)); Short(offset + 2, (int)(little ? value >> 16 : value & 0xffff)); }
            }
            int skip = encoded[2] == 0xff && encoded[3] == 0xe0 ? 4 + encoded[4] * 256 + encoded[5] : 2;
            return encoded.Take(2).Concat(header).Concat(encoded.Skip(skip)).ToArray();
        }
        int ppm = (int)Math.Round(dpi / 0.0254);
        byte[] density = { 112, 72, 89, 115, (byte)(ppm >> 24), (byte)(ppm >> 16), (byte)(ppm >> 8), (byte)ppm,
            (byte)(ppm >> 24), (byte)(ppm >> 16), (byte)(ppm >> 8), (byte)ppm, 1 };
        uint crc = uint.MaxValue;
        foreach (byte value in density)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320U);
        }
        crc = ~crc;
        return original.Take(33).Concat(new byte[] { 0, 0, 0, 9 }).Concat(density)
            .Concat(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }).Concat(original.Skip(33)).ToArray();
    }

    private static string TextBox(string content, int height) => "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0'><wp:positionH relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionV><wp:extent cx='2540000' cy='" + (height * 12700).ToString(CultureInfo.InvariantCulture) + "'/><wp:wrapNone/><wp:docPr id='5' name='Box'/><a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:prstGeom prst='rect'><a:avLst/></a:prstGeom></wps:spPr><wps:txbx><w:txbxContent>" + content + "</w:txbxContent></wps:txbx><wps:bodyPr><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";

    private static byte[] ExportMode(byte[] input, DocumentFormat format, FidelityMode mode, out ConversionResult result)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        var options = new ConversionOptions(mode);
        if (format == DocumentFormat.Docx)
        {
            var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = format };
            DocumentModel model = DocxReader.Read(source, context); FidelityChecks.Destination(model, format, context); DocxWriter.Write(model, output, context); result = context.Result(output.Length);
        }
        else result = new WordConverter().Convert(source, output, DocumentFormat.Docx, format, options, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static string OutputText(byte[] output, DocumentFormat format)
    {
        if (format == DocumentFormat.Pdf) { using PdfDocument pdf = PdfDocument.Open(output); return string.Concat(pdf.GetPages().Select(p => p.Text)); }
        if (format == DocumentFormat.Xps) return string.Concat(Part(output, "Documents/1/Pages/1.fpage").Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString")));
        if (format == DocumentFormat.Html || format is DocumentFormat.Text or DocumentFormat.Markdown) return Encoding.UTF8.GetString(output);
        if (format == DocumentFormat.Docx) return Part(output, "word/document.xml").ToString();
        if (format == DocumentFormat.Epub) return Part(output, "EPUB/content.xhtml").ToString();
        using var input = new MemoryStream(output);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        DocumentModel model = format == DocumentFormat.Doc ? DocFormat.Read(input, context) : KindleFormat.Read(input, format == DocumentFormat.Azw3, context);
        return string.Join("\n", model.Paragraphs.Select(p => p.Text));
    }

    private static byte[] ReplacePart(byte[] input, string path, string xml)
    {
        using var buffer = new MemoryStream(); buffer.Write(input, 0, input.Length);
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Update, true))
        { zip.GetEntry(path)!.Delete(); FidelityDocuments.Add(zip, path, Encoding.UTF8.GetBytes(xml)); }
        return buffer.ToArray();
    }
}
