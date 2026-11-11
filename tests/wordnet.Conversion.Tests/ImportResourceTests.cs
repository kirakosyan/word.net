using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ImportResourceTests
{
    private static readonly string[] AlternatingText = { "a", "b", "c" };

    [Theory]
    [InlineData(0x6646)]
    [InlineData(0x6645)]
    [InlineData(0x646b)]
    public void DocRejectsNestedIndirectPropertyFanout(int code)
    {
        byte[] data = new byte[14 * 63 + 2];
        for (int node = 0; node < 63; node++)
        {
            Le16(data, node * 14, 12);
            Pointer(code, (node + 1) * 14).CopyTo(data, node * 14 + 2);
            Pointer(code, (node + 1) * 14).CopyTo(data, node * 14 + 8);
        }
        Reject(Doc("\r", Pointer(code, 0), data), DocumentFormat.Doc, "Doc.Properties", BestEffort());
    }

    [Fact]
    public void DocBudgetsRepeatedIndirectApplicationsWithoutRetainingEachExpansion()
    {
        byte[] data = new byte[65537]; Le16(data, 0, 65535);
        for (int i = 2; i < data.Length; i += 3) { Le16(data, i, 0x2403); data[i + 2] = 1; }
        Reject(Doc(new string('\r', 1100), Pointer(0x6646, 0), data), DocumentFormat.Doc, "Limit.DocProperties", BestEffort());
        using var input = new MemoryStream(Doc("kept\r", Pointer(0x6646, 0), data));
        DocumentModel model = DocFormat.Read(input, Context(BestEffort()));
        ParagraphBlock paragraph = Assert.Single(model.Paragraphs);
        Assert.Equal("kept", paragraph.Text);
        Assert.Equal(ParagraphAlignment.Center, paragraph.Alignment);
    }

    [Fact]
    public void DocxSharesLargeImageResourceAcrossDistinctPlacements()
    {
        byte[] png = PaddedPng(16 * 1024 * 1024);
        string body = string.Concat(Enumerable.Range(0, 40).Select(i => FidelityDocuments.ImageParagraph(12700 * (i + 1), 12700)));
        byte[] docx = WithParts(TestDocuments.Docx(TestDocuments.Body(body),
            "<Relationship Id='image' Type='" + TestDocuments.R + "/image' Target='media/image.png'/>"),
            ("word/media/image.png", png));
        using var input = new MemoryStream(docx);
        DocumentImage[] images = DocxReader.Read(input, Context()).Paragraphs.SelectMany(p => p.Runs).Select(r => r.Image!).ToArray();
        Assert.Equal(40, images.Length);
        Assert.All(images, image => Assert.Same(images[0].Bytes, image.Bytes));
        Assert.Equal(1, images[0].FrameWidth); Assert.Equal(40, images[39].FrameWidth);
        Assert.NotSame(images[0], images[39]);
        images[0].Rotation = 90; Assert.Equal(0, images[39].Rotation);
        Reject(docx, DocumentFormat.Docx, "Limit.Bytes");
        Reject(docx, DocumentFormat.Docx, "Limit.ImagePlacements", new ConversionOptions { MaximumImagePlacements = 5 });
    }

    [Fact]
    public void EpubSharesImageBytesAcrossChaptersAndPreservesPlacementSizes()
    {
        byte[] epub = Epub(PaddedPng(1024 * 1024));
        using var input = new MemoryStream(epub);
        DocumentImage[] images = EpubFormat.Read(input, Context()).Paragraphs.SelectMany(p => p.Runs).Where(r => r.Image != null).Select(r => r.Image!).ToArray();
        Assert.Equal(80, images.Length);
        Assert.All(images, image => Assert.Same(images[0].Bytes, image.Bytes));
        Assert.Equal(7.5, images[0].Width); Assert.Equal(15, images[40].Width);
        Reject(epub, DocumentFormat.Epub, "Limit.ImagePlacements", new ConversionOptions { MaximumImagePlacements = 5 });
    }

    [Fact]
    public void DocSharesOnePictureOffsetAcrossCharacters()
    {
        byte[] source = Doc(new string('\u0001', 80) + "\r", Array.Empty<byte>(), Picture(PaddedPng(1024 * 1024)), picture: true);
        using var input = new MemoryStream(source);
        DocumentImage[] images = DocFormat.Read(input, Context(BestEffort())).Paragraphs.SelectMany(p => p.Runs).Where(r => r.Image != null).Select(r => r.Image!).ToArray();
        Assert.Equal(80, images.Length);
        Assert.All(images, image => Assert.Same(images[0].Bytes, image.Bytes));
        Assert.Equal(72, images[0].Width);
        Reject(source, DocumentFormat.Doc, "Limit.ImagePlacements", new ConversionOptions(FidelityMode.BestEffort) { MaximumImagePlacements = 5 });
    }

    [Fact]
    public void UniqueEncodedImagesHaveAnAggregateBudget()
    {
        var context = Context(new ConversionOptions(maximumExpandedBytes: 16384));
        byte[] png = PaddedPng(10000);
        _ = DocumentImage.Read(png, 1, 1, "first", context);
        var error = Assert.Throws<InvalidDataException>(() => DocumentImage.Read((byte[])png.Clone(), 1, 1, "second", context));
        Assert.StartsWith("Limit.ImageBytes:", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FidelityMode.Strict)]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void RtfRejectsOversizedFontNamesBeforeRunExpansion(FidelityMode mode)
    {
        string source = @"{\rtf1\ansi{\fonttbl{\f0 " + new string('A', 200000) + @";}}\f0 " + string.Concat(Enumerable.Repeat(@"a\b a\b0 ", 2000)) + "}";
        Reject(Encoding.ASCII.GetBytes(source), DocumentFormat.Rtf, "Limit.FontName", new ConversionOptions(mode));
    }

    [Fact]
    public void RtfSharesValidFontNamesAndRetainsAlternatingFormatting()
    {
        string family = new string('A', 256);
        byte[] source = Encoding.ASCII.GetBytes(@"{\rtf1\ansi{\fonttbl{\f0 " + family + @";}}\f0 a\b b\b0 c}");
        TextRun[] runs = RtfFormat.Read(source, Context()).Paragraphs.SelectMany(p => p.Runs).ToArray();
        Assert.Equal(AlternatingText, runs.Select(r => r.Text));
        Assert.False(runs[0].Bold); Assert.True(runs[1].Bold); Assert.False(runs[2].Bold);
        Assert.All(runs, run => { Assert.Equal(family, run.Appearance.FontFamily); Assert.Same(runs[0].Appearance.FontFamily, run.Appearance.FontFamily); });
    }

    [Fact]
    public void DocxResolvesLargeDefaultStyleOnceAndKeepsDirectOverridesIsolated()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:default='1' w:styleId='Normal'><w:rPr>"
            + string.Concat(Enumerable.Repeat("<w:b/>", 40000)) + "</w:rPr></w:style></w:styles>";
        string body = "<w:p><w:r><w:rPr><w:b w:val='0'/></w:rPr><w:t>plain</w:t></w:r></w:p>"
            + string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>bold</w:t></w:r></w:p>", 20000));
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body), styles: styles));
        ParagraphBlock[] paragraphs = DocxReader.Read(input, Context()).Paragraphs.ToArray();
        Assert.Equal(20001, paragraphs.Length);
        Assert.False(Assert.Single(paragraphs[0].Runs).Bold);
        Assert.All(paragraphs.Skip(1), p => Assert.True(Assert.Single(p.Runs).Bold));
    }

    [Fact]
    public void DocxReadsThousandsOfNotesWithoutReparsingTheirRelationships()
    {
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'>" + string.Concat(Enumerable.Range(1, 2000).Select(i => "<w:footnote w:id='" + i + "'/>")) + "</w:footnotes>";
        string links = "<Relationships xmlns='" + TestDocuments.PackageR + "'>" + string.Concat(Enumerable.Range(0, 10000).Select(i => "<Relationship Id='r" + i + "' Type='" + TestDocuments.R + "/hyperlink' Target='https://example.com/' TargetMode='External'/>")) + "</Relationships>";
        byte[] source = WithParts(TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:footnoteReference w:id='1'/></w:r></w:p>"),
            "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>"),
            ("word/footnotes.xml", Encoding.UTF8.GetBytes(notes)), ("word/_rels/footnotes.xml.rels", Encoding.UTF8.GetBytes(links)));
        using var input = new MemoryStream(source);
        DocumentModel model = DocxReader.Read(input, Context());
        Assert.Equal("footnotes:1", Assert.Single(model.Notes).Id);
    }

    [Fact]
    public void DocxStillRejectsDuplicateBookmarkNames()
    {
        Reject(TestDocuments.Docx(TestDocuments.Body("<w:p><w:bookmarkStart w:id='1' w:name='same'/><w:bookmarkEnd w:id='1'/><w:bookmarkStart w:id='2' w:name='same'/></w:p>")), DocumentFormat.Docx, "Docx.Bookmark");
    }

    [Fact]
    public void KindleRejectsRepeatedLateSkeletonRanges()
        => Reject(Kindle(20000, overlap: true), DocumentFormat.Azw3, "Kindle.Skeleton");

    [Fact]
    public void KindleCopiesManyDisjointLateSkeletonsOnEveryTarget()
    {
        using var input = new MemoryStream(Kindle(5000, overlap: false));
        DocumentModel model = KindleFormat.Read(input, true, Context());
        Assert.Equal(5000, model.Paragraphs.Count());
        Assert.All(model.Paragraphs, p => Assert.Equal("x", p.Text));
    }

    [Theory]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void PdfRejectsHighlyCompressedPageStreamsBeforeProviderAllocation(FidelityMode mode)
        => Reject(Pdf(1024 * 1024, 1), DocumentFormat.Pdf, "Limit.ExpandedBytes", new ConversionOptions(mode, maximumExpandedBytes: 65536));

    [Fact]
    public void PdfRejectsGigabyteStreamWithDefaultLimits()
        => Reject(Pdf(1024 * 1024 * 1024, 1), DocumentFormat.Pdf, "Limit.ExpandedBytes", BestEffort());

    [Fact]
    public void PdfStreamBudgetIsAggregateAcrossPages()
        => Reject(Pdf(40000, 2), DocumentFormat.Pdf, "Limit.ExpandedBytes", new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 65536));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PdfLzwPreflightPreservesCodeWidthTransitionsAndBoundsExpansion(int earlyChange)
    {
        var encoded = new List<byte>();
        int bits = 0, pending = 0, width = 9, next = 258;
        void Code(int code)
        {
            pending = (pending << width) | code; bits += width;
            while (bits >= 8) { bits -= 8; encoded.Add((byte)(pending >> bits)); }
        }
        Code(256);
        for (int i = 0; i < 2000; i++)
        {
            Code(32);
            if (i != 0) next++;
            if (width < 12 && next + earlyChange == 1 << width) width++;
        }
        Code(257);
        if (bits != 0) encoded.Add((byte)(pending << (8 - bits)));
        byte[] source = PdfEncoded(encoded.ToArray(), "/LZWDecode", parameters: "/DecodeParms << /EarlyChange " + earlyChange + " >>");
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, BestEffort(), TestContext.Current.CancellationToken);
        using var read = new MemoryStream(output.ToArray());
        Assert.Equal("", Assert.Single(DocxReader.Read(read, Context()).Paragraphs).Text);
        Reject(source, DocumentFormat.Pdf, "Limit.ExpandedBytes", new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 1024));
    }

    [Fact]
    public void PdfRunLengthPreflightBoundsRepeatedRuns()
    {
        byte[] encoded = Enumerable.Range(0, 2000).SelectMany(_ => new byte[] { 129, 32 }).Concat(new byte[] { 128 }).ToArray();
        Reject(PdfEncoded(encoded, "/RunLengthDecode"), DocumentFormat.Pdf, "Limit.ExpandedBytes", new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: 65536));
    }

    [Fact]
    public void PdfAdmitsSmallFlateContent()
    {
        using var input = new MemoryStream(Pdf(100, 1)); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, BestEffort(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Pdf.Reconstruction");
        using var read = new MemoryStream(output.ToArray());
        Assert.Equal("", DocxReader.Read(read, Context()).Paragraphs.Single().Text);
    }

    [Fact]
    public void NewlinesAtTheDefaultCharacterLimitFailBeforeMillionsOfBlocksAreAllocated()
        => Reject(Encoding.ASCII.GetBytes(new string('\n', 4 * 1024 * 1024)), DocumentFormat.Text, "Limit.Blocks");

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Text)]
    [InlineData(DocumentFormat.Rtf)]
    [InlineData(DocumentFormat.Html)]
    [InlineData(DocumentFormat.Markdown)]
    public void ReadersShareTheConfiguredBlockBudget(DocumentFormat format)
    {
        byte[] input = format switch
        {
            DocumentFormat.Docx => TestDocuments.Docx(TestDocuments.Body("<w:p/><w:p/><w:p/>")),
            DocumentFormat.Rtf => Encoding.ASCII.GetBytes(@"{\rtf1\ansi\par\par\par}"),
            DocumentFormat.Html => Encoding.UTF8.GetBytes("<p>one</p><p>two</p><p>three</p>"),
            DocumentFormat.Markdown => Encoding.UTF8.GetBytes("one\n\ntwo\n\nthree"),
            _ => Encoding.UTF8.GetBytes("one\ntwo\nthree"),
        };
        Reject(input, format, "Limit.Blocks", new ConversionOptions { MaximumBlocks = 2 });
    }

    [Fact]
    public void RtfRunFloodFailsBeforeAllocatingAMillionRuns()
        => Reject(Encoding.ASCII.GetBytes(@"{\rtf1\ansi " + string.Concat(Enumerable.Repeat(@"a\b a\b0 ", 500000)) + "}"), DocumentFormat.Rtf, "Limit.Runs");

    [Theory]
    [InlineData(DocumentFormat.Rtf)]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Html)]
    [InlineData(DocumentFormat.Markdown)]
    public void ReadersShareTheConfiguredRunBudget(DocumentFormat format)
    {
        byte[] input = format switch
        {
            DocumentFormat.Rtf => Encoding.ASCII.GetBytes(@"{\rtf1\ansi a\b b\b0 c}"),
            DocumentFormat.Docx => TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:t>a</w:t></w:r><w:r><w:t>b</w:t></w:r><w:r><w:t>c</w:t></w:r></w:p>")),
            DocumentFormat.Html => Encoding.UTF8.GetBytes("<p>a<b>b</b>c</p>"),
            _ => Encoding.UTF8.GetBytes("a**b**c"),
        };
        Reject(input, format, "Limit.Runs", new ConversionOptions { MaximumRuns = 2 });
    }

    [Fact]
    public void KindleRejectsEmptySkeletonsBeforeHtmlParsing()
        => Reject(Kindle(20000, overlap: true, empty: true), DocumentFormat.Azw3, "Kindle.Skeleton");

    [Fact]
    public void LongOrdinaryTextRemainsWithinDefaultBlockBudget()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ordinary line\n", 40000))));
        using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken);
        using var read = new MemoryStream(output.ToArray());
        ParagraphBlock[] paragraphs = DocxReader.Read(read, Context()).Paragraphs.ToArray();
        Assert.Equal(40001, paragraphs.Length);
        Assert.Equal("ordinary line", paragraphs[39999].Text);
    }

    [Fact]
    public void NewLimitsValidateAtAssignmentAndAreSnapshotted()
    {
        var options = new ConversionOptions { MaximumBlocks = 2, MaximumRuns = 2, MaximumImagePlacements = 2 };
        Assert.Throws<ArgumentOutOfRangeException>("MaximumBlocks", () => options.MaximumBlocks = 0);
        Assert.Throws<ArgumentOutOfRangeException>("MaximumRuns", () => options.MaximumRuns = -1);
        Assert.Throws<ArgumentOutOfRangeException>("MaximumImagePlacements", () => options.MaximumImagePlacements = 0);
        var context = Context(options);
        options.MaximumBlocks = options.MaximumRuns = options.MaximumImagePlacements = 3;
        for (int i = 0; i < 2; i++) { context.ReserveBlocks(); context.ReserveRun(); context.ReserveImagePlacement(); }
        Assert.Throws<InvalidDataException>(context.ReserveBlocks);
        Assert.Throws<InvalidDataException>(context.ReserveRun);
        Assert.Throws<InvalidDataException>(context.ReserveImagePlacement);
    }

    [Fact]
    public void DocxSharesResolvedTableStylesAcrossNotes()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='table' w:styleId='T'><w:tblPr><w:shd w:val='clear' w:fill='ABCDEF'/></w:tblPr></w:style></w:styles>";
        string table = "<w:tbl><w:tblPr><w:tblStyle w:val='T'/></w:tblPr><w:tblGrid><w:gridCol w:w='1000'/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>note</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'>" + string.Concat(Enumerable.Range(1, 2000).Select(i => "<w:footnote w:id='" + i + "'>" + table + "</w:footnote>")) + "</w:footnotes>";
        string body = "<w:p>" + string.Concat(Enumerable.Range(1, 2000).Select(i => "<w:r><w:footnoteReference w:id='" + i + "'/></w:r>")) + "</w:p>";
        byte[] source = WithParts(TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", styles: styles), ("word/footnotes.xml", Encoding.UTF8.GetBytes(notes)));
        using var input = new MemoryStream(source);
        DocumentModel model = DocxReader.Read(input, Context(BestEffort()));
        TableBlock[] tables = model.Notes.SelectMany(n => n.Blocks).OfType<TableBlock>().ToArray();
        Assert.Equal(2000, tables.Length);
        Assert.Equal(Enumerable.Range(1, 2000), model.Notes.Select(n => n.Number).OrderBy(n => n));
        Assert.All(tables, t => { Assert.Same(tables[0].Style, t.Style); Assert.Equal("ABCDEF", t.Style!.EffectiveBackground); });
    }

    [Fact]
    public void DocxCachesUnsupportedImageAdmissionAndPreservesEachPlaceholder()
    {
        byte[] tiff = new byte[16 * 1024 * 1024]; tiff[0] = tiff[1] = 73; tiff[2] = 42;
        string body = string.Concat(Enumerable.Range(1, 5).Select(i => FidelityDocuments.ImageParagraph(12700 * i, 12700)));
        byte[] source = WithParts(TestDocuments.Docx(TestDocuments.Body(body),
            "<Relationship Id='image' Type='" + TestDocuments.R + "/image' Target='media/image.tiff'/>"), ("word/media/image.tiff", tiff));
        var options = new ConversionOptions(FidelityMode.BestEffort) { MaximumImagePlacements = 5 };
        using var input = new MemoryStream(source);
        DocumentImage[] images = DocxReader.Read(input, Context(options)).Paragraphs.SelectMany(p => p.Runs).Select(r => r.Image!).ToArray();
        Assert.Equal(5, images.Length);
        Assert.All(images, image => Assert.True(image.IsPlaceholder));
        Assert.Equal(1, images[0].FrameWidth); Assert.Equal(5, images[4].FrameWidth);
        using var convert = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(convert, output, DocumentFormat.Docx, DocumentFormat.Html, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Image.UnsupportedFormat");
        Assert.Equal(5, System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(output.ToArray())).Descendants().Count(e => e.Name.LocalName == "img"));
    }

    private static ConversionOptions BestEffort() => new ConversionOptions(FidelityMode.BestEffort);
    private static ConversionContext Context(ConversionOptions? options = null) => new ConversionContext(options ?? new ConversionOptions(), TestContext.Current.CancellationToken);
    private static void Reject(byte[] bytes, DocumentFormat format, string code, ConversionOptions? options = null)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, format,
            format == DocumentFormat.Docx ? DocumentFormat.Html : DocumentFormat.Docx, options ?? new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith(code + ":", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
