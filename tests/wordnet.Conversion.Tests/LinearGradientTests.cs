using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class LinearGradientTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace S = "http://www.w3.org/2000/svg";
    private static readonly XNamespace X = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly string[] Positions = { "20000", "50000", "80000" };
    private static readonly string[] Colors = { "FF0000", "00FF00", "0000FF" };
    private static readonly string[] Coordinates = { "x1", "y1", "x2", "y2" };
    private static readonly string[] Offsets = { "0.2", "0.5", "0.8" };
    private const string Stops = "<a:gs pos='20000'><a:srgbClr val='FF0000'/></a:gs><a:gs pos='50000'><a:srgbClr val='00FF00'/></a:gs><a:gs pos='80000'><a:srgbClr val='0000FF'/></a:gs>";

    private static byte[] Source(string stops = Stops, string angle = "0", string geometry = "rect", string extras = "")
    {
        byte[] input = PresetVectorShapeTests.Source(geometry); using var bytes = new MemoryStream(); bytes.Write(input, 0, input.Length);
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry old = zip.GetEntry("word/document.xml")!; XDocument document;
            using (Stream read = old.Open()) document = XDocument.Load(read);
            XElement fill = document.Descendants(A + "solidFill").First();
            fill.ReplaceWith(XElement.Parse("<a:gradFill xmlns:a='" + A + "'><a:gsLst>" + stops + "</a:gsLst><a:lin ang='" + angle + "' scaled='1'/>" + extras + "</a:gradFill>"));
            old.Delete(); using Stream write = zip.CreateEntry("word/document.xml").Open(); document.Save(write);
        }
        return bytes.ToArray();
    }

    [Theory]
    [InlineData("0", "100", "140", "240", "140")]
    [InlineData("5400000", "170", "100", "170", "180")]
    [InlineData("10800000", "240", "140", "100", "140")]
    [InlineData("16200000", "170", "180", "170", "100")]
    public void CardinalGradientsRetainStopsAndRenderVectorPaint(string angle, string x1, string y1, string x2, string y2)
    {
        byte[] sourceBytes = Source(angle: angle); using var source = new MemoryStream(sourceBytes);
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        using (var word = WordprocessingDocument.Open(new MemoryStream(saved.ToArray()), false))
            Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken));
        using (var zip = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read))
        {
            XDocument xml = XDocument.Load(zip.GetEntry("word/document.xml")!.Open());
            XElement gradient = Assert.Single(xml.Descendants(A + "gradFill"));
            Assert.Equal(angle, (string?)gradient.Element(A + "lin")!.Attribute("ang"));
            Assert.Equal(Positions, gradient.Descendants(A + "gs").Select(stop => (string?)stop.Attribute("pos")));
            Assert.Equal(Colors, gradient.Descendants(A + "srgbClr").Select(color => (string?)color.Attribute("val")));
        }
        var options = new ConversionOptions(); options.Pdf.Compress = false;
        byte[] pdfBytes = new WordConverter().ConvertToPdf(sourceBytes, options);
        using (PdfDocument pdf = PdfDocument.Open(pdfBytes)) { Assert.Equal(1, pdf.NumberOfPages); Assert.Equal(0, pdf.GetPage(1).NumberOfImages); }
        string pdfText = Encoding.ASCII.GetString(pdfBytes); Assert.Contains("/ShadingType 2", pdfText, StringComparison.Ordinal); Assert.Contains("/WNGrad0", pdfText, StringComparison.Ordinal);
        TypographyTests.Save("gradient-" + angle + ".pdf", pdfBytes); TypographyTests.Save("gradient-" + angle + ".docx", saved.ToArray());
        using var previewSource = new MemoryStream(sourceBytes);
        var preview = Assert.Single(new WordConverter().RenderPages(previewSource, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(preview.GetBytes())); XElement svgGradient = Assert.Single(svg.Descendants(S + "linearGradient"));
        Assert.Equal(new[] { x1, y1, x2, y2 }, Coordinates.Select(key => (string?)svgGradient.Attribute(key)));
        Assert.Equal(3, svgGradient.Elements(S + "stop").Count());
        using var input = new MemoryStream(sourceBytes); using var xps = new MemoryStream();
        new WordConverter().Convert(input, xps, DocumentFormat.Docx, DocumentFormat.Xps, new ConversionOptions(), TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(new MemoryStream(xps.ToArray()), ZipArchiveMode.Read);
        XDocument page = XDocument.Load(archive.Entries.Single(entry => entry.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open());
        XElement brush = Assert.Single(page.Descendants(X + "LinearGradientBrush")); Assert.Equal("Absolute", (string?)brush.Attribute("MappingMode"));
        Assert.Equal(Offsets, brush.Element(X + "LinearGradientBrush.GradientStops")!.Elements(X + "GradientStop").Select(stop => (string?)stop.Attribute("Offset")));
        TypographyTests.Save("gradient-" + angle + ".xps", xps.ToArray()); TypographyTests.Save("gradient-" + angle + ".svg", preview.GetBytes());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unordered")]
    [InlineData("alpha")]
    [InlineData("theme")]
    [InlineData("angle")]
    [InlineData("path")]
    [InlineData("too-few")]
    [InlineData("too-many")]
    [InlineData("invalid-number")]
    public void UnsupportedOrMalformedGradientsFailBeforeOutput(string kind)
    {
        string stops = kind switch
        {
            "duplicate" => Stops.Replace("50000", "20000"),
            "unordered" => Stops.Replace("50000", "10000"),
            "alpha" => Stops.Replace("val='FF0000'/>", "val='FF0000'><a:alpha val='50000'/></a:srgbClr>"),
            "theme" => Stops.Replace("<a:srgbClr val='FF0000'/>", "<a:schemeClr val='phClr'/>"),
            "too-few" => "<a:gs pos='0'><a:srgbClr val='FF0000'/></a:gs>",
            "too-many" => string.Concat(Enumerable.Repeat("<a:gs pos='0'><a:srgbClr val='FF0000'/></a:gs>", 9)),
            "invalid-number" => Stops.Replace("20000", "NaN"),
            _ => Stops
        };
        byte[] data = Source(stops, kind == "angle" ? "2700000" : "0", extras: kind == "path" ? "<a:path path='circle'/>" : "");
        using var input = new MemoryStream(data); using var output = new MemoryStream(); output.WriteByte(42);
        Exception error = Assert.ThrowsAny<Exception>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.True(error is NotSupportedException or InvalidDataException); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void LocalThemeStopRemainsEditableAndRendersTheResolvedColor()
    {
        string stops = Stops.Replace("<a:srgbClr val='FF0000'/>", "<a:schemeClr val='accent1'/>");
        byte[] plain = Source(stops); string paragraph;
        using (var package = new ZipArchive(new MemoryStream(plain)))
        {
            XDocument xml = XDocument.Load(package.GetEntry("word/document.xml")!.Open());
            paragraph = xml.Descendants(W + "p").Single(p => p.Descendants(A + "gradFill").Any()).ToString(SaveOptions.DisableFormatting);
        }
        byte[] themed = DocxThemeTests.Package(paragraph); var options = new ConversionOptions(FidelityMode.BestEffort);
        using var input = new MemoryStream(themed); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        using (var package = new ZipArchive(new MemoryStream(saved.ToArray())))
        {
            XDocument xml = XDocument.Load(package.GetEntry("word/document.xml")!.Open());
            Assert.Equal("accent1", (string?)xml.Descendants(A + "gs").First().Element(A + "schemeClr")!.Attribute("val"));
            Assert.NotNull(package.GetEntry("word/theme/theme1.xml"));
        }
        using var previewInput = new MemoryStream(themed);
        PagePreview preview = Assert.Single(new WordConverter().RenderPages(previewInput, DocumentFormat.Docx, options: options, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(preview.GetBytes()));
        Assert.Equal("#2266CC", (string?)svg.Descendants(S + "stop").First().Attribute("stop-color"));
        using var missing = new MemoryStream(plain); using var untouched = new MemoryStream(); untouched.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(missing, untouched, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, untouched.ToArray());
    }

    [Fact]
    public void RemappedThemeStopIsEditableButItsReferenceDifferenceIsDiagnosed()
    {
        string stops = Stops.Replace("<a:srgbClr val='FF0000'/>", "<a:schemeClr val='accent1'/>");
        byte[] plain = Source(stops); string paragraph;
        using (var package = new ZipArchive(new MemoryStream(plain)))
        {
            XDocument xml = XDocument.Load(package.GetEntry("word/document.xml")!.Open());
            paragraph = xml.Descendants(W + "p").Single(p => p.Descendants(A + "gradFill").Any()).ToString(SaveOptions.DisableFormatting);
        }
        string settings = "<w:settings xmlns:w='" + W + "'><w:clrSchemeMapping w:accent1='accent2'/></w:settings>";
        byte[] source = DocxThemeTests.Package(paragraph, settings: settings);
        using (var input = new MemoryStream(source)) using (var output = new MemoryStream())
        {
            output.WriteByte(42);
            var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
            Assert.StartsWith("Shape.GradientThemeMapping:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        var options = new ConversionOptions(FidelityMode.BestEffort);
        using (var input = new MemoryStream(source)) using (var output = new MemoryStream())
        {
            ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.GradientThemeMapping");
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(1, pdf.NumberOfPages);
        }
        using var editableInput = new MemoryStream(source); WordDocument document = WordDocument.Load(editableInput, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray())); XDocument savedSettings = XDocument.Load(archive.GetEntry("word/settings.xml")!.Open());
        Assert.Equal("accent2", (string?)savedSettings.Descendants(W + "clrSchemeMapping").Single().Attribute(W + "accent1"));
    }

    [Theory]
    [InlineData("ellipse")]
    [InlineData("roundRect")]
    [InlineData("triangle")]
    [InlineData("diamond")]
    public void GradientPaintIsClippedToTheAdmittedVectorGeometry(string geometry)
    {
        byte[] source = Source(geometry: geometry); var options = new ConversionOptions(); options.Pdf.Compress = false;
        byte[] bytes = new WordConverter().ConvertToPdf(source, options);
        using PdfDocument pdf = PdfDocument.Open(bytes); Assert.Equal(0, pdf.GetPage(1).NumberOfImages);
        Assert.Contains("/ShadingType 2", Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);
        using var previewInput = new MemoryStream(source);
        PagePreview preview = Assert.Single(new WordConverter().RenderPages(previewInput, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(preview.GetBytes()));
        Assert.Single(svg.Descendants(S + "path"), p => ((string?)p.Attribute("fill"))?.StartsWith("url(#gradient", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TwoEndpointStopsUseAValidSimpleShadingFunction()
    {
        const string stops = "<a:gs pos='0'><a:srgbClr val='FF0000'/></a:gs><a:gs pos='100000'><a:srgbClr val='0000FF'/></a:gs>";
        var options = new ConversionOptions(); options.Pdf.Compress = false;
        byte[] bytes = new WordConverter().ConvertToPdf(Source(stops), options);
        using PdfDocument pdf = PdfDocument.Open(bytes); Assert.Equal(1, pdf.NumberOfPages);
        string text = Encoding.ASCII.GetString(bytes); Assert.Contains("/FunctionType 2", text, StringComparison.Ordinal); Assert.DoesNotContain("/FunctionType 3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SeparatePagesAndPageSelectionKeepTheirGradientResources()
    {
        byte[] source = Source(); using var data = new MemoryStream(); data.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(data, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.GetEntry("word/document.xml")!; XDocument xml;
            using (Stream read = entry.Open()) xml = XDocument.Load(read);
            XElement paragraph = xml.Descendants(W + "p").Single(p => p.Descendants(A + "gradFill").Any());
            var second = new XElement(paragraph); XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            second.Descendants(wp + "docPr").Single().SetAttributeValue("id", 2);
            paragraph.AddAfterSelf(new XElement(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page")))), second);
            entry.Delete(); using Stream write = zip.CreateEntry("word/document.xml").Open(); xml.Save(write);
        }
        var options = new ConversionOptions(); options.Pdf.Compress = false;
        byte[] both = new WordConverter().ConvertToPdf(data.ToArray(), options);
        using (PdfDocument pdf = PdfDocument.Open(both)) Assert.Equal(2, pdf.NumberOfPages);
        string content = Encoding.ASCII.GetString(both); Assert.Contains("/WNGrad0", content, StringComparison.Ordinal); Assert.Contains("/WNGrad1", content, StringComparison.Ordinal);
        options.Pdf.Pages.Add(new PageRange(2, 2)); byte[] selected = new WordConverter().ConvertToPdf(data.ToArray(), options);
        using PdfDocument one = PdfDocument.Open(selected); Assert.Equal(1, one.NumberOfPages);
        Assert.Contains("/WNGrad0", Encoding.ASCII.GetString(selected), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("tagged")]
    [InlineData("native")]
    public void UnqualifiedOutputProfilesRejectBeforeCopy(string profile)
    {
        var options = new ConversionOptions(FidelityMode.BestEffort);
        if (profile == "archive") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (profile == "tagged") options.Pdf.Accessibility = PdfAccessibility.Tagged;
        if (profile == "native") options.Typography.Enabled = true;
        using var input = new MemoryStream(Source()); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void AggregateStopLimitAppliesEvenInLenientMode()
    {
        string stops = string.Concat(Enumerable.Range(0, 8).Select(i => "<a:gs pos='" + (i * 10000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "'><a:srgbClr val='FF0000'/></a:gs>"));
        byte[] initial = Source(stops); using var memory = new MemoryStream(); memory.Write(initial, 0, initial.Length);
        using (var package = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = package.GetEntry("word/document.xml")!; XDocument xml;
            using (Stream read = entry.Open()) xml = XDocument.Load(read);
            XElement paragraph = xml.Descendants(W + "p").Single(p => p.Descendants(A + "gradFill").Any());
            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            for (int i = 0; i < 512; i++)
            {
                var copy = new XElement(paragraph); copy.Descendants(wp + "docPr").Single().SetAttributeValue("id", i + 2); paragraph.AddAfterSelf(copy);
            }
            entry.Delete(); using Stream write = package.CreateEntry("word/document.xml").Open(); xml.Save(write);
        }
        using var input = new MemoryStream(memory.ToArray()); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.GradientPaint:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void OutputLimitAndCancellationLeaveCallerOutputUnchanged()
    {
        byte[] bytes = Source();
        using (var input = new MemoryStream(bytes)) using (var output = new MemoryStream())
        {
            output.WriteByte(42); var limited = new ConversionOptions(maximumOutputBytes: 1024);
            Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, limited, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var cancelledInput = new MemoryStream(bytes); using var untouched = new MemoryStream(); untouched.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(cancelledInput, untouched, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), cancellation.Token));
        Assert.Equal(new byte[] { 42 }, untouched.ToArray());
    }
}
