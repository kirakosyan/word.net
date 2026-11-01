using System;
using System.IO;
using WordNet;

if (args.Length != 1 || Directory.Exists(args[0])) throw new ArgumentException("Expected a new output directory.");
Directory.CreateDirectory(args[0]);
const string label = "AVAVAVAVAVAV";
var options = new ConversionOptions(FidelityMode.Lenient);
var definition = ChartDefinition.CreateCircular(ChartKind.Pie, new[] { new ChartSlice(label, 1, "2468AC") }, showLegend: false)
    .WithDataLabels(ChartDataLabels.Category);
var document = WordDocument.Create(options);
document.AddChart(definition, width: 360, height: 260);
using (var output = File.Create(Path.Combine(args[0], "advance.docx"))) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(Path.Combine(args[0], "advance.pdf"))) document.Save(output, DocumentFormat.Pdf);
using (var output = File.Create(Path.Combine(args[0], "advance.xps"))) document.Save(output, DocumentFormat.Xps);
using (var input = File.OpenRead(Path.Combine(args[0], "advance.docx")))
{
    var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Format = PreviewFormat.Svg }, options);
    File.WriteAllBytes(Path.Combine(args[0], "advance.svg"), preview.Pages[0].GetBytes());
}
