using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var cases = new[] { "valid", "a2a", "tagged_a2b", "native_archive", "invalid", "missing" };
foreach (string name in cases)
{
    string item = Path.Combine(directory, name); Directory.CreateDirectory(item);
    var options = new ConversionOptions(FidelityMode.Lenient); int clockCalls = 0;
    options.Clock = () => { clockCalls++; return new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero); };
    if (name == "a2a") options.Pdf.Conformance = PdfConformance.PdfA2a;
    if (name == "tagged_a2b") { options.Pdf.Conformance = PdfConformance.PdfA2b; options.Pdf.Accessibility = PdfAccessibility.Tagged; }
    if (name == "native_archive") { options.Pdf.Conformance = PdfConformance.PdfA2b; options.Typography.Enabled = true; }
    if (name == "invalid") options.Pdf.Conformance = (PdfConformance)99;
    if (name == "missing") options.Pdf = null!;
    using var input = File.OpenRead(args[0]);
    TemplateInspection report = new WordConverter().InspectTemplate(input, new TemplateDataSchema(), DocumentFormat.Pdf, options);
    int inspectionClockCalls = clockCalls;
    File.WriteAllText(Path.Combine(item, "default.json"), JsonSerializer.Serialize(report));
    input.Position = 0;
    WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
    using var output = new MemoryStream(); output.WriteByte(42); string? errorType = null, errorCode = null;
    try { document.Save(output, DocumentFormat.Pdf, options); }
    catch (ArgumentException e) { errorType = e.GetType().Name; errorCode = e.Message.Split(':')[0]; }
    catch (NotSupportedException e) { errorType = e.GetType().Name; errorCode = e.Message.Split(':')[0]; }
    bool unchanged = output.ToArray().SequenceEqual(new byte[] { 42 });
    if (errorType == null) File.WriteAllBytes(Path.Combine(item, "output.pdf"), output.ToArray().Skip(1).ToArray());
    var findings = report.Findings.Select(f => new { f.Code, f.Unknown, f.PartId, f.SourcePhase }).ToArray();
    File.WriteAllText(Path.Combine(item, "result.json"), JsonSerializer.Serialize(new { report.IsReusable, findings, inspectionClockCalls, errorType, errorCode, unchanged }));
    Console.WriteLine("Inspected and exercised output profile: " + name);
}
