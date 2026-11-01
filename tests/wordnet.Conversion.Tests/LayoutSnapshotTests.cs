using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class LayoutSnapshotTests
{
    private static readonly string[] BorderSides = { "top", "left", "bottom", "right", "insideH", "insideV" };
    private static byte[] Source(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string P(string text) => TestDocuments.TextParagraph(text);

    private static string BorderParagraph(string text) => "<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='240' w:lineRule='auto'/></w:pPr>"
        + "<w:r><w:rPr><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p>";

    private static byte[] BorderSource(string border, string tableMargins = "", string cellMargins = "", string? content = null)
    {
        string edges = string.Concat(BorderSides.Select(side => "<w:" + side + " " + border + "/>"));
        string table = "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblBorders>" + edges + "</w:tblBorders>"
            + "<w:tblLayout w:type='fixed'/>" + tableMargins + "</w:tblPr><w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid>"
            + "<w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/>" + cellMargins + "</w:tcPr>"
            + (content ?? BorderParagraph("N")) + "</w:tc></w:tr></w:tbl>";
        return Source(BorderParagraph("A") + table + BorderParagraph("T"));
    }

    [Fact]
    public void UniformSingleCellBordersAccountForTheirWidthsAndMarginOwners()
    {
        const string none = "w:val='nil'", single = "w:val='single' w:sz='8' w:color='000000'";
        const string tableZero = "<w:tblCellMar><w:left w:w='0' w:type='dxa'/><w:right w:w='0' w:type='dxa'/></w:tblCellMar>";
        const string tableDefault = "<w:tblCellMar><w:left w:w='115' w:type='dxa'/><w:right w:w='115' w:type='dxa'/></w:tblCellMar>";
        const string cellZero = "<w:tcMar><w:left w:w='0' w:type='dxa'/><w:right w:w='0' w:type='dxa'/></w:tcMar>";
        Dictionary<string, LayoutBounds> Bounds(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
            return Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text != null && f.Bounds != null)
                .ToDictionary(f => f.Text!, f => f.Bounds!, StringComparer.Ordinal);
        }
        var disabled = Bounds(BorderSource(none)); var bordered = Bounds(BorderSource(single));
        Assert.Equal(disabled["N"].X + .5, bordered["N"].X, 3);
        Assert.Equal(disabled["N"].Y + 1, bordered["N"].Y, 3);
        Assert.Equal(disabled["T"].Y + 2, bordered["T"].Y, 3);
        var tableMargin = Bounds(BorderSource(single, tableZero));
        byte[] source = BorderSource(single, tableDefault, cellZero); var cellMargin = Bounds(source);
        Assert.Equal(cellMargin["N"].X + .5, tableMargin["N"].X, 3);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        var restored = Bounds(saved.ToArray());
        foreach (string text in new[] { "N", "T" })
        { Assert.Equal(cellMargin[text].X, restored[text].X, 3); Assert.Equal(cellMargin[text].Y, restored[text].Y, 3); }
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        using var part = archive.GetEntry("word/document.xml")!.Open();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"; XDocument xml = XDocument.Load(part);
        Assert.Equal(6, Assert.Single(xml.Descendants(w + "tblBorders")).Elements().Count());
        Assert.Equal("115", (string?)Assert.Single(xml.Descendants(w + "tblCellMar")).Element(w + "left")!.Attribute(w + "w"));
        Assert.Equal("0", (string?)Assert.Single(xml.Descendants(w + "tcMar")).Element(w + "left")!.Attribute(w + "w"));
    }

    [Fact]
    public void UnqualifiedBorderWidthsAndMultilineGeometryStayVisible()
    {
        const string single = "w:val='single' w:sz='8' w:color='000000'";
        const string odd = "w:val='single' w:sz='3' w:color='000000'";
        Dictionary<string, LayoutBounds> Bounds(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } },
                new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            return Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text != null && f.Bounds != null)
                .ToDictionary(f => f.Text!, f => f.Bounds!, StringComparer.Ordinal);
        }
        var qualified = Bounds(BorderSource(single)); var unqualified = Bounds(BorderSource(odd));
        Assert.True(qualified["N"].X > unqualified["N"].X + 5);
        Assert.True(unqualified["T"].Y > qualified["T"].Y + 3);
        byte[] multiple = BorderSource(single, content: BorderParagraph("N") + BorderParagraph("M"));
        using var strict = new MemoryStream(multiple);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(strict, DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Table.BorderGeometry", error.Message, StringComparison.Ordinal);
        using var lenient = new MemoryStream(multiple);
        var preview = new WordConverter().RenderPages(lenient, DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.Lenient),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(preview.Result.Diagnostics, diagnostic => diagnostic.Code == "Table.BorderGeometry");
        using var editableInput = new MemoryStream(BorderSource(single));
        WordDocument editable = WordDocument.Load(editableInput, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        editable.AddBookmark("cellmark", 1, 0, 1, TestContext.Current.CancellationToken);
        using var strictOutput = new MemoryStream();
        error = Assert.Throws<NotSupportedException>(() => editable.Save(strictOutput, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Table.BorderGeometry", error.Message, StringComparison.Ordinal); Assert.Equal(0, strictOutput.Length);
        using var lenientOutput = new MemoryStream();
        ConversionResult result = editable.Save(lenientOutput, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Table.BorderGeometry");
    }

    [Fact]
    public void PlainFixedDocxTablesUseDefaultCellMarginsWithoutAnExtraFollowingGap()
    {
        string Paragraph(string text) => "<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='240' w:lineRule='auto'/></w:pPr>"
            + "<w:r><w:rPr><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p>";
        string first = Paragraph("A"), cell = Paragraph("N"), last = Paragraph("T");
        string table = "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/></w:tcPr>" + cell + "</w:tc></w:tr></w:tbl>";
        Dictionary<string, LayoutBounds> Bounds(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
            return Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text != null && f.Bounds != null)
                .ToDictionary(f => f.Text!, f => f.Bounds!, StringComparer.Ordinal);
        }
        byte[] source = Source(first + table + last);
        var ordinary = Bounds(Source(first + cell + last)); var original = Bounds(source);
        Assert.Equal(ordinary["N"].X + 5.75, original["N"].X, 3);
        Assert.Equal(ordinary["N"].Y, original["N"].Y, 3);
        Assert.Equal(ordinary["T"].Y, original["T"].Y, 3);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        using (var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read))
        using (var part = archive.GetEntry("word/document.xml")!.Open())
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XDocument xml = XDocument.Load(part);
            Assert.Empty(xml.Descendants(w + "tblCellMar")); Assert.Empty(xml.Descendants(w + "tcMar"));
        }
        var restored = Bounds(saved.ToArray());
        Assert.Equal(original["N"].X, restored["N"].X, 3);
        Assert.Equal(original["T"].Y, restored["T"].Y, 3);
    }

    [Fact]
    public void ExplicitTableMarginsAreNotReplacedByImplicitDefaults()
    {
        const string body = "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/>"
            + "<w:tblCellMar><w:left w:w='0' w:type='dxa'/><w:right w:w='0' w:type='dxa'/></w:tblCellMar></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/></w:tcPr>"
            + "<w:p><w:r><w:t>Same</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        byte[] source = Source(P("Same") + body + P("After")); using var input = new MemoryStream(source);
        var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
        LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text == "Same").ToArray();
        Assert.Equal(2, fragments.Length); Assert.Equal(fragments[0].Bounds!.X, fragments[1].Bounds!.X, 3);
        LayoutFragment after = Assert.Single(preview.Layout!.Fragments, f => f.Text == "After");
        using var plainInput = new MemoryStream(Source(P("Same") + P("Same") + P("After")));
        var plain = new WordConverter().RenderPages(plainInput, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
        LayoutFragment ordinary = Assert.Single(plain.Layout!.Fragments, f => f.Text == "After");
        Assert.Equal(ordinary.Bounds!.Y, after.Bounds!.Y, 3);
    }

    [Fact]
    public void PartialMarginsUseDefaultsAndRetainTableAndCellOverrides()
    {
        string Paragraph(string text) => "<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='240' w:lineRule='auto'/></w:pPr>"
            + "<w:r><w:rPr><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p>";
        string first = Paragraph("A"), left = Paragraph("N"), right = Paragraph("M"), last = Paragraph("T");
        string table = "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/>"
            + "<w:tblCellMar><w:top w:w='40' w:type='dxa'/></w:tblCellMar></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='1500'/><w:gridCol w:w='1500'/></w:tblGrid><w:tr>"
            + "<w:tc><w:tcPr><w:tcW w:w='1500' w:type='dxa'/></w:tcPr>" + left + "</w:tc>"
            + "<w:tc><w:tcPr><w:tcW w:w='1500' w:type='dxa'/><w:tcMar><w:left w:w='0' w:type='dxa'/></w:tcMar></w:tcPr>" + right + "</w:tc></w:tr></w:tbl>";
        Dictionary<string, LayoutBounds> Bounds(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
            return Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text != null && f.Bounds != null)
                .ToDictionary(f => f.Text!, f => f.Bounds!, StringComparer.Ordinal);
        }
        byte[] source = Source(first + table + last);
        var ordinary = Bounds(Source(first + left + last)); var rightOrdinary = Bounds(Source(right)); var actual = Bounds(source);
        Assert.Equal(ordinary["N"].X + 5.75, actual["N"].X, 3);
        Assert.Equal(rightOrdinary["M"].X + 75, actual["M"].X, 3);
        Assert.Equal(ordinary["N"].Y + 2, actual["N"].Y, 3);
        Assert.Equal(ordinary["T"].Y + 2, actual["T"].Y, 3);
        using var input = new MemoryStream(source);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        using (var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read))
        using (var part = archive.GetEntry("word/document.xml")!.Open())
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XDocument xml = XDocument.Load(part);
            XElement defaults = Assert.Single(xml.Descendants(w + "tblCellMar"));
            Assert.Equal("40", (string?)Assert.Single(defaults.Elements()).Attribute(w + "w"));
            Assert.Equal(w + "top", Assert.Single(defaults.Elements()).Name);
            XElement cellMargins = Assert.Single(xml.Descendants(w + "tcMar"));
            XElement cellLeft = Assert.Single(cellMargins.Elements());
            Assert.Equal(w + "left", cellLeft.Name); Assert.Equal("0", (string?)cellLeft.Attribute(w + "w"));
        }
        var restored = Bounds(saved.ToArray());
        foreach (string text in new[] { "N", "M", "T" })
        { Assert.Equal(actual[text].X, restored[text].X, 3); Assert.Equal(actual[text].Y, restored[text].Y, 3); }
    }

    [Fact]
    public void UnknownMarginMetadataDoesNotEnterTheQualifiedTableProfile()
    {
        string Paragraph(string text) => "<w:p><w:pPr><w:spacing w:before='0' w:after='0'/></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";
        string table = "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/>"
            + "<w:tblCellMar><w:top w:w='40' w:type='dxa' EXTRA/></w:tblCellMar></w:tblPr>"
            + "<w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/></w:tcPr>"
            + Paragraph("N") + "</w:tc></w:tr></w:tbl>" + Paragraph("T");
        Dictionary<string, LayoutBounds> Bounds(string markup)
        {
            using var input = new MemoryStream(Source(markup));
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } },
                new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            return Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(f => f.Text != null && f.Bounds != null)
                .ToDictionary(f => f.Text!, f => f.Bounds!, StringComparer.Ordinal);
        }
        var admitted = Bounds(table.Replace(" EXTRA", ""));
        var unknown = Bounds(table.Replace("EXTRA", "w:unknown='1'"));
        Assert.Equal(unknown["N"].X + 5.75, admitted["N"].X, 3);
        Assert.Equal(admitted["T"].Y + 6, unknown["T"].Y, 3);
    }

    [Fact]
    public void DirectDocxRunsMapToTheSamePreviewGeometryWithoutChangingItsBytes()
    {
        string body = "<w:p><w:r><w:t>Alpha</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r></w:p>";
        byte[] source = Source(body); var converter = new WordConverter();
        using var regularInput = new MemoryStream(source);
        DocumentPreview regular = converter.RenderPages(regularInput, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        using var input = new MemoryStream(source);
        DocumentPreview preview = converter.RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Assert.Single(regular.Pages).GetBytes(), Assert.Single(preview.Pages).GetBytes());
        LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Bounds != null).ToArray();
        LayoutFragment alpha = Assert.Single(fragments, item => item.Text == "Alpha");
        LayoutFragment bold = Assert.Single(fragments, item => item.Text == "Bold");
        Assert.Equal("word/document.xml", alpha.RunSource!.Part);
        Assert.Equal("/document[1]/body[1]/p[1]/r[1]", alpha.RunSource.Path);
        Assert.Equal("/document[1]/body[1]/p[1]/r[2]", bold.RunSource!.Path);
        Assert.Equal(alpha.Source!.Path, bold.Source!.Path);
        using var pdf = PdfDocument.Open(converter.ConvertToPdf(source));
        var letters = pdf.GetPage(1).Letters.Take(5).ToArray();
        Assert.Equal(letters.Min(letter => letter.BoundingBox.Left), alpha.Bounds!.X, 3);
        Assert.Equal(letters.Max(letter => letter.BoundingBox.Right) - alpha.Bounds.X, alpha.Bounds.Width, 3);
    }

    [Fact]
    public void CoalescedWrappedAndCompositeRunsDoNotClaimOneSourceRun()
    {
        string coalesced = "<w:p><w:r><w:t>Joined</w:t></w:r><w:r><w:t>Word</w:t></w:r></w:p>";
        string wrapped = "<w:p><w:hyperlink w:anchor='target'><w:r><w:t>Linked</w:t></w:r></w:hyperlink></w:p>";
        string composite = "<w:p><w:r><w:t>Multi</w:t><w:t>Part</w:t></w:r></w:p>";
        foreach (string body in new[] { coalesced, wrapped, composite })
        {
            byte[] source = Source(body); var converter = new WordConverter();
            using var regularInput = new MemoryStream(source);
            var regular = converter.RenderPages(regularInput, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
            using var input = new MemoryStream(source);
            var preview = converter.RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(Assert.Single(regular.Pages).GetBytes(), Assert.Single(preview.Pages).GetBytes());
            LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Bounds != null).ToArray();
            Assert.NotEmpty(fragments); Assert.All(fragments, item => Assert.Null(item.RunSource));
        }
    }

    [Fact]
    public void NamespaceAmbiguousPathsDoNotClaimARunOwner()
    {
        string foreignRun = "<w:p><f:r xmlns:f='urn:foreign'/><w:r><w:t>Visible</w:t></w:r></w:p>";
        string foreignParagraph = "<f:p xmlns:f='urn:foreign'/>" + P("Visible");
        foreach (string body in new[] { foreignRun, foreignParagraph })
        {
            using var input = new MemoryStream(Source(body));
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx,
                new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } },
                new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Text == "Visible").ToArray();
            Assert.NotEmpty(fragments); Assert.All(fragments, item => { Assert.NotNull(item.Bounds); Assert.Null(item.RunSource); });
        }
    }

    [Fact]
    public void PreviewCapturesPrivateSourceLocationsAndManagedGlyphBounds()
    {
        byte[] source = Source(P("Alpha") + P("Beta")); var converter = new WordConverter();
        using var withoutInput = new MemoryStream(source);
        DocumentPreview without = converter.RenderPages(withoutInput, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(without.Layout);
        var policy = new PreviewOptions { Layout = new LayoutSnapshotOptions() };
        using var input = new MemoryStream(source);
        DocumentPreview preview = converter.RenderPages(input, DocumentFormat.Docx, policy, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); var layout = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout);
        var fragments = layout.Fragments.Where(item => item.Bounds != null).ToArray(); Assert.Equal(2, fragments.Length);
        Assert.All(fragments, item => { Assert.Null(item.Text); Assert.NotNull(item.Source); Assert.NotNull(item.RunSource); Assert.Null(item.CellSource); Assert.Null(item.ObjectSource); Assert.Equal("word/document.xml", item.Source!.Part); Assert.False(item.ExtendsOutsidePage); });
        Assert.NotEqual(fragments[0].Source!.Path, fragments[1].Source!.Path);
        Assert.Throws<NotSupportedException>(() => ((ICollection<LayoutFragment>)layout.Fragments).Add(fragments[0]));
        Assert.Contains("<svg", Encoding.UTF8.GetString(Assert.Single(preview.Pages).GetBytes()), StringComparison.Ordinal);
        using var pdf = PdfDocument.Open(converter.ConvertToPdf(source)); var letters = pdf.GetPage(1).Letters.Take(5).ToArray();
        double left = letters.Min(letter => letter.BoundingBox.Left), right = letters.Max(letter => letter.BoundingBox.Right);
        double top = pdf.GetPage(1).Height - letters.Max(letter => letter.BoundingBox.Top), bottom = pdf.GetPage(1).Height - letters.Min(letter => letter.BoundingBox.Bottom);
        LayoutBounds bounds = fragments[0].Bounds!;
        Assert.Equal(left, bounds.X, 3); Assert.Equal(right - left, bounds.Width, 3);
        Assert.Equal(top, bounds.Y, 3); Assert.Equal(bottom - top, bounds.Height, 3);
        policy.Format = PreviewFormat.Png; using var rasterInput = new MemoryStream(source);
        DocumentPreview raster = converter.RenderPages(rasterInput, DocumentFormat.Docx, policy, cancellationToken: TestContext.Current.CancellationToken);
        var rasterBounds = Assert.IsType<DocumentLayoutSnapshot>(raster.Layout).Fragments.Where(item => item.Bounds != null).Select(item => item.Bounds!).ToArray();
        Assert.Equal(fragments.Length, rasterBounds.Length);
        for (int i = 0; i < fragments.Length; i++)
        { Assert.Equal(fragments[i].Bounds!.X, rasterBounds[i].X); Assert.Equal(fragments[i].Bounds!.Y, rasterBounds[i].Y); }
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, Assert.Single(raster.Pages).GetBytes().Take(4));
    }

    [Fact]
    public void TextAndPageSelectionAreExplicitAndStableInTheSnapshot()
    {
        string paragraphs = string.Concat(Enumerable.Range(0, 50).Select(i => P("Line" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))));
        string section = "<w:sectPr><w:pgSz w:w='5000' w:h='3000'/><w:pgMar w:top='300' w:bottom='300' w:left='300' w:right='300'/></w:sectPr>";
        byte[] source = Source(paragraphs + section); var policy = new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } };
        policy.Pages.Add(new PageRange(2, 2)); using var input = new MemoryStream(source);
        DocumentPreview preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, policy, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(preview.SourcePageCount > 1); Assert.Equal(2, Assert.Single(preview.Pages).SourcePage);
        var layout = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout); Assert.NotEmpty(layout.Fragments);
        Assert.All(layout.Fragments, item => Assert.Equal(2, item.Page)); Assert.Contains(layout.Fragments, item => item.Text != null && item.Text.StartsWith("Line", StringComparison.Ordinal));
        string retained = string.Concat(layout.Fragments.Select(item => item.Text)); policy.Layout.IncludeText = false;
        Assert.Equal(retained, string.Concat(layout.Fragments.Select(item => item.Text)));
    }

    [Fact]
    public void MissingSourceGeometryIsExplicitAndFragmentLimitsFail()
    {
        string paragraph = "<w:p><w:bookmarkStart w:id='1' w:name='spot'/><w:r><w:t>Text</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        byte[] source = Source(paragraph + P("Second")); var converter = new WordConverter();
        using var input = new MemoryStream(source); var options = new PreviewOptions { Layout = new LayoutSnapshotOptions() };
        DocumentLayoutSnapshot layout = Assert.IsType<DocumentLayoutSnapshot>(converter.RenderPages(input, DocumentFormat.Docx, options, cancellationToken: TestContext.Current.CancellationToken).Layout);
        Assert.Contains(layout.Fragments, item => item.Bounds == null && item.UnavailableReason == "UnqualifiedOwner");
        input.Position = 0; options.Layout.MaximumFragments = 1;
        var error = Assert.Throws<InvalidDataException>(() => converter.RenderPages(input, DocumentFormat.Docx, options, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.LayoutFragments:", error.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
        input.Position = 0; options.Layout.MaximumFragments = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.RenderPages(input, DocumentFormat.Docx, options, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SourceLocatedLayoutDiagnosticCanBeMatchedWithoutAnotherRender()
    {
        string paragraph = "<w:p><w:pPr><w:spacing w:line='20' w:lineRule='exact'/></w:pPr><w:r><w:t>Compressed</w:t></w:r></w:p>";
        using var input = new MemoryStream(Source(paragraph));
        var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions() },
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        ConversionDiagnostic diagnostic = Assert.Single(preview.Result.Diagnostics, item => item.Code == "Layout.LineSpacing");
        DiagnosticOccurrence occurrence = Assert.Single(diagnostic.Occurrences);
        LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Bounds != null).ToArray();
        Assert.NotEmpty(fragments);
        Assert.All(fragments, fragment => { Assert.Equal(occurrence.Location.Part, fragment.Source!.Part); Assert.Equal(occurrence.Location.Path, fragment.Source.Path); });
    }

    [Fact]
    public void SimpleTableCellTextMapsItsOwnerAndRenderedGlyphBounds()
    {
        string table = "<w:tbl><w:tr><w:tc>" + P("Inside") + "</w:tc></w:tr></w:tbl>";
        byte[] source = Source(table); var converter = new WordConverter(); using var input = new MemoryStream(source);
        DocumentLayoutSnapshot snapshot = Assert.IsType<DocumentLayoutSnapshot>(converter.RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken).Layout);
        LayoutFragment fragment = Assert.Single(snapshot.Fragments, item => item.Text == "Inside");
        Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]/p[1]", fragment.Source!.Path);
        Assert.Equal("word/document.xml", fragment.CellSource!.Part);
        Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]", fragment.CellSource.Path);
        Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]/p[1]/r[1]", fragment.RunSource!.Path);
        Assert.NotNull(fragment.Bounds); Assert.False(fragment.ExtendsOutsidePage); Assert.Null(fragment.UnavailableReason);
        using var pdf = PdfDocument.Open(converter.ConvertToPdf(source)); var letters = pdf.GetPage(1).Letters.ToArray();
        double left = letters.Min(letter => letter.BoundingBox.Left), right = letters.Max(letter => letter.BoundingBox.Right);
        double top = pdf.GetPage(1).Height - letters.Max(letter => letter.BoundingBox.Top), bottom = pdf.GetPage(1).Height - letters.Min(letter => letter.BoundingBox.Bottom);
        Assert.Equal(left, fragment.Bounds!.X, 3); Assert.Equal(right - left, fragment.Bounds.Width, 3);
        Assert.Equal(top, fragment.Bounds.Y, 3); Assert.Equal(bottom - top, fragment.Bounds.Height, 3);
        input.Position = 0;
        DocumentLayoutSnapshot hidden = Assert.IsType<DocumentLayoutSnapshot>(converter.RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions() }, cancellationToken: TestContext.Current.CancellationToken).Layout);
        LayoutFragment privateFragment = Assert.Single(hidden.Fragments);
        Assert.Null(privateFragment.Text); Assert.Equal(fragment.CellSource.Path, privateFragment.CellSource!.Path);
    }

    [Fact]
    public void RepeatedTableHeadersRetainTheirSourceCellAcrossSelectedPages()
    {
        string header = "<w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc>" + P("HEADER") + "</w:tc></w:tr>";
        string rows = string.Concat(Enumerable.Range(0, 40).Select(i => "<w:tr><w:tc>" + P("Row" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)) + "</w:tc></w:tr>"));
        string section = "<w:sectPr><w:pgSz w:w='5000' w:h='3000'/><w:pgMar w:top='300' w:bottom='300' w:left='300' w:right='300'/></w:sectPr>";
        byte[] source = Source("<w:tbl>" + header + rows + "</w:tbl>" + section);
        var converter = new WordConverter(); var policy = new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } };
        using var input = new MemoryStream(source); DocumentPreview preview = converter.RenderPages(input, DocumentFormat.Docx, policy, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(preview.SourcePageCount > 1);
        LayoutFragment[] headers = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Text == "HEADER").ToArray();
        Assert.Equal(preview.SourcePageCount, headers.Length); Assert.Equal(preview.SourcePageCount, headers.Select(item => item.Page).Distinct().Count());
        Assert.All(headers, item => { Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]", item.CellSource!.Path); Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]/p[1]/r[1]", item.RunSource!.Path); Assert.NotNull(item.Bounds); });
        policy.Pages.Add(new PageRange(2, 2)); input.Position = 0;
        DocumentPreview selected = converter.RenderPages(input, DocumentFormat.Docx, policy, cancellationToken: TestContext.Current.CancellationToken);
        LayoutFragment repeated = Assert.Single(Assert.IsType<DocumentLayoutSnapshot>(selected.Layout).Fragments, item => item.Text == "HEADER");
        Assert.Equal(2, repeated.Page); Assert.Equal(headers[0].CellSource!.Path, repeated.CellSource!.Path);
    }

    [Fact]
    public void CellDiagnosticMatchesTheSamePreviewFragmentAndOwner()
    {
        string paragraph = "<w:p><w:pPr><w:spacing w:line='20' w:lineRule='exact'/></w:pPr><w:r><w:t>Compressed</w:t></w:r></w:p>";
        using var input = new MemoryStream(Source("<w:tbl><w:tr><w:tc>" + paragraph + "</w:tc></w:tr></w:tbl>"));
        var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions() },
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        ConversionDiagnostic diagnostic = Assert.Single(preview.Result.Diagnostics, item => item.Code == "Layout.LineSpacing");
        DiagnosticOccurrence occurrence = Assert.Single(diagnostic.Occurrences);
        LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => item.Bounds != null).ToArray();
        Assert.NotEmpty(fragments);
        Assert.All(fragments, item =>
        {
            Assert.Equal(occurrence.Location.Part, item.Source!.Part); Assert.Equal(occurrence.Location.Path, item.Source.Path);
            Assert.Equal("/document[1]/body[1]/tbl[1]/tr[1]/tc[1]", item.CellSource!.Path);
        });
    }

    [Fact]
    public void MergedNestedWrappedAndExplicitHeightCellsRemainUnqualified()
    {
        string merged = "<w:tbl><w:tblGrid><w:gridCol w:w='1500'/><w:gridCol w:w='1500'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" + P("Merged") + "</w:tc></w:tr></w:tbl>";
        string nested = "<w:tbl><w:tr><w:tc><w:tbl><w:tr><w:tc>" + P("Nested") + "</w:tc></w:tr></w:tbl>" + P("Tail") + "</w:tc></w:tr></w:tbl>";
        string wrapped = "<w:tbl><w:sdt><w:sdtPr/><w:sdtContent><w:tr><w:tc>" + P("Wrapped") + "</w:tc></w:tr></w:sdtContent></w:sdt></w:tbl>";
        string exact = "<w:tbl><w:tr><w:trPr><w:trHeight w:val='600' w:hRule='exact'/></w:trPr><w:tc>" + P("Exact") + "</w:tc></w:tr></w:tbl>";
        foreach (string body in new[] { merged, nested, wrapped, exact })
        {
            using var input = new MemoryStream(Source(body));
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } },
                new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            LayoutFragment[] fragments = Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
            Assert.NotEmpty(fragments);
            Assert.All(fragments, item => { Assert.Null(item.CellSource); Assert.Null(item.Bounds); Assert.Equal("UnqualifiedOwner", item.UnavailableReason); });
        }
    }

    [Fact]
    public void TableCellOwnerWorkUsesTheExpansionBudget()
    {
        string rows = string.Concat(Enumerable.Repeat("<w:tr><w:tc>" + P("X") + "</w:tc></w:tr>", 300));
        byte[] source = Source("<w:tbl>" + rows + "</w:tbl>");
        using var archiveInput = new MemoryStream(source);
        using var archive = new ZipArchive(archiveInput, ZipArchiveMode.Read);
        int limit = checked((int)archive.Entries.Sum(entry => entry.Length) + 1024);
        var options = new ConversionOptions(FidelityMode.BestEffort, maximumExpandedBytes: limit);
        using var input = new MemoryStream(source);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions() }, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.LayoutSnapshot: Cell owner inspection", error.Message, StringComparison.Ordinal); Assert.True(input.CanRead);
    }

    [Fact]
    public void InlineImageSnapshotMapsTheSourceDrawingAndRenderedFrame()
    {
        byte[] source = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph());
        var options = new ConversionOptions(FidelityMode.BestEffort); var converter = new WordConverter();
        using var input = new MemoryStream(source);
        var preview = converter.RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions() }, options, TestContext.Current.CancellationToken);
        LayoutFragment fragment = Assert.Single(Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments);
        Assert.Equal("/document[1]/body[1]/p[1]", fragment.Source!.Path);
        Assert.Equal("word/document.xml", fragment.ObjectSource!.Part);
        Assert.Equal("/document[1]/body[1]/p[1]/r[1]/drawing[1]", fragment.ObjectSource.Path);
        Assert.Null(fragment.CellSource); Assert.Null(fragment.RunSource); Assert.Null(fragment.Text); Assert.Null(fragment.UnavailableReason); Assert.False(fragment.ExtendsOutsidePage);
        LayoutBounds bounds = Assert.IsType<LayoutBounds>(fragment.Bounds); Assert.Equal(120, bounds.Width, 3); Assert.Equal(60, bounds.Height, 3);
        using var pdfBytes = new MemoryStream(); input.Position = 0;
        converter.Convert(input, pdfBytes, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(pdfBytes.ToArray()); var page = pdf.GetPage(1); var image = Assert.Single(page.GetImages());
        Assert.Equal(image.BoundingBox.Left, bounds.X, 3); Assert.Equal(page.Height - image.BoundingBox.Top, bounds.Y, 3);
        Assert.Equal(image.BoundingBox.Width, bounds.Width, 3); Assert.Equal(image.BoundingBox.Height, bounds.Height, 3);
    }

    [Fact]
    public void TransformedWrappedAndMixedInlineImagesRemainUnqualified()
    {
        string original = FidelityDocuments.ImageParagraph();
        string cropped = original.Replace("<a:stretch>", "<a:srcRect l='50000'/><a:stretch>");
        string rotated = original.Replace("<a:xfrm>", "<a:xfrm rot='5400000'>");
        string tiled = original.Replace("<a:stretch><a:fillRect/></a:stretch>", "<a:tile/>");
        string shifted = original.Replace("<a:off x='0' y='0'/>", "<a:off x='12700' y='0'/>");
        string unknownGraphic = original.Replace("uri='http://schemas.openxmlformats.org/drawingml/2006/picture'", "uri='urn:unknown'");
        string anchored = original.Replace("<wp:inline ", "<wp:anchor behindDoc='0' ")
            .Replace("<wp:extent", "<wp:positionH relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionV><wp:wrapNone/><wp:extent")
            .Replace("</wp:inline>", "</wp:anchor>");
        string mixed = original.Replace("</w:p>", "<w:r><w:t>After</w:t></w:r></w:p>");
        string wrapped = original.Replace("<w:r>", "<w:hyperlink w:anchor='target'><w:r>").Replace("</w:r>", "</w:r></w:hyperlink>");
        foreach (string body in new[] { cropped, rotated, tiled, shifted, unknownGraphic, anchored, mixed, wrapped })
        {
            byte[] source = FidelityDocuments.Docx("color-grid.png", body); using var input = new MemoryStream(source);
            var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } },
                new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
            LayoutFragment image = Assert.Single(Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments, item => item.Text == string.Empty && item.Source != null && item.Bounds == null);
            Assert.Null(image.ObjectSource); Assert.Equal("UnqualifiedOwner", image.UnavailableReason);
        }
    }

    [Fact]
    public void UnsupportedImagePlaceholderIsNotMappedAsTheSourceImage()
    {
        byte[] original = FidelityDocuments.Docx("color-grid.png", FidelityDocuments.ImageParagraph());
        using var edited = new MemoryStream(); edited.Write(original, 0, original.Length);
        using (var package = new ZipArchive(edited, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry relationships = package.GetEntry("word/_rels/document.xml.rels")!;
            string xml; using (var reader = new StreamReader(relationships.Open())) xml = reader.ReadToEnd();
            relationships.Delete();
            FidelityDocuments.Add(package, "word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(xml.Replace("media/color-grid.png", "media/replacement.svg")));
            FidelityDocuments.Add(package, "word/media/replacement.svg", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"));
            ZipArchiveEntry typesEntry = package.GetEntry("[Content_Types].xml")!;
            XDocument types; using (Stream stream = typesEntry.Open()) types = XDocument.Load(stream);
            XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
            types.Root!.Add(new XElement(contentTypes + "Default", new XAttribute("Extension", "svg"), new XAttribute("ContentType", "image/svg+xml")));
            typesEntry.Delete(); FidelityDocuments.Add(package, "[Content_Types].xml", Encoding.UTF8.GetBytes(types.ToString()));
        }
        using var input = new MemoryStream(edited.ToArray());
        var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions() },
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(preview.Result.Diagnostics, diagnostic => diagnostic.Code == "Image.UnsupportedFormat");
        LayoutFragment fragment = Assert.Single(Assert.IsType<DocumentLayoutSnapshot>(preview.Layout).Fragments);
        Assert.Null(fragment.ObjectSource); Assert.Null(fragment.Bounds); Assert.Equal("UnqualifiedOwner", fragment.UnavailableReason);
    }
}
