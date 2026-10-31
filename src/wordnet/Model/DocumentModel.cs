using System.Collections.Generic;
using System.Linq;

namespace WordNet.Model;

internal sealed class DocumentModel
{
    internal int ObjectCount { get; set; }
    internal ContentControlStore Controls { get; set; } = new ContentControlStore();
    internal bool HasControlDisplay { get; set; }
    internal bool HasLinkedTextBoxes { get; set; }
    internal DocumentTheme? Theme { get; set; }
    internal TableStyle? DefaultTableStyle { get; set; }
    internal ReviewStore Review { get; } = new ReviewStore();
    internal string Title { get; set; } = "";
    internal string Author { get; set; } = "";
    internal Dictionary<string, string> Styles { get; } = new Dictionary<string, string>(System.StringComparer.Ordinal);
    internal Dictionary<string, DocumentProperty> CustomProperties { get; } = new Dictionary<string, DocumentProperty>(System.StringComparer.OrdinalIgnoreCase);
    internal bool TypographyPrepared { get; set; }
    internal List<DocumentBlock> Blocks { get; } = new List<DocumentBlock>();
    internal PageSettings? FinalSection { get; set; }
    internal List<DocumentNote> Notes { get; } = new List<DocumentNote>();
    internal HashSet<TextRun> UnresolvedStyleReferences { get; } = new HashSet<TextRun>();
    internal Dictionary<PageSettings, List<int>> StylePages { get; } = new Dictionary<PageSettings, List<int>>();
    internal Dictionary<(TextRun Run, int Page), string> StyleResults { get; } = new Dictionary<(TextRun, int), string>();
    internal Dictionary<TextRun, string> FieldResults { get; } = new Dictionary<TextRun, string>();
    internal Dictionary<string, (int Page, string Label, double X, double Y)> BookmarkPages { get; } = new Dictionary<string, (int, string, double, double)>(System.StringComparer.Ordinal);
    internal Dictionary<TextRun, List<string>> TocTargets { get; } = new Dictionary<TextRun, List<string>>();
    internal bool ReferencesPrepared { get; set; }
    internal IEnumerable<TableBlock> Tables => Expand(AllBlocks).OfType<TableBlock>().Distinct();
    internal IEnumerable<ParagraphBlock> BodyParagraphs => Expand(Blocks).OfType<ParagraphBlock>().Distinct();
    internal IEnumerable<ParagraphBlock> Paragraphs => Expand(AllBlocks).OfType<ParagraphBlock>().Distinct();
    internal IEnumerable<DocumentBlock> AllBlocks => Blocks.Concat(Notes.SelectMany(n => n.Blocks)).Concat(Blocks.Select(b => b.Section).Concat(new[] { FinalSection }).Where(s => s != null).Distinct()
        .SelectMany(s => s!.Stories.Values).Distinct().SelectMany(story => story));
    internal static IEnumerable<DocumentBlock> Expand(IEnumerable<DocumentBlock> blocks)
    {
        foreach (DocumentBlock block in blocks)
        {
            yield return block;
            if (block is TableBlock table)
                foreach (DocumentBlock child in Expand(table.Rows.SelectMany(r => r).SelectMany(c => c.Blocks))) yield return child;
            if (block is ParagraphBlock paragraph)
                foreach (DocumentBlock child in Expand(paragraph.Runs.Where(r => r.Box != null).SelectMany(r => r.Box!.Blocks))) yield return child;
        }
    }
    internal int CharacterCount => Paragraphs.Sum(p => p.Text.Length);
    internal static IEnumerable<ParagraphBlock> CellParagraphs(IEnumerable<DocumentBlock> blocks)
    {
        foreach (DocumentBlock block in blocks)
            if (block is ParagraphBlock paragraph) yield return paragraph;
            else foreach (ParagraphBlock nested in ((TableBlock)block).Rows.SelectMany(r => r).SelectMany(c => c.Paragraphs)) yield return nested;
    }
}

// Constructed only from the bounded, local font and RGB scheme. The XML strings
// are immutable and never contain arbitrary source theme markup or relationships.
internal sealed class DocumentTheme
{
    internal DocumentTheme(string xml, string? mapping) { Xml = xml; Mapping = mapping; }
    internal string Xml { get; }
    internal string? Mapping { get; }
    internal bool Same(DocumentTheme? other) => other != null && Xml == other.Xml && Mapping == other.Mapping;
}

