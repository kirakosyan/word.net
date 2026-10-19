using System;
using System.IO;
using System.Threading;
using WordNet;
using WordNet.Examples;

string outputDirectory = Path.GetFullPath(args.Length == 0 ? "artifacts/automation-sample" : args[0]);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
string templates = Path.Combine(AppContext.BaseDirectory, "Templates");
byte[] logo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "logo.png"));
object invoiceData = AutomationRecipes.InvoiceData("Example customer",
    new[] { new InvoiceItem("Consulting", 2, 120.50m), new InvoiceItem("Support", 1, 25m) }, logo);
WordDocument invoice = AutomationRecipes.Populate(File.ReadAllBytes(Path.Combine(templates, "invoice.docx")), invoiceData, Console.Out,
    cancellationToken: cancellation.Token);
WordDocument report = AutomationRecipes.Populate(File.ReadAllBytes(Path.Combine(templates, "report.docx")), AutomationRecipes.ReportData(), Console.Out,
    cancellationToken: cancellation.Token);
AutomationRecipes.Export(invoice, outputDirectory, "invoice", Console.Out, cancellation.Token);
AutomationRecipes.Export(report, outputDirectory, "report", Console.Out, cancellation.Token);
P1Recipes.Run(outputDirectory, templates, cancellation.Token);
Console.WriteLine("Saved invoice.docx, invoice.pdf, report.docx and report.pdf to " + outputDirectory);
