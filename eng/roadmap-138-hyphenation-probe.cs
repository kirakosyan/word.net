using System;
using System.IO;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected source and new output directory.");
if (Directory.Exists(args[1])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[1]);
var options = new ConversionOptions(FidelityMode.BestEffort) { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) };
options.Hyphenation.Dictionary = new HyphenationLexicon("en-US", new[] { "ex-tra-or-di-nary" });
options.Pdf.Compress = false;
using var source = File.OpenRead(args[0]); WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
using (var saved = File.Create(Path.Combine(args[1], "saved.docx"))) document.Save(saved, DocumentFormat.Docx, options);
using var output = File.Create(Path.Combine(args[1], "dictionary-hyphenation.pdf"));
ConversionResult result = document.Save(output, DocumentFormat.Pdf, options);
if (!System.Linq.Enumerable.Any(result.Diagnostics, d => d.Code == "Hyphenation.SearchGeometry")) throw new InvalidDataException("Expected the search-geometry diagnostic.");
Console.WriteLine("Produced editable source text and the diagnosed hyphenated PDF.");
