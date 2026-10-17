using System;

namespace WordNet;

/// <summary>Base direction for the bounded horizontal Arabic/Latin typography profile.</summary>
public enum TextDirection
{
    /// <summary>Use source paragraph direction, then its first strong character.</summary>
    Auto = 0,
    /// <summary>Left to right.</summary>
    LeftToRight = 1,
    /// <summary>Right to left.</summary>
    RightToLeft = 2,
}

/// <summary>Optional native shaping for PDF and page previews. Requires wordnet.Ocr and caller Arabic TrueType fonts.</summary>
public sealed class TypographyOptions
{
    /// <summary>Gets or sets whether to enable the bounded Arabic/Latin profile. Default false preserves managed Latin layout.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets paragraph base direction.</summary>
    public TextDirection Direction { get; set; }
    /// <summary>Gets or sets the aggregate shaped-glyph work limit per layout pass, including repeated tokens. Default 1,000,000.</summary>
    public int MaximumGlyphs { get; set; } = 1000000;
    internal TypographyOptions Snapshot()
    {
        if (Direction is < TextDirection.Auto or > TextDirection.RightToLeft) throw new ArgumentOutOfRangeException(nameof(Direction));
        if (MaximumGlyphs <= 0 || MaximumGlyphs > 10000000) throw new ArgumentOutOfRangeException(nameof(MaximumGlyphs));
        return (TypographyOptions)MemberwiseClone();
    }
}
