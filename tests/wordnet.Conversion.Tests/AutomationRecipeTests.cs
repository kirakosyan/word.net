using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using WordNet.Examples;
using Xunit;

namespace WordNet.Tests;

public sealed class AutomationRecipeTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static byte[] Template(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Automation", name + ".docx"));
    private static byte[] Logo() => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "color-grid.png"));
    private static InvoiceItem[] Items(int count) => Enumerable.Range(1, count).Select(i => new InvoiceItem("Service " + i.ToString("D3", CultureInfo.InvariantCulture), 2, 12.50m)).ToArray();

    [Theory]
    [InlineData(2, true, 1)]
    [InlineData(0, false, 1)]
    [InlineData(100, true, 4)]
    public void InvoiceRetainsEditableRowsCallerLogoTotalsAndRealPageFields(int count, bool terms, int pages)
    {
        byte[] template = Template("invoice"), logo = Logo(); Schema(template);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        WordDocument document = AutomationRecipes.Populate(template, AutomationRecipes.InvoiceData("Example customer", Items(count), logo, terms), log,
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] docx = AutomationRecipes.Save(document, DocumentFormat.Docx, log, TestContext.Current.CancellationToken); Schema(docx);
        XDocument body = Part(docx, "word/document.xml");
        Assert.Equal(count + 1, body.Descendants(W + "tr").Count());
        Assert.Equal(terms, body.Descendants(W + "t").Any(t => t.Value == "Payment is due within 30 days."));
        Assert.Empty(body.Descendants(W + "sdt"));
        using (var zip = new ZipArchive(new MemoryStream(docx)))
        {
            using Stream image = zip.Entries.Single(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)).Open();
            using var bytes = new MemoryStream(); image.CopyTo(bytes); Assert.Equal(logo, bytes.ToArray());
        }
        using PdfDocument pdf = PdfDocument.Open(AutomationRecipes.Save(document, DocumentFormat.Pdf, log, TestContext.Current.CancellationToken));
        Assert.Equal(pages, pdf.NumberOfPages);
        string text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Example customer", text, StringComparison.Ordinal);
        Assert.Contains((count * 25m).ToString("F2", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        foreach (InvoiceItem item in Items(count)) Assert.Contains(item.Description, text, StringComparison.Ordinal);
        for (int page = 1; page <= pages; page++) Assert.Contains($"Page {page} of {pages}", pdf.GetPage(page).Text, StringComparison.Ordinal);
        Assert.Single(pdf.GetPage(1).GetImages());
        Assert.Contains("load:", log.ToString(), StringComparison.Ordinal); Assert.Contains("edit:", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("save Pdf: Pdf.ImageAlternativeText", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHasTwoSectionsAnEditableTableAndRefreshedFooterOnBothPages()
    {
        byte[] template = Template("report"); Schema(template);
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        WordDocument document = AutomationRecipes.Populate(template, AutomationRecipes.ReportData(), log, cancellationToken: TestContext.Current.CancellationToken);
        byte[] docx = AutomationRecipes.Save(document, DocumentFormat.Docx, log, TestContext.Current.CancellationToken); Schema(docx);
        XDocument body = Part(docx, "word/document.xml");
        Assert.Equal(2, body.Descendants(W + "sectPr").Count());
        Assert.Equal(3, body.Descendants(W + "tr").Count());
        Assert.Contains("Completed projects", body.Root!.Value, StringComparison.Ordinal);
        using (var zip = new ZipArchive(new MemoryStream(docx)))
        {
            using Stream footer = zip.Entries.Single(e => e.FullName.StartsWith("word/footer", StringComparison.Ordinal)).Open();
            Assert.Contains("NUMPAGES", XDocument.Load(footer).ToString(), StringComparison.Ordinal);
        }
        using PdfDocument pdf = PdfDocument.Open(AutomationRecipes.Save(document, DocumentFormat.Pdf, log, TestContext.Current.CancellationToken));
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Contains("24,000.00", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("Delivery plan", pdf.GetPage(2).Text, StringComparison.Ordinal);
        for (int page = 1; page <= 2; page++)
        {
            Assert.Contains("EXAMPLE ENGINEERING", pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.Contains($"Page {page} of 2", pdf.GetPage(page).Text, StringComparison.Ordinal);
            Assert.DoesNotContain("99", pdf.GetPage(page).Text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rows")]
    [InlineData("output")]
    [InlineData("cancel")]
    [InlineData("cancel-after-docx")]
    public void RecipeFailuresPreserveExistingOutputFiles(string failure)
    {
        string directory = Path.Combine(Path.GetTempPath(), "wordnet-recipe-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        byte[] sentinel = { 42, 43, 44 };
        string docx = Path.Combine(directory, "invoice.docx"), pdf = Path.Combine(directory, "invoice.pdf");
        File.WriteAllBytes(docx, sentinel); File.WriteAllBytes(pdf, sentinel);
        using var cancellation = new CancellationTokenSource();
        using var log = new CancelOnSaveWriter(cancellation, failure == "cancel-after-docx");
        try
        {
            object data = failure == "missing" ? new { Items = Items(2) } : AutomationRecipes.InvoiceData("Customer", Items(2), Logo());
            var options = failure == "output" ? new ConversionOptions(FidelityMode.BestEffort, maximumOutputBytes: 1) : AutomationRecipes.Options();
            var templateOptions = new TemplateOptions { MaximumRows = failure == "rows" ? 1 : 200 };
            if (failure == "cancel") cancellation.Cancel();
            void Execute()
            {
                WordDocument document = AutomationRecipes.Populate(Template("invoice"), data, log, options, templateOptions, cancellation.Token);
                AutomationRecipes.Export(document, directory, "invoice", log, cancellation.Token);
            }
            if (failure.StartsWith("cancel", StringComparison.Ordinal)) Assert.ThrowsAny<OperationCanceledException>(Execute);
            else Assert.Throws<InvalidDataException>(Execute);
            Assert.Equal(sentinel, File.ReadAllBytes(docx)); Assert.Equal(sentinel, File.ReadAllBytes(pdf));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExportReplacesExistingFilesOnlyAfterBothConversionsSucceed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wordnet-export-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        try
        {
            WordDocument report = AutomationRecipes.Populate(Template("report"), AutomationRecipes.ReportData(), TextWriter.Null, cancellationToken: TestContext.Current.CancellationToken);
            AutomationRecipes.Export(report, directory, "report", TextWriter.Null, TestContext.Current.CancellationToken);
            AutomationRecipes.Export(report, directory, "report", TextWriter.Null, TestContext.Current.CancellationToken);
            Schema(File.ReadAllBytes(Path.Combine(directory, "report.docx")));
            using PdfDocument pdf = PdfDocument.Open(Path.Combine(directory, "report.pdf")); Assert.Equal(2, pdf.NumberOfPages);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static XDocument Part(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using Stream part = zip.GetEntry(name)!.Open(); return XDocument.Load(part);
    }
    private static void Schema(byte[] bytes)
    {
        using WordprocessingDocument package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }
    private sealed class CancelOnSaveWriter : StringWriter
    {
        private readonly CancellationTokenSource cancellation;
        private readonly bool enabled;
        internal CancelOnSaveWriter(CancellationTokenSource cancellation, bool enabled) : base(CultureInfo.InvariantCulture)
        { this.cancellation = cancellation; this.enabled = enabled; }
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (enabled && value != null && value.StartsWith("save Docx:", StringComparison.Ordinal)) cancellation.Cancel();
        }
    }
}
