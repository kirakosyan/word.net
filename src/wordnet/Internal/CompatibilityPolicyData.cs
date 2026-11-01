using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WordNet.Internal;

// Internal foundation for the design contract. Parsing a policy does not admit a plan or authorize output.
internal sealed class CompatibilityPolicyData
{
    private const int MaximumCharacters = 1024 * 1024;
    private readonly ReadOnlyCollection<string> requiredSemantics;
    private readonly ReadOnlyCollection<CompatibilityPolicyLoss> allowedLosses;

    private CompatibilityPolicyData(string planDigest, List<string> semantics, List<CompatibilityPolicyLoss> losses)
    {
        PlanDigest = planDigest; requiredSemantics = semantics.AsReadOnly(); allowedLosses = losses.AsReadOnly();
    }

    internal string PlanDigest { get; }
    internal IReadOnlyList<string> RequiredSemantics => requiredSemantics;
    internal IReadOnlyList<CompatibilityPolicyLoss> AllowedLosses => allowedLosses;

    internal static CompatibilityPolicyData Parse(string json, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(json, nameof(json)); cancellationToken.ThrowIfCancellationRequested();
        if (json.Length > MaximumCharacters) throw new InvalidDataException("Limit.CompatibilityPolicy: Policy text exceeds its bound.");
        var options = new ConversionOptions(maximumCharacters: MaximumCharacters, maximumNestingDepth: 16);
        var context = new ConversionContext(options, cancellationToken);
        object parsed = TemplateData.Json(json, context);
        Dictionary<string, object?> root = Object(parsed, "schemaVersion", "planDigest", "requiredSemantics", "allowedLosses");
        if (root["schemaVersion"] is not string version || version != "1.0") throw Invalid();
        if (root["planDigest"] is not string digest || digest.Length != 64 || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw Invalid();

        List<object?> rawSemantics = Array(root["requiredSemantics"], 256);
        var semantics = new List<string>(rawSemantics.Count);
        foreach (object? item in rawSemantics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not string name || name is not ("content.text" or "content.links")) throw new InvalidDataException("Policy.UnknownSemantic: The required semantic is not registered.");
            semantics.Add(name);
        }
        semantics.Sort(StringComparer.Ordinal);
        for (int index = 1; index < semantics.Count; index++) if (semantics[index] == semantics[index - 1]) throw Invalid();

        List<object?> rawLosses = Array(root["allowedLosses"], 4096);
        var losses = new List<CompatibilityPolicyLoss>(rawLosses.Count);
        foreach (object? item in rawLosses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, object?> loss = Object(item, "code", "location");
            if (loss["code"] is not string code || code != "Docx.ThemeFont") throw new InvalidDataException("Policy.UnknownLoss: The loss code is not registered.");
            Dictionary<string, object?> location = Object(loss["location"], "partId", "childIndexes");
            if (location["partId"] is not string part || part.Length is < 9 or > 13 || !part.StartsWith("part-", StringComparison.Ordinal)
                || part.Skip(5).Any(c => c is < '0' or > '9')) throw Invalid();
            List<object?> rawIndices = Array(location["childIndexes"], 64); var indices = new List<int>(rawIndices.Count);
            foreach (object? rawIndex in rawIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rawIndex is not decimal number || number < 1 || number > int.MaxValue || decimal.Truncate(number) != number) throw Invalid();
                indices.Add((int)number);
            }
            losses.Add(new CompatibilityPolicyLoss(code, part, indices));
        }
        losses.Sort(CompatibilityPolicyLoss.Compare);
        for (int index = 1; index < losses.Count; index++) if (CompatibilityPolicyLoss.Compare(losses[index - 1], losses[index]) == 0) throw Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        return new CompatibilityPolicyData(digest, semantics, losses);
    }

    internal string CanonicalJson(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var text = new StringBuilder("{\"allowedLosses\":[");
        for (int index = 0; index < allowedLosses.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested(); if (index != 0) text.Append(',');
            CompatibilityPolicyLoss loss = allowedLosses[index];
            text.Append("{\"code\":\"").Append(loss.Code).Append("\",\"location\":{\"childIndexes\":[");
            for (int child = 0; child < loss.ChildIndexes.Count; child++)
            {
                if (child != 0) text.Append(','); text.Append(loss.ChildIndexes[child].ToString(CultureInfo.InvariantCulture));
            }
            text.Append("],\"partId\":\"").Append(loss.PartId).Append("\"}}");
        }
        text.Append("],\"planDigest\":\"").Append(PlanDigest).Append("\",\"requiredSemantics\":[");
        for (int index = 0; index < requiredSemantics.Count; index++)
        { cancellationToken.ThrowIfCancellationRequested(); if (index != 0) text.Append(','); text.Append('"').Append(requiredSemantics[index]).Append('"'); }
        text.Append("],\"schemaVersion\":\"1.0\"}"); cancellationToken.ThrowIfCancellationRequested();
        return text.ToString();
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

    private static Dictionary<string, object?> Object(object? value, params string[] names)
    {
        if (value is not Dictionary<string, object?> data || data.Count != names.Length || names.Any(name => !data.ContainsKey(name))) throw Invalid();
        return data;
    }

    private static List<object?> Array(object? value, int maximum)
    {
        if (value is not List<object?> array || array.Count > maximum) throw Invalid(); return array;
    }

    private static InvalidDataException Invalid() => new InvalidDataException("Policy.Invalid: The policy does not match the bounded version-1 contract.");
}

internal sealed class CompatibilityPolicyLoss
{
    private readonly ReadOnlyCollection<int> indices;
    internal CompatibilityPolicyLoss(string code, string partId, List<int> indices)
    { Code = code; PartId = partId; this.indices = System.Array.AsReadOnly(indices.ToArray()); }
    internal string Code { get; }
    internal string PartId { get; }
    internal IReadOnlyList<int> ChildIndexes => indices;

    internal static int Compare(CompatibilityPolicyLoss left, CompatibilityPolicyLoss right)
    {
        int result = StringComparer.Ordinal.Compare(left.Code, right.Code); if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(left.PartId, right.PartId); if (result != 0) return result;
        for (int index = 0; index < Math.Min(left.indices.Count, right.indices.Count); index++)
        { result = left.indices[index].CompareTo(right.indices[index]); if (result != 0) return result; }
        return left.indices.Count.CompareTo(right.indices.Count);
    }
}
