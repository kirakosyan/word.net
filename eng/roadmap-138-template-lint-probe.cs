using System;
using System.IO;
using System.Text.Json;
using WordNet;

if (args.Length != 3) throw new ArgumentException("Expected specimen name, DOCX path and new output directory.");
string name = args[0]; Directory.CreateDirectory(args[2]);
var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar),
    new TemplateSchemaPath("Total", name == "type" ? TemplatePathType.Date : TemplatePathType.Number));
using (var input = File.OpenRead(args[1]))
{
    TemplateInspection report = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Docx, new ConversionOptions(FidelityMode.Lenient));
    File.WriteAllText(Path.Combine(args[2], "report.json"), JsonSerializer.Serialize(report));
}
if (name == "repeat") { File.WriteAllText(Path.Combine(args[2], "result.txt"), "not-performed"); return; }
using var source = File.OpenRead(args[1]);
WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient));
try
{
    string data = name == "type" ? "{\"Total\":\"wrong\"}" : "{\"Customer\":{\"Name\":\"Ada\"},\"Total\":12.5}";
    document.MergeJson(data);
    using var saved = File.Create(Path.Combine(args[2], "saved.docx")); document.Save(saved, DocumentFormat.Docx);
    File.WriteAllText(Path.Combine(args[2], "result.txt"), "passed");
}
catch (InvalidDataException error)
{
    File.WriteAllText(Path.Combine(args[2], "result.txt"), error.Message.Split(':')[0]);
}
