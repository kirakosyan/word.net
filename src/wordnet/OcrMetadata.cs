using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WordNet;

/// <summary>Opt-in OCR geometry and text-span capture. Configure before concurrent use.</summary>
public sealed class OcrMetadataOptions
{
    /// <summary>Gets or sets whether structured OCR results include recognized text and regions. Default false.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the maximum aggregate word/line/paragraph regions, default 50,000.</summary>
    public int MaximumRegions { get; set; } = 50000;
    /// <summary>Gets or sets an aggregate metadata admission budget, default 4 MiB; also constrained by MaximumExpandedBytes.</summary>
    public int MaximumBytes { get; set; } = 4 * 1024 * 1024;
    internal OcrMetadataOptions Snapshot()
    {
        if (Enabled && (MaximumRegions <= 0 || MaximumRegions > 1000000 || MaximumBytes <= 0)) throw new ArgumentException("OCR metadata bounds must be positive; at most one million regions are supported.");
        return (OcrMetadataOptions)MemberwiseClone();
    }
}

/// <summary>A point on the original PDF page, in points (72/inch), with a top-left origin.</summary>
public sealed class OcrPoint
{
    internal OcrPoint(double x, double y) { X = x; Y = y; }
    /// <summary>Gets horizontal distance from the original page's left edge.</summary>
    public double X { get; }
    /// <summary>Gets vertical distance from the original page's top edge.</summary>
    public double Y { get; }
}

/// <summary>The semantic granularity of an OCR region.</summary>
public enum OcrRegionKind
{
    /// <summary>A paragraph grouping native OCR words.</summary>
    Paragraph = 0,
    /// <summary>A native OCR text line within a paragraph/known column.</summary>
    Line = 1,
    /// <summary>A recognized native OCR word.</summary>
    Word = 2,
}

/// <summary>Immutable geometry and an optional exact UTF-16 span into its OcrPage.Text.</summary>
public sealed class OcrRegion
{
    internal OcrRegion(OcrRegionKind kind, IEnumerable<OcrPoint> polygon, int? start, int length, double? confidence, bool clipped)
    {
        Kind = kind; Polygon = new ReadOnlyCollection<OcrPoint>(polygon.ToList()); TextStart = start; TextLength = length; Confidence = confidence; IsClipped = clipped;
        Left = Polygon.Min(p => p.X); Top = Polygon.Min(p => p.Y); Width = Polygon.Max(p => p.X) - Left; Height = Polygon.Max(p => p.Y) - Top;
    }
    /// <summary>Gets the region kind.</summary>
    public OcrRegionKind Kind { get; }
    /// <summary>Gets the inverse-transformed polygon, clipped to the original page if necessary.</summary>
    public IReadOnlyList<OcrPoint> Polygon { get; }
    /// <summary>Gets the original-page axis-aligned left bound in points.</summary>
    public double Left { get; }
    /// <summary>Gets the original-page axis-aligned top bound in points.</summary>
    public double Top { get; }
    /// <summary>Gets the axis-aligned width in points.</summary>
    public double Width { get; }
    /// <summary>Gets the axis-aligned height in points.</summary>
    public double Height { get; }
    /// <summary>Gets an exact UTF-16 offset into OcrPage.Text, or null if native text could not be matched unambiguously.</summary>
    public int? TextStart { get; }
    /// <summary>Gets the UTF-16 span length; zero when TextStart is null.</summary>
    public int TextLength { get; }
    /// <summary>Gets uncalibrated provider confidence (0–100), or null if unavailable. Group confidence is the arithmetic mean of available word confidences.</summary>
    public double? Confidence { get; }
    /// <summary>Gets whether the inverse-transformed region intersected the original page bounds.</summary>
    public bool IsClipped { get; }
}

/// <summary>Opt-in OCR metadata for one original PDF page.</summary>
public sealed class OcrPage
{
    internal OcrPage(int page, double width, double height, string text, int start, int confidence, IEnumerable<OcrRegion> regions)
    {
        SourcePage = page; Width = width; Height = height; Text = text; DocumentTextStart = start; MeanConfidence = confidence;
        Regions = new ReadOnlyCollection<OcrRegion>(regions.OrderBy(r => r.TextStart ?? int.MaxValue).ThenBy(r => r.Kind).ToList());
    }
    /// <summary>Gets the one-based physical page in the original PDF, including digital pages.</summary>
    public int SourcePage { get; }
    /// <summary>Gets original PDF page width in points.</summary>
    public double Width { get; }
    /// <summary>Gets original PDF page height in points.</summary>
    public double Height { get; }
    /// <summary>Gets canonical editable text for this imported page, with LF between paragraphs, including empty paragraphs.</summary>
    public string Text { get; }
    /// <summary>Gets the page text's UTF-16 offset in the imported model's body paragraphs joined by LF; later edits/destination losses can change offsets.</summary>
    public int DocumentTextStart { get; }
    /// <summary>Gets Tesseract's uncalibrated integer mean word confidence, preserving the ordinary diagnostic's page-mean semantics.</summary>
    public int MeanConfidence { get; }
    /// <summary>Gets word/line/paragraph regions ordered by mapped UTF-16 position, parent groups first. Unmapped regions follow mapped content.</summary>
    public IReadOnlyList<OcrRegion> Regions { get; }
}
