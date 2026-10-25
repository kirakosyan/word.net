using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class DirectPdfTests
{
    private static byte[] Odt(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Odt", name + ".odt"));

    [Fact]
    public void HtmlRendersHeadingListsTableAndActualImageWithDeclaredGeometry()
    {
        byte[] image = FidelityDocuments.Fixture("color-grid.png");
        string html = "<h1>Report</h1><ol start='3'><li>Alpha</li><li>Beta</li></ol>"
            + "<table style='width:240pt'><tr><td>Left</td><td>Right</td></tr></table>"
            + "<p><img alt='Grid' style='width:80px;height:40px' src='data:image/png;base64," + Convert.ToBase64String(image) + "'></p>";
        using PdfDocument pdf = PdfDocument.Open(Export(Encoding.UTF8.GetBytes(html), DocumentFormat.Html));
        var page = pdf.GetPage(1);
        Assert.Equal(1, pdf.NumberOfPages);
        Ordered(page.Text, "Report", "3.", "Alpha", "4.", "Beta", "Left", "Right");
        Assert.True(page.Letters.First(l => l.Value == "R").FontSize > page.Letters.First(l => l.Value == "A").FontSize);
        Assert.Equal(48, page.Letters[0].StartBaseLine.X, 3); // Default managed page margin.
        CheckImage(pdf);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("list")]
    [InlineData("table-image")]
    [InlineData("masters")]
    public void IndependentOdtFixturesRenderTextImagesAndMasterGeometry(string name)
    {
        using PdfDocument pdf = PdfDocument.Open(Export(Odt(name), DocumentFormat.Odt));
        string text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        if (name == "text")
        {
            Ordered(text, "Café", "Ω", "end", "next");
            Assert.Equal(400, pdf.GetPage(1).Width, 3);
            Assert.Equal(300, pdf.GetPage(1).Height, 3);
        }
        else if (name == "list") Ordered(text, "3.", "4.");
        else if (name == "table-image") { Ordered(text, "Left", "Right"); CheckImage(pdf); }
        else
        {
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Equal(400, pdf.GetPage(1).Width, 3);
            Assert.Equal(500, pdf.GetPage(2).Width, 3);
            Assert.Contains("First header", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("Second footer", pdf.GetPage(2).Text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("<p style='position:fixed'>Safe</p>")]
    [InlineData("<script>unsafe()</script><p>Safe</p>")]
    [InlineData("<p>Safe<img src='https://example.invalid/must-not-fetch.png'></p>")]
    public void HtmlLossesRetainSourceDiagnosticsAndStrictOutputSafety(string html)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        var converter = new WordConverter();
        using var input = new MemoryStream(bytes);
        DocumentInspection inspection = converter.Inspect(input, DocumentFormat.Html, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspection.WouldRejectStrict);
        input.Position = 0;
        using var output = Sentinel();
        Assert.Throws<NotSupportedException>(() => converter.Convert(input, output, DocumentFormat.Html, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0;
        using var permissive = new MemoryStream();
        ConversionResult result = converter.Convert(input, permissive, DocumentFormat.Html, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Equal(inspection.Diagnostics.Select(d => d.Code), result.Diagnostics.Select(d => d.Code));
        Assert.All(result.Diagnostics, d => Assert.NotEmpty(d.Occurrences));
        using PdfDocument pdf = PdfDocument.Open(permissive.ToArray());
        Assert.Contains("Safe", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Html)]
    [InlineData(DocumentFormat.Odt)]
    public async Task DirectPathsPreservePositionsOwnershipAndFailureAtomicity(DocumentFormat format)
    {
        byte[] bytes = format == DocumentFormat.Html ? Encoding.UTF8.GetBytes("<p>Direct text</p>") : Odt("text");
        using var input = new MemoryStream();
        input.WriteByte(0); input.Write(bytes, 0, bytes.Length); input.Position = 1;
        using var output = Sentinel();
        var converter = new WordConverter();
        await converter.ConvertAsync(input, output, format, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.True(output.CanWrite); Assert.Equal(input.Length, input.Position);
        Assert.Equal(42, output.ToArray()[0]);
        using (PdfDocument pdf = PdfDocument.Open(output.ToArray().Skip(1).ToArray())) Assert.True(pdf.GetPage(1).Letters.Count > 0);
        foreach (ConversionOptions options in new[] {
            new ConversionOptions(FidelityMode.BestEffort, maximumInputBytes: 4), new ConversionOptions(FidelityMode.BestEffort, maximumOutputBytes: 4),
            new ConversionOptions(FidelityMode.BestEffort, maximumCharacters: 2) })
        {
            input.Position = 1; using var sentinel = Sentinel();
            await Assert.ThrowsAsync<InvalidDataException>(() => converter.ConvertAsync(input, sentinel, format, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, sentinel.ToArray()); Assert.True(input.CanRead);
        }
        input.Position = 1; using var cancelledOutput = Sentinel();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => converter.ConvertAsync(input, cancelledOutput, format, DocumentFormat.Pdf, new ConversionOptions(), cancellation.Token));
        Assert.Equal(new byte[] { 42 }, cancelledOutput.ToArray()); Assert.Equal(1, input.Position);
    }

    [Fact]
    public void MalformedOdtAndPageLimitFailBeforeCopyingOutput()
    {
        var converter = new WordConverter();
        using var malformed = new MemoryStream(new byte[] { 1, 2, 3 }); using var output = Sentinel();
        Assert.Throws<InvalidDataException>(() => converter.Convert(malformed, output, DocumentFormat.Odt, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        using var masters = new MemoryStream(Odt("masters"));
        Assert.Throws<InvalidDataException>(() => converter.Convert(masters, output, DocumentFormat.Odt, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort, maximumPages: 1), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private static byte[] Export(byte[] bytes, DocumentFormat format)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, format, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        if (format == DocumentFormat.Odt) Assert.Contains(result.Diagnostics, d => d.Code == "Odt.StyleIdentityFlattened");
        return output.ToArray();
    }

    private static MemoryStream Sentinel() { var stream = new MemoryStream(); stream.WriteByte(42); return stream; }

    private static void Ordered(string text, params string[] fragments)
    {
        int offset = 0;
        foreach (string fragment in fragments)
        {
            int index = text.IndexOf(fragment, offset, StringComparison.Ordinal);
            Assert.True(index >= offset, "Missing or reordered fragment: " + fragment);
            offset = index + fragment.Length;
        }
    }

    private static void CheckImage(PdfDocument pdf)
    {
        var image = Assert.Single(pdf.GetPages().SelectMany(p => p.GetImages()));
        Assert.Equal(60, image.BoundingBox.Width, 3); Assert.Equal(30, image.BoundingBox.Height, 3);
        Assert.Equal(80, image.WidthInSamples); Assert.Equal(40, image.HeightInSamples);
        Assert.True(image.TryGetPng(out byte[]? png));
        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.True(bitmap.GetPixel(10, 20).Red > 180); Assert.True(bitmap.GetPixel(60, 20).Blue > 180);
    }
}
