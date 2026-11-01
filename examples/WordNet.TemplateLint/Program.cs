using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WordNet;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: WordNet.TemplateLint <template.docx> [data.json]");
    return 2;
}

// Replace these names and types with a declaration owned by your application.
var schema = new TemplateDataSchema(
    new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar),
    new TemplateSchemaPath("Total", TemplatePathType.Number));
try
{
    var options = new ConversionOptions();
    using var input = File.OpenRead(args[0]);
    TemplateInspection report = new WordConverter().InspectTemplate(input, schema, DocumentFormat.Pdf, options);
    bool errors = report.Findings.Any(f => f.Code is "Plan.MissingPath" or "Plan.PathType" or "Plan.ParentType" or "Plan.InvalidField" or "Plan.InvalidControl" or "Plan.InvalidControlPath" or "Plan.InvalidControlStructure");
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    if (args.Length == 1)
    {
        Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
    }
    else
    {
        // Read at most the configured character bound before passing JSON to the bounded parser.
        using var dataInput = File.OpenRead(args[1]);
        using var reader = new StreamReader(dataInput, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var data = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > options.MaximumCharacters - data.Length) throw new InvalidDataException("Limit.Characters: JSON data exceeds the character limit.");
            data.Append(buffer, 0, count);
        }
        TemplateDataInspection dataReport = schema.InspectJson(data.ToString(), options: options);
        errors |= dataReport.HasErrors;
        Console.WriteLine(JsonSerializer.Serialize(new { template = report, data = dataReport }, jsonOptions));
    }
    // Unknown template coverage remains: neither successful data checks nor exit 1 approve output.
    return errors ? 2 : 1;
}
catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    // Do not print file paths, source names, document text or values on the default error path.
    Console.Error.WriteLine("Template lint could not inspect the supplied inputs.");
    return 2;
}
