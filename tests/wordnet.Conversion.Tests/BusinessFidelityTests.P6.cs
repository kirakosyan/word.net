using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private static byte[] MixedQualificationDocument(string text, bool overlap = false, string? body = null)
    {
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XElement vector = Part(VectorShape("ellipse"), "word/document.xml").Descendants(W + "p")
            .Single(p => p.Descendants(wp + "anchor").Any());
        XElement box = Part(RotatedBox(90, "Box"), "word/document.xml").Descendants(W + "p")
            .Single(p => p.Descendants(wp + "anchor").Any());
        box.Descendants(wp + "positionH").Single().Element(wp + "posOffset")!.Value = overlap ? "1270000" : "3175000";
        box.Descendants(wp + "docPr").Single().SetAttributeValue("id", "2");
        int layer = 1;
        foreach (XElement anchor in vector.Descendants(wp + "anchor").Concat(box.Descendants(wp + "anchor")))
        {
            anchor.SetAttributeValue("relativeHeight", layer++); anchor.SetAttributeValue("simplePos", "0");
            anchor.SetAttributeValue("locked", "0"); anchor.SetAttributeValue("layoutInCell", "1"); anchor.SetAttributeValue("allowOverlap", "1");
            anchor.AddFirst(new XElement(wp + "simplePos", new XAttribute("x", "0"), new XAttribute("y", "0")));
        }
        return Docx((body ?? P(text)) + "<w:p>" + Field("PAGE", "9") + "</w:p>"
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
            XDocument xml = Part(docx.ToArray(), "word/document.xml");
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            Assert.Contains(xml.Descendants(a + "prstGeom"), e => (string?)e.Attribute("prst") == "ellipse");
            Assert.Contains(xml.Descendants(a + "xfrm"), e => (string?)e.Attribute("rot") == "5400000");
            Assert.Contains(xml.Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.Trim() == "PAGE");
            Assert.Empty(xml.Descendants(W + "ins")); Assert.Empty(xml.Descendants(W + "del"));
            using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray());
            var page = pdf.GetPage(1);
            Assert.Contains(sample.Text, page.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(sample.Decision == ReviewDecision.Accept ? "Original" : "Revised", page.Text, StringComparison.Ordinal);
            Assert.Contains("1", page.Text, StringComparison.Ordinal); Assert.DoesNotContain("9", page.Text, StringComparison.Ordinal);
            Assert.Contains("Box", page.Text, StringComparison.Ordinal);
            Assert.Contains(page.Operations, op => op is AppendDualControlPointBezierCurve);
            Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is PdfRectangle bounds && Math.Abs(bounds.Left - 100) < 0.01
                && Math.Abs(bounds.Right - 240) < 0.01 && Math.Abs(bounds.Bottom - 120) < 0.01 && Math.Abs(bounds.Top - 200) < 0.01);
        }
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("tagged")]
    [InlineData("typography")]
    public void MixedDrawingsRejectUnqualifiedOutputAtomically(string profile)
    {
        using var input = new MemoryStream(MixedQualificationDocument("Content"));
        using var output = new MemoryStream();
        var options = new ConversionOptions();
        if (profile == "archive") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (profile == "tagged") options.Pdf.Accessibility = PdfAccessibility.Tagged;
        if (profile == "typography") options.Typography.Enabled = true;
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith(profile == "typography" ? "Typography.Structure:" : "Shape.VectorProfile:", error.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void MixedDrawingsRejectOverlappingWordLayers()
    {
        using var input = new MemoryStream(MixedQualificationDocument("Content", overlap: true));
        using var output = new MemoryStream();
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output,
            DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Shape.DrawingOrder:", error.Message); Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(ReviewDecision.Accept, 14)]
    [InlineData(ReviewDecision.Reject, 10)]
    public void FormattingHistoryWithDrawingsKeepsTheChosenAppearance(ReviewDecision decision, int size)
    {
        string body = "<w:p><w:r><w:rPr><w:sz w:val='28'/><w:rPrChange w:id='1' w:author='Reviewer'><w:rPr><w:sz w:val='20'/></w:rPr></w:rPrChange></w:rPr><w:t>Styled</w:t></w:r></w:p>";
        WordDocument document = WordDocument.Load(new MemoryStream(MixedQualificationDocument("", body: body)), DocumentFormat.Docx,
            new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument view = document.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); view.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(saved.ToArray());
        Assert.Empty(Part(saved.ToArray(), "word/document.xml").Descendants(W + "rPrChange"));
        using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
        Assert.Contains("Styled", page.Text, StringComparison.Ordinal); Assert.Equal(size, page.Letters[0].FontSize, 3);
        Assert.Contains(page.Operations, op => op is AppendDualControlPointBezierCurve); Assert.Single(document.Revisions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewDecisionsKeepShrinkAndLinkedBoxTextExactlyOnce(bool linked)
    {
        string drawings = linked ? LinkedBox(0, 50, 50, 45, "chain text") + LinkedBox(1, 50, 120, 80)
            : string.Concat(Part(RotatedBox(0, string.Join(" ", Enumerable.Repeat("fitted", 20)), true), "word/document.xml")
                .Descendants(W + "p").Where(p => p.Descendants().Any(e => e.Name.LocalName == "anchor")));
        string history = "<w:p><w:del w:id='1' w:author='Reviewer'><w:r><w:delText>Original</w:delText></w:r></w:del><w:ins w:id='2' w:author='Reviewer'><w:r><w:t>Revised</w:t></w:r></w:ins></w:p>";
        WordDocument document = WordDocument.Load(new MemoryStream(Docx(history + drawings + Page)), DocumentFormat.Docx,
            new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        foreach (ReviewDecision decision in new[] { ReviewDecision.Accept, ReviewDecision.Reject })
        {
            WordDocument view = document.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
            using var saved = new MemoryStream(); view.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(saved.ToArray());
            using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); string text = string.Concat(pdf.GetPages().Select(p => p.Text));
            Assert.Contains(decision == ReviewDecision.Accept ? "Revised" : "Original", text, StringComparison.Ordinal);
            Assert.DoesNotContain(decision == ReviewDecision.Accept ? "Original" : "Revised", text, StringComparison.Ordinal);
            Assert.Equal(linked ? 1 : 20, text.Split(new[] { linked ? "chain" : "fitted" }, StringSplitOptions.None).Length - 1);
        }
    }

    [Fact]
    public void ParagraphMovesWithVectorsPreserveOrderInBothViews()
    {
        WordDocument LoadPlain(string body) => WordDocument.Load(new MemoryStream(Docx(body + Page)), DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument original = LoadPlain(P("A") + P("Move me") + P("B") + P("C"));
        WordDocument revised = LoadPlain(P("A") + P("B") + P("C") + P("Move me"));
        WordDocument compared = DocumentComparison.Compare(original, revised,
            new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)), cancellationToken: TestContext.Current.CancellationToken);
        using var history = new MemoryStream(); compared.Save(history, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        string body = string.Concat(Part(history.ToArray(), "word/document.xml").Root!.Element(W + "body")!.Elements().Where(e => e.Name != W + "sectPr"));
        WordDocument document = WordDocument.Load(new MemoryStream(MixedQualificationDocument("", body: body)), DocumentFormat.Docx,
            new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        foreach (ReviewDecision decision in new[] { ReviewDecision.Accept, ReviewDecision.Reject })
        {
            WordDocument view = document.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
            using var saved = new MemoryStream(); view.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(saved.ToArray());
            Assert.DoesNotContain(Part(saved.ToArray(), "word/document.xml").Descendants(), e => e.Name.LocalName.StartsWith("move", StringComparison.Ordinal));
            Assert.Equal(decision == ReviewDecision.Accept ? "A|B|C|Move me" : "A|Move me|B|C", string.Join("|", view.Paragraphs.Take(4).Select(p => p.Text)));
            using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
            Assert.Contains("Move me", page.Text, StringComparison.Ordinal);
            Assert.Equal(page.Text.IndexOf("Move me", StringComparison.Ordinal), page.Text.LastIndexOf("Move me", StringComparison.Ordinal));
            Assert.Contains(page.Operations, op => op is AppendDualControlPointBezierCurve);
        }
    }

    [Fact]
    public void HeaderRevisionsWithRotatedBoxesKeepTheChosenStory()
    {
        string drawings = string.Concat(Part(RotatedBox(90, "Box"), "word/document.xml").Descendants(W + "p"));
        string header = "<w:hdr xmlns:w='" + W + "'><w:p><w:del w:id='1' w:author='Reviewer'><w:r><w:delText>Old header</w:delText></w:r></w:del>"
            + "<w:ins w:id='2' w:author='Reviewer'><w:r><w:t>New header</w:t></w:r></w:ins></w:p></w:hdr>";
        string section = Page.Replace("<w:sectPr>", "<w:sectPr><w:headerReference w:type='default' r:id='header'/>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(P("Body") + drawings + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", header));
        WordDocument document = WordDocument.Load(new MemoryStream(input), DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        foreach (ReviewDecision decision in new[] { ReviewDecision.Accept, ReviewDecision.Reject })
        {
            WordDocument view = document.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
            using var saved = new MemoryStream(); view.Save(saved, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Schema(saved.ToArray());
            Assert.Equal(decision == ReviewDecision.Accept ? "New header" : "Old header", Part(saved.ToArray(), "word/header1.xml").Root!.Value);
            using var output = new MemoryStream(); view.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); string text = pdf.GetPage(1).Text;
            Assert.Contains(decision == ReviewDecision.Accept ? "New header" : "Old header", text, StringComparison.Ordinal);
            Assert.DoesNotContain(decision == ReviewDecision.Accept ? "Old header" : "New header", text, StringComparison.Ordinal);
            Assert.Contains("Box", text, StringComparison.Ordinal);
        }
    }
}