internal abstract class DocumentBlock
{
    internal int? TypedControlId { get; set; }
    internal TemplateControl? Control { get; set; }
    // Ordered from the outermost child to the innermost child; ordinary blocks
    // do not allocate a list. Existing owner checks reject any nested control.
    private List<TemplateControl>? nestedControls;
    internal IReadOnlyList<TemplateControl> NestedControls => (IReadOnlyList<TemplateControl>?)nestedControls ?? System.Array.Empty<TemplateControl>();
    internal TemplateControl? NestedControl => nestedControls == null || nestedControls.Count == 0 ? null : nestedControls[0];
    internal void PrependNestedControl(TemplateControl control) => (nestedControls ??= new List<TemplateControl>()).Insert(0, control);
    internal void AppendNestedControl(TemplateControl control) => (nestedControls ??= new List<TemplateControl>()).Add(control);
    internal void RemoveOuterNestedControl() => nestedControls!.RemoveAt(0);
    internal SourceLocation? Location { get; set; }
    internal PageSettings? Section { get; set; }
}

internal enum ParagraphAlignment { Left, Center, Right, Justified, Distributed }

internal sealed class ParagraphBlock : DocumentBlock
{
    internal double? FallbackFontSize { get; set; }
    internal double? FallbackAfter { get; set; }
    internal string? StyleId { get; set; }
    internal TextRun? Mark { get; set; }
    internal string? StructuralRevision { get; set; }
    internal string? ParagraphFormatRevision { get; set; }
    internal ParagraphFormatSnapshot? PreviousParagraphFormat { get; set; }
    internal bool ExplicitParagraphFormat { get; set; }
    internal bool RemoveWhenObjectEmpty { get; set; }
    internal System.Guid Identity { get; set; } = System.Guid.NewGuid();
    internal bool? RightToLeft { get; set; }
    internal List<TextRun> Runs { get; } = new List<TextRun>();

    internal int HeadingLevel { get; set; }

    internal bool IsCodeBlock { get; set; }

    internal ParagraphAlignment Alignment { get; set; }
    internal ListItem? List { get; set; }
    internal ParagraphLayout Layout { get; set; } = new ParagraphLayout();

    internal string Text => string.Concat(Runs.ConvertAll(run => run.Text));
}

