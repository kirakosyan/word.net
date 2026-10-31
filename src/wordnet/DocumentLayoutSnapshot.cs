using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WordNet;

/// <summary>Opt-in limits and privacy policy for a preview's immutable layout snapshot.</summary>
public sealed class LayoutSnapshotOptions
{
    /// <summary>Gets or sets whether source text is included. The default is false.</summary>
    public bool IncludeText { get; set; }
    /// <summary>Gets or sets the maximum number of retained fragments, from 1 to 100,000.</summary>
    public int MaximumFragments { get; set; } = 100000;

    internal LayoutSnapshotOptions Snapshot()
    {
        if (MaximumFragments < 1 || MaximumFragments > 100000) throw new ArgumentOutOfRangeException(nameof(MaximumFragments));
        return new LayoutSnapshotOptions { IncludeText = IncludeText, MaximumFragments = MaximumFragments };
    }
}

/// <summary>A page-space rectangle in points, measured from the upper-left page corner.</summary>
public sealed class LayoutBounds
{
    internal LayoutBounds(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
    /// <summary>Gets the left coordinate.</summary>
    public double X { get; }
    /// <summary>Gets the top coordinate.</summary>
    public double Y { get; }
    /// <summary>Gets the width.</summary>
    public double Width { get; }
    /// <summary>Gets the height.</summary>
    public double Height { get; }
}

/// <summary>One positioned item and its source paragraph, where known.</summary>
public sealed class LayoutFragment
{
    internal LayoutFragment(int page, int order, SourceLocation? source, LayoutBounds? bounds, string? text, string? unavailable, bool? outsidePage)
    { Page = page; Order = order; Source = source; Bounds = bounds; Text = text; UnavailableReason = unavailable; ExtendsOutsidePage = outsidePage; }
    /// <summary>Gets the one-based physical source page.</summary>
    public int Page { get; }
    /// <summary>Gets the zero-based item order emitted by layout on this page, not a reading-order guarantee.</summary>
    public int Order { get; }
    /// <summary>Gets the source paragraph location, or null when unavailable.</summary>
    public SourceLocation? Source { get; }
    /// <summary>Gets the union of managed text glyph boxes, or null when geometry is unavailable.</summary>
    public LayoutBounds? Bounds { get; }
    /// <summary>Gets retained item text only when the caller opted in; otherwise null.</summary>
    public string? Text { get; }
    /// <summary>Gets a stable reason when bounds are unavailable; otherwise null.</summary>
    public string? UnavailableReason { get; }
    /// <summary>Gets whether measured bounds extend outside the page, or null when bounds are unavailable.</summary>
    public bool? ExtendsOutsidePage { get; }
}

/// <summary>An immutable snapshot of selected pages; identities apply only to this source revision.</summary>
public sealed class DocumentLayoutSnapshot
{
    internal DocumentLayoutSnapshot(IEnumerable<LayoutFragment> fragments, bool unmappedGraphics)
    { Fragments = new ReadOnlyCollection<LayoutFragment>(fragments.ToList()); HasUnmappedGraphics = unmappedGraphics; }
    /// <summary>Gets fragments in physical page and layout-item order.</summary>
    public IReadOnlyList<LayoutFragment> Fragments { get; }
    /// <summary>Gets whether vector or background graphics are present without source mapping.</summary>
    public bool HasUnmappedGraphics { get; }
}
