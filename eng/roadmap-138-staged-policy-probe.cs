// Public staged-policy prototype probe, using independently authored DOCX fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

string fixtureDirectory = Path.GetFullPath(args[0]), outputDirectory = Path.GetFullPath(args[1]);
if (Directory.Exists(outputDirectory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(outputDirectory);
var clock = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
bool Unchanged(MemoryStream value, byte marker) { byte[] data = value.ToArray(); return data.Length == 1 && data[0] == marker; }
string Policy(string digest, string semantics = "\"content.text\"", string losses = "") =>
    "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + digest + "\",\"requiredSemantics\":[" + semantics + "],\"allowedLosses\":[" + losses + "]}";
var cases = new Dictionary<string, object>(StringComparer.Ordinal);
foreach (string name in new[] { "short", "maximum", "single", "lowercase", "mixed" })
{
    byte[] bytes = File.ReadAllBytes(Path.Combine(fixtureDirectory, name + ".docx"));
    using var compile = new MemoryStream(bytes, writable: false); TemplateCompatibilityPlan plan = new WordConverter().CompileTemplateCompatibilityPlan(compile, new TemplateDataSchema(), DocumentFormat.Pdf, clock);
    string canonical = plan.GetCanonicalJson(), digest = plan.GetIdentitySha256(), policy = Policy(digest);
    using var source = new MemoryStream(bytes, writable: false); using var destination = new MemoryStream(); destination.WriteByte(42);
    TemplatePolicyOutcome outcome = plan.Execute(source, destination, policy);
    byte[] bytesWithPrefix = destination.ToArray(); byte[] pdf = bytesWithPrefix.Skip(1).ToArray();
    File.WriteAllBytes(Path.Combine(outputDirectory, name + ".pdf"), pdf);
    File.WriteAllText(Path.Combine(outputDirectory, name + "-plan.json"), canonical);
    cases.Add(name, new { inputSha256 = Hash(bytes), canonical, digest, policy, outcome.State, outcome.Code, outcome.ExpectedInputSha256,
        outcome.ObservedInputSha256, outcome.OptionsSha256, outcome.OutputSha256, outcome.IndependentValidation,
        prefix = bytesWithPrefix[0], actualOutputSha256 = Hash(pdf), streamsOpen = compile.CanRead && source.CanRead && destination.CanWrite });
}
byte[] original = File.ReadAllBytes(Path.Combine(fixtureDirectory, "short.docx"));
using var initial = new MemoryStream(original, writable: false); TemplateCompatibilityPlan fixedPlan = new WordConverter().CompileTemplateCompatibilityPlan(initial, new TemplateDataSchema(), DocumentFormat.Pdf, clock);
using var changed = File.OpenRead(Path.Combine(fixtureDirectory, "maximum.docx")); using var changedOutput = new MemoryStream(); changedOutput.WriteByte(43);
TemplatePolicyOutcome changedResult = fixedPlan.Execute(changed, changedOutput, Policy(fixedPlan.GetIdentitySha256()));
using var stale = new MemoryStream(original, writable: false); using var staleOutput = new MemoryStream(); staleOutput.WriteByte(44);
TemplatePolicyOutcome staleResult = fixedPlan.Execute(stale, staleOutput, Policy(new string('0', 64)));
string extraLoss = "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[]}}";
using var unmatched = new MemoryStream(original, writable: false); using var unmatchedOutput = new MemoryStream(); unmatchedOutput.WriteByte(45);
TemplatePolicyOutcome unmatchedResult = fixedPlan.Execute(unmatched, unmatchedOutput, Policy(fixedPlan.GetIdentitySha256(), losses: extraLoss));
using var unknown = new MemoryStream(original, writable: false); using var unknownOutput = new MemoryStream(); unknownOutput.WriteByte(46); string unknownError = "accepted";
try { _ = fixedPlan.Execute(unknown, unknownOutput, Policy(fixedPlan.GetIdentitySha256(), "\"review.history\"")); }
catch (InvalidDataException error) { unknownError = error.Message.Split(':')[0]; }
var report = new
{
    coreSha256 = Hash(File.ReadAllBytes(typeof(WordConverter).Assembly.Location)), cases,
    negatives = new
    {
        changed = new { changedResult.State, changedResult.Code, unchangedOutput = Unchanged(changedOutput, 43), streamOpen = changed.CanRead },
        stale = new { staleResult.State, staleResult.Code, unchangedOutput = Unchanged(staleOutput, 44), position = stale.Position },
        unmatched = new { unmatchedResult.State, unmatchedResult.Code, unchangedOutput = Unchanged(unmatchedOutput, 45), position = unmatched.Position },
        unknown = new { code = unknownError, unchangedOutput = Unchanged(unknownOutput, 46), position = unknown.Position }
    }
};
File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured staged PDF execution and fail-closed policy negatives.");
