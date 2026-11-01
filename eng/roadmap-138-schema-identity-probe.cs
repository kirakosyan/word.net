#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 3 || args[2] is not ("empty" or "base" or "reordered" or "type" or "required" or "scope" or "name" or "case" or "root-item" or "extra"))
    throw new ArgumentException("Expected a DOCX, a new output directory and a known scenario.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
string scenario = args[2];
TemplateSchemaPath[] declarations = scenario == "empty" ? Array.Empty<TemplateSchemaPath>() : new[]
{
    new TemplateSchemaPath("Items", TemplatePathType.Collection),
    new TemplateSchemaPath(scenario == "case" ? "customer" : "Customer", TemplatePathType.Mapping),
    scenario == "root-item" ? new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar)
        : TemplateSchemaPath.ForRepeatItem(scenario == "scope" ? "Orders" : "Items", scenario == "name" ? "Customer.Code" : "Customer.Name",
            scenario == "type" ? TemplatePathType.Number : TemplatePathType.Scalar, scenario != "required"),
    TemplateSchemaPath.ForRepeatItem("Items", "Customer", TemplatePathType.Mapping),
};
if (scenario == "reordered") declarations = declarations.Reverse().ToArray();
if (scenario == "extra") declarations = declarations.Concat(new[] { new TemplateSchemaPath("Amount", TemplatePathType.Number) }).ToArray();
var schema = new TemplateDataSchema(declarations); var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
TemplateInspection template = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Docx, options);
TemplateDataInspection data = schema.InspectJson("{\"Customer\":{\"Name\":\"root\"},\"Items\":[{\"Customer\":{\"Name\":\"child\"}}]}", options: options);
File.WriteAllText(Path.Combine(directory, "default.json"), JsonSerializer.Serialize(new { template, data }));
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { identity = schema.IdentitySha256, templateSchemaSha256 = template.DataSchemaSha256, dataSchemaSha256 = data.DataSchemaSha256, template.IsReusable }));
Console.WriteLine("Computed bounded schema identity and inspected synthetic input without merging.");
