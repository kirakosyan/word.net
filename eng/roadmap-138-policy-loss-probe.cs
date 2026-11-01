// Public policy probe using independently authored theme and missing-link fixtures.
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
string permission = "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1,1,1,1]}}";
string Policy(string digest, string loss = "") => "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + digest
    + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":[" + loss + "]}";
var cases = new Dictionary<string, object>(StringComparer.Ordinal);
foreach (string name in new[] { "theme", "theme-second", "missing-link", "plain" })
{
    byte[] bytes = File.ReadAllBytes(Path.Combine(fixtureDirectory, name + ".docx"));
    using var initial = new MemoryStream(bytes, writable: false);
    TemplateCompatibilityPlan plan = new WordConverter().CompileTemplateCompatibilityPlan(initial, new TemplateDataSchema(), DocumentFormat.Pdf, clock, FidelityMode.BestEffort);
    string canonical = plan.GetCanonicalJson(), digest = plan.GetIdentitySha256(), policy = Policy(digest, name.StartsWith("theme", StringComparison.Ordinal) ? permission : "");
    using var source = new MemoryStream(bytes, writable: false); using var destination = new MemoryStream(); destination.WriteByte(42);
    TemplatePolicyOutcome result = plan.Execute(source, destination, policy);
    byte[] value = destination.ToArray();
    string? actualOutput = null;
    if (value.Length > 1)
    {
        byte[] pdf = value.Skip(1).ToArray(); File.WriteAllBytes(Path.Combine(outputDirectory, name + ".pdf"), pdf); actualOutput = Hash(pdf);
    }
    File.WriteAllText(Path.Combine(outputDirectory, name + "-plan.json"), canonical);
    cases.Add(name, new { inputSha256 = Hash(bytes), canonical, digest, policy, result.State, result.Code, result.ExpectedInputSha256,
        result.ObservedInputSha256, result.OptionsSha256, result.OutputSha256, result.IndependentValidation,
        findings = result.Findings.Select(item => new { item.Code, severity = item.Severity.ToString(), item.Count, item.LocationsTruncated,
            phases = item.Phases.Select(phase => phase.ToString()).ToArray(), item.SourceLocationsAvailable }).ToArray(),
        prefix = value[0], outputLength = value.Length - 1, actualOutputSha256 = actualOutput,
        streamsOpen = initial.CanRead && source.CanRead && destination.CanWrite });
}
byte[] themed = File.ReadAllBytes(Path.Combine(fixtureDirectory, "theme.docx"));
using var compile = new MemoryStream(themed, writable: false);
TemplateCompatibilityPlan themePlan = new WordConverter().CompileTemplateCompatibilityPlan(compile, new TemplateDataSchema(), DocumentFormat.Pdf, clock, FidelityMode.BestEffort);
object Reject(string policy)
{
    using var input = new MemoryStream(themed, writable: false); using var output = new MemoryStream(); output.WriteByte(43);
    TemplatePolicyOutcome outcome = themePlan.Execute(input, output, policy);
    return new { outcome.State, outcome.Code, inputPosition = input.Position, unchangedOutput = output.ToArray().SequenceEqual(new byte[] { 43 }) };
}
string strictError = "accepted";
using (var input = new MemoryStream(themed, writable: false))
{
    try { _ = new WordConverter().CompileTemplateCompatibilityPlan(input, new TemplateDataSchema(), DocumentFormat.Pdf, clock, FidelityMode.Strict); }
    catch (NotSupportedException error) { strictError = error.Message.Split(':')[0]; }
}
var report = new
{
    coreSha256 = Hash(File.ReadAllBytes(typeof(WordConverter).Assembly.Location)), cases,
    negatives = new { missingPermission = Reject(Policy(themePlan.GetIdentitySha256())),
        wrongLocation = Reject(Policy(themePlan.GetIdentitySha256(), permission.Replace("[1,1,1,1]", "[1,1,1,2]"))), strictError }
};
File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured exact permission and actual unexpected PDF write-stage observations.");
