#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 4 || args[3] is not ("valid" or "fallback" or "optional" or "missing-scope" or "empty" or "wrong-type" or "scope-type" or "typed-non-json" or "malformed" or "limit"))
    throw new ArgumentException("Expected JSON, a DOCX or dash, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[2]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
string scenario = args[3];
TemplateSchemaPath[] declarations = scenario switch
{
    "valid" or "fallback" or "empty" or "scope-type" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar) },
    "optional" or "missing-scope" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection, false), new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar, false) },
    "wrong-type" => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection), TemplateSchemaPath.ForRepeatItem("Items", "Amount", TemplatePathType.Number) },
    "typed-non-json" => new[] { new TemplateSchemaPath("When", TemplatePathType.Date), new TemplateSchemaPath("Logo", TemplatePathType.Image) },
    _ => new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection) },
};
string json = File.ReadAllText(args[0]); var options = new ConversionOptions(FidelityMode.Lenient);
TemplateDataInspection? report = null; string? inspectionError = null;
try { report = new TemplateDataSchema(declarations).InspectJson(json, scenario == "limit" ? 1 : 10000, options); }
catch (InvalidDataException error) { inspectionError = error.Message.Split(':')[0]; }
object? findings = null;
if (report != null)
{
    File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
    var paths = report.GetSensitivePathAliases();
    findings = report.Findings.Select(f => new { f.Code, path = paths[f.PathId], scope = f.ScopePathId == null ? null : paths[f.ScopePathId], f.ItemIndex, f.Unknown }).ToArray();
}
string? mergeError = null; string[]? before = null, after = null;
if (args[1] != "-")
{
    using var source = File.OpenRead(args[1]);
    WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, options);
    before = document.Paragraphs.Select(p => p.Text).ToArray();
    try { document.MergeJson(json); } catch (InvalidDataException error) { mergeError = error.Message.Split(':')[0]; }
    after = document.Paragraphs.Select(p => p.Text).ToArray();
    using var output = File.Create(Path.Combine(directory, "merged.docx")); document.Save(output, DocumentFormat.Docx, options);
}
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { inspectionError, report?.HasErrors, report?.HasUnknowns, findings, mergeError, before, after }));
Console.WriteLine("Inspected JSON declarations and observed a bounded merge when applicable.");
