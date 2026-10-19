using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using WordNet;

namespace WordNet.Examples;

// Shared with the regression suite: the runnable recipes exercise only public APIs.
internal static class AutomationRecipes
{
    internal static ConversionOptions Options() => new ConversionOptions(FidelityMode.BestEffort)
    { Clock = () => new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero) };

    internal static object InvoiceData(string customer, IReadOnlyList<InvoiceItem> items, byte[] logo, bool showTerms = true)
    {
        if (string.IsNullOrWhiteSpace(customer) || customer.Length > 200) throw new ArgumentException("A customer name of at most 200 characters is required.", nameof(customer));
        if (items.Count > 200) throw new ArgumentException("The sample accepts at most 200 line items.", nameof(items));
        var rows = new List<object>();
        decimal total = 0;
        foreach (InvoiceItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 200 || item.Quantity < 1 || item.UnitPrice < 0)
                throw new ArgumentException("Each item needs a description, positive quantity and nonnegative price.", nameof(items));
            decimal lineTotal = checked(item.Quantity * item.UnitPrice);
            total = checked(total + lineTotal);
            rows.Add(new { item.Description, item.Quantity, UnitPrice = Money(item.UnitPrice), LineTotal = Money(lineTotal) });
        }
        return new
        {
            Customer = customer,
            Items = rows,
            Issued = "Issued 2026-10-19",
            Total = Money(total),
            ShowTerms = showTerms,
            Logo = new TemplateImage(logo, 80, 40, "Example logo")
        };
    }

    internal static object ReportData() => new
    {
        Customer = "Prepared for Example customer",
        Issued = "Issued 2026-10-19",
        Summary = "Completed 18 projects with revenue of 24,000.00 EUR.",
        Items = new[] { new { Description = "Completed projects", Value = "18" }, new { Description = "Revenue (EUR)", Value = "24,000.00" } },
        Plan = "Expand support and review delivery milestones in November."
    };

    internal static WordDocument Populate(byte[] template, object data, TextWriter log, ConversionOptions? options = null,
        TemplateOptions? templateOptions = null, CancellationToken cancellationToken = default)
    {
        using var input = new MemoryStream(template, false);
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out ConversionResult loaded, options ?? Options(), cancellationToken);
        Report(log, "load", loaded.Diagnostics);
        DocumentEditResult edited = document.Merge(data, templateOptions ?? new TemplateOptions { Culture = CultureInfo.InvariantCulture, MaximumRows = 200 }, cancellationToken);
        Report(log, "edit", edited.Diagnostics);
        return document;
    }

    internal static void Export(WordDocument document, string directory, string name, TextWriter log, CancellationToken cancellationToken = default)
    {
        // Finish both conversions before touching an existing destination.
        byte[] docx = Save(document, DocumentFormat.Docx, log, cancellationToken);
        byte[] pdf = Save(document, DocumentFormat.Pdf, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        string docxPath = Path.Combine(directory, name + ".docx"), pdfPath = Path.Combine(directory, name + ".pdf");
        string stagedDocx = docxPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        string stagedPdf = pdfPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            File.WriteAllBytes(stagedDocx, docx); File.WriteAllBytes(stagedPdf, pdf);
            cancellationToken.ThrowIfCancellationRequested();
            Commit(stagedDocx, docxPath); Commit(stagedPdf, pdfPath);
        }
        finally
        {
            File.Delete(stagedDocx); File.Delete(stagedPdf);
        }
    }

    internal static byte[] Save(WordDocument document, DocumentFormat format, TextWriter log, CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        ConversionResult result = document.Save(output, format, cancellationToken: cancellationToken);
        Report(log, "save " + format, result.Diagnostics);
        return output.ToArray();
    }

    private static void Commit(string temporary, string destination)
    {
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static void Report(TextWriter log, string phase, IReadOnlyList<ConversionDiagnostic> diagnostics)
    {
        log.WriteLine(phase + ": " + diagnostics.Count.ToString(CultureInfo.InvariantCulture) + " diagnostics");
        foreach (ConversionDiagnostic diagnostic in diagnostics) log.WriteLine(phase + ": " + diagnostic.Code + ": " + diagnostic.Message);
    }
}

internal sealed class InvoiceItem
{
    internal InvoiceItem(string description, int quantity, decimal unitPrice) { Description = description; Quantity = quantity; UnitPrice = unitPrice; }
    public string Description { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
}
