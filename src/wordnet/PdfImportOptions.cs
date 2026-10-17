using System;

namespace WordNet;

/// <summary>Opt-in geometric digital-PDF reconstruction in the optional import provider.</summary>
public sealed class PdfImportOptions
{
    /// <summary>Gets or sets whether horizontal digital prose/simple ruled tables use geometric reconstruction. Default false preserves content-order import.</summary>
    public bool ReconstructLayout { get; set; }
    /// <summary>Gets or sets the known number of equal-width prose columns, one through three. No automatic universal column detection is claimed.</summary>
    public int Columns { get; set; } = 1;
    /// <summary>Gets or sets whether complete axis-aligned grids with populated cells become editable tables, default true.</summary>
    public bool DetectRuledTables { get; set; } = true;
    /// <summary>Gets or sets the maximum per-page letters/path segments/images considered, default 100,000.</summary>
    public int MaximumElements { get; set; } = 100000;
    internal PdfImportOptions Snapshot()
    {
        if (ReconstructLayout && (Columns < 1 || Columns > 3 || MaximumElements <= 0 || MaximumElements > 1000000)) throw new ArgumentException("PDF reconstruction supports one to three known columns and at most one million elements per page.");
        return (PdfImportOptions)MemberwiseClone();
    }
}
