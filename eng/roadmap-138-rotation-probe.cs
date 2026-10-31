using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected source DOCX and a new output directory.");
if (Directory.Exists(args[1])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[1]);
var options = new ConversionOptions(FidelityMode.BestEffort) { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) };
options.Pdf.Compress = false;
using (var source = File.OpenRead(args[0]))
{
    WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
    using var saved = File.Create(Path.Combine(args[1], "saved.docx")); document.Save(saved, DocumentFormat.Docx, options);
}
using (var first = File.OpenRead(Path.Combine(args[1], "saved.docx")))
{
    WordDocument document = WordDocument.Load(first, DocumentFormat.Docx, out _, options);
    using var saved = File.Create(Path.Combine(args[1], "saved-again.docx")); document.Save(saved, DocumentFormat.Docx, options);
}
foreach (DocumentFormat format in new[] { DocumentFormat.Pdf, DocumentFormat.Xps })
{
    using var source = File.OpenRead(args[0]); using var output = File.Create(Path.Combine(args[1], "output." + format.ToString().ToLowerInvariant()));
    ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, format, options);
    if (format == DocumentFormat.Pdf) File.WriteAllLines(Path.Combine(args[1], "diagnostics.txt"), result.Diagnostics.Select(diagnostic => diagnostic.Code));
}
using (var source = File.OpenRead(args[0]))
{
    var pages = new WordConverter().RenderPages(source, DocumentFormat.Docx, options: options);
    if (pages.Pages.Count != 1) throw new InvalidDataException("Expected one preview page.");
    File.WriteAllBytes(Path.Combine(args[1], "output.svg"), pages.Pages[0].GetBytes());
}
Console.WriteLine("Produced editable DOCX, PDF, XPS and SVG from the independent source.");
