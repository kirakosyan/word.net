using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;
using Xunit;

namespace WordNet.Tests;

public sealed class InteroperabilityReviewTests
{
    private static readonly string[] ListLabelRoles = { "/S /Lbl " };
    private static readonly string[] ParagraphTerminators = { @"\par " };
    private static readonly XNamespace W = TestDocuments.W;
    private const string Target = "<w:p><w:pPr><w:outlineLvl w:val='0'/><w:pageBreakBefore/></w:pPr><w:bookmarkStart w:id='1' w:name='Target'/><w:r><w:t>Target title</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
    private const string SmallPage = "<w:sectPr><w:pgSz w:w='400' w:h='400'/><w:pgMar w:top='20' w:bottom='20' w:left='20' w:right='20' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
    private static string Field(string instruction) => "<w:fldSimple w:instr='" + instruction + "'><w:r><w:t>cached</w:t></w:r></w:fldSimple>";

    [Theory]
    [InlineData("REF Target \\h")]
    [InlineData("PAGEREF Target \\h")]
    [InlineData("TOC \\o &quot;1-3&quot; \\h")]
    public void TaggedGeneratedLinksDegradeWithoutLeavingUnmappedAnnotations(string instruction)
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body("<w:p>" + Field(instruction) + "</w:p>" + Target));
        using var input = new MemoryStream(source); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "Generated links";
        var options = Tags(PdfAccessibility.Tagged, FidelityMode.BestEffort);
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(2, pdf.NumberOfPages); Assert.NotEmpty(pdf.GetPage(1).Text);
        Assert.Empty(pdf.GetPage(1).GetAnnotations()); Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.LinkSemantics");
        output.SetLength(0); output.WriteByte(42); options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        Assert.StartsWith("Pdf.LinkSemantics", Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void RepeatedHeaderLinksAndDecorativeImageLinksDegradeWithoutTagMappingFailure()
    {
        string header = "<w:p><w:hyperlink r:id='link'><w:r><w:t>Header link</w:t></w:r></w:hyperlink></w:p>";
        string rows = string.Concat(Enumerable.Range(0, 60).Select(i => "<w:tr><w:tc>" + TestDocuments.TextParagraph("Record " + i) + "</w:tc></w:tr>"));
        string body = "<w:tbl><w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc>" + header + "</w:tc></w:tr>" + rows + "</w:tbl>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body), TestDocuments.LinkRelationship("https://example.test/header")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "Repeated links";
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, Tags(PdfAccessibility.Tagged, FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.True(pdf.NumberOfPages > 1); Assert.NotEmpty(pdf.GetPage(1).GetAnnotations()); Assert.Empty(pdf.GetPage(2).GetAnnotations());
        Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.LinkSemantics");
        // A caller-authored decorative image is independently serialized to DOCX,
        // then wrapped in a hyperlink using the published OOXML relationship form.
        WordDocument decorative = WordDocument.Create(); decorative.AddImage(new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 60, 30, "", true), TestContext.Current.CancellationToken);
        using var word = new MemoryStream(); decorative.Save(word, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] linked = LinkPicture(word.ToArray()); using var linkedInput = new MemoryStream(linked); decorative = WordDocument.Load(linkedInput, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); decorative.Title = "Decorative link";
        using var linkedPdf = new MemoryStream(); result = decorative.Save(linkedPdf, DocumentFormat.Pdf, Tags(PdfAccessibility.Tagged, FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using PdfDocument parsed = PdfDocument.Open(linkedPdf.ToArray()); Assert.Single(parsed.GetPage(1).GetImages()); Assert.Empty(parsed.GetPage(1).GetAnnotations()); Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.LinkSemantics");
    }

    [Fact]
    public void ParentTreeKeysAreSortedAndResolveBothPageMcidsAndEarlyPageObjr()
    {
        string link = "<w:p><w:hyperlink r:id='link'><w:r><w:t>Early link</w:t></w:r></w:hyperlink></w:p>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(link + Target), TestDocuments.LinkRelationship("https://example.test/early")));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "Early annotation";
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, Tags(PdfAccessibility.PdfUa1), TestContext.Current.CancellationToken);
        byte[] bytes = output.ToArray(); TypographyTests.Save("accessible-early-link.pdf", bytes); using PdfDocument pdf = PdfDocument.Open(bytes);
        DictionaryToken catalog = pdf.Structure.Catalog.CatalogDictionary;
        DictionaryToken structure = Dictionary(pdf, catalog.Data["StructTreeRoot"]), parents = Dictionary(pdf, structure.Data["ParentTree"]);
        var nums = Assert.IsType<ArrayToken>(parents.Data["Nums"]).Data;
        int[] keys = Enumerable.Range(0, nums.Count / 2).Select(i => Assert.IsType<NumericToken>(nums[i * 2]).Int).ToArray(); Assert.Equal(keys.OrderBy(k => k), keys); Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(2, pdf.NumberOfPages);
        for (int page = 1; page <= 2; page++)
        {
            var marks = Assert.IsType<ArrayToken>(nums[(page - 1) * 2 + 1]); Assert.NotEmpty(marks.Data);
            foreach (IToken mark in marks.Data) Assert.Equal("StructElem", Assert.IsType<NameToken>(Dictionary(pdf, mark).Data["Type"]).Data);
        }
        var annotation = pdf.GetPage(1).GetAnnotations().First().AnnotationDictionary; int key = Assert.IsType<NumericToken>(annotation.Data["StructParent"]).Int;
        DictionaryToken owner = Dictionary(pdf, nums[Array.IndexOf(keys, key) * 2 + 1]); Assert.Equal("Link", Assert.IsType<NameToken>(owner.Data["S"]).Data);
        Assert.Contains(Assert.IsType<ArrayToken>(owner.Data["K"]).Data, k => Dictionary(pdf, k).Data.TryGetValue("Type", out IToken? type) && type is NameToken n && n.Data == "OBJR");
    }

    [Theory]
    [InlineData("REF Target \\h")]
    [InlineData("PAGEREF Target \\h")]
    [InlineData("TOC \\o &quot;1-3&quot; \\h")]
    public void StrictPngPreviewsIgnoreDestinationsOnOtherPhysicalPages(string instruction)
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body("<w:p>" + Field(instruction) + "</w:p>" + Target));
        using var input = new MemoryStream(source); DocumentPreview preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Png, Dpi = 72 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, preview.Pages.Count); Assert.DoesNotContain(preview.Result.Diagnostics, d => d.Code == "Pdf.OmittedDestination");
        using SKBitmap pixels = SKBitmap.Decode(preview.Pages[0].GetBytes()); Assert.True(pixels.Pixels.Count(p => p.Red < 128) > 10);
        var options = new ConversionOptions(); options.Pdf.Pages.Add(new PageRange(1, 1));
        Assert.StartsWith("Pdf.OmittedDestination", Assert.Throws<NotSupportedException>(() => new WordConverter().ConvertToPdf(source, options)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WordStyleRepeatedRtfRowDefinitionAcceptsMatchingCompleteGrid()
    {
        const string source = @"{\rtf1\ansi\trowd\cellx2000\cellx4000\pard\intbl{A\cell B\cell}\pard\intbl{\trowd\irow0\lastrow\cellx2000\cellx4000\row}}";
        byte[] word = Convert(Encoding.ASCII.GetBytes(source), DocumentFormat.Rtf); XElement root = Part(word); Schema(word);
        Assert.Equal(2, root.Descendants(W + "tc").Count()); Assert.Equal("AB", root.Value); Assert.All(root.Descendants(W + "gridCol"), c => Assert.Equal("2000", (string?)c.Attribute(W + "w")));
    }

    [Theory]
    [InlineData(@"\cellx2000\cellx3900")]
    [InlineData(@"\cellx2000")]
    [InlineData(@"\cellx2000\cellx4000\cellx5000")]
    [InlineData(@"\cellx2000\cellx4000\cell")]
    public void RepeatedRtfGridCannotChangeEdgesOrAddContent(string repeated)
    {
        string source = @"{\rtf1\trowd\cellx2000\cellx4000\intbl A\cell B\cell{\trowd" + repeated + @"\row}}";
        Assert.Throws<InvalidDataException>(() => Convert(Encoding.ASCII.GetBytes(source), DocumentFormat.Rtf));
        Assert.Throws<InvalidDataException>(() => Convert(Encoding.ASCII.GetBytes(@"{\rtf1\trowd\cellx2000\intbl A{\trowd\cellx2000 B\cell\row}\cell\row}"), DocumentFormat.Rtf));
    }

    [Theory]
    [InlineData("pngblip", "color-grid.png")]
    [InlineData("jpegblip", "color-grid.jpg")]
    public void WordShppictRetainsPrimaryRasterAndSkipsRedundantMetafile(string kind, string filename)
    {
        byte[] image = FidelityDocuments.Fixture(filename);
        string source = @"{\rtf1\ansi Before{\*\shppict{\pict\" + kind + @"\picwgoal1200\pichgoal600 " + string.Concat(image.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))) + @"}}{\nonshppict{\pict\wmetafile8 0000}}After}";
        byte[] word = Convert(Encoding.ASCII.GetBytes(source), DocumentFormat.Rtf); Schema(word); XElement root = Part(word); Assert.Equal("BeforeAfter", root.Value); Assert.Single(root.Descendants(W + "drawing"));
        using var zip = new ZipArchive(new MemoryStream(word)); using Stream stream = zip.Entries.Single(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)).Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); Assert.Equal(image, bytes.ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(77)]
    [InlineData(255)]
    public void SpecialRtfCharsetsRetainLiteralBytesWithAnExplicitLoss(int charset)
    {
        string source = @"{\rtf1\ansi{\fonttbl{\f0\fcharset" + charset + @" Symbol;}}\f0 \'b7\u937?}";
        byte[] bytes = Encoding.ASCII.GetBytes(source);
        foreach (FidelityMode mode in new[] { FidelityMode.BestEffort, FidelityMode.Lenient })
        {
            using var input = new MemoryStream(bytes); WordDocument document = WordDocument.Load(input, DocumentFormat.Rtf, out ConversionResult result, new ConversionOptions(mode), TestContext.Current.CancellationToken);
            Assert.Equal("·Ω", Assert.Single(document.Paragraphs).Text); Assert.Contains(result.Diagnostics, d => d.Code == "Rtf.FontCharset");
        }
        using var strict = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.StartsWith("Rtf.FontCharset", Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strict, output, DocumentFormat.Rtf, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void RtfCellTerminatorEndsTheLastParagraphAndRetainsAnExplicitEmptyLastParagraph()
    {
        const string source = @"{\rtf1\ansi\trowd\cellx2000\cellx4000\intbl One\par Two\cell Last\par\cell\row}";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(source)); WordDocument document = WordDocument.Load(input, DocumentFormat.Rtf, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Rtf, cancellationToken: TestContext.Current.CancellationToken);
        string text = Encoding.ASCII.GetString(output.ToArray()); Assert.Equal(2, text.Split(ParagraphTerminators, StringSplitOptions.None).Length - 1); Assert.DoesNotContain(@"\par \cell", text, StringComparison.Ordinal);
        TypographyTests.Save("writer-multiparagraph.rtf", output.ToArray());
    }

    [Theory]
    [InlineData("alt='Company logo'", false)]
    [InlineData("alt=''", true)]
    [InlineData("", true)]
    public void ElementAltOverridesDecorativeCallerResource(string attribute, bool decorative)
    {
        var options = new ConversionOptions(); options.HtmlInput.Images.Add("logo", new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 60, 30, "", true));
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("<title>Image semantics</title><p><img src='logo' " + attribute + "></p>"));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Html, out _, options, TestContext.Current.CancellationToken);
        Assert.Equal(decorative, document.Paragraphs.SelectMany(p => p.Runs).Single(r => r.Image != null).Image!.Decorative);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(output.ToArray());
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"; XElement root = Part(output.ToArray());
        Assert.Equal(decorative ? "" : "Company logo", (string?)root.Descendants(wp + "docPr").Single().Attribute("descr") ?? "");
        using var pdf = new MemoryStream(); document.Save(pdf, DocumentFormat.Pdf, Tags(PdfAccessibility.PdfUa1), TestContext.Current.CancellationToken);
        Assert.Equal(!decorative, InteropText.Contains(Encoding.ASCII.GetString(pdf.ToArray()), "/S /Figure "));
    }

    [Fact]
    public void InlineHighlightPaintsNestedTextAndTransparentChildrenWithoutBleedingIntoNextBlock()
    {
        const string html = "<p><span style='background-color:yellow'>a <b>bold</b><i style='background-color:blue'>blue</i><em style='background-color:transparent'>clear</em> c</span></p><p>plain</p>";
        XElement root = Part(Convert(Encoding.UTF8.GetBytes(html), DocumentFormat.Html));
        foreach (string value in new[] { "bold", "clear" }) Assert.Equal("FFFF00", (string?)root.Descendants(W + "r").Single(r => r.Value == value).Element(W + "rPr")?.Element(W + "shd")?.Attribute(W + "fill"));
        Assert.Equal("0000FF", (string?)root.Descendants(W + "r").Single(r => r.Value == "blue").Element(W + "rPr")?.Element(W + "shd")?.Attribute(W + "fill"));
        Assert.Empty(root.Descendants(W + "p").Last().Descendants(W + "shd"));
    }

    [Theory]
    [InlineData(PreviewFormat.Svg, false)]
    [InlineData(PreviewFormat.Svg, true)]
    [InlineData(PreviewFormat.Png, true)]
    public void RepeatedGlyphsShareTheOutlineBudgetAcrossPreviewPages(PreviewFormat format, bool native)
    {
        string body = string.Concat(Enumerable.Range(0, 80).Select(i => "<w:p>" + (i > 0 ? "<w:pPr><w:pageBreakBefore/></w:pPr>" : "") + "<w:r><w:rPr><w:sz w:val='10'/></w:rPr><w:t>A</w:t></w:r></w:p>"));
        byte[] source = TestDocuments.Docx(TestDocuments.Body(body + SmallPage));
        var options = new ConversionOptions(maximumExpandedBytes: 65536); options.Typography.Enabled = native;
        using var input = new MemoryStream(source); DocumentPreview result = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Format = format, Dpi = 36 }, options, TestContext.Current.CancellationToken);
        Assert.Equal(80, result.Pages.Count); Assert.All(result.Pages, p => Assert.Equal(result.Pages[0].GetBytes(), p.GetBytes()));
        input.Position = 0;
        DocumentPreview second = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Format = format, Dpi = 36 }, options, TestContext.Current.CancellationToken); Assert.Equal(result.Pages[0].GetBytes(), second.Pages[0].GetBytes());
    }

    [Fact]
    public void NewDistinctGlyphsStillHaveAnAggregateOutlineAdmissionLimit()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")));
        using var input = new MemoryStream(source);
        Assert.StartsWith("Limit.FontOutlines", Assert.Throws<InvalidDataException>(() => new WordConverter().RenderPages(input, DocumentFormat.Docx, options: new ConversionOptions(maximumExpandedBytes: 8192), cancellationToken: TestContext.Current.CancellationToken)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentedListParagraphsPreserveOnlyTheSynthesizedLabelRole()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("1. First item\n2. Second item")); WordDocument document = WordDocument.Load(input, DocumentFormat.Markdown, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        byte[] source = output.ToArray();
        using var memory = new MemoryStream(); memory.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            XElement root = Part(source);
            foreach (XElement paragraph in root.Descendants(W + "p"))
            {
                paragraph.Elements(W + "r").Remove();
                for (int i = 0; i < 1000; i++) paragraph.Add(new XElement(W + "r", new XElement(W + "t", "a")));
            }
            zip.GetEntry("word/document.xml")!.Delete(); using Stream stream = zip.CreateEntry("word/document.xml").Open(); root.Save(stream);
        }
        memory.Position = 0; document = WordDocument.Load(memory, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "Fragmented list";
        using var pdfOutput = new MemoryStream(); document.Save(pdfOutput, DocumentFormat.Pdf, Tags(PdfAccessibility.PdfUa1), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(pdfOutput.ToArray()); string text = string.Concat(pdf.GetPages().Select(p => p.Text)); Assert.Equal(2000, text.Count(c => c == 'a')); Assert.Contains("1.", text, StringComparison.Ordinal); Assert.Contains("2.", text, StringComparison.Ordinal);
        string raw = Encoding.ASCII.GetString(pdfOutput.ToArray()); Assert.Equal(2, raw.Split(ListLabelRoles, StringSplitOptions.None).Length - 1);
    }

    private static ConversionOptions Tags(PdfAccessibility mode, FidelityMode fidelity = FidelityMode.Strict)
    { var options = new ConversionOptions(fidelity); options.Pdf.Accessibility = mode; options.Pdf.Language = "en"; return options; }
    private static byte[] Convert(byte[] bytes, DocumentFormat format)
    { using var input = new MemoryStream(bytes); using var output = new MemoryStream(); new WordConverter().Convert(input, output, format, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static XElement Part(byte[] bytes) { using var zip = new ZipArchive(new MemoryStream(bytes)); using var stream = zip.GetEntry("word/document.xml")!.Open(); return XElement.Load(stream); }
    private static void Schema(byte[] bytes) { using var word = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(word)); }
    private static DictionaryToken Dictionary(PdfDocument pdf, IToken token) => Assert.IsType<DictionaryToken>(token is IndirectReferenceToken r ? pdf.Structure.TokenScanner.Get(r.Data)!.Data : token);
    private static byte[] LinkPicture(byte[] bytes)
    {
        using var memory = new MemoryStream(); memory.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            XElement root = Part(bytes); XElement run = root.Descendants(W + "r").Single(); run.ReplaceWith(new XElement(W + "hyperlink", new XAttribute(W + "anchor", "Target"), new XElement(run)));
            root.Element(W + "body")!.AddFirst(XElement.Parse("<w:p xmlns:w='" + W + "'><w:bookmarkStart w:id='1' w:name='Target'/><w:r><w:t>Target</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>"));
            zip.GetEntry("word/document.xml")!.Delete(); using Stream output = zip.CreateEntry("word/document.xml").Open(); root.Save(output);
        }
        return memory.ToArray();
    }
}
