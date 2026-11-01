using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 4) throw new ArgumentException("Expected source DOCX, language, font and a new output directory.");
if (Directory.Exists(args[3])) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(args[3]);
var language = args[1] switch { "JP" => EastAsianLineBreakLanguage.Japanese, "SC" => EastAsianLineBreakLanguage.SimplifiedChinese,
    "TC" => EastAsianLineBreakLanguage.TraditionalChinese, _ => throw new ArgumentException("Unknown language.") };
var options = new ConversionOptions(FidelityMode.BestEffort) { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) };
options.Typography.EastAsianLineBreak = language;
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans CJK " + args[1], args[2]));
using (var source = File.OpenRead(args[0]))
{
    WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
    using var saved = File.Create(Path.Combine(args[3], "saved.docx")); document.Save(saved, DocumentFormat.Docx, options);
}
using (var source = File.OpenRead(args[0]))
using (var output = File.Create(Path.Combine(args[3], "output.pdf")))
{
    ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options);
    File.WriteAllLines(Path.Combine(args[3], "diagnostics.txt"), result.Diagnostics.Select(diagnostic => diagnostic.Code));
}
Console.WriteLine("Produced editable DOCX and managed PDF for an independent specimen.");
