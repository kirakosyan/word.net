using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WordNet.Internal;

// Internal design-contract prototype. A self-hashed JSON plan is not trusted provenance and never authorizes output.
internal sealed class CompatibilityPlanData
{
    private const int MaximumCharacters = 4 * 1024 * 1024;
    private static readonly string[] RootRequired = { "schemaVersion", "identities", "sourceFormat", "destinationFormat", "stages", "requirements", "semantics", "findings" };
    private static readonly string[] RootOptional = { "observedInputBytes", "estimatedPages" };
    private static readonly string[] IdentityKeys = { "input", "options", "dataSchema", "fonts", "providers", "engine" };
    private static readonly string[] StageKeys = { "parse", "destination", "layout", "write", "validation" };
    private static readonly string[] RequirementKeys = { "pathId", "type", "required", "owner", "support" };
    private static readonly string[] SemanticKeys = { "code", "assessment" };
    private static readonly string[] FindingKeys = { "code", "phase", "evidence", "severity", "count", "locationsTruncated", "locations", "remediation" };
    private static readonly string[] FindingOrder = { "code", "phase", "evidence", "severity" };
    private static readonly string[] LocationKeys = { "partId", "childIndexes" };
    private readonly Dictionary<string, object?> plan;
    private CompatibilityPlanData(Dictionary<string, object?> plan) => this.plan = plan;

    internal static CompatibilityPlanData Parse(string json, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(json, nameof(json)); cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > MaximumCharacters) throw new InvalidDataException("Limit.CompatibilityPlan: Plan text exceeds its bound.");
        var context = new ConversionContext(new ConversionOptions(maximumCharacters: MaximumCharacters, maximumNestingDepth: 16), cancellationToken);
        var root = Object(TemplateData.Json(json, context), RootRequired, RootOptional);
        Choice(root["schemaVersion"], "1.0"); Choice(root["sourceFormat"], "docx");
        string destination = Choice(root["destinationFormat"], "docx", "pdf", "xps");
        var identities = Object(root["identities"], IdentityKeys);
        foreach (object? value in identities.Values) Digest(value);
        var stages = Object(root["stages"], StageKeys);
        Choice(stages["parse"], "performed"); Choice(stages["destination"], "performed");
        string layout = Choice(stages["layout"], "performed", "not-performed");
        Choice(stages["write"], "not-performed"); Choice(stages["validation"], "not-performed");
        if (destination == "docx" && layout == "performed") throw Invalid();
        if (root.TryGetValue("observedInputBytes", out object? bytes)) Integer(bytes, 0);
        if (root.TryGetValue("estimatedPages", out object? pages) && pages != null) Integer(pages, 1);

        var requirements = Array(root["requirements"], 4096);
        foreach (object? value in requirements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = Object(value, RequirementKeys);
            Alias(item["pathId"], "path-"); Choice(item["type"], "string", "boolean", "decimal", "date", "image", "object", "collection", "unknown");
            Boolean(item["required"]); Choice(item["owner"], "body", "cell", "header", "footer", "unknown"); Choice(item["support"], "supported", "unsupported", "unknown");
        }
        SortUnique(requirements, (left, right) => CompareText(Member(left, "pathId"), Member(right, "pathId")));

        var semantics = Array(root["semantics"], 256);
        foreach (object? value in semantics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = Object(value, SemanticKeys);
            Code(item["code"], lower: true); Choice(item["assessment"], "supported", "unsupported", "unknown");
        }
        SortUnique(semantics, (left, right) => CompareText(Member(left, "code"), Member(right, "code")));

