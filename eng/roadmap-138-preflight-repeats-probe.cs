using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("simple" or "mixed" or "complex"))
    throw new ArgumentException("Expected a DOCX, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var options = new ConversionOptions(FidelityMode.Lenient); var converter = new WordConverter();
using var input = File.OpenRead(args[0]);
TemplateDataSchema schema = args[2] == "mixed" ? new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Scalar)) : new TemplateDataSchema();
TemplateInspection report = converter.InspectTemplate(input, schema, DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.PartId, path = f.PathId == null ? null : paths[f.PathId], indices = f.SourceElementIndices }).ToArray();
var requirements = report.Requirements.Select(r => new { path = paths[r.PathId], r.DeclaredType, r.Required, r.ScopeUnknown, r.Count }).ToArray();
string? missingError = null, typeError = null; string[]? before = null, afterFailures = null, afterMerge = null; int? changes = null;
bool? wrongTypeFinding = null, validScopeUnknown = null; TemplatePathType? validType = null;
if (args[2] == "simple")
{
    input.Position = 0;
    TemplateInspection wrong = converter.InspectTemplate(input, new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Scalar)), DocumentFormat.Docx, options);
    wrongTypeFinding = wrong.Findings.Any(f => f.Code == "Plan.PathType");
    input.Position = 0;
    TemplateInspection valid = converter.InspectTemplate(input, new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection)), DocumentFormat.Docx, options);
    TemplateInspectionRequirement requirement = valid.Requirements.Single(); validScopeUnknown = requirement.ScopeUnknown; validType = requirement.DeclaredType;
    input.Position = 0;
    WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options); before = document.Paragraphs.Select(p => p.Text).ToArray();
    try { document.MergeJson("{}"); } catch (InvalidDataException e) { missingError = e.Message.Split(':')[0]; }
    try { document.MergeJson("{\"Items\":1}"); } catch (InvalidDataException e) { typeError = e.Message.Split(':')[0]; }
    afterFailures = document.Paragraphs.Select(p => p.Text).ToArray();
    changes = document.MergeJson("{\"Items\":[{},{}]}").Changes; afterMerge = document.Paragraphs.Select(p => p.Text).ToArray();
    using var output = File.Create(Path.Combine(directory, "merged.docx")); document.Save(output, DocumentFormat.Docx, options);
}
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements,
    wrongTypeFinding, validScopeUnknown, validType, missingError, typeError, changes, before, afterFailures, afterMerge }));
Console.WriteLine("Inspected a synthetic repeat template and recorded its merge outcome.");
