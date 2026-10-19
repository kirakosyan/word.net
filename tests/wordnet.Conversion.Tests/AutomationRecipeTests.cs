using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Xml;
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
    private static int ExpectedPages(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Automation", "manifest.json"));
        using var json = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max);
        XElement document = XDocument.Load(json).Root!.Element("documents")!.Elements("item").Single(d => d.Element("id")!.Value == name);
        return int.Parse(document.Element("expectedPages")!.Value, CultureInfo.InvariantCulture);
    }
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
        if (count == 2) Assert.Equal(ExpectedPages("invoice"), pdf.NumberOfPages);
        string text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Example customer", text, StringComparison.Ordinal);
        Assert.Contains((count * 25m).ToString("F2", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        foreach (InvoiceItem item in Items(count)) Assert.Contains(item.Description, text, StringComparison.Ordinal);
        for (int page = 1; page <= pages; page++) Assert.Contains($"Page {page} of {pages}", pdf.GetPage(page).Text, StringComparison.Ordinal);
        for (int page = 2; page <= pages; page++) Assert.Contains("Description", pdf.GetPage(page).Text, StringComparison.Ordinal);
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
        Assert.Equal(ExpectedPages("report"), pdf.NumberOfPages);
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
    [InlineData("cancel-after-staging")]
    public void RecipeFailuresPreserveExistingOutputFiles(string failure)
    {
        string directory = Path.Combine(Path.GetTempPath(), "wordnet-recipe-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        byte[] sentinel = { 42, 43, 44 };
        string docx = Path.Combine(directory, "invoice.docx"), pdf = Path.Combine(directory, "invoice.pdf");
        File.WriteAllBytes(docx, sentinel); File.WriteAllBytes(pdf, sentinel);
        using var cancellation = new CancellationTokenSource();
        string? trigger = failure == "cancel-after-staging" ? "stage:" : failure == "cancel-after-docx" ? "save Docx:" : null;
        using var log = new CallbackWriter(trigger, cancellation.Cancel);
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
            byte[] sentinel = { 42, 43, 44 };
            string wordPath = Path.Combine(directory, "report.docx"), pdfPath = Path.Combine(directory, "report.pdf");
            File.WriteAllBytes(wordPath, sentinel); File.WriteAllBytes(pdfPath, sentinel);
            AutomationRecipes.Export(report, directory, "report", TextWriter.Null, TestContext.Current.CancellationToken);
            Assert.NotEqual(sentinel, File.ReadAllBytes(wordPath)); Assert.NotEqual(sentinel, File.ReadAllBytes(pdfPath));
            Schema(File.ReadAllBytes(wordPath));
            using PdfDocument pdf = PdfDocument.Open(pdfPath); Assert.Equal(2, pdf.NumberOfPages);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PhaseLogsExplicitlyRetainInheritedDiagnostics()
    {
        using var template = new MemoryStream();
        byte[] original = Template("invoice"); template.Write(original, 0, original.Length); template.Position = 0;
        using (var zip = new ZipArchive(template, ZipArchiveMode.Update, true))
        {
            XDocument document;
            using (Stream input = zip.GetEntry("word/document.xml")!.Open()) document = XDocument.Load(input);
            document.Descendants(W + "rPr").First().Add(new XElement(W + "rtl"));
            zip.GetEntry("word/document.xml")!.Delete();
            using Stream output = zip.CreateEntry("word/document.xml").Open(); document.Save(output);
        }
        using var log = new StringWriter(CultureInfo.InvariantCulture);
        WordDocument invoice = AutomationRecipes.Populate(template.ToArray(), AutomationRecipes.InvoiceData("Customer", Items(2), Logo()), log,
            cancellationToken: TestContext.Current.CancellationToken);
        AutomationRecipes.Save(invoice, DocumentFormat.Docx, log, TestContext.Current.CancellationToken);
        AutomationRecipes.Save(invoice, DocumentFormat.Pdf, log, TestContext.Current.CancellationToken);
        foreach (string phase in new[] { "load", "edit", "save Docx", "save Pdf" })
            Assert.Contains(phase + ": Docx.RunFormatting", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("cumulative diagnostics", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RoundedInvoiceLinesSumToTheDisplayedTotal()
    {
        var items = new[] { new InvoiceItem("First", 1, 0.005m), new InvoiceItem("Second", 1, 0.005m), new InvoiceItem("Third", 1, 0.005m) };
        WordDocument invoice = AutomationRecipes.Populate(Template("invoice"), AutomationRecipes.InvoiceData("Customer", items, Logo()), TextWriter.Null,
            cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = AutomationRecipes.Save(invoice, DocumentFormat.Docx, TextWriter.Null, TestContext.Current.CancellationToken);
        XElement[] rows = Part(bytes, "word/document.xml").Descendants(W + "tr").Skip(1).ToArray();
        decimal total = 0;
        foreach (XElement row in rows)
        {
            string[] values = row.Elements(W + "tc").Select(c => c.Value).ToArray();
            Assert.Equal("0.005", values[2]); Assert.Equal("0.01", values[3]);
            total += decimal.Parse(values[3], CultureInfo.InvariantCulture);
        }
        Assert.Equal(0.03m, total);
        Assert.Contains("0.03", Part(bytes, "word/document.xml").Descendants(W + "t").Select(t => t.Value));
        using PdfDocument pdf = PdfDocument.Open(AutomationRecipes.Save(invoice, DocumentFormat.Pdf, TextWriter.Null, TestContext.Current.CancellationToken));
        Assert.Contains("0.03", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("0.02", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupPreservesCancellationAndAttemptsBothStagedFilesWhenOneCannotBeDeleted()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wordnet-cleanup-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        using var cancellation = new CancellationTokenSource();
        string? blocked = null;
        using var log = new CallbackWriter("stage:", () =>
        {
            string[] staged = Directory.GetFiles(directory, "*.tmp"); Assert.Equal(2, staged.Length);
            blocked = staged.Single(p => Path.GetFileName(p).StartsWith("report.docx.", StringComparison.Ordinal));
            File.Delete(blocked); Directory.CreateDirectory(blocked); // File.Delete now fails on every platform.
            cancellation.Cancel();
        });
        try
        {
            byte[] sentinel = { 42 };
            File.WriteAllBytes(Path.Combine(directory, "report.docx"), sentinel);
            File.WriteAllBytes(Path.Combine(directory, "report.pdf"), sentinel);
            WordDocument report = AutomationRecipes.Populate(Template("report"), AutomationRecipes.ReportData(), TextWriter.Null,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.ThrowsAny<OperationCanceledException>(() => AutomationRecipes.Export(report, directory, "report", log, cancellation.Token));
            Assert.NotNull(blocked); Assert.True(Directory.Exists(blocked));
            Assert.Equal(2, Directory.GetFiles(directory).Length); // The other staged file was still removed.
            Assert.Equal(sentinel, File.ReadAllBytes(Path.Combine(directory, "report.docx")));
            Assert.Equal(sentinel, File.ReadAllBytes(Path.Combine(directory, "report.pdf")));
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
    private sealed class CallbackWriter : StringWriter
    {
        private readonly string? trigger;
        private readonly Action callback;
        internal CallbackWriter(string? trigger, Action callback) : base(CultureInfo.InvariantCulture)
        { this.trigger = trigger; this.callback = callback; }
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (trigger != null && value != null && value.StartsWith(trigger, StringComparison.Ordinal)) callback();
        }
    }
}