internal sealed class TableBlock : DocumentBlock
{
    internal int HeaderRowCount { get; set; }
    internal bool HasHeaderRow { get => HeaderRowCount > 0; set => HeaderRowCount = value ? System.Math.Max(1, HeaderRowCount) : 0; }
    internal double? Width { get; set; }
    internal double? WidthPercent { get; set; }
    internal bool AutoFit { get; set; }
    internal bool ManagedImplicitAutoFit { get; set; }
    internal bool LayoutExplicit { get; set; }
    internal Dictionary<int, string> RowRevisions { get; } = new Dictionary<int, string>();
    internal Dictionary<int, TemplateControl> RowControls { get; } = new Dictionary<int, TemplateControl>();
    internal Dictionary<int, int> TypedRowControls { get; } = new Dictionary<int, int>();
    internal double LeftIndent { get; set; }
    internal List<double> ColumnWidths { get; } = new List<double>();
    internal string? Background { get; set; }
    internal TableThemeColor? BackgroundTheme { get; set; }
    internal TableStyle? Style { get; set; }
    internal bool UsesDefaultStyle { get; set; }
    internal TableStyleLook? Look { get; set; }
    internal string? EffectiveBackground => Background ?? Style?.EffectiveBackground;
    internal bool HasTableShading => EffectiveBackground != null || Look != null && Style?.EffectiveRegions.Count > 0;
    internal bool HasUnqualifiedConditionalInheritance => Look != null && Style is TableStyle style && style.BasedOn is TableStyle parent
        && Rows.Count > 0 && Rows[0].Count > 0
        && (parent.EffectiveRegions.Keys.Any(name => !style.Regions.ContainsKey(name) && RegionMayApply(name, style, Look))
            || parent.EffectiveRegionBorders.Any(region => RegionMayApply(region.Key, style, Look)
                && (!style.RegionBorders.TryGetValue(region.Key, out IReadOnlyDictionary<string, CellBorder>? child)
                    || region.Value.Keys.Any(side => !child.ContainsKey(side)))));
    private bool RegionMayApply(string name, TableStyle style, TableStyleLook look)
    {
        bool firstRow = look.Has(0x20), lastRow = look.Has(0x40), firstColumn = look.Has(0x80), lastColumn = look.Has(0x100);
        return name switch
        {
            "firstRow" => firstRow,
            "lastRow" => lastRow,
            "firstCol" => firstColumn,
            "lastCol" => lastColumn,
            "nwCell" => firstRow && firstColumn,
            "neCell" => firstRow && lastColumn,
            "swCell" => lastRow && firstColumn,
            "seCell" => lastRow && lastColumn,
            "band1Horz" or "band2Horz" => !look.Has(0x200) && (style.EffectiveRowBandSize ?? 1) > 0
                && Rows.Count > (firstRow ? 1 : 0) + (lastRow ? 1 : 0),
            "band1Vert" or "band2Vert" => !look.Has(0x400) && (style.EffectiveColumnBandSize ?? 1) > 0
                && Rows[0].Count > (firstColumn ? 1 : 0) + (lastColumn ? 1 : 0),
            _ => true
        };
    }
    internal bool HasUnqualifiedWholeConditionalShading => Style is TableStyle style && style.EffectiveBackground != null && style.EffectiveRegions.Count > 0;
    internal bool HasAmbiguousDefaultBandSize => Look != null && Style != null
        && (!Look.Has(0x200) && Style.EffectiveRowBandSize == null
                && (HasConditionalRegion(Style, "band1Horz") || HasConditionalRegion(Style, "band2Horz"))
            || !Look.Has(0x400) && Style.EffectiveColumnBandSize == null
                && (HasConditionalRegion(Style, "band1Vert") || HasConditionalRegion(Style, "band2Vert")));
    internal bool HasUnqualifiedRegionOverlap => Style is TableStyle overlapStyle
        && (overlapStyle.EffectiveRegions.Count > 0 || overlapStyle.EffectiveRegionBorders.Count > 0) && Look != null
        && (Rows.Count == 1 && Look.Has(0x20) && Look.Has(0x40)
            || Rows.Count > 0 && Rows[0].Count == 1 && Look.Has(0x80) && Look.Has(0x100));
    internal bool HasUnqualifiedBandSize => Look != null && Style != null
        && (!Look.Has(0x200) && Style.EffectiveRowBandSize is int rows && rows != 1
                && (HasConditionalRegion(Style, "band1Horz") || HasConditionalRegion(Style, "band2Horz"))
            || !Look.Has(0x400) && Style.EffectiveColumnBandSize is int columns && columns != 1
                && (HasConditionalRegion(Style, "band1Vert") || HasConditionalRegion(Style, "band2Vert")));
    private static bool HasConditionalRegion(TableStyle style, string name) => style.EffectiveRegions.ContainsKey(name) || style.EffectiveRegionBorders.ContainsKey(name);
    internal bool HasUnqualifiedConditionalBorderConflict => Look != null && Style is TableStyle style && style.EffectiveRegionBorders.Count > 0 && Rows.Count > 0 && Rows[0].Count > 0
        && (style.EffectiveRegionBorders.Any(region => RegionMayApply(region.Key, style, Look)
            && (style.EffectiveBorders.Count != 0 || Borders.Count != 0 || Rows.SelectMany(row => row).Any(cell => cell.Borders.Count != 0)
                || region.Value.Keys.Any(side => !OutsideConditionalEdge(region.Key, side)))) || HasOverlappingConditionalBorders(style));
    private bool HasOverlappingConditionalBorders(TableStyle style)
    {
        foreach (int row in new[] { 0, Rows.Count - 1 }.Distinct())
        {
            if (Rows[row].Count == 0) continue;
            foreach (int column in new[] { 0, Rows[row].Count - 1 }.Distinct())
            {
                var sides = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (string region in ActiveRegions(row, column))
                    if (style.EffectiveRegionBorders.TryGetValue(region, out IReadOnlyDictionary<string, CellBorder>? borders))
                        foreach (string side in borders.Keys)
                            if (!sides.Add(side)) return true;
            }
        }
        return false;
    }
    private static bool OutsideConditionalEdge(string region, string side) => region switch
    {
        "firstRow" => side == "top",
        "lastRow" => side == "bottom",
        "firstCol" => side == "left",
        "lastCol" => side == "right",
        "nwCell" => side is "top" or "left",
        "neCell" => side is "top" or "right",
        "swCell" => side is "bottom" or "left",
        "seCell" => side is "bottom" or "right",
        _ => false
    };
    internal string? CellBackground(int row, int column)
    {
        string? color = EffectiveBackground;
        if (Look == null || Style == null || Style.EffectiveRegions.Count == 0) return color;
        IReadOnlyDictionary<string, string> regions = Style.EffectiveRegions;
        foreach (string region in ActiveRegions(row, column)) color = Region(regions, region, color);
        return color;
    }
    private IEnumerable<string> ActiveRegions(int row, int column)
    {
        if (Look == null || Style == null) yield break;
        bool firstRow = Look.Has(0x20), lastRow = Look.Has(0x40), firstColumn = Look.Has(0x80), lastColumn = Look.Has(0x100);
        int rows = Rows.Count, columns = Rows[row].Count;
        // Office applies row bands before column bands, then columns, rows and corners.
        int rowStart = firstRow ? 1 : 0, columnStart = firstColumn ? 1 : 0;
        int rowSize = Style.EffectiveRowBandSize ?? 1, columnSize = Style.EffectiveColumnBandSize ?? 1;
        if (!Look.Has(0x200) && rowSize > 0 && row >= rowStart && row < rows - (lastRow ? 1 : 0))
            yield return (((row - rowStart) / rowSize) & 1) == 0 ? "band1Horz" : "band2Horz";
        if (!Look.Has(0x400) && columnSize > 0 && column >= columnStart && column < columns - (lastColumn ? 1 : 0))
            yield return (((column - columnStart) / columnSize) & 1) == 0 ? "band1Vert" : "band2Vert";
        if (firstColumn && column == 0) yield return "firstCol";
        if (lastColumn && column == columns - 1) yield return "lastCol";
        if (firstRow && row == 0) yield return "firstRow";
        if (lastRow && row == rows - 1) yield return "lastRow";
        if (firstRow && firstColumn && row == 0 && column == 0) yield return "nwCell";
        if (firstRow && lastColumn && row == 0 && column == columns - 1) yield return "neCell";
        if (lastRow && firstColumn && row == rows - 1 && column == 0) yield return "swCell";
        if (lastRow && lastColumn && row == rows - 1 && column == columns - 1) yield return "seCell";
    }
    private static string? Region(IReadOnlyDictionary<string, string> regions, string name, string? fallback) =>
        regions.TryGetValue(name, out string? color) ? color : fallback;
    internal bool HasTableBorders => Borders.Count != 0 || Style?.EffectiveBorders.Count > 0 || Look != null && Style?.EffectiveRegionBorders.Count > 0;
    // Direct table borders remain separate from cell overrides so that row edits
    // and report expansion compute their outside and inside edges afresh.
    internal Dictionary<string, CellBorder> Borders { get; } = new Dictionary<string, CellBorder>();
    internal Dictionary<int, double> RowLefts { get; } = new Dictionary<int, double>();

