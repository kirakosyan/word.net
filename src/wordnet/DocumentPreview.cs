using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WordNet;

/// <summary>An inclusive range of one-based physical source pages, independent of displayed labels.</summary>
public sealed class PageRange
{
    /// <summary>Creates an inclusive, positive, ascending physical page range.</summary>
    public PageRange(int first, int last)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
#else
        if (first < 1) throw new ArgumentOutOfRangeException(nameof(first));
        if (last < first) throw new ArgumentOutOfRangeException(nameof(last));
#endif
        First = first; Last = last;
    }
    /// <summary>Gets the first physical source page.</summary>
    public int First { get; }
    /// <summary>Gets the last physical source page.</summary>
    public int Last { get; }
}

/// <summary>The per-page preview encoding.</summary>
public enum PreviewFormat
{
    /// <summary>Managed, self-contained SVG with glyph outlines; text is not selectable.</summary>
    Svg = 0,
    /// <summary>PNG rasterization through the optional wordnet.Ocr provider.</summary>
    Png = 1,
}

/// <summary>Bounded preview settings. Configure before concurrent use.</summary>
public sealed class PreviewOptions
{
    /// <summary>Gets ascending, nonoverlapping physical ranges; an empty list selects all source pages.</summary>
    public IList<PageRange> Pages { get; } = new List<PageRange>();
    /// <summary>Gets or sets the encoding, default SVG.</summary>
    public PreviewFormat Format { get; set; }
    /// <summary>Gets or sets raster DPI, from 36 through 600, default 96. SVG dimensions remain in points.</summary>
    public int Dpi { get; set; } = 96;
    /// <summary>Gets or sets the maximum aggregate raster pixels, default 16,777,216.</summary>
    public long MaximumPixels { get; set; } = 16 * 1024 * 1024;
    /// <summary>Gets or sets optional source-to-page inspection for the same preview layout.</summary>
    public LayoutSnapshotOptions? Layout { get; set; }
}

/// <summary>One immutable preview with original physical identity and displayed label.</summary>
public sealed class PagePreview
{
    private readonly byte[] bytes;
    internal PagePreview(int page, string label, double width, double height, int pixelsWide, int pixelsHigh, PreviewFormat format, byte[] bytes)
    { SourcePage = page; Label = label; Width = width; Height = height; PixelWidth = pixelsWide; PixelHeight = pixelsHigh; Format = format; this.bytes = bytes; }
    /// <summary>Gets the one-based physical page in the full source layout.</summary>
    public int SourcePage { get; }
    /// <summary>Gets the original displayed page label.</summary>
    public string Label { get; }
    /// <summary>Gets page width in points.</summary>
    public double Width { get; }
    /// <summary>Gets page height in points.</summary>
    public double Height { get; }
    /// <summary>Gets pixel width at requested DPI (also reported for SVG).</summary>
    public int PixelWidth { get; }
    /// <summary>Gets pixel height at requested DPI (also reported for SVG).</summary>
    public int PixelHeight { get; }
    /// <summary>Gets the encoding.</summary>
    public PreviewFormat Format { get; }
    /// <summary>Returns a caller-owned copy of the encoded preview bytes.</summary>
    public byte[] GetBytes() => (byte[])bytes.Clone();
}

/// <summary>Completed previews and their ordinary conversion diagnostics.</summary>
public sealed class DocumentPreview
{
    internal DocumentPreview(int total, IEnumerable<PagePreview> pages, ConversionResult result, DocumentLayoutSnapshot? layout = null) { SourcePageCount = total; Pages = new ReadOnlyCollection<PagePreview>(pages.ToList()); Result = result; Layout = layout; }
    /// <summary>Gets the total number of physical pages before selection.</summary>
    public int SourcePageCount { get; }
    /// <summary>Gets encoded previews in ascending physical source order.</summary>
    public IReadOnlyList<PagePreview> Pages { get; }
    /// <summary>Gets diagnostics and aggregate encoded output measurements.</summary>
    public ConversionResult Result { get; }
    /// <summary>Gets the optional bounded snapshot of the selected pages from this same layout.</summary>
    public DocumentLayoutSnapshot? Layout { get; }
}
