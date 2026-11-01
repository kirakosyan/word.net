#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 4 || args[3] is not ("required" or "fallback" or "missing-parent" or "optional-parent" or "wrong-parent" or "optional-leaf" or "null-parent" or "deep"))
    throw new ArgumentException("Expected DOCX, JSON, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[2]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
string scenario = args[3];
TemplateSchemaPath[] declarations;
if (scenario == "deep")
    declarations = new[] { new TemplateSchemaPath("Items", TemplatePathType.Collection),
        TemplateSchemaPath.ForRepeatItem("Items", "Account", TemplatePathType.Mapping),
        TemplateSchemaPath.ForRepeatItem("Items", "Account.Contact", TemplatePathType.Mapping),
        TemplateSchemaPath.ForRepeatItem("Items", "Account.Contact.Name", TemplatePathType.Scalar) };
else
{
    TemplateSchemaPath[] root = { new TemplateSchemaPath("Items", TemplatePathType.Collection),
        new TemplateSchemaPath("Customer", TemplatePathType.Mapping), new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar) };
    TemplateSchemaPath[] parent = scenario switch
    {
        "missing-parent" => Array.Empty<TemplateSchemaPath>(),
        "optional-parent" => new[] { TemplateSchemaPath.ForRepeatItem("Items", "Customer", TemplatePathType.Mapping, false) },
        "wrong-parent" => new[] { TemplateSchemaPath.ForRepeatItem("Items", "Customer", TemplatePathType.Scalar) },
        _ => new[] { TemplateSchemaPath.ForRepeatItem("Items", "Customer", TemplatePathType.Mapping) },
    };
    declarations = root.Concat(parent).Concat(new[] { TemplateSchemaPath.ForRepeatItem("Items", "Customer.Name", TemplatePathType.Scalar, scenario != "optional-leaf") }).ToArray();
}
var schema = new TemplateDataSchema(declarations); var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Docx, options);
string json = File.ReadAllText(args[1]); TemplateDataInspection data = schema.InspectJson(json, options: options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(new { template = report, data }));
var paths = report.GetSensitivePathAliases(); var parts = report.GetSensitivePartAliases(); var dataPaths = data.GetSensitivePathAliases();
var findings = report.Findings.Select(f => new { f.Code, f.Unknown, path = f.PathId == null ? null : paths[f.PathId], scope = f.ScopePathId == null ? null : paths[f.ScopePathId], part = f.PartId == null ? null : parts[f.PartId], indices = f.SourceElementIndices }).ToArray();
var requirements = report.Requirements.Select(r => new { r.PathId, path = paths[r.PathId], scope = r.ScopePathId == null ? null : paths[r.ScopePathId], r.ScopeUnknown, r.DeclaredType, r.Required, r.Count }).ToArray();
var dataFindings = data.Findings.Select(f => new { f.Code, path = dataPaths[f.PathId], scope = f.ScopePathId == null ? null : dataPaths[f.ScopePathId], f.ItemIndex, f.Unknown }).ToArray();
input.Position = 0;
WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
string[] before = document.Paragraphs.Select(p => p.Text).ToArray();
document.MergeJson(json);
string[] after = document.Paragraphs.Select(p => p.Text).ToArray();
using (var output = File.Create(Path.Combine(directory, "merged.docx"))) document.Save(output, DocumentFormat.Docx, options);
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, requirements, data.HasErrors, data.HasUnknowns, dataFindings, before, after }));
Console.WriteLine("Inspected declared nested item paths and observed a bounded JSON merge.");