    internal double[] ColumnEdges()
    {
        var edges = new double[ColumnWidths.Count + 1];
        for (int i = 0; i < ColumnWidths.Count; i++) edges[i + 1] = edges[i] + ColumnWidths[i];
        return edges;
    }

    internal List<List<TableCell>> Rows { get; } = new List<List<TableCell>>();
    internal HashSet<int> NonSplittingRows { get; } = new HashSet<int>();
    internal Dictionary<int, (double Height, bool Exact)> RowHeights { get; } = new Dictionary<int, (double, bool)>();

    internal IEnumerable<KeyValuePair<string, CellBorder>> CellBorders(int row, int column) =>
        !HasTableBorders ? Rows[row][column].Borders
        : Look != null && Style?.EffectiveRegionBorders.Count > 0 ? ResolveCellBorders(row, column) : ResolveOrdinaryCellBorders(row, column);

    // Keep the existing direct/whole-table path and paint order when no
    // conditional border participates; it does not need a per-cell map.
    private IEnumerable<KeyValuePair<string, CellBorder>> ResolveOrdinaryCellBorders(int row, int column)
    {
        TableCell cell = Rows[row][column];
        IReadOnlyDictionary<string, CellBorder> defaults = Borders.Count != 0 ? Borders : Style!.EffectiveBorders;
        foreach (var border in cell.Borders) yield return border;
        for (int i = 0; i < 4; i++)
        {
            string side = i switch { 0 => "top", 1 => "bottom", 2 => "left", _ => "right" };
            string inherited = i switch
            {
                0 => row == 0 ? "top" : "insideH",
                1 => row == Rows.Count - 1 ? "bottom" : "insideH",
                2 => column == 0 ? "left" : "insideV",
                _ => column == Rows[row].Count - 1 ? "right" : "insideV"
            };
            if (!cell.Borders.ContainsKey(side) && defaults.TryGetValue(inherited, out CellBorder? border))
                yield return new KeyValuePair<string, CellBorder>(side, border);
        }
    }

