#nullable enable
using System;
using System.IO;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
using (var output = File.Create(Path.Combine(directory, "saved.docx"))) document.Save(output, DocumentFormat.Docx, options);
using var saved = File.OpenRead(Path.Combine(directory, "saved.docx"));
using var pdf = File.Create(Path.Combine(directory, "saved.pdf"));
new WordConverter().Convert(saved, pdf, DocumentFormat.Docx, DocumentFormat.Pdf, options);
Console.WriteLine("Saved an independently authored table and rendered the saved document.");
