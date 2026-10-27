using System.Collections.Generic;
using System.Linq;

namespace WordNet.Model;

internal sealed class DocumentModel
{
    internal int ObjectCount { get; set; }
    internal ContentControlStore Controls { get; set; } = new ContentControlStore();
    internal bool HasControlDisplay { get; set; }
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

internal abstract class DocumentBlock
{
    internal int? TypedControlId { get; set; }
    internal TemplateControl? Control { get; set; }
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
    internal double LeftIndent { get; set; }
    internal List<double> ColumnWidths { get; } = new List<double>();
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
}

internal enum VerticalMerge { None, Start, Continue }

internal sealed class TableCell
{
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
    internal string? Comment { get; set; }
    internal int CommentBoundary { get; set; }
    // Every semantic run rebuild must retain the complete field/history carrier.
    internal TextRun RetainMetadata(TextRun source)
    {
        ControlDisplay = source.ControlDisplay; ControlGlyph = source.ControlGlyph; TypedControlId = source.TypedControlId; FieldChildren = source.FieldChildren; FieldCache = source.FieldCache;
        Revision = source.Revision; Comment = source.Comment; CommentBoundary = source.CommentBoundary; BidiLevel = source.BidiLevel;
        return this;
    }
    internal TextRun WithText(string text) => new TextRun(text, Bold, Italic, Code, Link, FontSize, Color, Image, Appearance, FieldInstruction, Note, Box, Bookmark, Control, Object)
    { ControlDisplay = ControlDisplay, ControlGlyph = ControlGlyph, TypedControlId = TypedControlId, FieldChildren = FieldChildren, FieldCache = FieldCache, Revision = Revision, Comment = Comment, CommentBoundary = CommentBoundary, BidiLevel = BidiLevel };
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
    internal string? FontFamily { get; set; }
    internal bool Underline { get; set; }
    internal bool Strike { get; set; }
    internal string? Background { get; set; }
    internal string? VerticalAlignment { get; set; }
    internal bool HasValues => FontFamily != null || Underline || Strike || Background != null || VerticalAlignment != null;
    internal RunAppearance Copy() => (RunAppearance)MemberwiseClone();
}

internal sealed class CellPadding
{
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