    private Dictionary<string, CellBorder> ResolveCellBorders(int row, int column)
    {
        TableCell cell = Rows[row][column];
        IReadOnlyDictionary<string, CellBorder> defaults = Borders.Count != 0 ? Borders : Style!.EffectiveBorders;
        var selected = new Dictionary<string, CellBorder>();
        for (int i = 0; i < 4; i++)
        {
            string side = i switch { 0 => "top", 1 => "bottom", 2 => "left", _ => "right" };
            string inherited = i switch
            {
                0 => row == 0 ? "top" : "insideH",
                1 => row == Rows.Count - 1 ? "bottom" : "insideH",
                2 => column == 0 ? "left" : "insideV",
                _ => column == Rows[row].Count - 1 ? "right" : "insideV"
            };
            if (defaults.TryGetValue(inherited, out CellBorder? border)) selected[side] = border;
        }
        if (Style != null)
            foreach (string region in ActiveRegions(row, column))
                if (Style.EffectiveRegionBorders.TryGetValue(region, out IReadOnlyDictionary<string, CellBorder>? borders))
                    foreach (var border in borders)
                        if (border.Key is "top" or "bottom" or "left" or "right") selected[border.Key] = border.Value;
        foreach (var border in cell.Borders) selected[border.Key] = border.Value;
        return selected;
    }
}

// This closed snapshot contains only admitted whole-table and conditional style
// properties. Unsupported style elements are diagnosed before construction.
internal sealed class TableStyle
{
    internal TableStyle(string id, string name, TableStyle? basedOn, IDictionary<string, CellBorder> borders, string? background,
        IDictionary<string, string> regions, IDictionary<string, TableThemeColor> regionThemes,
        IDictionary<string, Dictionary<string, CellBorder>> regionBorders, int? rowBandSize, int? columnBandSize)
    {
        Id = id; Name = name; BasedOn = basedOn; Background = background;
        var specified = new Dictionary<string, CellBorder>(System.StringComparer.Ordinal);
        var effective = new Dictionary<string, CellBorder>(System.StringComparer.Ordinal);
        if (basedOn != null) foreach (var border in basedOn.EffectiveBorders) effective.Add(border.Key, border.Value);
        foreach (var border in borders)
        {
            var copy = new CellBorder { Color = border.Value.Color, Style = border.Value.Style, Width = border.Value.Width };
            specified.Add(border.Key, copy); effective[border.Key] = copy;
        }
        Borders = specified; EffectiveBorders = effective;
        EffectiveBackground = background ?? basedOn?.EffectiveBackground;
        var specifiedRegions = new Dictionary<string, string>(regions, System.StringComparer.Ordinal);
        var effectiveRegions = new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (basedOn != null) foreach (var region in basedOn.EffectiveRegions) effectiveRegions.Add(region.Key, region.Value);
        foreach (var region in specifiedRegions) effectiveRegions[region.Key] = region.Value;
        Regions = specifiedRegions; EffectiveRegions = effectiveRegions;
        var specifiedThemes = new Dictionary<string, TableThemeColor>(regionThemes, System.StringComparer.Ordinal);
        var effectiveThemes = new Dictionary<string, TableThemeColor>(System.StringComparer.Ordinal);
        if (basedOn != null) foreach (var region in basedOn.EffectiveRegionThemes) effectiveThemes.Add(region.Key, region.Value);
        foreach (string region in specifiedRegions.Keys)
        {
            if (specifiedThemes.TryGetValue(region, out TableThemeColor? value)) effectiveThemes[region] = value;
            else effectiveThemes.Remove(region);
        }
        RegionThemes = specifiedThemes; EffectiveRegionThemes = effectiveThemes;
        var specifiedBorders = new Dictionary<string, IReadOnlyDictionary<string, CellBorder>>(System.StringComparer.Ordinal);
        var effectiveRegionBorders = new Dictionary<string, Dictionary<string, CellBorder>>(System.StringComparer.Ordinal);
        if (basedOn != null)
            foreach (var region in basedOn.EffectiveRegionBorders)
                effectiveRegionBorders.Add(region.Key, region.Value.ToDictionary(edge => edge.Key, edge => edge.Value, System.StringComparer.Ordinal));
        foreach (var region in regionBorders)
        {
            var copied = new Dictionary<string, CellBorder>(System.StringComparer.Ordinal);
            if (!effectiveRegionBorders.TryGetValue(region.Key, out Dictionary<string, CellBorder>? effectiveEdges))
            { effectiveEdges = new Dictionary<string, CellBorder>(System.StringComparer.Ordinal); effectiveRegionBorders.Add(region.Key, effectiveEdges); }
            foreach (var edge in region.Value)
            {
                var border = new CellBorder { Color = edge.Value.Color, Style = edge.Value.Style, Width = edge.Value.Width };
                copied.Add(edge.Key, border); effectiveEdges[edge.Key] = border;
            }
            specifiedBorders.Add(region.Key, copied);
        }
        RegionBorders = specifiedBorders;
        EffectiveRegionBorders = effectiveRegionBorders.ToDictionary(region => region.Key, region => (IReadOnlyDictionary<string, CellBorder>)region.Value, System.StringComparer.Ordinal);
        RowBandSize = rowBandSize; ColumnBandSize = columnBandSize;
        EffectiveRowBandSize = rowBandSize ?? basedOn?.EffectiveRowBandSize;
        EffectiveColumnBandSize = columnBandSize ?? basedOn?.EffectiveColumnBandSize;
    }
    internal string Id { get; }
    internal string Name { get; }
    internal TableStyle? BasedOn { get; }
    internal IReadOnlyDictionary<string, CellBorder> Borders { get; }
    internal IReadOnlyDictionary<string, CellBorder> EffectiveBorders { get; }
    internal string? Background { get; }
    internal string? EffectiveBackground { get; }
    internal IReadOnlyDictionary<string, string> Regions { get; }
    internal IReadOnlyDictionary<string, string> EffectiveRegions { get; }
    internal IReadOnlyDictionary<string, TableThemeColor> RegionThemes { get; }
    internal IReadOnlyDictionary<string, TableThemeColor> EffectiveRegionThemes { get; }
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, CellBorder>> RegionBorders { get; }
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, CellBorder>> EffectiveRegionBorders { get; }
    internal int? RowBandSize { get; }
    internal int? ColumnBandSize { get; }
    internal int? EffectiveRowBandSize { get; }
    internal int? EffectiveColumnBandSize { get; }
}

