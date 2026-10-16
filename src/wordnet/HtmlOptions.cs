using System;

namespace WordNet;

/// <summary>Controls self-contained HTML output. Configure before starting conversions.</summary>
public sealed class HtmlOptions
{
    /// <summary>Gets or sets the maximum content width in CSS pixels. The default is 700.</summary>
    public int MaxContentWidthPx { get; set; } = 700;

    /// <summary>Gets or sets whether the first page's applicable header/footer are included as first/last blocks.</summary>
    public bool IncludeHeadersAndFooters { get; set; }

    /// <summary>Gets or sets whether images are embedded as data URIs. False retains alternative text.</summary>
    public bool EmbedImages { get; set; } = true;

    internal HtmlOptions Snapshot()
    {
        if (MaxContentWidthPx <= 0 || MaxContentWidthPx > 100000)
            throw new ArgumentOutOfRangeException(nameof(MaxContentWidthPx), "Content width must be between 1 and 100000 pixels.");
        return (HtmlOptions)MemberwiseClone();
    }
}
