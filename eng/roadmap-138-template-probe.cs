// Generate template outputs from a separately authored ODF fixture for independent reader checks.
// Compile in an isolated net10.0 project referencing the selected wordnet source or package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected an input ODT and an empty output directory.");
string inputPath = Path.GetFullPath(args[0]), outputPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outputPath);
if (Directory.GetFileSystemEntries(outputPath).Length != 0) throw new IOException("The output directory must be empty.");
byte[] input = File.ReadAllBytes(inputPath);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var results = new List<object>();
foreach (var item in new[] { (DocumentFormat.Ott, "generated-odf.ott"), (DocumentFormat.Dotx, "generated-word.dotx"), (DocumentFormat.Docx, "control-word.docx") })
{
    using var source = new MemoryStream(input); using var output = new MemoryStream();
    ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Odt, item.Item1, new ConversionOptions(FidelityMode.Lenient));
    byte[] bytes = output.ToArray();
    using (var file = new FileStream(Path.Combine(outputPath, item.Item2), FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
    results.Add(new { file = item.Item2, sha256 = Hash(bytes), diagnostics = result.Diagnostics.Select(d => new { d.Code, d.Severity }).ToArray() });
}
byte[] assembly = File.ReadAllBytes(typeof(WordConverter).Assembly.Location);
var report = new { input = Path.GetFileName(inputPath), inputSha256 = Hash(input), assembly = typeof(WordConverter).Assembly.FullName,
    assemblySha256 = Hash(assembly), output = results };
using var manifest = new FileStream(Path.Combine(outputPath, "probe.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
JsonSerializer.Serialize(manifest, report, new JsonSerializerOptions { WriteIndented = true });
