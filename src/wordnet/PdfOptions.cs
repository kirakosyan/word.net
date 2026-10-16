using System;

namespace WordNet;

/// <summary>The requested PDF output conformance.</summary>
public enum PdfConformance
{
    /// <summary>Ordinary PDF 1.7 with embedded fonts.</summary>
    Pdf17 = 0,
    /// <summary>PDF/A-2 level B archival output.</summary>
    PdfA2b = 1,
}

/// <summary>Controls PDF output. Configure before starting concurrent conversions.</summary>
public sealed class PdfOptions
{
    /// <summary>Gets or sets whether headings create a hierarchical document outline. The default is false.</summary>
    public bool OutlineHeadings { get; set; }

    /// <summary>Gets or sets whether text/font streams use Deflate compression. The default is true; image encodings are retained.</summary>
    public bool Compress { get; set; } = true;

    /// <summary>Gets or sets PDF conformance. The default is ordinary PDF 1.7.</summary>
    public PdfConformance Conformance { get; set; }

    internal PdfOptions Snapshot()
    {
        if (Conformance is not PdfConformance.Pdf17 and not PdfConformance.PdfA2b)
            throw new ArgumentOutOfRangeException(nameof(Conformance));
        return (PdfOptions)MemberwiseClone();
    }
}
