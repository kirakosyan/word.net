using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace WordNet.Tests;

// Hand-authored web-editor HTML, independent of the WordNet HTML writer.
public sealed class HtmlInputTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[] ImageExtents = { "508000", "914400" };

    [Fact]
    public void EditorHtmlMapsCssCascadeUnicodeLinksAndEditableLists()
    {
        const string html = "<html><head><title>Editor invoice</title><style>p{color:red;font-size:16px} .lead{color:blue} #name{color:#369 !important} p{font-size:18pt}</style></head>"
            + "<body><h1>Invoice</h1><p id='name' class='lead' style='color:green;font-size:2em'>Café <strong>bold <em>both</em></strong> <a href='https://example.com'>link</a> 😀</p><ol start='3'><li>First<ul><li>Nested</li></ul><li>Second</ol></body></html>";
        byte[] word = Convert(html, out ConversionResult result); Schema(word); XElement root = Part(word).Root!;
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains("Café bold both link 😀", string.Concat(root.Descendants(W + "t").Select(t => t.Value)), StringComparison.Ordinal);
        XElement name = root.Descendants(W + "p").Single(p => InteropText.Contains(p.Value, "Café"));
        Assert.All(name.Descendants(W + "color"), c => Assert.Equal("336699", (string?)c.Attribute(W + "val")));
        Assert.All(name.Descendants(W + "sz"), s => Assert.Equal("48", (string?)s.Attribute(W + "val")));
        Assert.NotNull(name.Descendants(W + "r").Single(r => r.Value == "both").Element(W + "rPr")?.Element(W + "i"));
        Assert.Equal(3, root.Descendants(W + "numPr").Count()); Assert.Single(root.Descendants(W + "hyperlink"));
        using var archive = new ZipArchive(new MemoryStream(word)); Assert.NotNull(archive.GetEntry("word/numbering.xml"));
    }

    [Fact]
    public void NestedMergedTablesRetainEditableStructureAndDimensions()
    {
        const string html = "<table style='width:80%'><tr><th colspan='2'>Header</th></tr><tr><td rowspan='2' style='width:25%;padding-left:1cm;background-color:#eee'><p>Outer</p><table style='width:1in'><tr><td>Inner</td></tr></table></td><td>A</td></tr><tr><td>B</td></tr></table>";
        byte[] output = Convert(html, out _); Schema(output); XElement root = Part(output).Root!;
        Assert.Equal(2, root.Descendants(W + "tbl").Count()); Assert.Single(root.Descendants(W + "tbl"), t => t.Ancestors(W + "tbl").Any());
        Assert.Contains(root.Descendants(W + "tblW"), v => (string?)v.Attribute(W + "type") == "pct" && (string?)v.Attribute(W + "w") == "4000");
        Assert.Contains(root.Descendants(W + "gridSpan"), s => (string?)s.Attribute(W + "val") == "2");
        Assert.Contains(root.Descendants(W + "vMerge"), s => (string?)s.Attribute(W + "val") == "restart");
        Assert.Equal(1, root.Descendants(W + "t").Count(t => t.Value == "Inner"));
        Assert.Contains(root.Descendants(W + "shd"), s => (string?)s.Attribute(W + "fill") == "EEEEEE");
    }

    [Theory]
    [InlineData("<p>A <b> B</b> C</p>", "A B C")]
    [InlineData("<p style='white-space:pre-wrap'> A  <span>B </span> C </p>", " A  B  C ")]
    [InlineData("<p>One<p>Deux &amp; é", "OneDeux & é")]
    public void WhitespaceAndHtml5RecoveryAreExplicit(string html, string expected)
    {
        byte[] output = Convert(html, out _); Schema(output);
        Assert.Equal(expected, string.Concat(Part(output).Descendants(W + "t").Select(t => t.Value)));
    }

    [Fact]
    public void DataAndCallerImagesHaveExactBytesAndPhysicalDimensions()
    {
        byte[] pixels = FidelityDocuments.Fixture("color-grid.png"); var options = new ConversionOptions();
        options.HtmlInput.Images.Add("logo", new TemplateImage(pixels, 40, 20, "Caller logo"));
        string html = "<p><img src='logo'><img alt='Data logo' style='width:96px;height:1in' src='data:image/png;base64," + System.Convert.ToBase64String(pixels) + "'></p>";
        byte[] output = Convert(html, out _, options); Schema(output); XElement root = Part(output).Root!;
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        Assert.Equal(ImageExtents, root.Descendants(wp + "extent").Select(v => (string?)v.Attribute("cx")));
        using var zip = new ZipArchive(new MemoryStream(output));
        Assert.All(zip.Entries.Where(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)), e => { using var stream = e.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); Assert.Equal(pixels, bytes.ToArray()); });
    }

    [Theory]
    [InlineData("<script>fetch('https://example.com')</script><p>Safe</p>")]
    [InlineData("<p onclick='bad()'>Safe</p>")]
    [InlineData("<link rel='stylesheet' href='file:///secret'><p>Safe</p>")]
    [InlineData("<style>p{background-image:url(https://example.com)}</style><p>Safe</p>")]
    [InlineData("<p><a href='javascript:bad()'>Safe</a></p>")]
    [InlineData("<p><img src='file:///secret' alt='Safe'></p>")]
    [InlineData("<style>@import 'https://example.com'; p:hover{color:red}</style><p>Safe</p>")]
    public void UnsupportedActiveAndExternalResourcesRejectStrictAndAreNeverFetched(string html)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(html)); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Html, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        byte[] word = Convert(html, out ConversionResult result, new ConversionOptions(FidelityMode.BestEffort)); Schema(word);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning); Assert.Contains("Safe", Part(word).Root!.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("bad()", Part(word).Root!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlInputSupportsInspectionEditingAndBinaryWord()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<h2>Editable</h2><p>Business text</p>"); using var input = new MemoryStream(bytes);
        var converter = new WordConverter(); Assert.True(converter.CanConvert(DocumentFormat.Html, DocumentFormat.Doc)); Assert.True(converter.CanConvert(DocumentFormat.Html, DocumentFormat.Docx));
        DocumentInspection report = converter.Inspect(input, DocumentFormat.Html, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken); Assert.False(report.WouldRejectStrict);
        input.Position = 0; WordDocument session = WordDocument.Load(input, DocumentFormat.Html, out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Business text", session.Paragraphs[1].Text);
        using var binary = new MemoryStream(); session.Save(binary, DocumentFormat.Doc, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(binary.Length > 512); Assert.True(input.CanRead);
    }

    [Fact]
    public void HtmlLimitsAndCancellationLeaveOutputUntouched()
    {
        string deep = string.Concat(Enumerable.Repeat("<div>", 12)) + "<p>Text</p>" + string.Concat(Enumerable.Repeat("</div>", 12));
        Assert.Throws<InvalidDataException>(() => Convert(deep, out _, new ConversionOptions(maximumNestingDepth: 8)));
        Assert.Throws<InvalidDataException>(() => Convert("<p>Long content</p>", out _, new ConversionOptions(maximumCharacters: 4)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var input = new MemoryStream(Encoding.UTF8.GetBytes("<p>text</p>")); using var output = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(input, output, DocumentFormat.Html, DocumentFormat.Docx, new ConversionOptions(), cancellationToken: cancel.Token)); Assert.Equal(0, output.Length);
    }

    private static byte[] Convert(string html, out ConversionResult result, ConversionOptions? options = null)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(html)); using var output = new MemoryStream();
        result = new WordConverter().Convert(input, output, DocumentFormat.Html, DocumentFormat.Docx, options ?? new ConversionOptions(), TestContext.Current.CancellationToken); return output.ToArray();
    }
    private static XDocument Part(byte[] bytes)
    { using var zip = new ZipArchive(new MemoryStream(bytes)); using var stream = zip.GetEntry("word/document.xml")!.Open(); return XDocument.Load(stream); }
    private static void Schema(byte[] bytes)
    { using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(doc)); }
}
