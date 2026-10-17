using System;
using System.Collections.Generic;

namespace WordNet;

/// <summary>The severity of an informational report or fidelity warning.</summary>
public enum DiagnosticSeverity
{
    /// <summary>An informational report that does not reject strict conversion.</summary>
    Info = 0,
    /// <summary>A fidelity loss or deterministic approximation.</summary>
    Warning = 1,
}

/// <summary>A detected conversion loss. Codes are stable; messages describe the affected feature.</summary>
public sealed class ConversionDiagnostic
{
    private readonly List<DiagnosticOccurrence> occurrences = new List<DiagnosticOccurrence>();
    private readonly HashSet<(string Part, string Path, DiagnosticPhase Phase)> locations = new HashSet<(string, string, DiagnosticPhase)>();
    internal ConversionDiagnostic(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
    {
        Code = code;
        Message = message;
        Severity = severity;
    }

    /// <summary>Gets the machine-readable warning code.</summary>
    public string Code { get; }

    /// <summary>Gets the warning description, without document text.</summary>
    public string Message { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Gets the number of occurrences grouped under this code.</summary>
    public int Count { get; internal set; } = 1;

    /// <summary>Gets up to 128 distinct source locations/stages; grouping and Count remain unchanged.</summary>
    public IReadOnlyList<DiagnosticOccurrence> Occurrences => occurrences.AsReadOnly();

    /// <summary>Gets whether additional distinct source locations could not be retained.</summary>
    public bool LocationsTruncated { get; private set; }

    internal void Observe(SourceLocation location, DiagnosticPhase phase)
    {
        if (LocationsTruncated) return;
        var key = (location.Part, location.Path, phase);
        if (locations.Contains(key)) return;
        if (occurrences.Count == 128) { LocationsTruncated = true; return; }
        locations.Add(key);
        occurrences.Add(new DiagnosticOccurrence(new SourceLocation(key.Part, key.Path), phase));
    }

    internal ConversionDiagnostic Copy()
    {
        var copy = new ConversionDiagnostic(Code, Message, Severity) { Count = Count };
        foreach (DiagnosticOccurrence occurrence in occurrences) copy.Observe(occurrence.Location, occurrence.Phase);
        copy.LocationsTruncated = LocationsTruncated; return copy;
    }
    internal void Include(ConversionDiagnostic source)
    {
        Count = checked(Count + source.Count);
        foreach (DiagnosticOccurrence occurrence in source.Occurrences) Observe(occurrence.Location, occurrence.Phase);
        LocationsTruncated |= source.LocationsTruncated;
    }
}

/// <summary>Reports successfully written output and any detected losses.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(long bytesWritten, IReadOnlyList<ConversionDiagnostic> diagnostics, ConversionMetrics metrics, IReadOnlyList<OcrPage>? ocrPages = null)
    {
        BytesWritten = bytesWritten;
        Diagnostics = diagnostics;
        Metrics = metrics;
        OcrPages = ocrPages ?? Array.Empty<OcrPage>();
    }

    /// <summary>Gets the number of bytes appended at the destination's initial position.</summary>
    public long BytesWritten { get; }

    /// <summary>Gets an immutable collection of detected losses, grouped by warning code.</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

    /// <summary>Gets measurements of this completed conversion.</summary>
    public ConversionMetrics Metrics { get; }
    /// <summary>Gets opt-in structured OCR metadata, or an empty collection by default. Recognized text appears only when explicitly requested.</summary>
    public IReadOnlyList<OcrPage> OcrPages { get; }
}

/// <summary>A requested to resolved font-family mapping used by fixed-page output.</summary>
public sealed class FontUsage
{
    internal FontUsage(string requested, string resolved) { RequestedFamily = requested; ResolvedFamily = resolved; }
    /// <summary>Gets the requested family.</summary>
    public string RequestedFamily { get; }
    /// <summary>Gets the resolved family.</summary>
    public string ResolvedFamily { get; }
}

/// <summary>Per-call output measurements; reflowable formats have no page count.</summary>
public sealed class ConversionMetrics
{
    internal ConversionMetrics(int? pages, long bytes, TimeSpan elapsed, IReadOnlyList<FontUsage> fonts, int substitutions)
    { PageCount = pages; BytesWritten = bytes; ElapsedTime = elapsed; FontsUsed = fonts; SubstitutionCount = substitutions; }
    /// <summary>Gets generated fixed-page count, or null for reflowable output.</summary>
    public int? PageCount { get; }
    /// <summary>Gets the number of bytes written.</summary>
    public long BytesWritten { get; }
    /// <summary>Gets elapsed conversion and I/O time.</summary>
    public TimeSpan ElapsedTime { get; }
    /// <summary>Gets the distinct font-family mappings used.</summary>
    public IReadOnlyList<FontUsage> FontsUsed { get; }
    /// <summary>Gets the number of distinct substituted families.</summary>
    public int SubstitutionCount { get; }
}
