using System;
using System.IO;
using WordNet;

if (args.Length != 3) throw new ArgumentException("Expected input DOCX, output DOCX and PDF paths.");
if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Choose new output paths.");
WordDocument document;
using (var input = File.OpenRead(args[0])) document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient));
document.InsertCrossTabAtCellBookmark("place", new[] { new CrossTabRecord("North", "Q1", 2.5m),
    new CrossTabRecord("South", "Q2", 4m), new CrossTabRecord("North", "Q1", 1m) }, includeTotals: true);
using (var output = File.Create(args[1])) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(args[2])) document.Save(output, DocumentFormat.Pdf);
Console.WriteLine("Inserted prepared values into the fixed-grid table cell.");
