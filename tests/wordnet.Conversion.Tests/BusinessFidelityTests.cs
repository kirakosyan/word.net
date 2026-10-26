using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;
using WordNet.Formats;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

// Fixtures below are authored directly as OOXML, independently of production writers.
public sealed partial class BusinessFidelityTests
{
    private const string Page = "<w:sectPr><w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
    private static readonly XNamespace W = TestDocuments.W;

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void NestedDecoratedCellsKeepGeometryAndReadingOrder(DocumentFormat format)
    {
        string nested = Table(Cell(P("Inner"), "<w:shd w:fill='00FF00'/>") + Cell(P("Right")), 1600, 1600);
        string outer = Table(Cell(P("Before") + Table(Cell(nested + P("After")), 3200) + P("Tail"), "<w:shd w:fill='FFFF00'/><w:tcBorders><w:top w:val='double' w:sz='12' w:color='FF0000'/></w:tcBorders>"), 4000);
        byte[] output = Export(Docx(outer + Page), format, out ConversionResult result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "Table.NestedFlattened" or "Table.Decoration");
        if (format == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output, SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
            Assert.Equal("BeforeInnerRightAfterTail", pdf.GetPage(1).Text);
            Assert.All(pdf.GetPage(1).Letters, l => Assert.InRange(l.StartBaseLine.X, 30, 230));
            using SKBitmap raster = pdf.GetPageAsSKBitmap(1, 1, SKColors.White);
            Assert.Equal(SKColors.Yellow, raster.GetPixel(225, 35));
            Assert.Equal(SKColors.Lime, raster.GetPixel(100, 54));
            SaveRaster(raster, "nested-tables");
        }
        else
        {
            SaveBytes(output, "nested-tables.xps");
            XDocument page = Part(output, "Documents/1/Pages/1.fpage");
            Assert.Equal("BeforeInnerRightAfterTail", string.Concat(page.Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString"))));
            Assert.Contains(page.Descendants().Attributes("Fill"), a => a.Value == "#FF00FF00");
        }
    }

    [Fact]
    public void NestedLongRowsSplitAndNonsplittingRowsPreserveDestination()
    {
        string lines = string.Concat(Enumerable.Range(0, 50).Select(i => P("Line" + i.ToString("D2", CultureInfo.InvariantCulture))));
        string table = Table(Cell(Table(Cell(lines), 3000)), 4000);
        using PdfDocument pdf = PdfDocument.Open(Export(Docx(table + Page), DocumentFormat.Pdf));
        Assert.True(pdf.NumberOfPages > 3);
        Assert.Equal(string.Concat(Enumerable.Range(0, 50).Select(i => "Line" + i.ToString("D2", CultureInfo.InvariantCulture))), string.Concat(pdf.GetPages().Select(p => p.Text)));
        Assert.All(pdf.GetPages(), p => Assert.All(p.Letters, l => Assert.InRange(l.StartBaseLine.Y, 30, 270)));
        Fails<NotSupportedException>(Docx(table.Replace("<w:tr>", "<w:tr><w:trPr><w:cantSplit/></w:trPr>") + Page), DocumentFormat.Pdf);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PercentageWidthsResolveInsideTheContainingArea(bool autofit)
    {
        string table = "<w:tbl><w:tblPr><w:tblW w:type='pct' w:w='5000'/><w:tblLayout w:type='" + (autofit ? "autofit" : "fixed") + "'/></w:tblPr>"
            + "<w:tr>" + Cell(P("A"), "<w:tcW w:type='pct' w:w='1250'/>") + Cell(P("B"), "<w:tcW w:type='pct' w:w='3750'/>") + "</w:tr></w:tbl>";
        byte[] input = Docx(table + Page);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(30, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3);
        Assert.InRange(pdf.GetPage(1).Letters[1].StartBaseLine.X, 110, 145);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Equal("pct", (string?)Part(word, "word/document.xml").Descendants(W + "tblW").Single().Attribute(W + "type"));
    }

    [Fact]
    public void AutofitWithTinyRunsIsDeterministicAndBounded()
    {
        string text = "<w:p>" + string.Concat(Enumerable.Repeat("<w:r><w:t>x</w:t></w:r>", 3000)) + "</w:p>";
        string table = Table(Cell(text) + Cell(P("B")), 3000, 3000).Replace("<w:tbl>", "<w:tbl><w:tblPr><w:tblLayout w:type='autofit'/></w:tblPr>");
        byte[] input = Docx(table + Page);
        byte[] first = Export(input, DocumentFormat.Pdf), second = Export(input, DocumentFormat.Pdf);
        Assert.Equal(first, second);
        using PdfDocument pdf = PdfDocument.Open(first);
        Assert.Equal(3000, pdf.GetPages().Sum(p => p.Text.Count(c => c == 'x')));
        Assert.True(pdf.NumberOfPages < 10);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(90, false, false)]
    [InlineData(180, false, false)]
    [InlineData(270, false, false)]
    [InlineData(0, true, false)]
    [InlineData(90, true, false)]
    [InlineData(180, true, false)]
    [InlineData(270, true, false)]
    [InlineData(0, false, true)]
    [InlineData(90, false, true)]
    [InlineData(180, false, true)]
    [InlineData(270, false, true)]
    public void RasterCropRotationAndMirrorMatchIndependentPixelCoordinates(int rotation, bool mirror, bool mirrorVertical)
    {
        string image = FidelityDocuments.ImageParagraph(1524000, 762000).Replace(" descr='Red and blue grid'", "")
            .Replace("<a:xfrm>", "<a:xfrm rot='" + (rotation * 60000).ToString(CultureInfo.InvariantCulture) + "' flipH='" + (mirror ? "1" : "0") + "' flipV='" + (mirrorVertical ? "1" : "0") + "'>")
            .Replace("<a:stretch>", "<a:srcRect l='25000'/><a:stretch>");
        byte[] pixels = AsymmetricImage();
        byte[] input = ReplaceImage(FidelityDocuments.Docx("color-grid.png", image + Page), pixels);
        byte[] output = Export(input, DocumentFormat.Pdf);
        using PdfDocument pdf = PdfDocument.Open(output, SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
        using SKBitmap raster = pdf.GetPageAsSKBitmap(1, 1, SKColors.White);
        using SKBitmap original = SKBitmap.Decode(pixels);
        double width = rotation % 180 == 0 ? 120 : 60, height = rotation % 180 == 0 ? 60 : 120;
        double angle = rotation * Math.PI / 180, c = Math.Round(Math.Cos(angle)), s = Math.Round(Math.Sin(angle));
        foreach (double u in new[] { 0.15, 0.8 }) foreach (double v in new[] { 0.2, 0.7 })
        {
            double x = (u - 0.5) * 120 * (mirror ? -1 : 1), y = (v - 0.5) * 60 * (mirrorVertical ? -1 : 1);
            int dx = (int)Math.Round(30 + width / 2 + c * x - s * y), dy = (int)Math.Round(30 + height / 2 + s * x + c * y);
            SKColor expected = original.GetPixel((int)((0.25 + u * 0.75) * original.Width), (int)(v * original.Height));
            SKColor actual = raster.GetPixel(dx, dy);
            Assert.InRange(Math.Abs(actual.Red - expected.Red) + Math.Abs(actual.Green - expected.Green) + Math.Abs(actual.Blue - expected.Blue), 0, 20);
        }
        Assert.Equal(SKColors.White, raster.GetPixel(29, 29));
        SaveRaster(raster, "transform-" + rotation.ToString(CultureInfo.InvariantCulture) + "-" + mirror + "-" + mirrorVertical);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Equal((rotation * 60000).ToString(CultureInfo.InvariantCulture), (string?)Part(word, "word/document.xml").Descendants().Single(e => e.Name.LocalName == "xfrm").Attribute("rot"));
        byte[] xpsBytes = Export(input, DocumentFormat.Xps);
        SaveBytes(xpsBytes, "transform-" + rotation.ToString(CultureInfo.InvariantCulture) + "-" + mirror + "-" + mirrorVertical + ".xps");
        XDocument xps = Part(xpsBytes, "Documents/1/Pages/1.fpage");
        Assert.Single(xps.Descendants(), e => e.Name.LocalName == "ImageBrush");
    }

    [Theory]
    [InlineData("l='100000'")]
    [InlineData("l='50000' r='50000'")]
    [InlineData("t='NaN'")]
    public void InvalidCropNeverReplacesCallerOutput(string crop) => Fails<InvalidDataException>(FidelityDocuments.Docx("color-grid.png",
        FidelityDocuments.ImageParagraph().Replace("<a:stretch>", "<a:srcRect " + crop + "/><a:stretch>")), DocumentFormat.Pdf);

    [Fact]
    public void AnchoredLogoWrapsBodyAndKeepsPhysicalPosition()
    {
        string logo = AnchorImage("square", false);
        string body = logo.Replace("</w:p>", "<w:r><w:t>Body flows beside the floating logo with enough words to wrap around its rectangular bounds.</w:t></w:r></w:p>");
        byte[] input = FidelityDocuments.Docx("color-grid.png", body + Page);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        var image = Assert.Single(pdf.GetPage(1).GetImages());
        Assert.Equal(30, image.BoundingBox.Left, 3); Assert.Equal(270, image.BoundingBox.Top, 3);
        Assert.All(pdf.GetPage(1).Letters.Where(l => l.StartBaseLine.Y > 210), l => Assert.True(l.StartBaseLine.X >= 150));
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Single(Part(word, "word/document.xml").Descendants(), e => e.Name.LocalName == "anchor");
    }

    [Fact]
    public void ColumnsAndExplicitColumnBreakUseSeparateOrigins()
    {
        string section = Page.Replace("</w:sectPr>", "<w:cols w:num='2' w:space='400'/></w:sectPr>");
        byte[] input = Docx("<w:p><w:r><w:t>Left</w:t><w:br w:type='column'/><w:t>Right</w:t></w:r></w:p>" + section);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(1, pdf.NumberOfPages); Assert.Equal("LeftRight", pdf.GetPage(1).Text);
        Assert.Equal(30, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3);
        Assert.Equal(210, pdf.GetPage(1).Letters[4].StartBaseLine.X, 3);
        Assert.Equal(pdf.GetPage(1).Letters[0].StartBaseLine.Y, pdf.GetPage(1).Letters[4].StartBaseLine.Y, 3);
        Schema(Export(input, DocumentFormat.Docx));
    }

    [Fact]
    public void ParityPagesAndDisplayedNumbersAreSeparate()
    {
        string first = "<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>";
        string last = Page.Replace("<w:sectPr>", "<w:sectPr><w:type w:val='oddPage'/><w:pgNumType w:start='4' w:fmt='lowerRoman'/>");
        byte[] input = Docx(first + "<w:p>" + Field("PAGE", "99") + "</w:p>" + last);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(3, pdf.NumberOfPages); Assert.Equal("First", pdf.GetPage(1).Text); Assert.Empty(pdf.GetPage(2).Text); Assert.Equal("iv", pdf.GetPage(3).Text);
        Schema(Export(input, DocumentFormat.Docx));
    }

    [Fact]
    public void ContinuousSectionDoesNotAddAPhysicalPage()
    {
        string first = "<w:p><w:pPr>" + Page + "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>";
        byte[] input = Docx(first + P("Second") + Page.Replace("<w:sectPr>", "<w:sectPr><w:type w:val='continuous'/>"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf)); Assert.Equal(1, pdf.NumberOfPages); Assert.Equal("FirstSecond", pdf.GetPage(1).Text);
    }

    [Fact]
    public void LegalCompoundLabelsAndNeverRestartingRomanLevelsStayEditable()
    {
        string numbering = "<w:numbering xmlns:w='" + TestDocuments.W + "'><w:abstractNum w:abstractNumId='5'>"
            + "<w:lvl w:ilvl='0'><w:start w:val='2'/><w:numFmt w:val='upperLetter'/><w:lvlText w:val='%1.'/><w:suff w:val='space'/></w:lvl>"
            + "<w:lvl w:ilvl='1'><w:start w:val='4'/><w:numFmt w:val='lowerRoman'/><w:lvlRestart w:val='0'/><w:lvlText w:val='%1.%2)'/><w:suff w:val='space'/></w:lvl>"
            + "</w:abstractNum><w:num w:numId='7'><w:abstractNumId w:val='5'/></w:num></w:numbering>";
        byte[] input = FidelityDocuments.Docx(body: FidelityDocuments.ListParagraph("Parent") + FidelityDocuments.ListParagraph("Child", 1)
            + FidelityDocuments.ListParagraph("Next") + FidelityDocuments.ListParagraph("Continued", 1), lists: true, numbering: numbering);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal("B. ParentB.iv) ChildC. NextC.v) Continued", pdf.GetPage(1).Text);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Contains(Part(word, "word/numbering.xml").Descendants(W + "numFmt"), e => (string?)e.Attribute(W + "val") == "lowerRoman");
        Assert.Contains("C.v)", Encoding.UTF8.GetString(Export(input, DocumentFormat.Html)), StringComparison.Ordinal);
    }

    [Fact]
    public void FootnotesReserveBottomSpaceAndRemainEditableAndLinked()
    {
        byte[] input = Notes(P("Body") + "<w:p><w:r><w:t>Reference</w:t><w:footnoteReference w:id='7'/></w:r></w:p>", P("Independent note"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf, out ConversionResult result));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Notes.PlacedAtEnd");
        var note = pdf.GetPage(1).Letters.First(l => l.Value == "I");
        Assert.True(note.StartBaseLine.Y < 60);
        Assert.All(pdf.GetPage(1).Letters.Take("BodyReference1".Length), l => Assert.True(l.StartBaseLine.Y > 200));
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word); Assert.Contains("Independent note", Part(word, "word/footnotes.xml").ToString(), StringComparison.Ordinal);
        string html = Encoding.UTF8.GetString(Export(input, DocumentFormat.Html)); Assert.Contains("doc-noteref", html, StringComparison.Ordinal); Assert.Contains("href=\"#note-footnotes-7\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LongFootnotesSplitWithoutBodyOverlapOrMissingText()
    {
        string note = string.Concat(Enumerable.Range(0, 35).Select(i => P("Note" + i.ToString("D2", CultureInfo.InvariantCulture))));
        byte[] input = Notes("<w:p><w:r><w:t>Reference</w:t><w:footnoteReference w:id='7'/></w:r></w:p>" + string.Concat(Enumerable.Repeat(P("Body"), 10)), note);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        foreach (int i in Enumerable.Range(0, 35)) Assert.Contains("Note" + i.ToString("D2", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.True(pdf.NumberOfPages >= 4);
        Assert.All(pdf.GetPages(), p => Assert.All(p.Letters, l => Assert.InRange(l.StartBaseLine.Y, 30, 270)));
    }

    [Fact]
    public void TocAndCrossReferencesRefreshFromActualDestinationPages()
    {
        string heading = "<w:p><w:pPr><w:outlineLvl w:val='0'/><w:pageBreakBefore/></w:pPr><w:bookmarkStart w:id='4' w:name='Clause'/><w:r><w:t>Clause title</w:t></w:r><w:bookmarkEnd w:id='4'/></w:p>";
        string body = "<w:p>" + Field("TOC \\o &quot;1-3&quot; \\h", "stale toc") + "</w:p><w:p>" + Field("REF Clause \\h", "stale ref") + "</w:p><w:p>" + Field("PAGEREF Clause \\h", "99") + "</w:p><w:p>"
            + Field("SEQ Figure \\r 4", "99") + Field("SEQ Figure", "99") + "</w:p>" + heading + Page;
        byte[] input = Docx(body);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(2, pdf.NumberOfPages); Assert.Contains("Clause title", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("45", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Equal("Clause title", pdf.GetPage(2).Text); Assert.True(pdf.GetPage(1).GetAnnotations().Count() >= 3);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Equal("99", Part(word, "word/document.xml").Descendants(W + "fldSimple").Single(e => ((string?)e.Attribute(W + "instr"))?.StartsWith("PAGEREF", StringComparison.Ordinal) == true).Value);
    }

    [Theory]
    [InlineData("REF Missing")]
    [InlineData("REF Cycle")]
    [InlineData("REF Cycle \\unsupported")]
    public void MissingCyclicAndUnsupportedReferencesRetainCaches(string instruction)
    {
        byte[] input = Docx("<w:p><w:bookmarkStart w:id='0' w:name='Cycle'/>" + Field(instruction, "cached") + "<w:bookmarkEnd w:id='0'/></w:p>");
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf, out ConversionResult result));
        Assert.Equal("cached", pdf.GetPage(1).Text); Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
    }

    [Theory]
    [InlineData("top", 30 + 11 * 1.069)]
    [InlineData("center", 30 + (80 - 11 * 1.362) / 2 + 11 * 1.069)]
    [InlineData("bottom", 110 - 11 * .293)]
    public void CellAlignmentUsesTheRetainedRowHeight(string alignment, double baseline)
    {
        string table = Table(Cell(P("Aligned"), "<w:vAlign w:val='" + alignment + "'/><w:shd w:fill='EEEEEE'/>"), 4000)
            .Replace("<w:tr>", "<w:tr><w:trPr><w:trHeight w:val='1600' w:hRule='exact'/></w:trPr>");
        byte[] input = Docx(table + Page);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(300 - baseline, pdf.GetPage(1).Letters[0].StartBaseLine.Y, 2); Schema(Export(input, DocumentFormat.Docx));
    }

    [Fact]
    public void ExactRowHeightRejectsOverflowBeforeReplacingOutput()
    {
        string table = Table(Cell(P("Too tall")), 4000).Replace("<w:tr>", "<w:tr><w:trPr><w:trHeight w:val='20' w:hRule='exact'/></w:trPr>");
        Fails<NotSupportedException>(Docx(table + Page), DocumentFormat.Pdf);
    }

    [Fact]
    public void PositionedTextBoxKeepsParagraphsFillBorderAndPosition()
    {
        string box = "<w:p><w:r><w:drawing><wp:anchor xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' behindDoc='0'>"
            + "<wp:positionH relativeFrom='page'><wp:posOffset>2540000</wp:posOffset></wp:positionH><wp:positionV relativeFrom='page'><wp:posOffset>1270000</wp:posOffset></wp:positionV>"
            + "<wp:extent cx='1778000' cy='1016000'/><wp:wrapNone/><wp:docPr id='1' name='Box'/>"
            + "<a:graphic xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:graphicData uri='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'>"
            + "<wps:wsp xmlns:wps='http://schemas.microsoft.com/office/word/2010/wordprocessingShape'><wps:cNvSpPr txBox='1'/><wps:spPr><a:prstGeom prst='rect'><a:avLst/></a:prstGeom>"
            + "<a:solidFill><a:srgbClr val='00FFFF'/></a:solidFill><a:ln w='12700'><a:solidFill><a:srgbClr val='FF0000'/></a:solidFill></a:ln></wps:spPr><wps:txbx><w:txbxContent>"
            + P("Box first") + P("Box second") + "</w:txbxContent></wps:txbx><wps:bodyPr><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";
        byte[] input = Docx(P("Body") + box + Page);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf, out ConversionResult result), SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
        Assert.Equal("BodyBox firstBox second", pdf.GetPage(1).Text); Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Shape.TextBoxFlattened");
        var letters = pdf.GetPage(1).Letters; Assert.Equal(207.2, letters[4].StartBaseLine.X, 2); Assert.Equal(196.4 - 11 * 1.069, letters[4].StartBaseLine.Y, 2);
        using SKBitmap raster = pdf.GetPageAsSKBitmap(1, 1, SKColors.White); Assert.Equal(SKColors.Cyan, raster.GetPixel(330, 170)); SaveRaster(raster, "positioned-text-box");
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word); Assert.Equal(2, Part(word, "word/document.xml").Descendants(W + "txbxContent").Single().Elements(W + "p").Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingImageLayersHaveDeterministicPaintOrder(bool behind)
    {
        string body = AnchorImage("none", behind).Replace("</w:p>", "<w:r><w:rPr><w:sz w:val='80'/></w:rPr><w:t>MMMM</w:t></w:r></w:p>");
        using PdfDocument pdf = PdfDocument.Open(Export(FidelityDocuments.Docx("color-grid.png", body + Page), DocumentFormat.Pdf), SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
        Assert.Equal("MMMM", pdf.GetPage(1).Text); Assert.Single(pdf.GetPage(1).GetImages());
        using SKBitmap raster = pdf.GetPageAsSKBitmap(1, 1, SKColors.White);
        int dark = 0;
        for (int y = 33; y < 65; y++) for (int x = 31; x < 105; x++)
        { SKColor pixel = raster.GetPixel(x, y); if (pixel.Red < 40 && pixel.Green < 40 && pixel.Blue < 40) dark++; }
        Assert.True(behind ? dark > 100 : dark == 0);
    }

    [Theory]
    [InlineData("eachSect")]
    [InlineData("eachPage")]
    public void NoteRestartAndRomanLabelsStayDistinctFromPartIds(string restart)
    {
        string first = "<w:p><w:pPr>" + Page.Replace("</w:sectPr>", "<w:footnotePr><w:numFmt w:val='lowerRoman'/><w:numStart w:val='3'/><w:numRestart w:val='" + restart + "'/></w:footnotePr></w:sectPr>")
            + "</w:pPr><w:r><w:t>First</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        string body = first + "<w:p><w:r><w:t>Second</w:t><w:footnoteReference w:id='9'/></w:r></w:p>";
        string section = Page.Replace("</w:sectPr>", "<w:footnotePr><w:numFmt w:val='lowerRoman'/><w:numStart w:val='3'/><w:numRestart w:val='" + restart + "'/></w:footnotePr></w:sectPr>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body + section), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new KeyValuePair<string, string>("word/footnotes.xml", "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='7'>" + P("One") + "</w:footnote><w:footnote w:id='9'>" + P("Two") + "</w:footnote></w:footnotes>"));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.StartsWith("Firstiii", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.StartsWith("Secondiii", pdf.GetPage(2).Text, StringComparison.Ordinal);
        byte[] word = Export(input, DocumentFormat.Docx); Schema(word);
        Assert.Equal("7,9", string.Join(",", Part(word, "word/document.xml").Descendants(W + "footnoteReference").Select(e => (string)e.Attribute(W + "id")!)));
    }

    [Fact]
    public void TableReferenceAndNoteShareTheSamePageAfterRowSplitting()
    {
        string content = string.Concat(Enumerable.Range(0, 25).Select(i => P("Cell" + i.ToString(CultureInfo.InvariantCulture))))
            + "<w:p><w:r><w:t>Reference</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        using PdfDocument pdf = PdfDocument.Open(Export(Notes(Table(Cell(content), 4000), P("Attached note")), DocumentFormat.Pdf));
        var page = Assert.Single(pdf.GetPages(), p => p.Text.Contains("Reference"));
        Assert.Contains("Attached note", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(pdf.GetPages().Where(p => p.Number != page.Number), p => p.Text.Contains("Attached note"));
    }

    [Fact]
    public void ArbitraryRotationRetainsAnExplicitStrictLoss()
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph().Replace("<a:xfrm>", "<a:xfrm rot='60000'>"));
        Fails<NotSupportedException>(input, DocumentFormat.Pdf);
    }

    [Fact]
    public void NumberingIndentDoesNotEraseInheritedParagraphSpacing()
    {
        string numbering = FidelityDocuments.Numbering.Replace("<w:lvlText w:val='%1.'/>", "<w:lvlText w:val='%1.'/><w:pPr><w:ind w:left='720' w:hanging='360'/></w:pPr>");
        byte[] initial = FidelityDocuments.Docx(body: FidelityDocuments.ListParagraph("Body").Replace("<w:pPr>", "<w:pPr><w:pStyle w:val='Legal'/>") + Page, lists: true, numbering: numbering);
        using var memory = new MemoryStream(); memory.Write(initial, 0, initial.Length);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            FidelityDocuments.Add(zip, "word/styles.xml", Encoding.UTF8.GetBytes("<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='Legal'><w:name w:val='Legal'/><w:pPr><w:spacing w:before='200'/></w:pPr></w:style></w:styles>"));
            XElement relationships = Part(initial, "word/_rels/document.xml.rels").Root!;
            relationships.Add(new XElement(XName.Get("Relationship", TestDocuments.PackageR), new XAttribute("Id", "styles"), new XAttribute("Type", TestDocuments.R + "/styles"), new XAttribute("Target", "styles.xml")));
            zip.GetEntry("word/_rels/document.xml.rels")!.Delete(); FidelityDocuments.Add(zip, "word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(relationships.ToString()));
            XElement types = Part(initial, "[Content_Types].xml").Root!;
            types.Add(new XElement(types.Name.Namespace + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")));
            zip.GetEntry("[Content_Types].xml")!.Delete(); FidelityDocuments.Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(types.ToString()));
        }
        byte[] input = memory.ToArray(); Schema(input);
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.Equal(48, pdf.GetPage(1).Letters[0].StartBaseLine.X, 3); Assert.Equal(300 - 40 - 11 * 1.069, pdf.GetPage(1).Letters[0].StartBaseLine.Y, 3);
        Schema(Export(input, DocumentFormat.Docx));
    }

    private static byte[] AsymmetricImage()
    {
        using var bitmap = new SKBitmap(80, 40);
        for (int y = 0; y < 40; y++) for (int x = 0; x < 80; x++)
            bitmap.SetPixel(x, y, x < 40 ? y < 20 ? SKColors.Red : SKColors.Blue : y < 20 ? SKColors.Lime : SKColors.Yellow);
        for (int y = 1; y < 5; y++) for (int x = 1; x < 8; x++) bitmap.SetPixel(x, y, SKColors.Black);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private static byte[] ReplaceImage(byte[] input, byte[] pixels)
    {
        using var buffer = new MemoryStream(); buffer.Write(input, 0, input.Length);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true))
        { zip.GetEntry("word/media/color-grid.png")!.Delete(); FidelityDocuments.Add(zip, "word/media/color-grid.png", pixels); }
        return buffer.ToArray();
    }
    private static void SaveBytes(byte[] bytes, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("WORDNET_BUSINESS_ARTIFACTS"); if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }
    private static void SaveRaster(SKBitmap raster, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("WORDNET_BUSINESS_ARTIFACTS"); if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory); using SKData data = raster.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(Path.Combine(directory, name + ".png"), data.ToArray());
    }

    private static string P(string text) => TestDocuments.TextParagraph(text);
    private static string Cell(string text, string properties = "") => "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + text + "</w:tc>";
    private static string Table(string cells, params int[] widths) => "<w:tbl><w:tblGrid>" + string.Concat(widths.Select(w => "<w:gridCol w:w='" + w.ToString(CultureInfo.InvariantCulture) + "'/>")) + "</w:tblGrid><w:tr>" + cells + "</w:tr></w:tbl>";
    private static byte[] Docx(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string Field(string instruction, string cached) => "<w:fldSimple w:instr='" + instruction + "'><w:r><w:t>" + cached + "</w:t></w:r></w:fldSimple>";
    private static string AnchorImage(string wrap, bool behind) => FidelityDocuments.ImageParagraph().Replace(" descr='Red and blue grid'", "")
        .Replace("<wp:inline ", "<wp:anchor behindDoc='" + (behind ? "1" : "0") + "' ")
        .Replace("<wp:extent", "<wp:positionH relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionV><wp:wrap" + (wrap == "square" ? "Square wrapText='bothSides'" : "None") + "/><wp:extent")
        .Replace("</wp:inline>", "</wp:anchor>");
    private static byte[] Notes(string body, string note) => TestDocuments.Docx(TestDocuments.Body(body + Page),
        "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
        extra: new KeyValuePair<string, string>("word/footnotes.xml", "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='7'>" + note + "</w:footnote></w:footnotes>"));
    private static byte[] Export(byte[] input, DocumentFormat format) => Export(input, format, out _);
    private static byte[] Export(byte[] input, DocumentFormat format, out ConversionResult result)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        if (format == DocumentFormat.Docx)
        {
            var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken) { Destination = format };
            var model = DocxReader.Read(source, context); FidelityChecks.Destination(model, format, context); DocxWriter.Write(model, output, context);
            result = context.Result(output.Length); return output.ToArray();
        }
        result = new WordConverter().Convert(source, output, DocumentFormat.Docx, format, new ConversionOptions { Clock = () => new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero) }, TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    private static XDocument Part(byte[] bytes, string name)
    { using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
    private static void Schema(byte[] bytes)
    { using WordprocessingDocument word = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken)); }
    private static void Fails<T>(byte[] bytes, DocumentFormat format) where T : Exception
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<T>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, format)); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
