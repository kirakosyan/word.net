using System;
using System.IO;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected source and new output directories.");
if (Directory.Exists(args[1])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[1]);
foreach (string geometry in new[] { "rect", "triangle", "diamond" })
{
    string path = Path.Combine(args[0], geometry + ".docx");
    WordDocument document;
    using (var input = File.OpenRead(path)) document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort));
    using (var saved = File.Create(Path.Combine(args[1], geometry + ".docx"))) document.Save(saved, DocumentFormat.Docx);
    using (var pdf = File.Create(Path.Combine(args[1], geometry + ".pdf"))) document.Save(pdf, DocumentFormat.Pdf);
    using (var xps = File.Create(Path.Combine(args[1], geometry + ".xps"))) document.Save(xps, DocumentFormat.Xps);
    using var source = File.OpenRead(path);
    var preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, options: new ConversionOptions(FidelityMode.BestEffort));
    if (preview.Pages.Count != 1) throw new InvalidDataException("Expected one SVG page.");
    File.WriteAllBytes(Path.Combine(args[1], geometry + ".svg"), preview.Pages[0].GetBytes());
}
Console.WriteLine("Retained three independent preset shapes and rendered PDF, XPS and SVG vectors.");
