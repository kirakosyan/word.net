using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class LayoutSnapshotTests
{
    private static byte[] Source(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string P(string text) => TestDocuments.TextParagraph(text);

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
        Assert.All(fragments, item => { Assert.Null(item.Text); Assert.NotNull(item.Source); Assert.Equal("word/document.xml", item.Source!.Part); Assert.False(item.ExtendsOutsidePage); });
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
    public void TableCellTextIsReportedWithoutInventingBounds()
    {
        string table = "<w:tbl><w:tr><w:tc>" + P("Inside") + "</w:tc></w:tr></w:tbl>";
        using var input = new MemoryStream(Source(table));
        DocumentLayoutSnapshot snapshot = Assert.IsType<DocumentLayoutSnapshot>(new WordConverter().RenderPages(input, DocumentFormat.Docx,
            new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } }, cancellationToken: TestContext.Current.CancellationToken).Layout);
        LayoutFragment fragment = Assert.Single(snapshot.Fragments, item => item.Text == "Inside");
        Assert.NotNull(fragment.Source); Assert.Null(fragment.Bounds); Assert.Null(fragment.ExtendsOutsidePage); Assert.Equal("UnqualifiedOwner", fragment.UnavailableReason);
    }
}
