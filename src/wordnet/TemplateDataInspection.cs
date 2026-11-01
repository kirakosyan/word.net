using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;

namespace WordNet;

/// <summary>A privacy-safe mismatch between JSON data and a declared template path.</summary>
public sealed class TemplateDataFinding
{
    internal TemplateDataFinding(string code, string remediation, string path, string? scope, int? itemIndex, bool unknown)
    { Code = code; Remediation = remediation; PathId = path; ScopePathId = scope; ItemIndex = itemIndex; Unknown = unknown; }
    /// <summary>Gets a stable machine-readable finding code.</summary>
    public string Code { get; }
    /// <summary>Gets a machine-readable action identifier without path names or data values.</summary>
    public string Remediation { get; }
    /// <summary>Gets the opaque alias of the declared path.</summary>
    public string PathId { get; }
    /// <summary>Gets the opaque repeat collection alias for an item finding, or null.</summary>
    public string? ScopePathId { get; }
    /// <summary>Gets a zero-based collection item index when the finding concerns one item.</summary>
    /// <remarks>Indexes can reveal data structure and should be handled accordingly.</remarks>
    public int? ItemIndex { get; }
    /// <summary>Gets whether missing scope prevented assessment rather than establishing a mismatch.</summary>
    public bool Unknown { get; }
}

/// <summary>A bounded comparison of JSON data with caller declarations, not a template or output guarantee.</summary>
public sealed class TemplateDataInspection
{
    private readonly IReadOnlyDictionary<string, string> paths;
    internal TemplateDataInspection(List<TemplateDataFinding> findings, IDictionary<string, string> paths)
    {
        Findings = findings.AsReadOnly();
        this.paths = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(paths, StringComparer.Ordinal));
    }
    /// <summary>Gets mismatches and explicit unknowns without data names or values.</summary>
    public IReadOnlyList<TemplateDataFinding> Findings { get; }
    /// <summary>Gets whether a declared path or item has a definite mismatch.</summary>
    public bool HasErrors => Findings.Any(finding => !finding.Unknown);
    /// <summary>Gets whether a missing repeat collection prevented item assessment.</summary>
    public bool HasUnknowns => Findings.Any(finding => finding.Unknown);
    /// <summary>Returns alias-to-path names, explicitly revealing caller data names.</summary>
    public IReadOnlyDictionary<string, string> GetSensitivePathAliases() => paths;
}

public sealed partial class TemplateDataSchema
{
    /// <summary>Checks bounded JSON data against these root and repeat-item declarations without merging a template.</summary>
    /// <param name="json">A JSON object. Duplicate keys and unsupported template keys fail.</param>
    /// <param name="maximumRows">Maximum items in any JSON array, from 1 to 100,000.</param>
    /// <param name="options">Input, depth, character and expansion bounds; no conversion or field clock is run.</param>
    /// <param name="cancellationToken">Cancels parsing or inspection without returning a partial report.</param>
    /// <remarks>Only declared paths are checked. Required item paths are checked on each present item, even if a later merge could fall back to a root value. Null values are admitted as empty template values. JSON has no date or image object type; non-null values cannot satisfy those declarations. This is not a reusable plan or output validation.</remarks>
    public TemplateDataInspection InspectJson(string json, int maximumRows = 10000, ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(json, nameof(json));
        if (maximumRows is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        options ??= new ConversionOptions();
        var context = new ConversionContext(options, cancellationToken);
        context.CheckCancellation();
        Dictionary<string, object?> root = TemplateData.Snapshot(TemplateData.Json(json, context), context, maximumRows);
        int work = 0;
        void Work()
        {
            context.CheckCancellation();
            if (++work > 100000) throw new InvalidDataException("Limit.TemplateDataInspection: Inspection work limit exceeded.");
        }
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in Paths.SelectMany(path => path.RepeatCollectionPath == null
                ? new[] { path.Path } : new[] { path.Path, path.RepeatCollectionPath! }).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
        {
            Work();
            if (aliases.Count == 4096) throw new InvalidDataException("Limit.TemplateDataInspection: Alias limit exceeded.");
            aliases.Add(name, "path-" + (aliases.Count + 1).ToString("D4", CultureInfo.InvariantCulture));
        }
        var findings = new List<TemplateDataFinding>();
        void Add(string code, string path, string? scope = null, int? index = null, bool unknown = false)
        {
            Work();
            if (findings.Count == 4096) throw new InvalidDataException("Limit.TemplateDataInspection: Finding limit exceeded.");
            string remediation = code switch
            {
                "Data.MissingPath" => "Remediation.ProvidePath",
                "Data.MissingItemPath" => "Remediation.ProvideItemPath",
                "Data.ScopeMissing" => "Remediation.ProvideCollection",
                "Data.PathType" or "Data.ItemPathType" or "Data.ScopeType" => "Remediation.FixDataType",
                _ => throw new InvalidOperationException("Unknown template data finding code."),
            };
            findings.Add(new TemplateDataFinding(code, remediation, aliases[path], scope == null ? null : aliases[scope], index, unknown));
        }
        bool Lookup(object? value, string path, out object? found)
        {
            found = value;
            foreach (string part in path.Split('.'))
            {
                Work();
                if (found is not Dictionary<string, object?> map || !map.TryGetValue(part, out found)) { found = null; return false; }
            }
            return true;
        }
        static bool Compatible(object? value, TemplatePathType type) => value == null || (type switch
        {
            TemplatePathType.Scalar => value is string or bool or decimal or double,
            TemplatePathType.Number => value is decimal or double,
            TemplatePathType.Mapping => value is Dictionary<string, object?>,
            TemplatePathType.Collection => value is List<object?>,
            _ => false,
        });
        foreach (TemplateSchemaPath path in Paths.OrderBy(item => item.RepeatCollectionPath, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal))
        {
            Work();
            if (path.RepeatCollectionPath == null)
            {
                if (!Lookup(root, path.Path, out object? value))
                { if (path.Required) Add("Data.MissingPath", path.Path); }
                else if (!Compatible(value, path.Type)) Add("Data.PathType", path.Path);
                continue;
            }
            string scope = path.RepeatCollectionPath;
            if (!Lookup(root, scope, out object? collection))
            { Add("Data.ScopeMissing", path.Path, scope, unknown: true); continue; }
            if (collection == null) continue;
            if (collection is not List<object?> items) { Add("Data.ScopeType", path.Path, scope); continue; }
            for (int index = 0; index < items.Count; index++)
            {
                Work();
                if (!Lookup(items[index], path.Path, out object? value))
                { if (path.Required) Add("Data.MissingItemPath", path.Path, scope, index); }
                else if (!Compatible(value, path.Type)) Add("Data.ItemPathType", path.Path, scope, index);
            }
        }
        context.CheckCancellation();
        return new TemplateDataInspection(findings, aliases.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal));
    }
}
