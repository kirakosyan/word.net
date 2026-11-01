using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 4) throw new ArgumentException("Expected input DOCX, saved DOCX, PDF and SVG paths.");
foreach (string path in args.AsSpan(1)) if (File.Exists(path)) throw new IOException("Choose new output paths.");
var options = new ConversionOptions(FidelityMode.BestEffort);
WordDocument document;
using (var input = File.OpenRead(args[0])) document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
DocumentObject chart = document.Objects.Single();
if (chart.Chart?.Kind != ChartKind.ClusteredBar) throw new InvalidDataException("Expected a clustered horizontal chart.");
using (var output = File.Create(args[1])) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(args[2])) document.Save(output, DocumentFormat.Pdf);
using (var source = File.OpenRead(args[1]))
{
    DocumentPreview preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Svg }, options);
    File.WriteAllBytes(args[3], preview.Pages[0].GetBytes());
}
Console.WriteLine("Retained and rendered an independently authored horizontal chart.");
