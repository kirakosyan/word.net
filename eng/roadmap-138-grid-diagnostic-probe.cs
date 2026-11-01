using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a source DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
byte[] source = File.ReadAllBytes(args[0]);
byte[] sentinel = { 13, 29, 41, 53 };
var converter = new WordConverter();
var lenient = new ConversionOptions(FidelityMode.Lenient);
using var input = new MemoryStream(source, writable: false);
using var pdf = new MemoryStream();
ConversionResult result = converter.Convert(input, pdf, DocumentFormat.Docx, DocumentFormat.Pdf, lenient);
File.WriteAllBytes(Path.Combine(directory, "output.pdf"), pdf.ToArray());
object[] diagnostics = result.Diagnostics.Select(item => new { item.Code, severity = item.Severity.ToString(), item.Count, item.LocationsTruncated,
    locations = item.Occurrences.Select(occurrence => new { part = occurrence.Location.Part, path = occurrence.Location.Path, phase = occurrence.Phase.ToString() }) }).Cast<object>().ToArray();
var strict = RunStrict(source);
using var editableSource = new MemoryStream(source, writable: false);
WordDocument document = WordDocument.Load(editableSource, DocumentFormat.Docx, out _, lenient);
using var clonedPdf = new MemoryStream(); clonedPdf.Write(sentinel, 0, sentinel.Length); clonedPdf.Position = 1;
string? clonedError = null;
try { document.Save(clonedPdf, DocumentFormat.Pdf, new ConversionOptions()); }
catch (NotSupportedException error) { clonedError = error.Message; }
bool clonedUntouched = clonedPdf.ToArray().SequenceEqual(sentinel) && clonedPdf.Position == 1;
using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, lenient);
File.WriteAllBytes(Path.Combine(directory, "saved.docx"), saved.ToArray());
var savedStrict = RunStrict(saved.ToArray());
var payload = new { inputOpen = input.CanRead, diagnostics, strict, clonedError, clonedUntouched, savedStrict };
File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(payload));
Console.WriteLine("Rendered, inspected, and exercised strict source, cloned and saved output.");

object RunStrict(byte[] data)
{
    using var sourceStream = new MemoryStream(data, writable: false);
    using var destination = new MemoryStream(); destination.Write(sentinel, 0, sentinel.Length); destination.Position = 1;
    string? error = null;
    try { converter.Convert(sourceStream, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions()); }
    catch (NotSupportedException exception) { error = exception.Message; }
    return new { error, outputUntouched = destination.ToArray().SequenceEqual(sentinel) && destination.Position == 1,
                 inputOpen = sourceStream.CanRead, outputOpen = destination.CanWrite };
}
