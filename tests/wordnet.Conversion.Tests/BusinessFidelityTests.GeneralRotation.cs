using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(135)]
    [InlineData(225)]
    [InlineData(-45)]
    public void GeneralAngleBoxRetainsEditableRotationAndVectorFrame(int degrees)
    {
        byte[] input = RotatedBox(degrees); byte[] saved = Export(input, DocumentFormat.Docx); Schema(saved);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Assert.Equal(degrees * 60000, (int?)Part(saved, "word/document.xml").Descendants(a + "xfrm").Single().Attribute("rot"));
        using PdfDocument pdf = PdfDocument.Open(ExportRotated(input, DocumentFormat.Pdf));
        var page = pdf.GetPage(1); var letters = page.Letters.ToArray(); Assert.Equal("Rotate", string.Concat(letters.Select(letter => letter.Value)));
        double dx = letters[letters.Length - 1].StartBaseLine.X - letters[0].StartBaseLine.X;
        double dy = letters[letters.Length - 1].StartBaseLine.Y - letters[0].StartBaseLine.Y;
        double actualAngle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360, expectedAngle = (degrees % 360 + 360) % 360;
        Assert.InRange(Math.Abs(actualAngle - expectedAngle), 0, .02);
        double radians = degrees * Math.PI / 180, width = Math.Abs(Math.Cos(radians)) * 140 + Math.Abs(Math.Sin(radians)) * 80;
        double height = Math.Abs(Math.Sin(radians)) * 140 + Math.Abs(Math.Cos(radians)) * 80;
        var bounds = Assert.Single(page.Paths).GetBoundingRectangle()!.Value;
        Assert.InRange(Math.Abs(bounds.Left - (170 - width / 2)), 0, .02);
        Assert.InRange(Math.Abs(bounds.Bottom - (300 - (140 + height / 2))), 0, .02);
        Assert.InRange(Math.Abs(bounds.Width - width), 0, .02); Assert.InRange(Math.Abs(bounds.Height - height), 0, .02);
        XDocument xps = Part(ExportRotated(input, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        XElement path = Assert.Single(xps.Descendants(), e => e.Name.LocalName == "Path" && (string?)e.Attribute("Fill") == "#FF00FFFF");
        Assert.NotNull(path.Attribute("RenderTransform"));
        using var source = new MemoryStream(input); PagePreview preview = Assert.Single(new WordConverter().RenderPages(source, DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.BestEffort), cancellationToken: TestContext.Current.CancellationToken).Pages);
        Assert.Contains("rotate(" + expectedAngle.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 170 140)", Encoding.UTF8.GetString(preview.GetBytes()), StringComparison.Ordinal);
    }

    [Fact]
    public void FractionalDrawingAngleSurvivesEditableOutput()
    {
        byte[] source = SetRotation(RotatedBox(45), 2700001); byte[] saved = Export(source, DocumentFormat.Docx); Schema(saved);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Assert.Equal(2700001, (int?)Part(saved, "word/document.xml").Descendants(a + "xfrm").Single().Attribute("rot"));
        using PdfDocument pdf = PdfDocument.Open(ExportRotated(source, DocumentFormat.Pdf)); Assert.Equal("Rotate", pdf.GetPage(1).Text);
    }

    [Fact]
    public void GeneralAngleBoxKeepsItsInlineImageAndVectorFrame()
    {
        string image = FidelityDocuments.ImageParagraph(508000, 254000).Replace(" descr='Red and blue grid'", "").Replace("id='1' name='Color grid'", "id='2' name='Color grid'");
        byte[] source = RotatedBox(45, boxContent: image, imageName: "color-grid.png"); byte[] saved = Export(source, DocumentFormat.Docx); Schema(saved);
        using (var package = new ZipArchive(new MemoryStream(saved))) Assert.Contains(package.Entries, entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        using (PdfDocument pdf = PdfDocument.Open(ExportRotated(source, DocumentFormat.Pdf)))
        { Assert.Single(pdf.GetPage(1).GetImages()); Assert.Single(pdf.GetPage(1).Paths, path => path.IsFilled && path.FillColor != null); }
        XDocument xps = Part(ExportRotated(source, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
        XElement brush = Assert.Single(xps.Descendants(), element => element.Name.LocalName == "ImageBrush");
        Assert.Contains(brush.Ancestors(), parent => parent.Name.LocalName == "Canvas" && parent.Attribute("RenderTransform") != null);
        using var input = new MemoryStream(source); PagePreview preview = Assert.Single(new WordConverter().RenderPages(input, DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.BestEffort), cancellationToken: TestContext.Current.CancellationToken).Pages);
        XNamespace svg = "http://www.w3.org/2000/svg"; XElement document = XElement.Parse(Encoding.UTF8.GetString(preview.GetBytes()));
        XElement embedded = Assert.Single(document.Descendants(svg + "image"));
        Assert.Contains(embedded.Ancestors(svg + "g"), group => (string?)group.Attribute("transform") == "rotate(45 170 140)");
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(90, false)]
    public void RotatedLinksUseTransformedHitBoundsAndDiagnoseNonCardinalBounds(int degrees, bool diagnosed)
    {
        const string contents = "<w:p><w:hyperlink r:id='rotlink'><w:r><w:t>Linked</w:t></w:r></w:hyperlink></w:p>";
        byte[] source = WithLink(RotatedBox(degrees, boxContent: contents));
        using var strictInput = new MemoryStream(source); using var untouched = new MemoryStream(); untouched.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, untouched, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.RotationReference:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, untouched.ToArray());
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.RotationReference");
        Assert.Equal(diagnosed, result.Diagnostics.Any(diagnostic => diagnostic.Code == "Shape.RotatedLinkBounds"));
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1); var link = Assert.Single(page.GetHyperlinks());
        Assert.Equal("https://example.test/callout", link.Uri);
        foreach (var letter in page.Letters)
        {
            Assert.InRange(letter.StartBaseLine.X, link.Bounds.Left - .02, link.Bounds.Right + .02);
            Assert.InRange(letter.StartBaseLine.Y, link.Bounds.Bottom - .02, link.Bounds.Top + .02);
        }
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(90, false)]
    public void RotatedLinkedImageKeepsItsPixelsAndTransformedAnnotation(int degrees, bool diagnosed)
    {
        string image = FidelityDocuments.ImageParagraph(508000, 254000).Replace(" descr='Red and blue grid'", "")
            .Replace("id='1' name='Color grid'", "id='2' name='Color grid'")
            .Replace("<w:p>", "<w:p><w:hyperlink r:id='rotlink'>").Replace("</w:p>", "</w:hyperlink></w:p>");
        byte[] source = WithLink(RotatedBox(degrees, boxContent: image, imageName: "color-grid.png"));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.RotationReference");
        Assert.Equal(diagnosed, result.Diagnostics.Any(diagnostic => diagnostic.Code == "Shape.RotatedLinkBounds"));
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
        var actualImage = Assert.Single(page.GetImages()); var annotation = Assert.Single(page.GetAnnotations());
        var box = actualImage.BoundingBox; var corners = new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight };
        Assert.InRange(Math.Abs(corners.Min(p => p.X) - annotation.Rectangle.Left), 0, .02);
        Assert.InRange(Math.Abs(corners.Max(p => p.X) - annotation.Rectangle.Right), 0, .02);
        Assert.InRange(Math.Abs(corners.Min(p => p.Y) - annotation.Rectangle.Bottom), 0, .02);
        Assert.InRange(Math.Abs(corners.Max(p => p.Y) - annotation.Rectangle.Top), 0, .02);
    }

    [Fact]
    public void GeneralRotationDiagnosesReferenceDisagreementAndPreservesOutputOnStrictRejection()
    {
        byte[] source = RotatedBox(30); using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.RotationReference:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        using var bestEffortInput = new MemoryStream(source); using var bestEffortOutput = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(bestEffortInput, bestEffortOutput, DocumentFormat.Docx, DocumentFormat.Xps, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.RotationReference");
    }

    [Fact]
    public void UprightTextInRotatedBoxIsDiagnosedAndInvalidRotationIsRejected()
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        byte[] upright = EditDrawing(RotatedBox(45), xml => xml.Descendants(wps + "bodyPr").Single().SetAttributeValue("upright", "1"));
        using var source = new MemoryStream(upright); using var destination = new MemoryStream(); destination.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.TextBoxLayout:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, destination.ToArray());
        using var bestEffortSource = new MemoryStream(upright); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(bestEffortSource, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.TextBoxLayout");
        byte[] malformed = EditDrawing(RotatedBox(45), xml => xml.Descendants(a + "xfrm").Single().SetAttributeValue("rot", "invalid"));
        Assert.Throws<InvalidDataException>(() => ExportRotated(malformed, DocumentFormat.Pdf));
    }

    [Fact]
    public void FlippedBoxIsDiagnosedInsteadOfSilentlyLosingTheFlip()
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        byte[] source = EditDrawing(RotatedBox(45), xml => xml.Descendants(a + "xfrm").Single().SetAttributeValue("flipH", "1"));
        using var input = new MemoryStream(source); using var destination = new MemoryStream(); destination.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.GeometryApproximated:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, destination.ToArray());
        using var bestEffortInput = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(bestEffortInput, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Shape.GeometryApproximated");
    }

    [Fact]
    public void GeneralRotationAdmitsTextAutofitButRejectsFittedInlineImages()
    {
        string text = string.Join(" ", Enumerable.Repeat("sample", 24));
        using PdfDocument pdf = PdfDocument.Open(ExportRotated(RotatedBox(45, text, true), DocumentFormat.Pdf));
        Assert.Equal(text.Replace(" ", ""), string.Concat(pdf.GetPage(1).Letters.Select(letter => letter.Value)).Replace(" ", ""));
        Assert.Contains(pdf.GetPage(1).Letters, letter => letter.FontSize < 10.9);
        string image = FidelityDocuments.ImageParagraph(508000, 254000).Replace(" descr='Red and blue grid'", "").Replace("id='1' name='Color grid'", "id='2' name='Color grid'");
        var error = Assert.Throws<NotSupportedException>(() => ExportRotated(RotatedBox(45, autofit: true, boxContent: image, imageName: "color-grid.png"), DocumentFormat.Pdf));
        Assert.StartsWith("Shape.TextBoxContent:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledGeneralRotationDoesNotPublishOutput()
    {
        using var input = new MemoryStream(RotatedBox(45)); using var destination = new MemoryStream(); destination.WriteByte(42);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new WordConverter().Convert(input, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), cancellation.Token));
        Assert.Equal(new byte[] { 42 }, destination.ToArray());
    }

    private static byte[] ExportRotated(byte[] source, DocumentFormat format)
    {
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Docx, format, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static byte[] SetRotation(byte[] source, int raw)
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        return EditDrawing(source, xml => xml.Descendants(a + "xfrm").Single().SetAttributeValue("rot", raw));
    }

    private static byte[] EditDrawing(byte[] source, Action<XDocument> edit)
    {
        using var bytes = new MemoryStream(); bytes.Write(source, 0, source.Length);
        using (var package = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = package.GetEntry("word/document.xml")!; XDocument xml;
            using (Stream read = entry.Open()) xml = XDocument.Load(read);
            edit(xml);
            entry.Delete(); using Stream write = package.CreateEntry("word/document.xml").Open(); xml.Save(write);
        }
        return bytes.ToArray();
    }

    private static byte[] WithLink(byte[] source)
    {
        using var bytes = new MemoryStream(); bytes.Write(source, 0, source.Length);
        using (var package = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            ZipArchiveEntry? entry = package.GetEntry("word/_rels/document.xml.rels"); XDocument xml;
            if (entry == null) xml = new XDocument(new XElement(rel + "Relationships"));
            else { using Stream read = entry.Open(); xml = XDocument.Load(read); }
            xml.Root!.Add(new XElement(rel + "Relationship", new XAttribute("Id", "rotlink"), new XAttribute("Type", TestDocuments.R + "/hyperlink"),
                new XAttribute("Target", "https://example.test/callout"), new XAttribute("TargetMode", "External")));
            entry?.Delete(); using Stream write = package.CreateEntry("word/_rels/document.xml.rels").Open(); xml.Save(write);
        }
        return bytes.ToArray();
    }
}
