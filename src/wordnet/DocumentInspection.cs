using System;
using System.Collections.Generic;

namespace WordNet;

/// <summary>The pipeline stage that observed a finding.</summary>
public enum DiagnosticPhase
{
    /// <summary>Source parsing and reconstruction, including any input provider.</summary>
    Parse = 0,
    /// <summary>Destination feature checks before serialization.</summary>
    Destination = 1,
    /// <summary>Font resolution and fixed-page layout.</summary>
    Layout = 2,
    /// <summary>Output serialization; unavailable to preflight.</summary>
    Write = 3,
    /// <summary>Optional input provider processing, including PDF import and OCR.</summary>
    Provider = 4,
}

/// <summary>A source identifier containing no document text or attribute values.</summary>
public sealed class SourceLocation
{
    internal SourceLocation(string part, string path) { Part = part; Path = path; }
    /// <summary>Gets the source package part, or a format-level identifier.</summary>
    public string Part { get; }
    /// <summary>Gets an indexed element path, or / for document-level findings.</summary>
    public string Path { get; }
    /// <summary>Returns the part and indexed path.</summary>
    public override string ToString() => Part + ":" + Path;
}

/// <summary>A distinct source location and stage within a grouped diagnostic.</summary>
public sealed class DiagnosticOccurrence
{
    internal DiagnosticOccurrence(SourceLocation location, DiagnosticPhase phase) { Location = location; Phase = phase; }
    /// <summary>Gets the source location; / means document-level precision.</summary>
    public SourceLocation Location { get; }
    /// <summary>Gets the stage that observed the finding.</summary>
    public DiagnosticPhase Phase { get; }
}

/// <summary>Exact counts in the parsed supported model, not estimates of original content or memory.</summary>
public sealed class DocumentFeatures
{
    internal DocumentFeatures(int paragraphs, int tables, int images, int fields, int lists)
    { Paragraphs = paragraphs; Tables = tables; Images = images; Fields = fields; ListItems = lists; }
    /// <summary>Gets unique retained paragraphs, including stories and nested cells.</summary>
    public int Paragraphs { get; }
    /// <summary>Gets unique retained tables, including nested tables.</summary>
    public int Tables { get; }
    /// <summary>Gets retained inline image occurrences.</summary>
    public int Images { get; }
    /// <summary>Gets retained field run occurrences.</summary>
    public int Fields { get; }
    /// <summary>Gets retained list paragraphs.</summary>
    public int ListItems { get; }
}

/// <summary>Bounded document inspection without output serialization.</summary>
public sealed class DocumentInspection
{
    internal DocumentInspection(long bytes, int characters, DocumentFeatures features, ConversionResult result, bool layout, bool strict,
        int? archiveEntries, long? expandedBytes, long imagePixels)
    {
        InputBytes = bytes; Characters = characters; Features = features; Diagnostics = result.Diagnostics;
        PageCount = result.Metrics.PageCount; Fonts = result.Metrics.FontsUsed;
        LayoutPerformed = layout; WouldRejectStrict = strict;
        ArchiveEntryCount = archiveEntries; DeclaredExpandedBytes = expandedBytes; DecodedImagePixels = imagePixels;
    }
    /// <summary>Gets exact bytes consumed from the source's initial position.</summary>
    public long InputBytes { get; }
    /// <summary>Gets characters retained by the parsed model, including stories.</summary>
    public int Characters { get; }
    /// <summary>Gets DOCX ZIP entry count when known; null means unavailable.</summary>
    public int? ArchiveEntryCount { get; }
    /// <summary>Gets aggregate DOCX uncompressed sizes declared by ZIP metadata, not actual memory consumption.</summary>
    public long? DeclaredExpandedBytes { get; }
    /// <summary>Gets aggregate decoded image pixels admitted by the parser's resource checks, not an RSS forecast.</summary>
    public long DecodedImagePixels { get; }
    /// <summary>Gets counts of features retained by parsing, before destination approximation.</summary>
    public DocumentFeatures Features { get; }
    /// <summary>Gets grouped findings. Unperformed layout and serialization may find additional losses.</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
    /// <summary>Gets resolved fixed-page font mappings; empty for other destinations.</summary>
    public IReadOnlyList<FontUsage> Fonts { get; }
    /// <summary>Gets pages from actual layout, or null when layout was not performed.</summary>
    public int? PageCount { get; }
    /// <summary>Gets whether full fixed-page layout was performed.</summary>
    public bool LayoutPerformed { get; }
    /// <summary>Gets whether a detected fidelity loss would reject Strict conversion.</summary>
    public bool WouldRejectStrict { get; }
}
