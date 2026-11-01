#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("both" or "item" or "empty-invalid" or "nonmerge" or "two-blocks" or "field-extra" or "wrong-type"))
    throw new ArgumentException("Expected a DOCX, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var options = new ConversionOptions(FidelityMode.Lenient);
var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", args[2] == "wrong-type" ? TemplatePathType.Scalar : TemplatePathType.Collection),
    new TemplateSchemaPath("Name", TemplatePathType.Scalar));
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases(); var parts = report.GetSensitivePartAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.PartId, part = f.PartId == null ? null : parts[f.PartId],
    path = f.PathId == null ? null : paths[f.PathId], indices = f.SourceElementIndices }).ToArray();
var requirements = report.Requirements.Select(r => new { path = paths[r.PathId], r.PathId, r.DeclaredType, r.Required, r.ScopeUnknown, r.Count }).ToArray();
string? error = null; string[]? before = null, after = null;
string? data = args[2] switch
{
    "both" => "{\"Name\":\"root\",\"Items\":[{\"Name\":\"child\"},{\"Other\":1}]}",
    "item" => "{\"Name\":\"root\",\"Items\":[{\"Code\":\"child\"}]}",
    "empty-invalid" => "{\"Name\":\"root\",\"Items\":[]}",
    "wrong-type" => "{\"Name\":\"root\",\"Items\":1}",
    _ => null,
};
if (data != null)
{
    input.Position = 0; WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
    before = document.Paragraphs.Select(p => p.Text).ToArray();
    try { document.MergeJson(data); } catch (InvalidDataException e) { error = e.Message.Split(':')[0]; }
    after = document.Paragraphs.Select(p => p.Text).ToArray();
    using var output = File.Create(Path.Combine(directory, "merged.docx")); document.Save(output, DocumentFormat.Docx, options);
}
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements, error, before, after }));
Console.WriteLine("Inspected repeated-field scope and observed bounded merges.");
