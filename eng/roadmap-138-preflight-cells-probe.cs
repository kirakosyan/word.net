using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("simple" or "unsupported"))
    throw new ArgumentException("Expected a DOCX, a new output directory and simple or unsupported.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var converter = new WordConverter();
var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping), new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar));
using var input = File.OpenRead(args[0]);
TemplateInspection report = converter.InspectTemplate(input, schema, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Lenient));
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases(); var parts = report.GetSensitivePartAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.Count, path = f.PathId == null ? null : paths[f.PathId], part = f.PartId == null ? null : parts[f.PartId] }).ToArray();
var requirements = report.Requirements.Select(r => new { path = paths[r.PathId], r.DeclaredType, r.Required, r.ScopeUnknown, r.Count }).ToArray();
bool? rejected = null; int? changes = null; string[]? before = null, afterFailure = null, afterMerge = null;
if (args[2] == "simple")
{
    input.Position = 0;
    WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient));
    before = document.Paragraphs.Select(p => p.Text).ToArray();
    try { document.MergeJson("{\"Customer\":{\"Name\":\"Ada\"}}"); rejected = false; }
    catch (InvalidDataException) { rejected = true; }
    afterFailure = document.Paragraphs.Select(p => p.Text).ToArray();
    changes = document.MergeJson("{\"Customer\":{\"Name\":\"Ada\",\"Code\":\"Q42\"}}").Changes;
    afterMerge = document.Paragraphs.Select(p => p.Text).ToArray();
    using var output = File.Create(Path.Combine(directory, "merged.docx"));
    document.Save(output, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Lenient));
}
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements, rejected, changes, before, afterFailure, afterMerge }));
Console.WriteLine("Inspected the synthetic template and recorded bounded outcomes.");
