// Independent contract probe; the policy parser remains internal and does not enforce conversion.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;
using WordNet.Internal;

string inputPath = Path.GetFullPath(args[0]), outputPath = Path.GetFullPath(args[1]), planPath = Path.GetFullPath(args[2]);
if (File.Exists(outputPath)) throw new IOException("Choose a new output path.");
string input = File.ReadAllText(inputPath);
CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(input);
string invalidVersion = input.Replace("\"1.0\"", "\"2.0\"");
string invalidLoss = input.Replace("Docx.ThemeFont", "Docx.Unknown");
string invalidSemantic = input.Replace("content.text", "review.history");
string invalidIndex = input.Replace("1,\n          2,\n          1", "1,\n          0,\n          1");
string duplicateVersion = input.Replace("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"1.0\", \"schemaVersion\": \"1.0\"");
if (new[] { invalidVersion, invalidLoss, invalidSemantic, invalidIndex, duplicateVersion }.Any(value => value == input))
    throw new InvalidOperationException("A negative-control mutation did not apply.");
string Error(string value)
{
    try { _ = CompatibilityPolicyData.Parse(value); return "accepted"; }
    catch (InvalidDataException error) { return error.Message.Split(':')[0]; }
}
string[] checks = { Error(invalidVersion), Error(invalidLoss), Error(invalidSemantic), Error(invalidIndex), Error(duplicateVersion) };
string unsortedPolicy = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + policy.PlanDigest + "\",\"requiredSemantics\":[],\"allowedLosses\":["
    + "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0002\",\"childIndexes\":[1]}},"
    + "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1.0,2]}},"
    + "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[]}}]}";
CompatibilityPolicyData sorted = CompatibilityPolicyData.Parse(unsortedPolicy);
string planInput = File.ReadAllText(planPath);
CompatibilityPlanData plan = CompatibilityPlanData.Parse(planInput);
string staleInput = planInput.Replace(new string('1', 64), new string('a', 64));
string uncertainInput = planInput.Replace("\"assessment\": \"supported\"", "\"assessment\": \"unknown\"");
string truncatedInput = planInput.Replace("\"locationsTruncated\": false", "\"locationsTruncated\": true");
if (new[] { staleInput, uncertainInput, truncatedInput }.Any(value => value == planInput))
    throw new InvalidOperationException("A plan negative-control mutation did not apply.");
CompatibilityPlanData uncertain = CompatibilityPlanData.Parse(uncertainInput), truncated = CompatibilityPlanData.Parse(truncatedInput);
CompatibilityPolicyData Rebind(CompatibilityPlanData value) => CompatibilityPolicyData.Parse(input.Replace(policy.PlanDigest, value.CanonicalSha256()));
string[] admissions =
{
    plan.CheckAdmission(policy, false), plan.CheckAdmission(policy, true),
    CompatibilityPlanData.Parse(staleInput).CheckAdmission(policy, false),
    uncertain.CheckAdmission(Rebind(uncertain), false), truncated.CheckAdmission(Rebind(truncated), false),
    plan.CheckAdmission(CompatibilityPolicyData.Parse(input.Replace("part-0001", "part-0002")), false)
};
var report = new
{
    inputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(inputPath))),
    coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(WordConverter).Assembly.Location))),
    policy.PlanDigest, canonical = policy.CanonicalJson(), canonicalSha256 = policy.CanonicalSha256(),
    required = policy.RequiredSemantics.ToArray(), losses = policy.AllowedLosses.Select(loss => new
    { loss.Code, loss.PartId, childIndexes = loss.ChildIndexes.ToArray() }).ToArray(), checks,
    unsortedPolicy, sortedCanonical = sorted.CanonicalJson(), sortedSha256 = sorted.CanonicalSha256(),
    planInputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(planPath))),
    planCanonical = plan.CanonicalJson(), planSha256 = plan.CanonicalSha256(), admissions
};
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured canonical policy and rejected malformed or unregistered inputs.");
