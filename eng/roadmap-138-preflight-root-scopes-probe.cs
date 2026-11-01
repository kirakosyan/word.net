#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("root" or "wrapped" or "repeat_table"))
    throw new ArgumentException("Expected a DOCX, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, new TemplateDataSchema(), DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(report));
var paths = report.GetSensitivePathAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.PartId, path = f.PathId == null ? null : paths[f.PathId], indices = f.SourceElementIndices }).ToArray();
var requirements = report.Requirements.Select(r => new { path = paths[r.PathId], r.DeclaredType, r.Required, r.ScopeUnknown, r.Count }).ToArray();
string? error = null; string[]? before = null, afterFailure = null, afterMerge = null;
if (args[2] == "root")
{
    input.Position = 0;
    WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
    before = document.Paragraphs.Select(p => p.Text).ToArray();
    try { document.MergeJson("{\"Items\":[{\"Inside\":\"inner\"}]}"); }
    catch (InvalidDataException e) { error = e.Message.Split(':')[0]; }
    afterFailure = document.Paragraphs.Select(p => p.Text).ToArray();
    document.MergeJson("{\"Items\":[{\"Inside\":\"inner\"}],\"Outside\":\"outer\",\"Cell\":\"cell\",\"Body\":\"body\"}");
    afterMerge = document.Paragraphs.Select(p => p.Text).ToArray();
    using var output = File.Create(Path.Combine(directory, "merged.docx")); document.Save(output, DocumentFormat.Docx, options);
}
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements, error, before, afterFailure, afterMerge }));
Console.WriteLine("Inspected independently authored root and repeating owner scopes.");
