using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, new TemplateDataSchema(), DocumentFormat.Docx,
    new ConversionOptions(FidelityMode.Lenient));
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var aliases = report.GetSensitivePathAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.PartId,
    path = f.PathId == null ? null : aliases[f.PathId], indices = f.SourceElementIndices }).ToArray();
File.WriteAllText(Path.Combine(directory, "findings.json"), JsonSerializer.Serialize(findings));
Console.WriteLine("Inspected source field positions without binding data.");
