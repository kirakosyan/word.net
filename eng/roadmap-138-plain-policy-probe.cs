// Independent-fixture probe for a narrow internal source profile; no policy execution is exposed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;
using WordNet.Internal;

string fixtureDirectory = Path.GetFullPath(args[0]), outputDirectory = Path.GetFullPath(args[1]);
if (Directory.Exists(outputDirectory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(outputDirectory);
string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
var cases = new Dictionary<string, object>(StringComparer.Ordinal);
foreach (string path in Directory.GetFiles(fixtureDirectory, "*.docx").OrderBy(value => value, StringComparer.Ordinal))
{
    byte[] bytes = File.ReadAllBytes(path); string name = Path.GetFileNameWithoutExtension(path);
    using var source = new MemoryStream(bytes, writable: false); string result;
    try { PlainDocxPolicyProfile.Validate(source); result = "accepted"; }
    catch (NotSupportedException error) { result = error.Message.Split(':')[0]; }
    if (result != "accepted")
    {
        cases.Add(name, new { sourceSha256 = Hash(bytes), result, streamOpen = source.CanRead });
        continue;
    }
    using var conversionSource = new MemoryStream(bytes, writable: false); using var destination = new MemoryStream();
    var fixedTime = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    var options = new ConversionOptions { Clock = () => fixedTime };
    ConversionResult conversion = new WordConverter().Convert(conversionSource, destination, DocumentFormat.Docx, DocumentFormat.Pdf, options);
    byte[] pdf = destination.ToArray(); File.WriteAllBytes(Path.Combine(outputDirectory, name + ".pdf"), pdf);
    cases.Add(name, new { sourceSha256 = Hash(bytes), result, streamOpen = source.CanRead && conversionSource.CanRead && destination.CanWrite,
        outputSha256 = Hash(pdf), diagnostics = conversion.Diagnostics.Select(item => new { item.Code, item.Severity, item.Count }).ToArray() });
}
var report = new { coreSha256 = Hash(File.ReadAllBytes(typeof(WordConverter).Assembly.Location)), cases };
File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured narrow source admission and strict PDF results.");
