using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected source and new output directories.");
if (Directory.Exists(args[1])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[1]);
string[] kinds = { "line", "ellipse", "roundRect", "rect", "triangle", "diamond" };
foreach (string mode in new[] { "described", "decorative", "selection", "two", "stroke", "box", "box-decorative", "negative" }
    .Concat(kinds.SelectMany(kind => new[] { kind + "-described", kind + "-decorative" })))
{
    string source = Path.Combine(args[0], (mode == "negative" ? "described" : mode) + ".docx");
    var options = new ConversionOptions(FidelityMode.BestEffort);
    options.Pdf.Compress = false; options.Pdf.Language = "en";
    options.Pdf.Accessibility = mode == "negative" ? PdfAccessibility.None : PdfAccessibility.PdfUa1; if (mode != "negative") options.Pdf.Conformance = PdfConformance.PdfA2a;
    options.Pdf.VectorSemantics = mode == "negative" ? PdfVectorSemantics.Reject : mode == "decorative" || mode.EndsWith("-decorative", StringComparison.Ordinal) ? PdfVectorSemantics.Decorative : PdfVectorSemantics.DescribedAtAnchor;
    if (mode is "box" or "box-decorative") options.Pdf.TextBoxReadingOrder = PdfTextBoxReadingOrder.AnchorOrder;
    if (mode == "selection") options.Pdf.Pages.Add(new PageRange(2, 2));
    WordDocument document;
    using (var input = File.OpenRead(source)) document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
    if (mode != "negative") using (var saved = File.Create(Path.Combine(args[1], mode + ".docx"))) document.Save(saved, DocumentFormat.Docx);
    using var pdf = File.Create(Path.Combine(args[1], "accessible-vector-" + mode + ".pdf")); document.Save(pdf, DocumentFormat.Pdf, options);
}
Console.WriteLine("Produced independently authored described, decorative, selected and multi-shape PDF specimens.");
