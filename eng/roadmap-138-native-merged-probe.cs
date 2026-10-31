using System;
using System.IO;
using WordNet;

if (args.Length != 4) throw new ArgumentException("Expected input DOCX, new output PDF, Arabic font and Devanagari font paths.");
string outputPath = Path.GetFullPath(args[1]);
if (File.Exists(outputPath)) throw new IOException("Choose a new output path.");
var options = new ConversionOptions(FidelityMode.BestEffort);
options.Typography.Enabled = true;
options.Typography.ExtendedBidi = true;
options.Typography.Devanagari = true;
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", args[2]));
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Devanagari", args[3]));
using var input = File.OpenRead(args[0]);
using var output = File.Create(outputPath);
ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options);
if (!System.Linq.Enumerable.Any(result.Diagnostics, diagnostic => diagnostic.Code == "Typography.MergedTableSpacing"))
    throw new InvalidOperationException("The row-spacing difference was not diagnosed.");
Console.WriteLine("Rendered the independent Hindi merged table with its row-spacing diagnostic.");
