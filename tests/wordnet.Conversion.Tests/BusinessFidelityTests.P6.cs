using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static byte[] MixedQualificationDocument(string text)
    {
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XElement vector = Part(VectorShape("ellipse"), "word/document.xml").Descendants(W + "p")
            .Single(p => p.Descendants(wp + "anchor").Any());
        XElement box = Part(RotatedBox(90, "Box"), "word/document.xml").Descendants(W + "p")
            .Single(p => p.Descendants(wp + "anchor").Any());
        box.Descendants(wp + "positionH").Single().Element(wp + "posOffset")!.Value = "3175000";
        box.Descendants(wp + "docPr").Single().SetAttributeValue("id", "2");
        return Docx(P(text) + "<w:p>" + Field("PAGE", "1") + "</w:p>"
            + vector.ToString(SaveOptions.DisableFormatting) + box.ToString(SaveOptions.DisableFormatting) + Page);
    }

    [Fact]
    public void MixedFieldsReviewAndDrawingsKeepBothViewsAndVectorOutput()
    {
        WordDocument old = WordDocument.Load(new MemoryStream(MixedQualificationDocument("Original")), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument revised = WordDocument.Load(new MemoryStream(MixedQualificationDocument("Revised")), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        var attribution = new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
        WordDocument result = DocumentComparison.Compare(old, revised, attribution, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var sample in new[] { (Decision: ReviewDecision.Accept, Text: "Revised"), (Decision: ReviewDecision.Reject, Text: "Original") })
        {
            WordDocument view = result.CreateReviewView(sample.Decision, cancellationToken: TestContext.Current.CancellationToken);
            using var docx = new MemoryStream(); view.Save(docx, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(docx.ToArray());
            using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray());
            Assert.Contains(sample.Text, pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("1", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("Box", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains(pdf.GetPage(1).Paths, path => path.GetBoundingRectangle() != null);
        }
    }

    [Fact]
    public void MixedDrawingsRejectUnqualifiedArchiveOutputAtomically()
    {
        using var input = new MemoryStream(MixedQualificationDocument("Content"));
        using var output = new MemoryStream();
        var options = new ConversionOptions(); options.Pdf.Conformance = PdfConformance.PdfA2b;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }
}
