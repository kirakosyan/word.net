using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterpriseConversionTests
{
    [Fact]
    public void IndependentlyWordGeneratedDocPreservesStoriesAndLastParagraphPagination()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "word-generated-stories.doc"));
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        new WordConverter().Convert(source, output, DocumentFormat.Doc, DocumentFormat.Pdf, Options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal(5, pdf.NumberOfPages);
        for (int page = 1; page <= 5; page++)
        {
            Assert.Contains("Enterprise body " + page, pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.Contains("Enterprise letterhead", pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.Contains("Page " + page + " of 5", pdf.GetPage(page).Text, StringComparison.Ordinal);
        }
    }

    private static string DirectoryPath => Path.Combine(TestDocuments.FixtureDirectory, "Enterprise");
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(DirectoryPath, name + ".docx"));
    private static ConversionOptions Options => new ConversionOptions(FidelityMode.Lenient)
    { Clock = () => new DateTimeOffset(2026, 10, 15, 12, 34, 56, TimeSpan.Zero) };

    public static IEnumerable<object[]> Corpus => Directory.GetFiles(DirectoryPath, "*.docx").OrderBy(p => p, StringComparer.Ordinal)
        .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GeneratedCorpusProducesSafeHtmlAndSearchablePdfOrRejectsHostileInput(string name)
    {
        byte[] input = Fixture(name); var converter = new WordConverter();
        if (name is "malformed-xml" or "expanded-limit")
        {
            using var source = new MemoryStream(input); using var destination = new MemoryStream(); destination.WriteByte(42);
            Assert.Throws<InvalidDataException>(() => converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, Options, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, destination.ToArray()); return;
        }
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(converter.ConvertToHtml(input, Options)));
        Assert.Contains("Enterprise", html.Body!.TextContent, StringComparison.Ordinal);
        Assert.Empty(html.QuerySelectorAll("script,iframe,object,link,style"));
        Assert.All(html.QuerySelectorAll("[src],[href]"), element =>
        {
            string url = element.GetAttribute("src") ?? element.GetAttribute("href")!;
            Assert.True(url.StartsWith("data:", StringComparison.Ordinal) || url.StartsWith("https:", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.Ordinal));
        });
        using PdfDocument pdf = PdfDocument.Open(converter.ConvertToPdf(input, Options));
        Assert.Contains("Enterprise", string.Concat(pdf.GetPages().Select(p => p.Text)), StringComparison.Ordinal);
        Assert.InRange(pdf.NumberOfPages, 1, 100);
        Assert.All(pdf.GetPages(), page => Assert.NotEmpty(page.Letters));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(25)]
    public void SpecifiedPageBreakCountsAreExact(int pages)
    {
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Fixture("pages-" + pages.ToString("00", System.Globalization.CultureInfo.InvariantCulture)), Options));
        Assert.Equal(pages, pdf.NumberOfPages);
        for (int page = 1; page <= pages; page++) Assert.Contains("Enterprise page " + page, pdf.GetPage(page).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderFooterFieldsUseEachPageAndFinalPageCount()
    {
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Fixture("letter-stories"), Options));
        Assert.Equal(5, pdf.NumberOfPages);
        for (int page = 1; page <= 5; page++)
        {
            Assert.Contains("Enterprise letterhead", pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.Contains("Page " + page + " of 5", pdf.GetPage(page).Text, StringComparison.Ordinal);
            var header = pdf.GetPage(page).Letters.Where(l => l.StartBaseLine.Y > 790).ToArray();
            Assert.NotEmpty(header);
            Assert.All(pdf.GetPage(page).Letters, l => Assert.True(l.StartBaseLine.Y >= 0 && l.StartBaseLine.Y < pdf.GetPage(page).Height));
        }
    }

    [Theory]
    [InlineData("letter-stories", 5)]
    [InlineData("first-even-stories", 2)]
    public void BinaryWordPreservesHeaderFooterStoriesAndFields(string name, int count)
    {
        var converter = new WordConverter();
        using var source = new MemoryStream(Fixture(name)); using var binary = new MemoryStream();
        converter.Convert(source, binary, DocumentFormat.Docx, DocumentFormat.Doc, Options, TestContext.Current.CancellationToken);
        // MS-DOC 2.3 requires one non-editable paragraph after the final non-main part.
        using (var container = RootStorage.Open(new MemoryStream(binary.ToArray())))
        {
            byte[] Read(string part) { using CfbStream stream = container.OpenStream(part); using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); }
            byte[] word = Read("WordDocument"), table = Read("1Table");
            int clx = BitConverter.ToInt32(word, 418), countPieces = (BitConverter.ToInt32(table, clx + 1) - 4) / 12;
            int cpMac = BitConverter.ToInt32(table, clx + 5 + countPieces * 4);
            Assert.Equal(BitConverter.ToInt32(word, 76) + BitConverter.ToInt32(word, 84) + 1, cpMac);
            int hdd = BitConverter.ToInt32(word, 154 + 11 * 8), size = BitConverter.ToInt32(word, 158 + 11 * 8);
            Assert.True(BitConverter.ToInt32(table, hdd + size - 4) >= BitConverter.ToInt32(table, hdd + size - 8));
        }
        binary.Position = 0; using var output = new MemoryStream();
        converter.Convert(binary, output, DocumentFormat.Doc, DocumentFormat.Pdf, Options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal(count, pdf.NumberOfPages);
        if (name == "letter-stories") for (int page = 1; page <= count; page++)
        {
            Assert.Contains("Enterprise letterhead", pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.Contains("Page " + page + " of 5", pdf.GetPage(page).Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("FIRST HEADER", pdf.GetPage(1).Text, StringComparison.Ordinal);
            Assert.Contains("EVEN HEADER", pdf.GetPage(2).Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FirstAndEvenStoriesAndHtmlPolicyAreApplied()
    {
        byte[] input = Fixture("first-even-stories"); var converter = new WordConverter();
        using PdfDocument pdf = PdfDocument.Open(converter.ConvertToPdf(input, Options));
        Assert.Contains("FIRST HEADER", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("EVEN HEADER", pdf.GetPage(2).Text, StringComparison.Ordinal);
        using var omitted = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(converter.ConvertToHtml(input, Options)));
        Assert.DoesNotContain("FIRST HEADER", omitted.Body!.TextContent, StringComparison.Ordinal);
        ConversionOptions included = Options; included.Html.IncludeHeadersAndFooters = true;
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(converter.ConvertToHtml(input, included)));
        Assert.Contains("FIRST HEADER", html.Body!.TextContent, StringComparison.Ordinal);
        Assert.Contains("EVEN HEADER", html.Body.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldsAndNestedTablesSurviveDocxSerialization()
    {
        foreach (string name in new[] { "letter-stories", "nested-invoice", "table-shading", "appearance", "simple-fields", "font-request" })
        {
            byte[] input = Fixture(name);
            var context = new ConversionContext(Options, TestContext.Current.CancellationToken);
            using var source = new MemoryStream(input); using var output = new MemoryStream();
            DocumentModel model = DocxReader.Read(source, context); DocxWriter.Write(model, output, context);
            using WordprocessingDocument word = WordprocessingDocument.Open(new MemoryStream(output.ToArray()), false);
            var errors = new OpenXmlValidator().Validate(word, TestContext.Current.CancellationToken).ToArray();
            Assert.True(errors.Length == 0, name + ": " + string.Join("; ", errors.Select(e => e.Description + " at " + e.Path?.XPath)));
            using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(new WordConverter().ConvertToHtml(output.ToArray(), Options)));
            if (name == "nested-invoice") Assert.Single(html.QuerySelectorAll("table table"));
            else if (name == "letter-stories") Assert.DoesNotContain("99", html.Body!.TextContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClockAndUnsupportedFieldDiagnosticsAreDeterministic()
    {
        var converter = new WordConverter();
        byte[] first = converter.ConvertToPdf(Fixture("simple-fields"), Options);
        Assert.Equal(first, converter.ConvertToPdf(Fixture("simple-fields"), Options));
        using PdfDocument pdf = PdfDocument.Open(first);
        Assert.Contains("2026-10-15", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("12:34:56", pdf.GetPage(1).Text, StringComparison.Ordinal);
        using var input = new MemoryStream(Fixture("cached-merge")); using var output = new MemoryStream();
        ConversionResult report = converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Html, Options, TestContext.Current.CancellationToken);
        Assert.Contains(report.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult" && d.Severity == DiagnosticSeverity.Info);
        Assert.Contains("Cached customer", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        Assert.Equal(output.Length, report.Metrics.BytesWritten);
        Assert.Null(report.Metrics.PageCount);
    }

    [Fact]
    public async Task OneConverterSupportsConcurrentDeterministicExports()
    {
        var converter = new WordConverter(); byte[] input = Fixture("letter-stories");
        byte[] expectedPdf = converter.ConvertToPdf(input, Options), expectedHtml = converter.ConvertToHtml(input, Options);
        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            Assert.Equal(expectedPdf, converter.ConvertToPdf(input, Options));
            Assert.Equal(expectedHtml, converter.ConvertToHtml(input, Options));
        }, TestContext.Current.CancellationToken)));
    }
}
