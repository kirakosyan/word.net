using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 7 || args[6] is not ("directional" or "ordinary")) throw new ArgumentException("Expected input, saved DOCX, PDF, three fonts and the expected profile.");
if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Choose new output paths.");
var options = new ConversionOptions(FidelityMode.BestEffort);
options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; options.Typography.Devanagari = true;
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", args[3]));
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Devanagari", args[4]));
options.Fonts.MathFamily = "STIX Two Math";
options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", args[5]));
WordDocument document;
using (var input = File.OpenRead(args[0])) document = WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.BestEffort));
using (var output = File.Create(args[1])) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(args[2]))
{
    ConversionResult result = document.Save(output, DocumentFormat.Pdf, options);
    if (!result.Diagnostics.Any(item => item.Code == "Object.EditableOutputLoss")
        || !result.Diagnostics.Any(item => item.Code == "Typography.EquationReferenceApproximation"))
        throw new InvalidDataException("The editable-object or reference-geometry loss was not diagnosed.");
    if (result.Diagnostics.Any(item => item.Code == "Typography.EquationDirectionalOrder") != (args[6] == "directional"))
        throw new InvalidDataException("The directional-order diagnostic did not match the profile.");
}
Console.WriteLine("Retained one editable equation and rendered native paragraphs with the independent math pipeline.");
