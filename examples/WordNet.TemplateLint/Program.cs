using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: WordNet.TemplateLint <template.docx>");
    return 2;
}

// Replace these names and types with a declaration owned by your application.
// No data object, property getter, or value is passed to the inspection.
var schema = new TemplateDataSchema(
    new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar),
    new TemplateSchemaPath("Total", TemplatePathType.Number));
using var input = File.OpenRead(args[0]);
TemplateInspection report = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Pdf);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
// This first inspection always includes unknown coverage and is not an approval.
return report.Findings.Any(f => f.Code is "Plan.MissingPath" or "Plan.PathType" or "Plan.ParentType" or "Plan.InvalidField" or "Plan.InvalidControl" or "Plan.InvalidControlPath" or "Plan.InvalidControlStructure") ? 2 : 1;