        var findings = Array(root["findings"], 4096);
        foreach (object? value in findings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = Object(value, FindingKeys);
            Code(item["code"], lower: false); Code(item["remediation"], lower: false);
            string phase = Choice(item["phase"], "parse", "provider", "destination", "layout");
            string evidence = Choice(item["evidence"], "observed", "predicted", "unknown");
            Choice(item["severity"], "info", "warning"); int count = Integer(item["count"], 1); Boolean(item["locationsTruncated"]);
            if (phase == "layout" && evidence == "observed" && layout != "performed") throw Invalid();
            var locations = Array(item["locations"], 128); if (locations.Count > count) throw Invalid();
            foreach (object? location in locations) ValidateLocation(location, cancellationToken);
            SortUnique(locations, CompareLocation);
        }
        SortUnique(findings, CompareFinding);
        cancellationToken.ThrowIfCancellationRequested(); return new CompatibilityPlanData(root);
    }

    internal string CanonicalJson(CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder(); Write(plan, output, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        return output.ToString();
    }

    internal string CanonicalSha256(CancellationToken cancellationToken = default)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(CanonicalJson(cancellationToken)); cancellationToken.ThrowIfCancellationRequested();
#if NET8_0_OR_GREATER
        byte[] hash = SHA256.HashData(bytes);
#else
        using var algorithm = SHA256.Create(); byte[] hash = algorithm.ComputeHash(bytes);
#endif
#if NET10_0_OR_GREATER
        return Convert.ToHexStringLower(hash);
#elif NET8_0_OR_GREATER
        return Convert.ToHexString(hash).ToLowerInvariant();
#else
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
    }

    // This is only the design's closed admission oracle. It does not authenticate a plan, run conversion or validate output.
    internal string CheckAdmission(CompatibilityPolicyData policy, bool strict, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(policy, nameof(policy)); cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(policy.PlanDigest, CanonicalSha256(cancellationToken), StringComparison.Ordinal)) return "Policy.StalePlan";
        foreach (object? value in Array(plan["requirements"], 4096))
        {
            cancellationToken.ThrowIfCancellationRequested(); var item = (Dictionary<string, object?>)value!;
            if ((string)item["type"]! == "unknown" || (string)item["owner"]! == "unknown" || (string)item["support"]! != "supported") return "Policy.PathUnknown";
        }
        var semantics = Array(plan["semantics"], 256).Select(value => (Dictionary<string, object?>)value!).ToDictionary(item => (string)item["code"]!, StringComparer.Ordinal);
        if (semantics.Any(item => item.Key is not ("content.text" or "content.links")
            || (string)item.Value["assessment"]! != "supported" && !(item.Key == "content.links" && (string)item.Value["assessment"]! == "unsupported"))) return "Policy.SemanticUnknown";
        foreach (string required in policy.RequiredSemantics)
        {
            if (!semantics.TryGetValue(required, out var value)) return "Policy.RequiredSemanticUnknown";
            if ((string)value["assessment"]! == "unsupported") return "Policy.RequiredSemanticUnsupported";
            if ((string)value["assessment"]! != "supported") return "Policy.RequiredSemanticUnknown";
        }
        var matched = new HashSet<CompatibilityPolicyLoss>();
        foreach (object? value in Array(plan["findings"], 4096))
        {
            cancellationToken.ThrowIfCancellationRequested(); var finding = (Dictionary<string, object?>)value!;
            string code = (string)finding["code"]!, evidence = (string)finding["evidence"]!, remediation = (string)finding["remediation"]!;
            if (code != "Docx.ThemeFont" || remediation != "Remediation.SetExplicitFont" || evidence != "observed"
                || (string)finding["severity"]! != "warning") return "Policy.UnknownFinding";
            if (strict) return "Policy.StrictLoss";
            var locations = Array(finding["locations"], 128);
            if ((bool)finding["locationsTruncated"]! || locations.Count == 0) return "Policy.LocationUnknown";
            foreach (object? rawLocation in locations)
            {
                cancellationToken.ThrowIfCancellationRequested(); var location = (Dictionary<string, object?>)rawLocation!;
                CompatibilityPolicyLoss? match = policy.AllowedLosses.FirstOrDefault(loss => loss.Code == code && SameLocation(loss, location));
                if (match == null) return "Policy.LossNotAllowed"; matched.Add(match);
            }
        }
        if (matched.Count != policy.AllowedLosses.Count) return "Policy.UnmatchedPermission";
        cancellationToken.ThrowIfCancellationRequested(); return "Policy.Admitted";
    }

    private static bool SameLocation(CompatibilityPolicyLoss loss, Dictionary<string, object?> location)
    {
        if (loss.PartId != (string)location["partId"]!) return false;
        List<object?> values = (List<object?>)location["childIndexes"]!;
        return loss.ChildIndexes.Count == values.Count && loss.ChildIndexes.Select((value, index) => value == (int)(decimal)values[index]!).All(equal => equal);
    }

    private static void ValidateLocation(object? value, CancellationToken cancellationToken)
    {
        var location = Object(value, LocationKeys); Alias(location["partId"], "part-");
        foreach (object? index in Array(location["childIndexes"], 64)) { cancellationToken.ThrowIfCancellationRequested(); Integer(index, 1); }
    }

    private static int CompareLocation(object? left, object? right)
    {
        int result = CompareText(Member(left, "partId"), Member(right, "partId")); if (result != 0) return result;
        var a = (List<object?>)Member(left, "childIndexes")!; var b = (List<object?>)Member(right, "childIndexes")!;
        for (int index = 0; index < Math.Min(a.Count, b.Count); index++)
        { result = ((decimal)a[index]!).CompareTo((decimal)b[index]!); if (result != 0) return result; }
        return a.Count.CompareTo(b.Count);
    }

    private static int CompareFinding(object? left, object? right)
    {
        foreach (string key in FindingOrder)
        { int result = CompareText(Member(left, key), Member(right, key)); if (result != 0) return result; }
        return 0;
    }

    private static void SortUnique(List<object?> values, Comparison<object?> comparison)
    {
        values.Sort(comparison); for (int index = 1; index < values.Count; index++) if (comparison(values[index - 1], values[index]) == 0) throw Invalid();
    }

    private static object? Member(object? value, string key) => ((Dictionary<string, object?>)value!)[key];
    private static int CompareText(object? left, object? right) => StringComparer.Ordinal.Compare((string)left!, (string)right!);
    private static Dictionary<string, object?> Object(object? value, string[] required, string[]? optional = null)
    {
        if (value is not Dictionary<string, object?> result || required.Any(key => !result.ContainsKey(key))
            || result.Keys.Any(key => !required.Contains(key, StringComparer.Ordinal) && !(optional?.Contains(key, StringComparer.Ordinal) ?? false))) throw Invalid();
        return result;
    }
    private static List<object?> Array(object? value, int maximum) { if (value is not List<object?> result || result.Count > maximum) throw Invalid(); return result; }
    private static bool Boolean(object? value) => value is bool result ? result : throw Invalid();
    private static int Integer(object? value, int minimum)
    {
        if (value is not decimal number || number < minimum || number > int.MaxValue || decimal.Truncate(number) != number) throw Invalid(); return (int)number;
    }
    private static string Choice(object? value, params string[] choices)
    { if (value is not string text || !choices.Contains(text, StringComparer.Ordinal)) throw Invalid(); return text; }
    private static void Digest(object? value)
    { if (value is not string text || text.Length != 64 || text.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw Invalid(); }
    private static void Alias(object? value, string prefix)
    {
        if (value is not string text || text.Length < prefix.Length + 4 || text.Length > prefix.Length + 8 || !text.StartsWith(prefix, StringComparison.Ordinal)
            || text.Skip(prefix.Length).Any(c => c is < '0' or > '9')) throw Invalid();
    }
    private static void Code(object? value, bool lower)
    {
        if (value is not string text || text.Length > 128) throw Invalid(); string[] segments = text.Split('.'); if (segments.Length < 2) throw Invalid();
        foreach (string part in segments)
        {
            if (part.Length == 0 || !Letter(part[0], lower) || part.Skip(1).Any(c => !Letter(c, lower) && c is not (>= '0' and <= '9'))) throw Invalid();
        }
        static bool Letter(char c, bool lower) => c is >= 'a' and <= 'z' || !lower && c is >= 'A' and <= 'Z';
    }
    private static InvalidDataException Invalid() => new InvalidDataException("Plan.Invalid: The plan does not match the bounded version-1 contract.");

    private static void Write(object? value, StringBuilder output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value == null) { output.Append("null"); return; }
        if (value is bool flag) { output.Append(flag ? "true" : "false"); return; }
        if (value is decimal number) { output.Append(number.ToString("0", CultureInfo.InvariantCulture)); return; }
        if (value is string text)
        {
            if (text.Any(c => c < ' ' || c > '~' || c is '"' or '\\')) throw Invalid();
            output.Append('"').Append(text).Append('"'); return;
        }
        if (value is List<object?> array)
        {
            output.Append('['); for (int index = 0; index < array.Count; index++) { if (index != 0) output.Append(','); Write(array[index], output, cancellationToken); }
            output.Append(']'); return;
        }
        if (value is Dictionary<string, object?> dictionary)
        {
            output.Append('{'); bool first = true;
            foreach (string key in dictionary.Keys.OrderBy(item => item, StringComparer.Ordinal))
            { if (!first) output.Append(','); first = false; Write(key, output, cancellationToken); output.Append(':'); Write(dictionary[key], output, cancellationToken); }
            output.Append('}'); return;
        }
        throw Invalid();
    }
}
