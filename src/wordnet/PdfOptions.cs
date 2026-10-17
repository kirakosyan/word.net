using System;
using System.Collections.Generic;

namespace WordNet;

/// <summary>The requested PDF output conformance.</summary>
public enum PdfConformance
{
    /// <summary>Ordinary PDF 1.7 with embedded fonts.</summary>
    Pdf17 = 0,
    /// <summary>PDF/A-2 level B archival output.</summary>
    PdfA2b = 1,
}

/// <summary>Opt-in PDF accessibility profiles.</summary>
public enum PdfAccessibility
{
    /// <summary>Ordinary untagged output.</summary>
    None = 0,
    /// <summary>Semantic tags; missing author semantics produce diagnostics.</summary>
    Tagged = 1,
    /// <summary>Bounded PDF/UA-1 profile; missing or unsupported semantics fail before copying output.</summary>
    PdfUa1 = 2,
}

/// <summary>Controls PDF output. Configure before starting concurrent conversions.</summary>
public sealed class PdfOptions
{
    /// <summary>Gets or sets the opt-in accessibility profile.</summary>
    public PdfAccessibility Accessibility { get; set; }
    /// <summary>Gets or sets the explicit BCP 47 document language for tagged output, for example en-US.</summary>
    public string? Language { get; set; }
    private List<PageRange> pages = new List<PageRange>();
    /// <summary>Gets ascending nonoverlapping physical source-page ranges; empty selects all. Full layout precedes selection, retaining original PAGE/NUMPAGES and labels.</summary>
    public IList<PageRange> Pages => pages;
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
        var copy = (PdfOptions)MemberwiseClone();
        if (pages.Count > 100000) throw new ArgumentException("Too many PDF page ranges.");
        copy.pages = new List<PageRange>(pages); return copy;
    }
}
