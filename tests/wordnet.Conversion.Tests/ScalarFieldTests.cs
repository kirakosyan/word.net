using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class ScalarFieldTests
{
    private static byte[] Source(string instruction) => TestDocuments.Docx(TestDocuments.Body("<w:p><w:fldSimple w:instr='" + SecurityElement.Escape(instruction)
        + "'><w:r><w:t>stale</w:t></w:r></w:fldSimple></w:p>"));

    [Theory]
    [InlineData("DOCPROPERTY Title", "Quarterly")]
    [InlineData("DOCPROPERTY \"Author\" \\* MERGEFORMAT", "Analyst")]
    [InlineData("IF 12.5 >= -2 \"yes\" \"no\"", "yes")]
    [InlineData("IF \"North\" = \"north\" \"match\" \"different\"", "different")]
    [InlineData("IF 0 <> 0 \"yes\" \"no\"", "no")]
    [InlineData("= (2 + 3) * -4 / 2", "-10")]
    [InlineData("=1+2*3", "7")]
    [InlineData("= 2.50 + 2.50", "5")]
    [InlineData("= 1.5 * 2", "3")]
    [InlineData("= 10 / 3", "3.33333333333333")]
    [InlineData("= 1.25 + 2.5 \\* MERGEFORMAT", "3.75")]
    public void EditableCachesAndPdfXpsContainCalculatedValues(string instruction, string expected)
    {
        using var input = new MemoryStream(Source(instruction));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.Title = "Quarterly"; document.Author = "Analyst";
        using var word = new MemoryStream(); document.Save(word, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        word.Position = 0;
        using (var package = WordprocessingDocument.Open(word, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
            var field = Assert.Single(package.MainDocumentPart!.Document!.Descendants<global::DocumentFormat.OpenXml.Wordprocessing.SimpleField>());
            Assert.Equal(instruction, field.Instruction!.Value); Assert.Equal(expected, field.InnerText);
        }
        using var pdf = new MemoryStream(); document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using (PdfDocument read = PdfDocument.Open(pdf.ToArray())) Assert.Equal(expected, read.GetPage(1).Text);
        using var xps = new MemoryStream(); document.Save(xps, DocumentFormat.Xps, cancellationToken: TestContext.Current.CancellationToken); xps.Position = 0;
        using var archive = new ZipArchive(xps); using var page = archive.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        XDocument xml = XDocument.Load(page);
        Assert.Equal(expected, string.Concat(xml.Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString"))));
    }

    [Theory]
    [InlineData("DOCPROPERTY Missing")]
    [InlineData("IF \"Apple\" = \"A*\" \"yes\" \"no\"")]
    [InlineData("DOCPROPERTY Title unexpected")]
    [InlineData("IF 1 = 1 \"missing false branch\"")]
    [InlineData("IF \"1\" = 1 \"mixed\" \"types\"")]
    [InlineData("= 1 / 0")]
    [InlineData("= 79228162514264337593543950335 + 1")]
    [InlineData("= SUM(ABOVE)")]
    [InlineData("= 1 \\# 0.00")]
    [InlineData("IF { DOCPROPERTY Title } = \"x\" \"yes\" \"no\"")]
    public void UnsupportedAndArithmeticErrorsRetainTheirCacheInEveryMode(string instruction)
    {
        var converter = new WordConverter(); using var source = new MemoryStream(Source(instruction));
        DocumentInspection report = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.WouldRejectStrict); Assert.Contains(report.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult" && d.Severity == DiagnosticSeverity.Info);
        foreach (FidelityMode mode in new[] { FidelityMode.Strict, FidelityMode.BestEffort, FidelityMode.Lenient })
        {
            source.Position = 0; using var output = new MemoryStream();
            ConversionResult result = converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(mode), TestContext.Current.CancellationToken);
            Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult" && d.Severity == DiagnosticSeverity.Info);
            using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("stale", pdf.GetPage(1).Text);
        }
    }

    [Fact]
    public void DeepAndLongFormulasCannotExceedTheClosedGrammar()
    {
        UnsupportedAndArithmeticErrorsRetainTheirCacheInEveryMode("= " + new string('(', 17) + "1" + new string(')', 17));
        UnsupportedAndArithmeticErrorsRetainTheirCacheInEveryMode("= " + string.Join("+", Enumerable.Repeat("1", 257)));
    }
}
