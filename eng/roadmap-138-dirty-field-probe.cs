#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var schema = new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar),
    new TemplateSchemaPath("Items", TemplatePathType.Collection), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar));
using var source = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Lenient));
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases(); var parts = report.GetSensitivePartAliases();
var findings = report.Findings.Select(finding => new { finding.Code, finding.Remediation, finding.Unknown,
    part = finding.PartId == null ? null : parts[finding.PartId],
    scope = finding.ScopePathId == null ? null : paths[finding.ScopePathId], indices = finding.SourceElementIndices }).ToArray();
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings }));
Console.WriteLine("Inspected synthetic field metadata without evaluating it.");
