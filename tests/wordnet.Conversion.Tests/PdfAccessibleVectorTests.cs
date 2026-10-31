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

public sealed class PdfAccessibleVectorTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Description = "Blue trend diagram";

    internal static byte[] Source(string description = Description, string change = "", string geometry = "triangle")
    {
        byte[] input = PresetVectorShapeTests.Source(geometry, description: description);
        using var original = new ZipArchive(new MemoryStream(input), ZipArchiveMode.Read); using var result = new MemoryStream();
        using (var output = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                using Stream from = entry.Open(); using Stream to = output.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "word/document.xml") { from.CopyTo(to); continue; }
                XDocument xml = XDocument.Load(from); XElement anchor = xml.Descendants(Wp + "anchor").Single();
                XElement position = anchor.Element(Wp + "positionV")!; position.SetAttributeValue("relativeFrom", "paragraph"); position.Element(Wp + "posOffset")!.Value = "0";
                anchor.Element(Wp + "wrapNone")!.ReplaceWith(new XElement(Wp + "wrapTopAndBottom"));
                if (change == "wrap") anchor.Element(Wp + "wrapTopAndBottom")!.ReplaceWith(new XElement(Wp + "wrapNone"));
                if (change == "behind") anchor.SetAttributeValue("behindDoc", "1");
                if (change == "hidden") anchor.Element(Wp + "docPr")!.SetAttributeValue("hidden", "1");
                if (change == "title") anchor.Element(Wp + "docPr")!.SetAttributeValue("title", "Separate title");
                if (change == "mixed") anchor.Ancestors(W + "p").Single().AddFirst(new XElement(W + "r", new XElement(W + "t", "Mixed")));
                if (change == "page") xml.Descendants(W + "p").First().Add(new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page"))));
                if (change == "stroke")
                {
                    XElement line = anchor.Descendants(A + "ln").Single(); line.SetAttributeValue("w", "25400");
                    line.ReplaceNodes(new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", "FF0000"))));
                }
                if (change == "two")
                {
                    XElement paragraph = anchor.Ancestors(W + "p").Single(); var second = new XElement(paragraph);
                    XElement properties = second.Descendants(Wp + "docPr").Single(); properties.SetAttributeValue("id", "2"); properties.SetAttributeValue("descr", "Second blue diagram");
                    paragraph.AddAfterSelf(second);
                }
                if (change == "box")
                {
                    string box = PdfAccessibleTextBoxTests.Box(TestDocuments.TextParagraph("Box text")).Replace("id='1' name='Box'", "id='2' name='Box'");
                    XElement wrapper = XElement.Parse("<root xmlns:w='" + TestDocuments.W + "'>" + box + "</root>");
                    anchor.Ancestors(W + "p").Single().AddAfterSelf(wrapper.Elements().Single());
                }
                xml.Save(to);
            }
        return result.ToArray();
    }

    private static ConversionOptions Options(PdfVectorSemantics semantics = PdfVectorSemantics.DescribedAtAnchor)
    {
        var options = new ConversionOptions(); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en";
        options.Pdf.VectorSemantics = semantics; options.Pdf.Compress = false; return options;
    }

    private static byte[] Export(byte[] source, ConversionOptions options)
    {
        using var input = new MemoryStream(source); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        document.Title = "Synthetic accessible shapes"; using var output = new MemoryStream();
        document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken); return output.ToArray();
    }

    [Fact]
    public void DescribedVectorHasFigureAlternativeAndRealVectorGeometry()
    {
        byte[] source = Source(); using (var word = WordprocessingDocument.Open(new MemoryStream(source), false))
            Assert.Empty(new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken));
        byte[] pdf = Export(source, Options()); string raw = Encoding.ASCII.GetString(pdf);
        Assert.Contains("/Figure", raw, StringComparison.Ordinal); Assert.Contains("/Alt", raw, StringComparison.Ordinal);
        Assert.Contains(string.Concat(Encoding.BigEndianUnicode.GetBytes(Description).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))), raw, StringComparison.OrdinalIgnoreCase);
        using PdfDocument parsed = PdfDocument.Open(pdf); var page = Assert.Single(parsed.GetPages()); Assert.Equal(0, page.NumberOfImages); Assert.NotEmpty(page.Paths);
        Evidence("described", pdf);
    }

    [Fact]
    public void ExplicitDecorativeVectorHasNoFigureAlternative()
    {
        byte[] pdf = Export(Source(""), Options(PdfVectorSemantics.Decorative)); string raw = Encoding.ASCII.GetString(pdf);
        Assert.DoesNotContain("/Figure", raw, StringComparison.Ordinal); Assert.Contains("/Artifact", raw, StringComparison.Ordinal);
        using PdfDocument parsed = PdfDocument.Open(pdf); Assert.NotEmpty(Assert.Single(parsed.GetPages()).Paths);
        Evidence("decorative", pdf);
    }

    [Fact]
    public void UntaggedVectorControlReportsItsLostDescription()
    {
        using var input = new MemoryStream(Source()); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Shape.PresetAlternative");
        Assert.DoesNotContain("/StructTreeRoot", Encoding.ASCII.GetString(output.ToArray()), StringComparison.Ordinal);
        Evidence("negative", output.ToArray());
    }

    [Fact]
    public void SelectedPageRetainsTheShapeOwnerWithoutOmittedText()
    {
        var options = Options(); options.Pdf.Pages.Add(new PageRange(2, 2)); byte[] pdf = Export(Source(change: "page"), options);
        using PdfDocument parsed = PdfDocument.Open(pdf); var page = Assert.Single(parsed.GetPages());
        Assert.DoesNotContain("Before", page.Text, StringComparison.Ordinal); Assert.Contains("After", page.Text, StringComparison.Ordinal);
        Evidence("selection", pdf);
    }

    [Fact]
    public void MultipleFiguresAndSeparateStrokeRemainInSourceOrder()
    {
        byte[] two = Export(Source(change: "two"), Options());
        using (PdfDocument pdf = PdfDocument.Open(two)) Assert.True(Assert.Single(pdf.GetPages()).Paths.Count >= 2);
        byte[] stroke = Export(Source(change: "stroke"), Options());
        using (PdfDocument pdf = PdfDocument.Open(stroke)) Assert.True(Assert.Single(pdf.GetPages()).Paths.Count >= 2);
        Evidence("two", two); Evidence("stroke", stroke);
    }

    [Theory]
    [InlineData(PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData(PdfVectorSemantics.Decorative)]
    public void VectorAndExplicitlyOrderedTextBoxRemainSeparate(PdfVectorSemantics semantics)
    {
        var options = Options(semantics); options.Pdf.TextBoxReadingOrder = PdfTextBoxReadingOrder.AnchorOrder;
        byte[] pdf = Export(Source(semantics == PdfVectorSemantics.Decorative ? "" : Description, change: "box"), options); using PdfDocument parsed = PdfDocument.Open(pdf);
        var page = Assert.Single(parsed.GetPages()); Assert.Contains("Box text", page.Text, StringComparison.Ordinal); Assert.NotEmpty(page.Paths);
        Evidence(semantics == PdfVectorSemantics.Decorative ? "box-decorative" : "box", pdf);
    }

    [Theory]
    [InlineData("line", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("line", PdfVectorSemantics.Decorative)]
    [InlineData("ellipse", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("ellipse", PdfVectorSemantics.Decorative)]
    [InlineData("roundRect", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("roundRect", PdfVectorSemantics.Decorative)]
    [InlineData("rect", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("rect", PdfVectorSemantics.Decorative)]
    [InlineData("triangle", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("triangle", PdfVectorSemantics.Decorative)]
    [InlineData("diamond", PdfVectorSemantics.DescribedAtAnchor)]
    [InlineData("diamond", PdfVectorSemantics.Decorative)]
    public void EachAdmittedVectorKindUsesTheExplicitSemanticPolicy(string geometry, PdfVectorSemantics semantics)
    {
        bool decorative = semantics == PdfVectorSemantics.Decorative;
        byte[] pdf = Export(Source(decorative ? "" : Description, geometry: geometry), Options(semantics));
        using PdfDocument parsed = PdfDocument.Open(pdf); Assert.NotEmpty(Assert.Single(parsed.GetPages()).Paths);
        Evidence(geometry + (decorative ? "-decorative" : "-described"), pdf);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("missing")]
    [InlineData("decorative-description")]
    [InlineData("title")]
    [InlineData("wrap")]
    [InlineData("behind")]
    [InlineData("mixed")]
    [InlineData("archive")]
    [InlineData("native")]
    public void UnqualifiedOwnersAndSemanticsLeaveOutputUntouched(string failure)
    {
        var options = Options(failure == "default" ? PdfVectorSemantics.Reject : failure == "decorative-description" ? PdfVectorSemantics.Decorative : PdfVectorSemantics.DescribedAtAnchor);
        if (failure == "archive") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (failure == "native") { options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; }
        string description = failure is "missing" or "default" ? "" : Description;
        byte[] source = Source(description, failure is "title" or "wrap" or "behind" or "mixed" ? failure : "");
        using var input = new MemoryStream(source); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        document.Title = "Synthetic accessible shapes"; using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        string expected = failure is "missing" or "decorative-description" or "title" ? "Pdf.VectorAlternative:"
            : failure is "wrap" or "behind" or "mixed" ? "Pdf.VectorSemantics:"
            : failure == "native" ? "Typography.Structure:" : "Shape.VectorProfile:";
        Assert.StartsWith(expected, error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void PolicySnapshotInvalidValueAndCancellationAreExplicit()
    {
        var options = Options(); PdfOptions snapshot = options.Pdf.Snapshot(); options.Pdf.VectorSemantics = PdfVectorSemantics.Decorative;
        Assert.Equal(PdfVectorSemantics.DescribedAtAnchor, snapshot.VectorSemantics);
        options.Pdf.VectorSemantics = (PdfVectorSemantics)9;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Pdf.Snapshot());
        using var invalidSource = new MemoryStream(Source("")); using var invalidOutput = new MemoryStream(); invalidOutput.WriteByte(42);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordConverter().Convert(invalidSource, invalidOutput, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, invalidOutput.ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var source = new MemoryStream(Source()); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), cancelled.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void TaggedPolicyDoesNotClaimAccessibleSemanticsForSvgPreview()
    {
        using var source = new MemoryStream(Source());
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().RenderPages(source, DocumentFormat.Docx, options: Options(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.PresetAlternative:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviouslyAdmittedHiddenVectorIsNotRenderedAsAVisibleFigure()
    {
        using var source = new MemoryStream(Source("", "hidden", "ellipse")); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.Vector:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("hidden")]
    [InlineData("adjustment")]
    public void AccessibleOutputNeverDropsUnsupportedVectorsInLenientMode(string kind)
    {
        byte[] bytes = kind == "unknown" ? Source("", geometry: "hexagon") : kind == "hidden" ? Source("", "hidden", "ellipse")
            : PresetVectorShapeTests.Source("triangle", adjustment: "<a:gd name='adj' fmla='val 25000'/>");
        var options = new ConversionOptions(FidelityMode.Lenient); options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        options.Pdf.Language = "en"; options.Pdf.VectorSemantics = PdfVectorSemantics.Decorative;
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith(kind == "unknown" ? "Shape.VectorUnsupported:" : "Shape.Vector:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void EditableSessionCannotOmitAnUnknownVectorInLenientMode()
    {
        using var input = new MemoryStream(Source("", geometry: "hexagon"));
        var error = Assert.Throws<NotSupportedException>(() => WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.VectorUnsupported:", error.Message, StringComparison.Ordinal);
    }

    private static void Evidence(string name, byte[] bytes)
    {
        string? directory = Environment.GetEnvironmentVariable("WORDNET_INTEROP_ARTIFACTS");
        if (directory != null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "accessible-vector-" + name + ".pdf"), bytes); }
    }
}
