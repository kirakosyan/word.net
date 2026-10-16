using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class PullRequestReviewTests
{
    [Theory]
    [InlineData("TOC", false)]
    [InlineData("IF 1 = 1", true)]
    [InlineData("HYPERLINK &quot;https://example.org/&quot;", true)]
    public void CachedFieldsStayInTheirOriginalParagraphsAndCells(string instruction, bool table)
    {
        string body = "<w:p>" + Begin(instruction) + Text("First") + "</w:p>"
            + (table ? "<w:tbl><w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:p>" + Text("Cell") + "</w:p></w:tc></w:tr></w:tbl>" : "")
            + "<w:p>" + Text("Last") + End + Text("After") + "</w:p>";
        DocumentModel model = Read(TestDocuments.Docx(TestDocuments.Body(body)));
        Assert.Equal("First", ((ParagraphBlock)model.Blocks[0]).Text);
        if (table) Assert.Equal("Cell", ((TableBlock)model.Blocks[1]).Rows[0][0].Text);
        Assert.Equal("LastAfter", ((ParagraphBlock)model.Blocks.Last()).Text);
        if (instruction.StartsWith("HYPERLINK", StringComparison.Ordinal))
            Assert.All(model.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Text != "After"), r => Assert.Equal("https://example.org/", r.Link));
    }

    [Fact]
    public void NestedSupportedFieldDoesNotMoveAnOuterCachedResult()
    {
        string body = "<w:p>" + Begin("TOC") + Text("Entry ")
            + "<w:fldSimple w:instr='PAGE'>" + Text("5") + "</w:fldSimple></w:p><w:p>" + Text("Second entry") + End + "</w:p>";
        DocumentModel model = Read(TestDocuments.Docx(TestDocuments.Body(body)));
        Assert.Collection(model.Paragraphs, p => Assert.Equal("Entry 5", p.Text), p => Assert.Equal("Second entry", p.Text));
        Assert.Equal("PAGE", model.Paragraphs.First().Runs.Last().FieldInstruction);
    }

    [Fact]
    public void BinaryCachedFieldKeepsParagraphBoundaries()
    {
        // Author the field delimiters directly in the binary text stream, independently
        // of the writer's single-run field serializer. Replacements preserve all offsets.
        var model = new DocumentModel();
        var first = Paragraph("@TOC@First"); var last = Paragraph("Last@After");
        model.Blocks.Add(first); model.Blocks.Add(last);
        using var binary = new MemoryStream(); DocFormat.Write(model, binary, Context());
        using (RootStorage root = RootStorage.Open(binary, StorageModeFlags.LeaveOpen))
        {
            using CfbStream stream = root.OpenStream("WordDocument"); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            byte[] data = buffer.ToArray(), text = Encoding.Unicode.GetBytes("@TOC@First\rLast@After");
            int start = Enumerable.Range(0, data.Length - text.Length + 1).Single(i => data.Skip(i).Take(text.Length).SequenceEqual(text));
            data[start] = 0x13; data[start + 8] = 0x14; data[start + 30] = 0x15;
            stream.Position = 0; stream.Write(data, 0, data.Length); root.Flush();
        }
        binary.Position = 0;
        DocumentModel read = DocFormat.Read(binary, Context());
        Assert.Collection(read.Paragraphs, p => Assert.Equal("First", p.Text), p => Assert.Equal("LastAfter", p.Text));
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void NumberedHeadingRegistersItsLabelFaceBeforeLayout(DocumentFormat format)
    {
        var model = new DocumentModel(); var heading = Paragraph("Numbered heading");
        heading.HeadingLevel = 1; heading.List = new ListItem { Id = "heading", Ordered = true };
        heading.Runs.Clear(); heading.Runs.Add(new TextRun("Numbered heading", appearance: new RunAppearance { FontFamily = "Arial" }));
        model.Blocks.Add(heading);
        var context = Context(); context.Destination = format;
        var page = Assert.Single(FixedLayout.Create(model, context));
        Assert.Equal("1. Numbered heading", string.Concat(page.Text.Select(t => t.Text)));
        Assert.All(page.Text, t => Assert.InRange(t.Font, 0, context.FontData.Length - 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HtmlPageFieldsNeverInvokeFixedPageLayout(bool includeStories)
    {
        var model = new DocumentModel(); var section = new PageSettings(); model.FinalSection = section;
        var header = Paragraph(""); header.Runs.Add(new TextRun("27", fieldInstruction: "NUMPAGES"));
        section.Stories.Add("header-default", new System.Collections.Generic.List<DocumentBlock> { header });
        var body = Paragraph("Body"); body.Section = section;
        body.Runs.Add(new TextRun("4", fieldInstruction: "PAGE"));
        body.Runs.Add(new TextRun("", image: DocumentImage.Placeholder(1000, 50, "Logo", Context())));
        body.Runs.Add(new TextRun("Brand", appearance: new RunAppearance { FontFamily = "Uninstalled Brand" }));
        model.Blocks.Add(body);
        var options = new ConversionOptions(); options.Html.IncludeHeadersAndFooters = includeStories;
        var context = new ConversionContext(options, TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); HtmlFormat.Write(model, output, context);
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output.ToArray()));
        Assert.Contains("Body4Brand", html.Body!.TextContent, StringComparison.Ordinal);
        if (includeStories) Assert.Contains("27", html.Body.TextContent, StringComparison.Ordinal);
        else Assert.DoesNotContain("27", html.Body.TextContent, StringComparison.Ordinal);
        Assert.Single(html.QuerySelectorAll("img"));
        ConversionResult result = context.Result(output.Length);
        Assert.Null(result.Metrics.PageCount); Assert.Empty(result.Metrics.FontsUsed);
        Assert.Contains(result.Diagnostics, d => d.Code == "Html.PageFieldsUsedCachedResult");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Layout.", StringComparison.Ordinal) || d.Code == "Font.Substituted");
    }

    [Theory]
    [InlineData("ins")]
    [InlineData("del")]
    [InlineData("moveTo")]
    [InlineData("moveFrom")]
    public void StrictRejectsTrackedChangesBeforeReplacingOutput(string revision)
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:" + revision + ">" + Text("Revision") + "</w:" + revision + "></w:p>"));
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Text,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        source.Position = 0; using var accepted = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, accepted, DocumentFormat.Docx, DocumentFormat.Text,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Revisions.Accepted" && d.Severity == DiagnosticSeverity.Warning);
        if (revision is "ins" or "moveTo") Assert.Contains("Revision", Encoding.UTF8.GetString(accepted.ToArray()), StringComparison.Ordinal);
        else Assert.DoesNotContain("Revision", Encoding.UTF8.GetString(accepted.ToArray()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteConvenienceOverloadsExposeDiagnosticsAndMetrics(bool html)
    {
        byte[] input = Fixture("contract-notes"); var converter = new WordConverter();
        ConversionResult result;
        byte[] output = html ? converter.ConvertToHtml(input, out result, new ConversionOptions(FidelityMode.Lenient))
            : converter.ConvertToPdf(input, out result, new ConversionOptions(FidelityMode.Lenient));
        Assert.Contains(result.Diagnostics, d => d.Code == "Notes.PlacedAtEnd" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Equal(output.LongLength, result.BytesWritten); Assert.Equal(output.LongLength, result.Metrics.BytesWritten);
        if (html) Assert.Null(result.Metrics.PageCount); else Assert.True(result.Metrics.PageCount > 0);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\0\0\0")]
    [InlineData("appended motion video")]
    public void JpegTrailingDataIsAccepted(string suffix)
    {
        byte[] jpeg = ImageBytes("logo-jpg", "word/media/logo.jpg");
        byte[] bytes = jpeg.Concat(Encoding.ASCII.GetBytes(suffix)).ToArray();
        DocumentImage image = DocumentImage.Read(bytes, null, null, "Logo", Context());
        Assert.Equal("jpg", image.Extension); Assert.True(image.PixelsWide > 0);
    }

    [Fact]
    public void JpegMetadataCannotHideAMissingEndMarker()
    {
        byte[] jpeg = ImageBytes("logo-jpg", "word/media/logo.jpg");
        byte[] metadata = { 255, 225, 0, 4, 255, 217 };
        byte[] truncated = jpeg.Take(2).Concat(metadata).Concat(jpeg.Skip(2).Take(jpeg.Length - 4)).ToArray();
        Assert.Throws<InvalidDataException>(() => DocumentImage.Read(truncated, null, null, "Logo", Context()));
    }

    [Theory]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void UnsupportedImageUsesAnExplicitPlaceholder(FidelityMode mode)
    {
        byte[] input = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph());
        // A genuine TIFF signature in a generically named image part must reach the fallback.
        using var rewritten = new MemoryStream(); rewritten.Write(input, 0, input.Length);
        using (var zip = new ZipArchive(rewritten, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
            string name = entry.FullName; entry.Delete();
            using Stream stream = zip.CreateEntry(name).Open(); byte[] tiff = { 73, 73, 42, 0, 8, 0, 0, 0 };
            stream.Write(tiff, 0, tiff.Length);
        }
        byte[] htmlBytes = new WordConverter().ConvertToHtml(rewritten.ToArray(), out ConversionResult result, new ConversionOptions(mode));
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(htmlBytes));
        Assert.Single(html.QuerySelectorAll("img"));
        Assert.Contains(result.Diagnostics, d => d.Code == "Image.UnsupportedFormat" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(500)]
    [InlineData(900)]
    public void FontCatalogPrefersRegularWeightOverEarlierVariants(int variantWeight)
    {
        byte[] regular = FixedLayout.FontBytes(0);
        byte[] variant = (byte[])regular.Clone();
        int os2 = FontTable(variant, "OS/2"); variant[os2 + 4] = (byte)(variantWeight >> 8); variant[os2 + 5] = (byte)variantWeight;
        using var a = new MemoryStream(variant); using var b = new MemoryStream(regular);
        FontSource alternate = Assert.Single(FontSource.FromStream(a)), normal = Assert.Single(FontSource.FromStream(b));
        var options = new ConversionOptions(); options.Fonts.Sources.Add(alternate); options.Fonts.Sources.Add(normal);
        var model = new DocumentModel(); model.Blocks.Add(Paragraph("Regular text"));
        var resolver = new FontResolver(model, new ConversionContext(options, TestContext.Current.CancellationToken));
        byte[] selected = resolver.Data[resolver.Resolve(new TextRun("Regular text"), false)];
        int table = FontTable(selected, "OS/2"); Assert.Equal(400, (selected[table + 4] << 8) | selected[table + 5]);
    }

    [Theory]
    [InlineData("w:hint='eastAsia'")]
    [InlineData("w:cs='Arial'")]
    [InlineData("w:eastAsia='MS Mincho'")]
    public void NonLatinFontHintsDoNotEraseInheritedLatinFamily(string attributes)
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii='Arial'/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:rFonts " + attributes + "/></w:rPr><w:t>Inherited</w:t></w:r></w:p>"), styles: styles);
        Assert.Equal("Arial", Assert.Single(Read(input).Paragraphs.First().Runs).Appearance.FontFamily);
    }

    [Fact]
    public void NumberedParagraphWithTabsProducesSchemaValidDocx()
    {
        var model = new DocumentModel(); var paragraph = Paragraph("List\ttext");
        paragraph.List = new ListItem { Id = "list", Ordered = true }; paragraph.Layout.Tabs.Add(new TabStop { Position = 72 });
        model.Blocks.Add(paragraph);
        using var output = new MemoryStream(); DocxWriter.Write(model, output, Context()); output.Position = 0;
        using WordprocessingDocument word = WordprocessingDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CellTabsUseTheCellsTextOrigin(bool header, bool custom)
    {
        var model = new DocumentModel(); var section = new PageSettings(); model.FinalSection = section;
        var table = new TableBlock { Section = section }; table.ColumnWidths.Add(200); table.ColumnWidths.Add(200);
        var p = Paragraph("\tZ"); if (custom) p.Layout.Tabs.Add(new TabStop { Position = 72, Alignment = "right" });
        var first = new TableCell(Paragraph("Left")); var second = new TableCell(p) { Padding = new CellPadding { Left = 10 } };
        table.Rows.Add(new System.Collections.Generic.List<TableCell> { first, second });
        if (header) section.Stories.Add("header-default", new System.Collections.Generic.List<DocumentBlock> { table });
        else model.Blocks.Add(table);
        PositionedText letter = Assert.Single(FixedLayout.Create(model, Context()).SelectMany(page => page.Text), t => t.Text == "Z");
        // Cell two starts at 248pt, its text at 258pt. A default tab advances 36pt;
        // the explicit right stop ends the glyph at 330pt (the glyph is about 7pt).
        Assert.InRange(letter.X, custom ? 321 : 293.99, custom ? 325 : 294.01);
    }

    [Fact]
    public void TabsAndBreaksRetainRunDecorationInDocxAndHtml()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:u/><w:color w:val='336699'/></w:rPr><w:tab/><w:br/></w:r></w:p>"));
        DocumentModel model = Read(input);
        Assert.All(model.Paragraphs.First().Runs, r => { Assert.True(r.Appearance.Underline); Assert.Equal("336699", r.Color); });
        using var output = new MemoryStream(); DocxWriter.Write(model, output, Context());
        Assert.All(Read(output.ToArray()).Paragraphs.First().Runs, r => Assert.True(r.Appearance.Underline));
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(new WordConverter().ConvertToHtml(input)));
        Assert.All(html.QuerySelectorAll("span"), s => Assert.Contains("text-decoration:underline", s.GetAttribute("style"), StringComparison.Ordinal));
    }

    [Fact]
    public void StrictXpsRetainsStoriesFieldsAndRequestedFontsWithoutFalseLosses()
    {
        var model = new DocumentModel(); var section = new PageSettings(); model.FinalSection = section;
        section.Stories.Add("header-default", new System.Collections.Generic.List<DocumentBlock> { Paragraph("Header") });
        var paragraph = Paragraph(""); paragraph.Section = section;
        paragraph.Runs.Add(new TextRun("7", appearance: new RunAppearance { FontFamily = "Noto Sans" }, fieldInstruction: "PAGE")); model.Blocks.Add(paragraph);
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken); context.Destination = DocumentFormat.Xps;
        FidelityChecks.Destination(model, DocumentFormat.Xps, context);
        using var output = new MemoryStream(); XpsFormat.Write(model, output, context);
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()));
        string markup = string.Join("", zip.Entries.Where(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
        Assert.Contains("Header", markup, StringComparison.Ordinal); Assert.Contains("UnicodeString=\"1\"", markup, StringComparison.Ordinal);
        Assert.Empty(context.Result(output.Length).Diagnostics);
    }

    [Fact]
    public void HtmlIncludesOnlyTheStoryVariantsForTheFirstPage()
    {
        var options = new ConversionOptions(FidelityMode.Lenient); options.Html.IncludeHeadersAndFooters = true;
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(new WordConverter().ConvertToHtml(Fixture("first-even-stories"), options)));
        Assert.Contains("FIRST HEADER", html.Body!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("EVEN HEADER", html.Body.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaledCellLineStaysInItsOwnColumn()
    {
        var model = new DocumentModel(); var section = new PageSettings { Width = 500, Height = 200, Left = 30, Right = 30, Top = 20, Bottom = 20 }; model.FinalSection = section;
        var table = new TableBlock { Section = section }; table.ColumnWidths.Add(220); table.ColumnWidths.Add(220);
        var right = Paragraph("First\nRight"); right.Layout.LineSpacing = 600; right.Layout.MultipleLineSpacing = false; right.Layout.FirstLine = 30;
        table.Rows.Add(new System.Collections.Generic.List<TableCell> { new TableCell(Paragraph("Left")), new TableCell(right) }); model.Blocks.Add(table);
        var context = Context(); context.Destination = DocumentFormat.Pdf;
        PositionedText[] items = FixedLayout.Create(model, context).SelectMany(p => p.Text).ToArray();
        PositionedText item = Assert.Single(items, t => t.Text == "Right");
        Assert.InRange(item.X, 249.99, 250.01);
        Assert.InRange(items.First(t => t.Text.Length > 0 && t.Text[0] == 'F').X, 257.99, 258.01);
        Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Table.LineScaledToFit");

        model.Blocks.Clear();
        section.Stories.Add("header-default", new System.Collections.Generic.List<DocumentBlock> { table });
        PositionedText left = FixedLayout.Create(model, Context()).SelectMany(p => p.Text).First(t => t.Text.Length > 0 && t.Text[0] == 'L');
        Assert.InRange(left.X, 29.99, 30.01);
    }

    private static int FontTable(byte[] bytes, string tag)
    {
        int count = (bytes[4] << 8) | bytes[5];
        int record = Enumerable.Range(0, count).Select(i => 12 + i * 16).Single(p => Encoding.ASCII.GetString(bytes, p, 4) == tag);
        return (bytes[record + 8] << 24) | (bytes[record + 9] << 16) | (bytes[record + 10] << 8) | bytes[record + 11];
    }
    private static byte[] ImageBytes(string fixture, string entry)
    {
        using var archive = new ZipArchive(new MemoryStream(Fixture(fixture)));
        using Stream input = archive.GetEntry(entry)!.Open(); using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", name + ".docx"));
    private static ConversionContext Context() => new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
    private static DocumentModel Read(byte[] bytes) { using var input = new MemoryStream(bytes); return DocxReader.Read(input, Context()); }
    private static ParagraphBlock Paragraph(string text) { var p = new ParagraphBlock(); p.Runs.Add(new TextRun(text)); return p; }
    private static string Text(string text) => "<w:r><w:t xml:space='preserve'>" + text + "</w:t></w:r>";
    private static string Begin(string instruction) => "<w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>" + instruction + "</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r>";
    private const string End = "<w:r><w:fldChar w:fldCharType='end'/></w:r>";
}
