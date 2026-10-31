using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 3) throw new ArgumentException("Expected kind, input DOCX and a new output directory.");
string kind = args[0]; if (Directory.Exists(args[2])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[2]);
var options = new ConversionOptions(FidelityMode.BestEffort);
WordDocument document;
using (var input = File.OpenRead(args[1])) document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
DocumentObject chart = document.Objects.Single();
ChartKind expected = kind == "bar" ? ChartKind.StackedBar : kind == "column" ? ChartKind.StackedColumn : throw new ArgumentException("Unknown chart kind.");
if (chart.Chart?.Kind != expected) throw new InvalidDataException("Expected the independently authored stacked chart.");
using (var output = File.Create(Path.Combine(args[2], "saved.docx"))) document.Save(output, DocumentFormat.Docx);
ConversionResult result;
using (var output = File.Create(Path.Combine(args[2], "output.pdf"))) result = document.Save(output, DocumentFormat.Pdf);
using (var output = File.Create(Path.Combine(args[2], "output.xps"))) document.Save(output, DocumentFormat.Xps);
using (var source = File.OpenRead(Path.Combine(args[2], "saved.docx")))
{
    DocumentPreview preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Svg }, options);
    File.WriteAllBytes(Path.Combine(args[2], "output.svg"), preview.Pages[0].GetBytes());
}
if (!result.Diagnostics.Any(d => d.Code == "Chart.StackedLayoutApproximation")) throw new InvalidDataException("Missing bounded-layout diagnostic.");
File.WriteAllLines(Path.Combine(args[2], "diagnostics.txt"), result.Diagnostics.Select(d => d.Code).OrderBy(s => s, StringComparer.Ordinal));
Console.WriteLine("Retained and rendered an independently authored stacked chart.");
