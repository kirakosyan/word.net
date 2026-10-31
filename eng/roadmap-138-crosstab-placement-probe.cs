using System;
using System.IO;
using WordNet;

if (args.Length != 1) throw new ArgumentException("Expected a new output DOCX path.");
string path = Path.GetFullPath(args[0]);
if (File.Exists(path)) throw new IOException("Choose a new output path.");
var document = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
document.AddParagraphs(new[] { "Before", "Insert", "After" });
document.AddBookmark("place", 1, 0, 6);
document.InsertCrossTabAtBookmark("place", new[] { new CrossTabRecord("North", "Q1", 2.5m),
    new CrossTabRecord("South", "Q2", 4m), new CrossTabRecord("North", "Q1", 1m) }, includeTotals: true);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
using var output = File.Create(path);
document.Save(output, DocumentFormat.Docx);
Console.WriteLine("Created a placed cross-tab with independently checkable values.");