// Captured only for a resolved, bounded theme and clear conditional cell fill.
internal sealed class TableThemeColor
{
    internal TableThemeColor(string token, string? tint, string? shade, string? fallback)
    { Token = token; Tint = tint; Shade = shade; Fallback = fallback; }
    internal string Token { get; }
    internal string? Tint { get; }
    internal string? Shade { get; }
    internal string? Fallback { get; }
    internal bool Same(TableThemeColor? other) => other != null && Token == other.Token && Tint == other.Tint && Shade == other.Shade && Fallback == other.Fallback;
}

// A validated mask and its explicitly specified, agreeing flags. Omitted flags
// remain omitted so editable output does not manufacture explicit settings.
internal sealed class TableStyleLook
{
    internal TableStyleLook(int mask, IDictionary<string, bool> flags)
    { Mask = mask; Flags = new Dictionary<string, bool>(flags, System.StringComparer.Ordinal); }
    internal int Mask { get; }
    internal bool FirstRow => (Mask & 0x20) != 0;
    internal bool Has(int bit) => (Mask & bit) != 0;
    internal IReadOnlyDictionary<string, bool> Flags { get; }
}

internal enum VerticalMerge { None, Start, Continue }

internal sealed class TableCell
{
    internal int? TypedControlId { get; set; }
    internal List<DocumentBlock> Content { get; } = new List<DocumentBlock>();
    internal IEnumerable<DocumentBlock> Blocks => Content.Count > 0 ? Content : Paragraphs;
    internal TableCell() { }
    internal TableCell(ParagraphBlock paragraph) => Paragraphs.Add(paragraph);
    internal List<ParagraphBlock> Paragraphs { get; } = new List<ParagraphBlock>();
    internal double? CellWidth { get; set; }
    internal double? WidthPercent { get; set; }
    internal CellPadding? Padding { get; set; }
    internal int ColumnSpan { get; set; } = 1;
    internal VerticalMerge Merge { get; set; }
    internal string VerticalAlignment { get; set; } = "top";
    internal string? Background { get; set; }
    internal Dictionary<string, CellBorder> Borders { get; } = new Dictionary<string, CellBorder>();
    internal string Text => string.Join("\n", Paragraphs.Select(p => p.Text));
}

internal sealed class CellBorder
{
    internal string Style { get; set; } = "single";
    internal string Color { get; set; } = "000000";
    internal double Width { get; set; } = 0.5;
}

