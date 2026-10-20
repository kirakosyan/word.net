using System;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class NativeAccessibilityTests
{
    [Fact]
    public void NativeLinkCarriersRetainSourceAnnotationOwnership()
    {
        var options = ExtendedTypographyTests.Options(); options.Pdf.Language = "hi-IN"; options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        options.Pdf.Compress = false; var model = new DocumentModel { Title = "Link reading sample" };
        var heading = new ParagraphBlock { HeadingLevel = 1 }; heading.Runs.Add(new TextRun("Link reading sample")); model.Blocks.Add(heading);
        var body = new ParagraphBlock(); body.Runs.Add(new TextRun("हिन्दी ")); body.Runs.Add(new TextRun("लिंक", link: "https://example.org/reading")); model.Blocks.Add(body);
        var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        using var output = new MemoryStream(); PdfFormat.Write(model, output, context); byte[] bytes = output.ToArray();
        string raw = Encoding.ASCII.GetString(bytes); Assert.Contains("/OBJR", raw, StringComparison.Ordinal); Assert.Contains("/StructParent", raw, StringComparison.Ordinal);
        using var input = new MemoryStream(bytes); var imported = PdfImportBridge.Read(input, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal("Link reading sample हिन्दी लिंक", string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)));
        string? folder = Environment.GetEnvironmentVariable("WORDNET_P2_EVIDENCE"); if (folder != null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "native-hindi-link.pdf"), bytes); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeUaUsesOneLogicalCarrierPerSourceTokenAndExplicitLanguage(bool hindi)
    {
        const string heading = "Native reading sample";
        string text = hindi ? "Hindi हिन्दी कर्म क्षि र्कि क्‍ष क्‌ष ज़िंदा end" : "Arabic \u2067مَرْحَبًا (123 [45])\u2069 end";
        ConversionOptions options = ExtendedTypographyTests.Options(hindi); options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        options.Pdf.Language = hindi ? "hi-IN" : "ar"; options.Pdf.Compress = false;
        WordDocument doc = WordDocument.Create(options); doc.Title = heading;
        doc.AddParagraph(heading, headingLevel: 1, cancellationToken: TestContext.Current.CancellationToken);
        doc.AddParagraph(text, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken); byte[] bytes = output.ToArray();
        string raw = Encoding.ASCII.GetString(bytes); Assert.Contains("/StructTreeRoot", raw, StringComparison.Ordinal); Assert.Contains("/ActualText", raw, StringComparison.Ordinal);
        using var input = new MemoryStream(bytes); var imported = PdfImportBridge.Read(input, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(heading + " " + text, string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)));
        string? folder = Environment.GetEnvironmentVariable("WORDNET_P2_EVIDENCE");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); string name = hindi ? "native-hindi" : "native-arabic";
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), bytes); File.WriteAllText(Path.Combine(folder, name + ".txt"), heading + "\n" + text);
            options.Pdf.Accessibility = PdfAccessibility.None; using var negative = new MemoryStream(); doc.Save(negative, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
            File.WriteAllBytes(Path.Combine(folder, name + "-negative.pdf"), negative.ToArray());
        }
    }
    [Theory]
    [InlineData("title")]
    [InlineData("language")]
    [InlineData("wrong-language")]
    [InlineData("heading")]
    [InlineData("mixed")]
    public void BestEffortNativeUaStillRejectsMissingOrUnqualifiedSemanticsAtomically(string kind)
    {
        ConversionOptions options = ExtendedTypographyTests.Options(mode: FidelityMode.Lenient); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = kind == "language" ? null : kind == "wrong-language" ? "en-US" : "hi-IN";
        WordDocument doc = WordDocument.Create(options); if (kind != "title") doc.Title = "Reading";
        doc.AddParagraph(kind == "mixed" ? "हिन्दी مرحبا" : "हिन्दी", headingLevel: kind == "heading" ? 3 : 0, cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Assert.Throws<NotSupportedException>(() => doc.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }
}
