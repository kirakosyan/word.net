using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WordNet.Internal;

namespace WordNet;

/// <summary>The declared type of a template data path; no data values are inspected.</summary>
public enum TemplatePathType
{
    /// <summary>A scalar whose more precise type is not specified.</summary>
    Scalar = 0,
    /// <summary>A number supported by template number formatting.</summary>
    Number = 1,
    /// <summary>A date supported by template date formatting.</summary>
    Date = 2,
    /// <summary>A caller-provided template image.</summary>
    Image = 3,
    /// <summary>A named object.</summary>
    Mapping = 4,
    /// <summary>A collection of template values.</summary>
    Collection = 5,
}

/// <summary>A declared template path. Paths contain names, not values, and may themselves be sensitive.</summary>
public sealed class TemplateSchemaPath
{
    /// <summary>Declares a dotted path, its type, and whether it is guaranteed to be present.</summary>
    public TemplateSchemaPath(string path, TemplatePathType type, bool required = true)
    {
        Guard.NotNull(path, nameof(path));
        if (path.Length > 256 || !path.Split('.').All(TemplateData.Identifier)) throw new ArgumentException("Template.Path: Unsupported property path.", nameof(path));
        if (type is < TemplatePathType.Scalar or > TemplatePathType.Collection) throw new ArgumentOutOfRangeException(nameof(type));
        Path = path; Type = type; Required = required;
    }
    /// <summary>Gets the caller-supplied path. Do not publish it if names are sensitive.</summary>
    public string Path { get; }
    /// <summary>Gets the declared type.</summary>
    public TemplatePathType Type { get; }
    /// <summary>Gets whether the schema promises that the path is present.</summary>
    public bool Required { get; }
}

/// <summary>An immutable, bounded declaration of template paths, with no values or caller property access.</summary>
public sealed class TemplateDataSchema
{
    private readonly ReadOnlyCollection<TemplateSchemaPath> paths;
    /// <summary>Copies up to 4,096 unique path declarations.</summary>
    public TemplateDataSchema(params TemplateSchemaPath[] paths)
    {
        Guard.NotNull(paths, nameof(paths));
        if (paths.Length > 4096) throw new ArgumentOutOfRangeException(nameof(paths));
        var copy = (TemplateSchemaPath[])paths.Clone();
        if (copy.Any(p => p == null) || copy.Select(p => p.Path).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Template schema paths must be non-null and unique.", nameof(paths));
        this.paths = Array.AsReadOnly(copy);
    }
    /// <summary>Gets the declared paths. The names are caller data and may be sensitive.</summary>
    public IReadOnlyList<TemplateSchemaPath> Paths => paths;
}

/// <summary>A privacy-safe finding. Part aliases identify whole parts, not individual XML elements.</summary>
public sealed class TemplateInspectionFinding
{
    internal TemplateInspectionFinding(string code, string remediation, string? path, string? part, int count, bool unknown,
        DiagnosticSeverity severity, DiagnosticPhase? sourcePhase, bool locationsTruncated)
    {
        Code = code; Remediation = remediation; PathId = path; PartId = part; Count = count; Unknown = unknown;
        Severity = severity; SourcePhase = sourcePhase; LocationsTruncated = locationsTruncated;
    }
    /// <summary>Gets a stable machine-readable finding code.</summary>
    public string Code { get; }
    /// <summary>Gets a machine-readable remediation identifier.</summary>
    public string Remediation { get; }
    /// <summary>Gets an opaque path alias when one is known.</summary>
    public string? PathId { get; }
    /// <summary>Gets an opaque part-level alias when one is known.</summary>
    public string? PartId { get; }
    /// <summary>Gets the number of grouped observations.</summary>
    public int Count { get; }
    /// <summary>Gets the original source-diagnostic severity, or the inspection finding severity.</summary>
    public DiagnosticSeverity Severity { get; }
    /// <summary>Gets the original diagnostic phase when all retained occurrences share one; inspection findings have no source phase.</summary>
    public DiagnosticPhase? SourcePhase { get; }
    /// <summary>Gets whether source occurrence locations were omitted or truncated.</summary>
    public bool LocationsTruncated { get; }
    /// <summary>Gets whether the finding represents uninspected or uncertain coverage.</summary>
    public bool Unknown { get; }
}

/// <summary>A body merge-field path observed without evaluating its value.</summary>
public sealed class TemplateInspectionRequirement
{
    internal TemplateInspectionRequirement(string path, TemplatePathType? type, bool? required, bool scopeUnknown, int count)
    { PathId = path; DeclaredType = type; Required = required; ScopeUnknown = scopeUnknown; Count = count; }
    /// <summary>Gets the opaque path alias.</summary>
    public string PathId { get; }
    /// <summary>Gets the declared type, or null if the declaration or scope is unknown.</summary>
    public TemplatePathType? DeclaredType { get; }
    /// <summary>Gets whether the declaration guarantees presence, or null if unavailable.</summary>
    public bool? Required { get; }
    /// <summary>Gets whether repeating scope prevented path resolution.</summary>
    public bool ScopeUnknown { get; }
    /// <summary>Gets the number of observed field occurrences.</summary>
    public int Count { get; }
}

/// <summary>An immutable, non-reusable template inspection; it is not a conversion or fidelity guarantee.</summary>
public sealed class TemplateInspection
{
    private readonly IReadOnlyDictionary<string, string> paths;
    private readonly IReadOnlyDictionary<string, string> parts;
    internal TemplateInspection(string inputHash, DocumentFormat destination, List<TemplateInspectionFinding> findings, List<TemplateInspectionRequirement> requirements,
        IDictionary<string, string> paths, IDictionary<string, string> parts)
    {
        InputSha256 = inputHash; Destination = destination; Findings = findings.AsReadOnly(); Requirements = requirements.AsReadOnly();
        this.paths = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(paths, StringComparer.Ordinal));
        this.parts = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(parts, StringComparer.Ordinal));
    }
    /// <summary>Gets the source-byte SHA-256. This fingerprint may identify a sensitive document.</summary>
    public string InputSha256 { get; }
    /// <summary>Gets the intended destination, which has not been validated by this inspection.</summary>
    public DocumentFormat Destination { get; }
    /// <summary>Gets privacy-safe findings. An empty set would not prove conversion or fidelity.</summary>
    public IReadOnlyList<TemplateInspectionFinding> Findings { get; }
    /// <summary>Gets observed ordinary-body field requirements, not a complete template inventory.</summary>
    public IReadOnlyList<TemplateInspectionRequirement> Requirements { get; }
    /// <summary>Always false: options, fonts and provider identities are not bound by this inspection.</summary>
    public bool IsReusable { get; }
    /// <summary>Returns alias-to-path names, explicitly revealing caller data names.</summary>
    public IReadOnlyDictionary<string, string> GetSensitivePathAliases() => paths;
    /// <summary>Returns alias-to-part names, explicitly revealing source package structure.</summary>
    public IReadOnlyDictionary<string, string> GetSensitivePartAliases() => parts;
}