internal sealed class TextRun
{
    internal int? TypedControlId { get; set; }
    internal bool ControlDisplay { get; set; }
    internal ControlGlyph? ControlGlyph { get; set; }
    internal TextRun(string text, bool bold = false, bool italic = false, bool code = false, string? link = null,
        double? fontSize = null, string? color = null, DocumentImage? image = null, RunAppearance? appearance = null, string? fieldInstruction = null, DocumentNote? note = null, PositionedBox? box = null, string? bookmark = null, TemplateControl? control = null, DocumentObjectData? retainedObject = null)
    {
        this.text = text;
        Bold = bold;
        Italic = italic;
        Code = code;
        Link = link;
        FontSize = fontSize;
        Color = color;
        Image = image;
        Appearance = appearance ?? new RunAppearance();
        FieldInstruction = fieldInstruction; Note = note; Box = box; Bookmark = bookmark;
        Control = control;
        Object = retainedObject;
    }

    private readonly string text;
    internal string Text => Object?.Description ?? (Note == null ? text : "[" + Note.Label + "]");
    internal DocumentObjectData? Object { get; }
    internal int? BidiLevel { get; set; }
    internal bool Bold { get; }
    internal bool Italic { get; }
    internal bool Code { get; }
    internal string? Link { get; }
    internal double? FontSize { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal RunAppearance Appearance { get; }
    internal string? FieldInstruction { get; }
    internal DocumentNote? Note { get; }
    internal PositionedBox? Box { get; }
    internal string? Bookmark { get; }
    internal TemplateControl? Control { get; }
    internal Dictionary<int, TextRun>? FieldChildren { get; set; }
    internal List<TextRun>? FieldCache { get; set; }
    internal string? Revision { get; set; }
    internal string? FormatRevision { get; set; }
    internal TextRun? PreviousFormat { get; set; }
    internal bool ExplicitFormat { get; set; }
    internal string? Comment { get; set; }
    internal int CommentBoundary { get; set; }
    // Every semantic run rebuild must retain the complete field/history carrier.
    internal TextRun RetainMetadata(TextRun source)
    {
        ControlDisplay = source.ControlDisplay; ControlGlyph = source.ControlGlyph; TypedControlId = source.TypedControlId; FieldChildren = source.FieldChildren; FieldCache = source.FieldCache;
        Revision = source.Revision; FormatRevision = source.FormatRevision; PreviousFormat = source.PreviousFormat; ExplicitFormat = source.ExplicitFormat; Comment = source.Comment; CommentBoundary = source.CommentBoundary; BidiLevel = source.BidiLevel;
        return this;
    }
    internal TextRun WithText(string text) => new TextRun(text, Bold, Italic, Code, Link, FontSize, Color, Image, Appearance, FieldInstruction, Note, Box, Bookmark, Control, Object)
    { ControlDisplay = ControlDisplay, ControlGlyph = ControlGlyph, TypedControlId = TypedControlId, FieldChildren = FieldChildren, FieldCache = FieldCache, Revision = Revision, FormatRevision = FormatRevision, PreviousFormat = PreviousFormat, ExplicitFormat = ExplicitFormat, Comment = Comment, CommentBoundary = CommentBoundary, BidiLevel = BidiLevel };
}

internal sealed class TemplateControl
{
    internal int? Id { get; set; }
    internal string? Alias { get; set; }
    internal TemplateControl(string tag) { Tag = tag; }
    internal string Tag { get; }
    internal bool Executable { get; set; } = true;
}

internal sealed class RunAppearance
{
    internal string? ReviewCompanions { get; set; }
    internal string? FontFamily { get; set; }
    internal string? ThemeFontAscii { get; set; }
    internal string? ThemeFontHighAnsi { get; set; }
    internal string? ThemeColor { get; set; }
    internal string? ThemeTint { get; set; }
    internal string? ThemeShade { get; set; }
    internal bool Underline { get; set; }
    internal bool Strike { get; set; }
    internal string? Background { get; set; }
    internal string? VerticalAlignment { get; set; }
    internal bool HasValues => FontFamily != null || Underline || Strike || Background != null || VerticalAlignment != null;
    internal RunAppearance Copy() => (RunAppearance)MemberwiseClone();
}

internal sealed class CellPadding
{
    // Tracks which sides were explicitly read or inherited from the source.
    // A value is not evidence that an omitted source margin was zero.
    internal int SourceSides { get; set; }
    internal bool SourceAmbiguous { get; set; }
    internal double Top { get; set; }
    internal double Right { get; set; }
    internal double Bottom { get; set; }
    internal double Left { get; set; }
    internal CellPadding Copy() => (CellPadding)MemberwiseClone();
}

internal sealed class DocumentNote
{
    internal bool ExplicitReferenceMark { get; set; }
    internal string Id { get; set; } = "";
    internal bool Endnote { get; set; }
    internal int PartId { get; set; }
    internal NotePolicy Policy { get; set; } = new NotePolicy();
    internal int Number { get; set; }
    internal string Format { get; set; } = "decimal";
    internal List<DocumentBlock> Blocks { get; } = new List<DocumentBlock>();
    internal string Label => ListCounter.Number(Number, Format);
}

internal sealed class ListItem
{
    internal string Id { get; set; } = "";
    internal int Level { get; set; }
    internal int Start { get; set; } = 1;
    internal bool Ordered { get; set; }
    internal ListLevel[]? Definition { get; set; }
    internal ListItem Copy() => (ListItem)MemberwiseClone();
}

internal sealed class ListLevel
{
    internal string Format { get; set; } = "decimal";
    internal string Pattern { get; set; } = "";
    internal int Start { get; set; } = 1;
    internal int RestartAfter { get; set; }
    internal bool Legal { get; set; }
    internal string Suffix { get; set; } = "tab";
    internal ParagraphLayout Layout { get; set; } = new ParagraphLayout();
    internal TextRun Appearance { get; set; } = new TextRun("");
    internal HashSet<string> LayoutProperties { get; } = new HashSet<string>();
    internal void ApplyLayout(ParagraphLayout target)
    {
        if (LayoutProperties.Contains("ind")) { target.Left = Layout.Left; target.Right = Layout.Right; target.FirstLine = Layout.FirstLine; }
        if (Layout.Before.HasValue) target.Before = Layout.Before;
        if (Layout.After.HasValue) target.After = Layout.After;
        if (Layout.LineSpacing.HasValue) { target.LineSpacing = Layout.LineSpacing; target.MultipleLineSpacing = Layout.MultipleLineSpacing; target.ExactLineSpacing = Layout.ExactLineSpacing; }
        if (LayoutProperties.Contains("tabs")) { target.Tabs.Clear(); target.Tabs.AddRange(Layout.Tabs); }
        if (LayoutProperties.Contains("keepNext")) target.KeepWithNext = Layout.KeepWithNext;
        if (LayoutProperties.Contains("keepLines")) target.KeepTogether = Layout.KeepTogether;
        if (LayoutProperties.Contains("pageBreakBefore")) target.PageBreakBefore = Layout.PageBreakBefore;
    }
}

internal sealed class ListCounter
{
    private readonly Dictionary<string, int?[]> counters = new Dictionary<string, int?[]>();
    internal int CurrentValue { get; private set; }
    internal ListCounter Copy()
    {
        var copy = new ListCounter { CurrentValue = CurrentValue };
        foreach (var pair in counters) copy.counters.Add(pair.Key, (int?[])pair.Value.Clone()); return copy;
    }
    internal string Label(ListItem item)
    {
        if (!counters.TryGetValue(item.Id, out int?[]? values)) counters.Add(item.Id, values = new int?[9]);
        int value = values[item.Level].HasValue ? checked(values[item.Level]!.Value + 1) : item.Start;
        values[item.Level] = value; CurrentValue = value;
        for (int i = item.Level + 1; i < 9; i++)
            if (item.Definition == null || item.Definition[i].RestartAfter >= item.Level) values[i] = null;
        if (item.Definition == null) return item.Ordered ? Number(value, "decimal") + "." : "•";
        ListLevel level = item.Definition[item.Level];
        string pattern = level.Pattern;
        for (int i = 0; i < 9; i++)
            pattern = pattern.Replace("%" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Number(values[i] ?? item.Definition[i].Start, level.Legal ? "decimal" : item.Definition[i].Format));
        return pattern;
    }

    internal static string Number(int value, string format)
    {
        if (value <= 0) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (format is "lowerLetter" or "upperLetter")
        {
            string result = "";
            while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; }
            return format == "lowerLetter" ? result.ToLowerInvariant() : result;
        }
        if (format is "lowerRoman" or "upperRoman" && value > 3999) throw new System.NotSupportedException("Numbering.RomanRange: Roman labels are bounded to 3999.");
        if (format is "lowerRoman" or "upperRoman")
        {
            int[] amounts = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] digits = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < amounts.Length; i++) while (value >= amounts[i]) { result.Append(digits[i]); value -= amounts[i]; }
            return format == "lowerRoman" ? result.ToString().ToLowerInvariant() : result.ToString();
        }
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
