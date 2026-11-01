#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("required" or "optional" or "wrong-scope" or "two-scopes" or "nested" or "number" or "mismatch" or "mismatch-empty"))
    throw new ArgumentException("Expected a DOCX, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
string scenario = args[2];
TemplateSchemaPath[] declarations = scenario switch
{
    "required" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar) },
    "optional" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar, false) },
    "wrong-scope" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Other", "Name", TemplatePathType.Scalar) },
    "two-scopes" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Orders", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Orders", "Name", TemplatePathType.Scalar, false) },
    "nested" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), TemplateSchemaPath.ForRepeatItem("Items", "Customer", TemplatePathType.Mapping) },
    "number" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), TemplateSchemaPath.ForRepeatItem("Items", "Amount", TemplatePathType.Number) },
    _ => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), TemplateSchemaPath.ForRepeatItem("Items", "Amount", TemplatePathType.Mapping) },
};
var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, new TemplateDataSchema(declarations), DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases(); var parts = report.GetSensitivePartAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, path = f.PathId == null ? null : paths[f.PathId],
    scope = f.ScopePathId == null ? null : paths[f.ScopePathId], part = f.PartId == null ? null : parts[f.PartId], indices = f.SourceElementIndices }).ToArray();
var requirements = report.Requirements.Select(r => new { path = paths[r.PathId], r.PathId,
    scope = r.ScopePathId == null ? null : paths[r.ScopePathId], r.ScopeUnknown, r.DeclaredType, r.Required, r.Count }).ToArray();
string data = scenario switch
{
    "required" => "{\"Name\":\"root\",\"Items\":[{\"Name\":\"child\"},{\"Name\":\"other\"}]}",
    "optional" or "wrong-scope" => "{\"Name\":\"root\",\"Items\":[{\"Name\":\"child\"},{\"Other\":1}]}",
    "two-scopes" => "{\"Name\":\"root\",\"Items\":[{\"Name\":\"first\"}],\"Orders\":[{\"Name\":\"second\"},{\"Other\":1}]}",
    "nested" => "{\"Items\":[{\"Customer\":{\"Name\":\"deep\"}}]}",
    "number" => "{\"Items\":[{\"Amount\":12.5}]}",
    "mismatch" => "{\"Items\":[{\"Amount\":{}}]}",
    _ => "{\"Items\":[]}",
};
input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
string[] before = document.Paragraphs.Select(p => p.Text).ToArray(); string? error = null;
try { document.MergeJson(data); } catch (InvalidDataException e) { error = e.Message.Split(':')[0]; }
string[] after = document.Paragraphs.Select(p => p.Text).ToArray();
using (var output = File.Create(Path.Combine(directory, "merged.docx"))) document.Save(output, DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements, error, before, after }));
Console.WriteLine("Inspected declared item scope and observed a bounded merge.");
