using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 6 || args[5] is not ("directional" or "ordinary")) throw new ArgumentException("Expected input, saved DOCX, PDF, Arabic font, Devanagari font and expected profile.");
if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Choose new output paths.");
var options = new ConversionOptions(FidelityMode.BestEffort);
options.Typography.Enabled = true;
options.Typography.ExtendedBidi = true;
options.Typography.Devanagari = true;
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", args[3]));
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Devanagari", args[4]));
WordDocument document;
using (var input = File.OpenRead(args[0])) document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
using (var output = File.Create(args[1])) document.Save(output, DocumentFormat.Docx);
using (var output = File.Create(args[2]))
{
    ConversionResult result = document.Save(output, DocumentFormat.Pdf);
    if (result.Diagnostics.Any(item => item.Code == "Field.UnsupportedUsedCachedResult"))
        throw new InvalidDataException("The scalar value was not refreshed.");
    if (!result.Diagnostics.Any(item => item.Code == "Typography.FieldLayoutApproximation"))
        throw new InvalidDataException("The scalar baseline approximation was not diagnosed.");
    if (result.Diagnostics.Any(item => item.Code == "Typography.FieldDirectionalOrder") != (args[5] == "directional"))
        throw new InvalidDataException("The directional-field diagnostic did not match the profile.");
}
Console.WriteLine("Refreshed a scalar field, retained the DOCX owner and shaped its native output.");
