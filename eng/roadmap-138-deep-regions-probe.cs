using System;
using System.IO;
using WordNet;

if (args.Length != 4) throw new ArgumentException("Expected input DOCX, preserved DOCX, expanded DOCX and PDF paths.");
foreach (string path in args.AsSpan(1)) if (File.Exists(path)) throw new IOException("Choose new output paths.");
var options = new ConversionOptions(FidelityMode.Lenient);
WordDocument document;
using (var source = File.OpenRead(args[0])) document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
using (var preserved = File.Create(args[1])) document.Save(preserved, DocumentFormat.Docx);
using (var source = File.OpenRead(args[1])) document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
document.Merge(new { Global = "Root", Orders = new object[] {
    new { Customer = "North", Groups = new object[] {
        new { Group = "A", Units = new[] { new { Product = "Apple" }, new { Product = "Pear" } } },
        new { Group = "Empty", Units = Array.Empty<object>() } } },
    new { Customer = "South", Groups = new[] { new { Group = "B", Units = new[] { new { Product = "Plum" } } } } }
} });
using (var output = File.Create(args[2])) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(args[3])) document.Save(output, DocumentFormat.Pdf);
Console.WriteLine("Expanded independent three-level report.");
