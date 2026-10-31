using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class FixedPage
{
    internal PageSettings Settings { get; set; } = new PageSettings();
    internal PageSettings SourceSettings { get; set; } = new PageSettings();
    internal List<PositionedText> Text { get; } = new List<PositionedText>();
    internal List<FixedShape> Shapes { get; } = new List<FixedShape>();
    internal List<(double X, double Y, double Width, double Height, string Wrap)> Exclusions { get; } = new List<(double, double, double, double, string)>();
    internal List<(DrawingAnchor Anchor, double X, double Y, double Width, double Height, bool Guarded)> DrawingBounds { get; } = new List<(DrawingAnchor, double, double, double, double, bool)>();
    internal bool HasGuardedDrawing { get; set; }
    internal HashSet<PageSettings> Sections { get; } = new HashSet<PageSettings>();
    internal int PageNumber { get; set; } = 1;
    internal bool ParityBlank { get; set; }
    internal double BodyBottom { get; set; }
    internal double MaximumAscent { get; set; }
    internal double MaximumDescent { get; set; }
    internal int NoteCount { get; set; }
    internal List<FixedHeading> Headings { get; } = new List<FixedHeading>();
}

internal sealed class FixedHeading
{
    internal FixedHeading(string title, int level, double x, double y) { Title = title; Level = level; X = x; Y = y; }
    internal string Title { get; }
    internal int Level { get; }
    internal double X { get; }
    internal double Y { get; }
}

internal sealed class PositionedText
{
    internal PositionedText(string text, double x, double y, double size, int font, string? color = null, DocumentImage? image = null, string? link = null, TextRun? field = null, PositionedBox? box = null, ParagraphBlock? sourceParagraph = null, TextRun? sourceRun = null, ShapedWord? nativeShape = null, bool label = false, ObjectVisual? visual = null, int rotation = 0, double rotationX = 0, double rotationY = 0, bool logicalCarrier = false, double carrierWidth = 0)
    { Visual = visual; SourceParagraph = sourceParagraph; SourceRun = sourceRun; NativeShape = nativeShape; IsLabel = label; Box = box; Text = text; X = x; Y = y; Size = size; Font = font; Color = color; Image = image; Link = link; Field = field; Rotation = rotation; RotationX = rotationX; RotationY = rotationY; LogicalCarrier = logicalCarrier; CarrierWidth = carrierWidth; }
    internal ObjectVisual? Visual { get; }
    internal string Text { get; }
    internal double X { get; }
    internal double Y { get; }
    internal double Size { get; }
    internal int Font { get; }
    internal string? Color { get; }
    internal DocumentImage? Image { get; }
    internal string? Link { get; }
    internal TextRun? Field { get; }
    internal PositionedBox? Box { get; }
    internal ParagraphBlock? SourceParagraph { get; }
    internal TextRun? SourceRun { get; }
    internal ShapedWord? NativeShape { get; }
    internal bool IsLabel { get; }
    internal int Rotation { get; }
    internal double RotationX { get; }
    internal double RotationY { get; }
    internal bool LogicalCarrier { get; }
    internal double CarrierWidth { get; }
    internal PositionedText Reshaped(ShapedWord shape) => new PositionedText(Text, X, Y, Size, Font, Color, Image, Link, Field, Box, SourceParagraph, SourceRun, shape, IsLabel, Visual, Rotation, RotationX, RotationY, LogicalCarrier, CarrierWidth);
    internal PositionedText At(double x, double y) => new PositionedText(Text, x, y, Size, Font, Color, Image, Link, Field, Box, SourceParagraph, SourceRun, NativeShape, IsLabel, Visual, Rotation, RotationX + x - X, RotationY + y - Y, LogicalCarrier, CarrierWidth);
    internal PositionedText WithText(string text) => new PositionedText(text, X, Y, Size, Font, Color, Image, Link, Field, Box, SourceParagraph, SourceRun, NativeShape, IsLabel, Visual, Rotation, RotationX, RotationY, LogicalCarrier, CarrierWidth);
    internal PositionedText Rotated(int rotation, double x, double y) => new PositionedText(Text, X, Y, Size, Font, Color, Image, Link, Field, Box, SourceParagraph, SourceRun, NativeShape, IsLabel, Visual, rotation, x, y, LogicalCarrier, CarrierWidth);
}

internal static partial class FixedLayout
{
    internal const double Width = 595.276;
    internal const double Height = 841.89;
    internal static readonly string[] FontNames = { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf", "NotoSans-Italic.ttf", "NotoSans-BoldItalic.ttf" };
    private sealed class Line
    {
        internal List<PositionedText> Items { get; } = new List<PositionedText>();
        internal List<FixedShape> Shapes { get; } = new List<FixedShape>();
        internal double Origin { get; set; }
        internal double PageLeft { get; set; }
        internal double? FlowWidth { get; set; }
        internal char BreakAfter { get; set; }
        internal double Ascent { get; set; }
        internal double Descent { get; set; }
        internal double Height => Ascent + Descent;
    }

    private sealed class CellLayout
    {
        internal List<Line> Lines { get; } = new List<Line>();
        internal CellPadding Padding { get; set; } = new CellPadding();
        internal int Column { get; set; }
        internal double Left { get; set; }
        internal double Width { get; set; }
        internal double Extent { get; set; }
        internal TableCell Source { get; set; } = null!;
        internal TableBlock? Table { get; set; }
        internal int Row { get; set; }
        internal int CellIndex { get; set; }
        internal int RowSpan { get; set; } = 1;
        internal VerticalMerge Merge { get; set; }
        internal double Height => Padding.Top + Lines.Sum(l => l.Height) + Padding.Bottom;
    }

    private sealed class RowLayout
    {
        internal List<CellLayout> Cells { get; } = new List<CellLayout>();
        internal double Height { get; set; }
        internal bool NonSplitting { get; set; }
    }

    private sealed class MeasuredText
    {
        internal ShapedWord? Native { get; set; }
        internal double Width { get; set; }
        internal double[] X { get; set; } = Array.Empty<double>();
        internal double[] Y { get; set; } = Array.Empty<double>();
        internal double[] OriginalX { get; set; } = Array.Empty<double>();
    }

    internal static byte[] FontBytes(int index)
    {
        using Stream stream = typeof(FixedLayout).GetTypeInfo().Assembly.GetManifestResourceStream("WordNet.Resources.Fonts." + FontNames[index])
            ?? throw new InvalidOperationException("Embedded font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }

    internal static List<FixedPage> Create(DocumentModel model, ConversionContext context)
    {
        if (context.Hyphenation.Dictionary != null)
        {
            PdfOptions pdf = context.Options.Pdf ?? throw new ArgumentException("PDF options must not be null.");
            if (context.Destination != DocumentFormat.Pdf || context.RenderingPreview || context.PreviewIntermediate || context.Typography.Enabled
                || pdf.Accessibility != PdfAccessibility.None || pdf.Conformance != PdfConformance.Pdf17
                || pdf.Pages.Count != 0 || context.Options.BalanceFinalColumns)
                throw new NotSupportedException("Hyphenation.Profile: Dictionary breaks currently require ordinary managed PDF without page selection or preview.");
            if (model.Notes.Count != 0 || model.ObjectCount != 0 || model.Review.HasHistory || model.Blocks.Any(b => b is not ParagraphBlock p
                || p.Alignment is not ParagraphAlignment.Left and not ParagraphAlignment.Justified || p.HeadingLevel != 0 || p.List != null || p.Control != null || p.NestedControl != null
                || p.TypedControlId.HasValue || p.StructuralRevision != null || p.ParagraphFormatRevision != null || p.Runs.Count != 1
                || p.Runs.Any(r => r.Image != null || r.Box != null || r.Note != null || r.Object != null || r.FieldInstruction != null
                    || r.Bookmark != null || r.Link != null || r.Control != null || r.TypedControlId.HasValue || r.ControlDisplay
                    || r.Revision != null || r.FormatRevision != null || r.Comment != null || r.Text.Any(c => c is '\t' or '\n' or '\f' or '\v'))))
                throw new NotSupportedException("Hyphenation.Owner: Dictionary breaks currently require plain, left-aligned body paragraphs with one run each.");
            if (model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Where(s => s != null).Any(s => s!.Columns != 1))
                throw new NotSupportedException("Hyphenation.Owner: Multicolumn text requires separate qualification.");
        }
        DiagnosticPhase previousPhase = context.Phase;
        context.Phase = DiagnosticPhase.Layout;
        bool references = DocumentReferences.HasFields(model);
        bool styleReferences = StyleReferences.HasFields(model);
        if (references) _ = DocumentReferences.Refresh(model, context);
        List<FixedPage> pages = CreateCore(model, context, 1, null);
        if (DocumentFields.NeedsPages(model))
        {
            int expected = pages.Count;
            var sections = Counts(pages);
            pages = CreateCore(model, context, expected, sections);
            var current = Counts(pages);
            if (pages.Count != expected || current.Count != sections.Count || current.Any(p => !sections.TryGetValue(p.Key, out int count) || count != p.Value))
                pages = CreateCore(model, context, pages.Count, current);
        }
        if (references || styleReferences)
        {
            bool stable = false;
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = styleReferences && StyleReferences.Refresh(model, pages, context);
                if (references) changed |= DocumentReferences.Refresh(model, context);
                if (!changed) { stable = true; break; }
                pages = CreateCore(model, context, pages.Count, Counts(pages));
            }
            if (!stable && ((styleReferences && StyleReferences.Refresh(model, pages, context)) | (references && DocumentReferences.Refresh(model, context))))
                context.Loss("Field.LayoutDidNotConverge", "Calculated references did not stabilize within four layout passes.");
        }
        if (context.Options.BalanceFinalColumns) pages = BalanceColumns(model, context, pages);
        if (context.HyphenationUsed && pages.Count != 1)
            throw new NotSupportedException("Hyphenation.Pagination: Hyphenated words cannot yet cross physical page boundaries.");
        if (styleReferences) StyleReferences.ReportUnresolved(model, context);
        context.Phase = previousPhase;
        return pages;

        static Dictionary<PageSettings, int> Counts(List<FixedPage> result) => result.SelectMany(p => p.Sections).Distinct().ToDictionary(s => s, s => result.Count(p => p.Sections.Contains(s)));
    }

    private static List<FixedPage> CreateCore(DocumentModel model, ConversionContext context, int totalPages, Dictionary<PageSettings, int>? sectionTotals, Dictionary<PageSettings, (int Page, double Bottom)>? balancedBottoms = null)
    {
        context.PhysicalPage = 0;
        NativeTypography.Prepare(model, context);
        using var native = NativeTypography.Open(context);
        using var measure = new PdfDocumentBuilder();
        var resolver = new FontResolver(model, context);
        var data = resolver.Data.ToArray();
        context.FontData = data;
        var fonts = data.Select(bytes => measure.AddTrueTypeFont(bytes)).ToArray();
        var maps = data.Select(bytes => TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable).ToArray();
        var kerning = data.Select(bytes => new FontKerning(bytes)).ToArray();
        // Per-call, bounded token measurements; repeated template runs avoid repeated parsing.
        var measurements = new Dictionary<(string Text, int Font, double Size), MeasuredText>();
        int cachedCharacters = 0;
        int? shapingLevel = null;
        PdfPageBuilder metrics = measure.AddPage(Width, Height);
        PageSettings sourceSettings = model.Blocks.FirstOrDefault()?.Section ?? model.FinalSection ?? new PageSettings();
        PageSettings settings = sourceSettings;
        settings.Validate();
        var pages = new List<FixedPage> { new FixedPage { Settings = settings, SourceSettings = sourceSettings } };
        var counter = new ListCounter();
        var storyHeights = new Dictionary<(PageSettings Section, List<DocumentBlock> Story), double>();
        Dictionary<PositionedBox, List<PositionedBox>> boxChains = LinkedTextBoxes.Validate(model, context);
        var chainLines = new Dictionary<PositionedBox, (List<Line> Lines, double Origin)>();
        int renderPage = 0, renderSectionPages = totalPages;
        double y = 0;
        settings = ReserveStories(sourceSettings); pages[0].Settings = settings;
        pages[0].Sections.Add(sourceSettings); pages[0].PageNumber = sourceSettings.PageNumberStart ?? 1;
        int columnIndex = 0;
        double columnTop = settings.Top;
        ApplyColumn();
        y = settings.Top;
        PageSettings? previousSection = model.Blocks.FirstOrDefault()?.Section;
        bool hasAnchors = model.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Image?.Anchor != null || r.Box != null);
        var prepared = new Dictionary<ParagraphBlock, List<Line>>();
        var preparedOrigins = new Dictionary<ParagraphBlock, (int Page, double Y, double Left, double Width, bool Anchored, ListCounter Counter)>();
        var pageNotes = new Dictionary<FixedPage, List<Line>>();
        var assignedNotes = new HashSet<DocumentNote>();
        var pendingNotes = new Queue<Line>();
        for (int blockIndex = 0; blockIndex < model.Blocks.Count; blockIndex++)
        {
            DocumentBlock block = model.Blocks[blockIndex];
            using var location = context.At(block.Location);
            context.CheckCancellation();
            if (!ReferenceEquals(previousSection, block.Section))
            {
                previousSection = block.Section;
                PageSettings next = block.Section ?? new PageSettings(); next.Validate();
                bool continuous = next.BreakType == "continuous";
                if (continuous && (next.Width != sourceSettings.Width || next.Height != sourceSettings.Height || next.Left != sourceSettings.Left || next.Right != sourceSettings.Right || next.Top != sourceSettings.Top || next.Bottom != sourceSettings.Bottom))
                { context.Loss("Layout.ContinuousGeometry", "A continuous section with incompatible physical geometry begins a new page."); continuous = false; }
                if (!continuous && next.BreakType is "oddPage" or "evenPage" && (pages.Count + 1) % 2 != (next.BreakType == "oddPage" ? 1 : 0))
                { AddPage(); pages[pages.Count - 1].ParityBlank = true; }
                sourceSettings = next; settings = ReserveStories(sourceSettings);
                if (continuous)
                {
                    if (next.PageNumberStart.HasValue) context.Loss("Layout.ContinuousPageRestart", "A page-number restart on a shared physical page takes effect on the following page.");
                    pages[pages.Count - 1].Sections.Add(next);
                    columnTop = Math.Max(y, pages[pages.Count - 1].BodyBottom); y = columnTop; columnIndex = 0; ApplyColumn();
                }
                else AddPage();
            }
            if (block is ParagraphBlock paragraph)
            {
                if (paragraph.Layout.PageBreakBefore && y > settings.Top) AddPage();
                if (sourceSettings.ExplicitColumns.Count != 0)
                {
                    if (paragraph.Layout.KeepTogether || paragraph.Layout.KeepWithNext)
                    {
                        ListCounter savedCounter = counter.Copy(); double required = paragraph.Layout.Before ?? 0;
                        int next = blockIndex;
                        while (next < model.Blocks.Count && model.Blocks[next] is ParagraphBlock member && ReferenceEquals(member.Section, paragraph.Section))
                        {
                            List<Line> measured = Lines(member, settings.Left, settings.Width - settings.Left - settings.Right,
                                member.HeadingLevel > 0 ? 26 - member.HeadingLevel * 2 : 12, false);
                            required += next == blockIndex || member.Layout.KeepTogether || member.Layout.KeepWithNext ? measured.Sum(l => l.Height) : measured[0].Height;
                            if (!member.Layout.KeepWithNext || required > settings.Height - settings.Top - settings.Bottom) break;
                            required += (member.Layout.After ?? member.FallbackAfter ?? 6); next++;
                        }
                        counter = savedCounter;
                        if (required <= settings.Height - settings.Top - settings.Bottom) NewPageIfNeeded(required);
                    }
                    bool first = true;
                    y += paragraph.Layout.Before ?? 0;
                    _ = Lines(paragraph, settings.Left, settings.Width - settings.Left - settings.Right,
                        paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 12, false, Flow);
                    y += paragraph.Layout.After ?? paragraph.FallbackAfter ?? 6;
                    continue;

                    void Flow(Line original)
                    {
                        Line line = FitLine(original, settings.Height - settings.Top - settings.Bottom);
                        ReserveNotes(line); NewPageIfNeeded(line.Height);
                        double width = settings.Width - settings.Left - settings.Right;
                        if (line.FlowWidth.HasValue && Math.Abs(line.FlowWidth.Value - width) > 0.01)
                        {
                            // The line crossing a column boundary must be measured again at its actual destination width.
                            var remaining = new ParagraphBlock
                            {
                                FallbackFontSize = paragraph.FallbackFontSize,
                                FallbackAfter = paragraph.FallbackAfter,
                                Mark = paragraph.Mark,
                                StyleId = paragraph.StyleId,
                                Alignment = paragraph.Alignment,
                                RightToLeft = paragraph.RightToLeft,
                                HeadingLevel = paragraph.HeadingLevel,
                                Layout = paragraph.Layout.Copy()
                            };
                            remaining.Layout.FirstLine = first ? paragraph.Layout.FirstLine : 0;
                            if (paragraph.List != null && paragraph.List.Definition?[paragraph.List.Level].LayoutProperties.Contains("ind") != true)
                                remaining.Layout.Left += 18 * paragraph.List.Level;
                            foreach (PositionedText item in line.Items)
                            {
                                TextRun source = item.SourceRun ?? item.Field ?? new TextRun("");
                                remaining.Runs.Add(new TextRun(item.Text, source.Bold, source.Italic, source.Code, source.Link, source.FontSize,
                                    source.Color, item.Image, source.Appearance.Copy(), item.Field?.FieldInstruction, source.Note, item.Box, source.Bookmark));
                            }
                            _ = Lines(remaining, settings.Left, width, paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 12, false, Flow);
                            if (line.BreakAfter == '\f') AddPage(); else if (line.BreakAfter == '\v') AdvanceColumn();
                            return;
                        }
                        if (first && paragraph.HeadingLevel > 0)
                            pages[pages.Count - 1].Headings.Add(new FixedHeading(paragraph.Text, paragraph.HeadingLevel, settings.Left, settings.Height - y));
                        first = false; Place(line, y); y += line.Height;
                        if (line.BreakAfter == '\f') AddPage(); else if (line.BreakAfter == '\v') AdvanceColumn();
                    }
                }
                List<Line> lines = Prepare(paragraph);
                double before = paragraph.Layout.Before ?? 0;
                double totalHeight = before + lines.Sum(l => l.Height);
                if (paragraph.Layout.KeepWithNext)
                {
                    int index = blockIndex + 1;
                    double after = paragraph.Layout.After ?? paragraph.FallbackAfter ?? 6;
                    while (index < model.Blocks.Count && model.Blocks[index] is ParagraphBlock following
                        && ReferenceEquals(block.Section, following.Section) && !following.Layout.PageBreakBefore)
                    {
                        List<Line> nextLines = Prepare(following);
                        totalHeight += (following.Layout.Before ?? 0) + after
                            + (following.Layout.KeepTogether || following.Layout.KeepWithNext ? nextLines.Sum(l => l.Height) : nextLines[0].Height);
                        // Once the group exceeds a page, no later member can make it fit.
                        // Bound the lookahead instead of rescanning arbitrarily long keep chains.
                        if (!following.Layout.KeepWithNext || totalHeight > settings.Height - settings.Top - settings.Bottom) break;
                        after = following.Layout.After ?? following.FallbackAfter ?? 6;
                        index++;
                    }
                }
                if ((paragraph.Layout.KeepTogether || paragraph.Layout.KeepWithNext) && totalHeight <= settings.Height - settings.Top - settings.Bottom)
                    NewPageIfNeeded(totalHeight);
                lines = Prepare(paragraph);
                y += before;
                bool firstLine = true;
                foreach (Line original in lines)
                {
                    Line line = FitLine(original, settings.Height - settings.Top - settings.Bottom);
                    ReserveNotes(line);
                    NewPageIfNeeded(line.Height);
                    if (firstLine && paragraph.HeadingLevel > 0)
                    {
                        string title = string.Concat(paragraph.Runs.Select(r => DocumentFields.Render(r, model, pages.Count, totalPages, totalPages, context)));
                        pages[pages.Count - 1].Headings.Add(new FixedHeading(title, paragraph.HeadingLevel, settings.Left, settings.Height - y));
                    }
                    firstLine = false;
                    Place(line, y); y += line.Height;
                    if (line.BreakAfter == '\f') AddPage(); else if (line.BreakAfter == '\v') AdvanceColumn();
                }
                y += paragraph.Layout.After ?? paragraph.FallbackAfter ?? 6;
            }
            else if (block is TableBlock table)
            {
                RenderTable(table);
                y += 6;
            }
        }
        foreach (DocumentNote note in model.Notes.Where(n => n.Endnote))
            foreach (Line line in NoteLines(note)) { NewPageIfNeeded(line.Height); Place(line, y); y += line.Height; }
        while (pendingNotes.Count > 0) AddPage();
        if (model.FinalSection != null && !ReferenceEquals(previousSection, model.FinalSection) && model.Blocks.Count > 0)
        {
            PageSettings next = model.FinalSection; next.Validate();
            bool shared = next.BreakType == "continuous" && next.Width == sourceSettings.Width && next.Height == sourceSettings.Height
                && next.Left == sourceSettings.Left && next.Right == sourceSettings.Right && next.Top == sourceSettings.Top && next.Bottom == sourceSettings.Bottom;
            if (shared)
            {
                pages[pages.Count - 1].Sections.Add(next);
                if (next.PageNumberStart.HasValue) context.Loss("Layout.ContinuousPageRestart", "A page-number restart on a shared physical page takes effect on the following page.");
            }
            else
            {
                if (next.BreakType == "continuous") context.Loss("Layout.ContinuousGeometry", "A continuous section with incompatible physical geometry begins a new page.");
                sourceSettings = next; settings = ReserveStories(sourceSettings); AddPage();
            }
        }
        var sectionPages = pages.SelectMany(p => p.Sections).Distinct().ToDictionary(s => s, s => pages.Count(p => p.Sections.Contains(s)));
        var sectionIndices = new Dictionary<PageSettings, int>();
        for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            FixedPage page = pages[pageIndex]; sourceSettings = page.SourceSettings; settings = page.Settings;
            if (page.ParityBlank) continue;
            context.PageNumberFormat = sourceSettings.PageNumberFormat;
            int sectionIndex = sectionIndices.TryGetValue(sourceSettings, out int previous) ? previous + 1 : 1;
            sectionIndices[sourceSettings] = sectionIndex;
            context.PhysicalPage = pageIndex + 1;
            renderPage = page.PageNumber; renderSectionPages = sectionPages[sourceSettings];
            AddStory("header-", sourceSettings.HeaderDistance, atEnd: false);
            AddStory("footer-", sourceSettings.Height - sourceSettings.FooterDistance, atEnd: true);
            void AddStory(string prefix, double position, bool atEnd)
            {
                string variant = sourceSettings.DifferentFirstPage && sectionIndex == 1 ? "first"
                    : sourceSettings.EvenAndOdd && (pageIndex + 1) % 2 == 0 ? "even" : "default";
                if (!sourceSettings.Stories.TryGetValue(prefix + variant, out List<DocumentBlock>? story)) return;
                List<Line> lines = StoryLines(story);
                double top = atEnd ? position - lines.Sum(l => l.Height) : position;
                if (context.EffectiveMode == FidelityMode.Lenient)
                {
                    double constrained = Math.Max(0, Math.Min(top, settings.Height - lines.Sum(l => l.Height)));
                    if (constrained != top) context.Report("Layout.StoryPositionApproximated", "A header/footer outside page bounds is moved onto the page.", DiagnosticSeverity.Warning);
                    top = constrained;
                }
                var items = new List<PositionedText>();
                foreach (Line line in lines)
                {
                    int start = page.Text.Count;
                    PlaceOn(page, line, top);
                    items.AddRange(page.Text.Skip(start)); page.Text.RemoveRange(start, page.Text.Count - start);
                    top += line.Height;
                }
                if (atEnd) page.Text.AddRange(items); else page.Text.InsertRange(0, items);
            }
        }
        foreach (var pair in pageNotes)
        {
            FixedPage page = pair.Key;
            double top = page.Settings.Height - page.Settings.Bottom - pair.Value.Sum(l => l.Height);
            page.Shapes.Add(new FixedShape(page.SourceSettings.Left, top - 6, Math.Min(72, page.Settings.Width - page.SourceSettings.Left - page.SourceSettings.Right), 0, "000000", 0.5));
            foreach (Line line in pair.Value)
            {
                foreach (PositionedText item in line.Items) page.Text.Add(item.At(item.X, top + line.Ascent + item.Y));
                top += line.Height;
            }
        }
        context.PageCount = pages.Count;
        return pages;

        PageSettings ReserveStories(PageSettings source)
        {
            settings = source;
            double header = 0, footer = 0;
            foreach (var story in source.Stories)
            {
                TextRun[] referencesInStory = DocumentModel.Expand(story.Value).OfType<ParagraphBlock>().SelectMany(p => p.Runs)
                    .Where(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "STYLEREF").ToArray();
                bool cacheable = referencesInStory.Length > 0 && !DocumentModel.Expand(story.Value).OfType<ParagraphBlock>().SelectMany(p => p.Runs)
                    .Any(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES");
                double height;
                if (!cacheable || !storyHeights.TryGetValue((source, story.Value), out height))
                {
                    int savedPhysicalPage = context.PhysicalPage;
                    try
                    {
                        height = StoryLines(story.Value).Sum(l => l.Height);
                        var measured = new HashSet<string>(StringComparer.Ordinal);
                        if (referencesInStory.Length > 0 && model.StylePages.TryGetValue(source, out List<int>? candidates))
                            foreach (int candidate in candidates)
                            {
                                context.CheckCancellation();
                                var values = referencesInStory.Select(r => model.StyleResults.TryGetValue((r, candidate), out string? value) ? value : r.Text).ToArray();
                                int characters = checked(values.Sum(v => v.Length));
                                context.ReserveFieldWork(checked(characters + referencesInStory.Length));
                                string signature = string.Concat(values.Select(v => v.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + v));
                                if (!measured.Add(signature)) continue;
                                context.PhysicalPage = candidate;
                                height = Math.Max(height, StoryLines(story.Value).Sum(l => l.Height));
                            }
                        if (cacheable) storyHeights[(source, story.Value)] = height;
                    }
                    finally { context.PhysicalPage = savedPhysicalPage; }
                }
                if (story.Key.StartsWith("header-", StringComparison.Ordinal)) header = Math.Max(header, height);
                else footer = Math.Max(footer, height);
            }
            PageSettings reserved = source.Copy();
            if (header > 0) reserved.Top = Math.Max(source.Top, source.HeaderDistance + header + 6);
            if (footer > 0) reserved.Bottom = Math.Max(source.Bottom, source.FooterDistance + footer + 6);
            if (context.EffectiveMode == FidelityMode.Lenient && reserved.Top + reserved.Bottom >= reserved.Height)
            {
                reserved.Top = Math.Min(reserved.Top, reserved.Height * 0.45);
                reserved.Bottom = Math.Min(reserved.Bottom, reserved.Height * 0.45);
                context.ReportStrictRejection("Layout.StoryAreaConstrained", "Oversized header/footer reservation is constrained to retain printable body space.");
            }
            reserved.Validate(); return reserved;
        }

        List<Line> StoryLines(List<DocumentBlock> story)
        {
            ListCounter bodyCounter = counter; counter = new ListCounter();
            var result = new List<Line>();
            foreach (DocumentBlock block in story)
            {
                if (block is ParagraphBlock paragraph) AddParagraph(paragraph, settings.Left, settings.Width - settings.Left - settings.Right, result);
                else if (block is TableBlock table)
                {
                    result.AddRange(NestedLines(BuildRows(table, settings.Left, settings.Width - settings.Left - settings.Right, 0)));
                }
            }
            if (context.EffectiveMode == FidelityMode.Lenient)
            {
                double height = result.Sum(l => l.Height), maximum = settings.Height * 0.35;
                if (height > maximum)
                {
                    context.Report("Layout.StoryScaledToFit", "An oversized header/footer is proportionally scaled to retain body space.", DiagnosticSeverity.Warning);
                    double factor = maximum / height;
                    result = result.Select(l => ScaleLine(l, factor)).ToList();
                }
            }
            counter = bodyCounter; return result;
        }

        void AddParagraph(ParagraphBlock paragraph, double left, double width, List<Line> target)
        {
            using var location = context.At(paragraph.Location);
            List<Line> lines = Lines(paragraph, left, width, 12, false);
            if (lines.Count > 0) { lines[0].Ascent += paragraph.Layout.Before ?? 0; lines[lines.Count - 1].Descent += paragraph.Layout.After ?? paragraph.FallbackAfter ?? 6; }
            target.AddRange(lines);
        }

        void RenderTable(TableBlock table)
        {
            bool reportedBorderRepeat = false, reportedShadingRepeat = false;
            double tablePageLeft = settings.Left;
            double tableWidth = settings.Width - settings.Left - settings.Right;
            ListCounter tableCounter = counter.Copy();
            List<RowLayout> rows = BuildRows(table, settings.Left, settings.Width - settings.Left - settings.Right, 0);
            ListCounter afterTableCounter = counter.Copy();
            double headerHeight = rows.Take(table.HeaderRowCount).Sum(row => row.Height);
            double pageHeight = settings.Height - settings.Top - settings.Bottom;
            if (headerHeight >= pageHeight)
            {
                if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableHeader: Repeating headers leave no space for body rows.");
                context.ReportStrictRejection("Table.HeaderRepeatOmitted", "An oversized table header is retained once without repetition.");
                table.HeaderRowCount = 0; headerHeight = 0;
            }
            if (table.HeaderRowCount > 0 && table.HeaderRowCount < rows.Count)
            {
                int body = table.HeaderRowCount, end = body + 1;
                for (int member = body; member < end; member++)
                    foreach (CellLayout cell in rows[member].Cells) end = Math.Max(end, member + cell.RowSpan);
                double bodyHeight = rows.Skip(body).Take(end - body).Sum(row => row.Height);
                if (bodyHeight > pageHeight - headerHeight && end == body + 1 && !table.NonSplittingRows.Contains(body))
                    bodyHeight = rows[body].Cells.Select(cell => cell.Padding.Top + (cell.Lines.FirstOrDefault()?.Height ?? 0) + cell.Padding.Bottom).DefaultIfEmpty(0).Max();
                if (headerHeight + bodyHeight <= pageHeight) NewPageIfNeeded(headerHeight + bodyHeight);
            }
            for (int r = 0; r < rows.Count;)
            {
                context.CheckCancellation();
                int end = r == 0 ? Math.Max(1, table.HeaderRowCount) : r + 1;
                for (int member = r; member < end; member++)
                    foreach (CellLayout cell in rows[member].Cells) end = Math.Max(end, member + cell.RowSpan);
                double height = rows.Skip(r).Take(end - r).Sum(row => row.Height);
                double capacity = settings.Height - settings.Top - settings.Bottom - (r >= table.HeaderRowCount ? headerHeight : 0);
                var reservation = new Line { Ascent = height };
                reservation.Items.AddRange(rows.Skip(r).Take(end - r).SelectMany(row => row.Cells).SelectMany(c => c.Lines).SelectMany(l => l.Items));
                DocumentNote[] groupNotes = reservation.Items.Where(i => i.Field?.Note != null && !i.Field.Note.Endnote)
                    .Select(i => i.Field!.Note!).Distinct().Where(n => !assignedNotes.Contains(n)).ToArray();
                if (groupNotes.Length != 0)
                {
                    double noteHeight = Math.Min(pageHeight * .4, groupNotes.SelectMany(NoteLines).Sum(l => l.Height)) + 12;
                    capacity = Math.Min(capacity, pageHeight - (r >= table.HeaderRowCount ? headerHeight : 0) - noteHeight);
                }
                if (height <= capacity) ReserveNotes(reservation, height, () => TablePage(r >= table.HeaderRowCount));
                if (height > capacity)
                {
                    if (end - r > 1 || table.NonSplittingRows.Contains(r) || r < table.HeaderRowCount)
                    {
                        if (end - r > 1 && context.Typography.Enabled && context.Typography.ExtendedBidi
                            && rows.Skip(r).Take(end - r).SelectMany(row => row.Cells).Any(cell => cell.RowSpan > 1))
                            throw new NotSupportedException("Typography.MergedGroupHeight: A vertically merged native table group cannot exceed the available page height.");
                        if (context.EffectiveMode != FidelityMode.Lenient)
                            throw new NotSupportedException("Layout.TableGroupHeight: A non-splitting row or vertically merged group exceeds a page.");
                        context.ReportStrictRejection(end - r > 1 ? "Table.MergedGroupSplit" : "Table.RowForcedSplit", "An oversized row/group is split at line boundaries to retain its content.");
                        for (int member = r; member < end; member++) SplitRow(rows[member], r >= table.HeaderRowCount, member);
                    }
                    else SplitRow(rows[r], r >= table.HeaderRowCount, r);
                }
                else
                {
                    if (y + height > settings.Height - settings.Bottom) { TablePage(r >= table.HeaderRowCount); continue; }
                    for (int member = r; member < end; member++) PlaceRow(rows[member]);
                }
                r = end;
            }

            void PlaceRow(RowLayout row)
            {
                foreach (CellLayout cell in row.Cells)
                {
                    if (cell.Merge == VerticalMerge.Continue) continue;
                    pages[pages.Count - 1].Shapes.AddRange(CellShapes(cell, y, cell.Extent).Select(s => s.At(s.X + settings.Left - tablePageLeft, s.Y)));
                    double top = y + CellOffset(cell);
                    foreach (Line line in cell.Lines) { Place(line, top); top += line.Height; }
                }
                y += row.Height;
            }

            void TablePage(bool repeat)
            {
                if (repeat && table.HeaderRowCount > 0 && table.HasTableBorders && !reportedBorderRepeat)
                {
                    context.Loss("Table.BorderRepeat", "Direct table borders on repeated headers are approximated across pages.");
                    reportedBorderRepeat = true;
                }
                if (repeat && table.HeaderRowCount > 0 && table.HasTableShading && !reportedShadingRepeat)
                {
                    context.Loss("Table.ShadingRepeat", "Direct table shading on repeated headers is not independently qualified.");
                    reportedShadingRepeat = true;
                }
                AdvanceColumn();
                double available = settings.Width - settings.Left - settings.Right;
                if (sourceSettings.ExplicitColumns.Count != 0 && Math.Abs(available - tableWidth) > 0.01)
                {
                    counter = tableCounter.Copy(); rows = BuildRows(table, settings.Left, available, 0); counter = afterTableCounter.Copy();
                    tablePageLeft = settings.Left; tableWidth = available;
                    headerHeight = rows.Take(table.HeaderRowCount).Sum(row => row.Height);
                    if (headerHeight >= settings.Height - settings.Top - settings.Bottom) throw new NotSupportedException("Layout.TableHeader: Repeating headers leave no body space in this column.");
                }
                if (repeat) foreach (RowLayout header in rows.Take(table.HeaderRowCount)) PlaceRow(header);
            }

            void SplitRow(RowLayout row, bool repeat, int rowIndex)
            {
                if (table.HasTableBorders)
                    context.Loss("Table.BorderSplit", "Direct table borders on split rows are approximated across pages.");
                if (table.HasTableShading)
                    context.Loss("Table.ShadingSplit", "Direct table shading on split rows is not independently qualified.");
                if (row.Cells.All(c => c.RowSpan == 1) && row.Cells.SelectMany(c => c.Lines).SelectMany(l => l.Items).Any(i => i.Field?.Note != null))
                {
                    bool nonSplitting = row.NonSplitting; row.NonSplitting = false;
                    List<Line> bands = NestedLines(new List<RowLayout> { row }); row.NonSplitting = nonSplitting;
                    foreach (Line band in bands)
                    {
                        ReserveNotes(band, band.Height, () => TablePage(repeat));
                        if (y + band.Height > settings.Height - settings.Bottom) TablePage(repeat);
                        if (band.Height > settings.Height - settings.Bottom - y) throw new NotSupportedException("Layout.TableNoteHeight: A row band with notes does not fit the page.");
                        Place(band, y); y += band.Height;
                    }
                    return;
                }
                int[] offsets = new int[row.Cells.Count];
                while (row.Cells.Where((cell, index) => offsets[index] < cell.Lines.Count).Any())
                {
                    context.CheckCancellation();
                    double used = 0;
                    bool progress = false;
                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        CellLayout cell = row.Cells[c];
                        double top = y + cell.Padding.Top;
                        while (offsets[c] < cell.Lines.Count && top + cell.Lines[offsets[c]].Height + cell.Padding.Bottom <= settings.Height - settings.Bottom)
                        {
                            Line line = cell.Lines[offsets[c]++]; Place(line, top); top += line.Height; progress = true;
                        }
                        used = Math.Max(used, top - y + cell.Padding.Bottom);
                    }
                    if (!progress && y <= settings.Top + (repeat ? headerHeight : 0) + 0.001)
                    {
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableLineHeight: A cell line and its padding cannot fit below repeating headers.");
                        context.ReportStrictRejection("Table.LineScaledToFit", "Unusable cell padding is omitted and an oversized line is scaled to fit printable space.");
                        used = settings.Height - settings.Bottom - y;
                        for (int c = 0; c < row.Cells.Count; c++)
                            if (offsets[c] < row.Cells[c].Lines.Count) Place(FitLine(row.Cells[c].Lines[offsets[c]++], used), y);
                    }
                    foreach (CellLayout cell in row.Cells.Where(c => c.Merge != VerticalMerge.Continue))
                        pages[pages.Count - 1].Shapes.AddRange(CellShapes(cell, y, used).Select(s => s.At(s.X + settings.Left - tablePageLeft, s.Y)));
                    y += used;
                    if (row.Cells.Where((cell, index) => offsets[index] < cell.Lines.Count).Any())
                    {
                        double oldWidth = tableWidth; TablePage(repeat);
                        if (Math.Abs(tableWidth - oldWidth) > 0.01)
                        {
                            RowLayout destination = rows[rowIndex];
                            for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                            {
                                CellLayout target = destination.Cells[cellIndex]; var remaining = row.Cells[cellIndex].Lines.Skip(offsets[cellIndex]).ToArray();
                                target.Lines.Clear();
                                foreach (Line part in remaining)
                                {
                                    if (part.Shapes.Count != 0) throw new NotSupportedException("Layout.UnequalNestedTable: A nested table cannot split across columns with different widths.");
                                    if (part.Items.Count == 0) { target.Lines.Add(new Line { PageLeft = settings.Left, Ascent = part.Ascent, Descent = part.Descent }); continue; }
                                    ParagraphBlock? source = part.Items.Select(p => p.SourceParagraph).FirstOrDefault(p => p != null);
                                    var fragment = new ParagraphBlock
                                    {
                                        FallbackFontSize = source?.FallbackFontSize,
                                        FallbackAfter = source?.FallbackAfter,
                                        Mark = source?.Mark,
                                        StyleId = source?.StyleId,
                                        HeadingLevel = source?.HeadingLevel ?? 0,
                                        Alignment = source?.Alignment ?? ParagraphAlignment.Left,
                                        RightToLeft = source?.RightToLeft,
                                        Layout = source?.Layout.Copy() ?? new ParagraphLayout()
                                    };
                                    fragment.Layout.FirstLine = 0;
                                    foreach (PositionedText item in part.Items)
                                    {
                                        TextRun run = item.SourceRun ?? item.Field ?? new TextRun("");
                                        fragment.Runs.Add(new TextRun(item.Text, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color,
                                            item.Image, run.Appearance.Copy(), item.Field?.FieldInstruction, run.Note, item.Box, run.Bookmark));
                                    }
                                    target.Lines.AddRange(Lines(fragment, target.Left + target.Padding.Left,
                                        target.Width - target.Padding.Left - target.Padding.Right, source?.HeadingLevel > 0 ? 26 - source.HeadingLevel * 2 : 11, false));
                                }
                                offsets[cellIndex] = 0;
                            }
                            row = destination;
                        }
                    }
                }
            }
        }

        List<RowLayout> BuildRows(TableBlock table, double origin, double availableWidth, int depth)
        {
            var rows = new List<RowLayout>();
            if (depth > 0 && table.HeaderRowCount > 0) context.Loss("Table.NestedHeaders", "Nested-table headers are rendered once; repetition inside a split parent row is not supported.");
            if (depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Nested table exceeds the depth limit.");
            double[] widths = TableSizing.Columns(table, availableWidth, ContentWidths, context);
            bool fixedGrid = !table.AutoFit && (!table.ManagedImplicitAutoFit || context.Typography.Enabled) && !table.WidthPercent.HasValue && !table.Rows.SelectMany(c => c).Any(c => c.WidthPercent.HasValue);
            double[] edges = new double[widths.Length + 1];
            for (int i = 0; i < widths.Length; i++) edges[i + 1] = edges[i] + widths[i];
            for (int r = 0; r < table.Rows.Count; r++)
            {
                context.CheckCancellation();
                List<TableCell> source = table.Rows[r];
                var row = new RowLayout { NonSplitting = table.NonSplittingRows.Contains(r) }; rows.Add(row);
                int columns = source.Sum(c => c.ColumnSpan), column = 0, cellIndex = 0;
                double left = origin + table.LeftIndent, total = edges[edges.Length - 1];
                foreach (TableCell cell in source)
                {
                    double width = fixedGrid && cell.CellWidth.HasValue
                        ? cell.CellWidth.Value : edges[column + cell.ColumnSpan] - edges[column];
                    CellPadding padding = cell.Padding ?? new CellPadding { Right = 12 };
                    if (left < -0.1 || left + width > settings.Width + 0.1 || width <= padding.Left + padding.Right
                        || sourceSettings.ExplicitColumns.Count != 0 && (left < origin - 0.1 || left + width > origin + availableWidth + 0.1))
                    {
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableWidth: Table cells do not fit the page or their margins.");
                        context.ReportStrictRejection("Table.WidthApproximated", "Cell width and padding are constrained to printable space.");
                        left = Math.Max(settings.Left, Math.Min(left, settings.Width - settings.Right - 1));
                        width = Math.Max(1, Math.Min(width, settings.Width - settings.Right - left));
                        padding = new CellPadding();
                    }
                    var preparedCell = new CellLayout { Source = cell, Table = !table.HasTableBorders && !table.HasTableShading ? null : table, Row = r, CellIndex = cellIndex, Left = left, Width = width, Padding = padding, Column = column, Merge = cell.Merge };
                    if (cell.Merge != VerticalMerge.Continue)
                    {
                        DocumentBlock[] blocks = cell.Blocks.ToArray();
                        for (int p = 0; p < blocks.Length; p++)
                        {
                            if (blocks[p] is TableBlock nested)
                            {
                                preparedCell.Lines.AddRange(NestedLines(BuildRows(nested, left + padding.Left, width - padding.Left - padding.Right, depth + 1)));
                                continue;
                            }
                            var paragraph = (ParagraphBlock)blocks[p];
                            using var cellLocation = context.At(paragraph.Location);
                            if (paragraph.Layout.Before is double before && before > 0) preparedCell.Lines.Add(new Line { Descent = before });
                            preparedCell.Lines.AddRange(Lines(paragraph, left + padding.Left, width - padding.Left - padding.Right,
                                paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 11, false));
                            double after = paragraph.Layout.After ?? paragraph.FallbackAfter ?? (p + 1 < blocks.Length ? 6 : 0);
                            if (after > 0) preparedCell.Lines.Add(new Line { Descent = after });
                        }
                    }
                    row.Cells.Add(preparedCell);
                    left += width; column += cell.ColumnSpan; cellIndex++;
                }
            }
            for (int r = 0; r < rows.Count; r++)
            {
                foreach (CellLayout cell in rows[r].Cells)
                    if (cell.Merge == VerticalMerge.Start)
                        while (r + cell.RowSpan < rows.Count && rows[r + cell.RowSpan].Cells.Any(c => c.Column == cell.Column && c.Merge == VerticalMerge.Continue)) cell.RowSpan++;
                rows[r].Height = rows[r].Cells.Where(c => c.RowSpan == 1).Select(c => c.Height).DefaultIfEmpty(0).Max();
            }
            for (int r = 0; r < rows.Count; r++)
                foreach (CellLayout cell in rows[r].Cells.Where(c => c.RowSpan > 1))
                {
                    double available = rows.Skip(r).Take(cell.RowSpan).Sum(row => row.Height);
                    rows[r + cell.RowSpan - 1].Height += Math.Max(0, cell.Height - available);
                }
            foreach (var height in table.RowHeights)
            {
                if (height.Value.Exact && height.Value.Height < rows[height.Key].Height)
                {
                    if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TableRowHeight: An exact row height cannot contain its content.");
                    context.ReportStrictRejection("Layout.TableRowHeight", "An exact row height is increased to retain its content.");
                }
                rows[height.Key].Height = Math.Max(rows[height.Key].Height, height.Value.Height);
            }
            for (int r = 0; r < rows.Count; r++)
                foreach (CellLayout cell in rows[r].Cells) cell.Extent = rows.Skip(r).Take(cell.RowSpan).Sum(row => row.Height);
            return rows;
        }

        string ManagedText(string text, int font)
        {
            if (context.EffectiveMode == FidelityMode.Strict || native != null) return text;
            var safe = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c is '\n' or '\t' or '\f' or '\v' || !(c >= '\u0590' && c <= '\u0dff') && !char.IsSurrogate(c) && maps[font] != null && maps[font]!.TryGetGlyphIndex(c, out int glyph) && glyph != 0) safe.Append(c);
                else { safe.Append('?'); context.ReportStrictRejection("Font.MissingGlyph", "An unsupported glyph or shaping requirement uses a visible fallback character."); }
            }
            return safe.ToString();
        }

        (double Minimum, double Preferred) ContentWidths(TableCell cell)
        {
            double minimum = 1, preferred = 1;
            foreach (ParagraphBlock paragraph in cell.Paragraphs)
            {
                using var location = context.At(paragraph.Location);
                double line = 0;
                foreach (TextRun run in RunCoalescer.Combine(paragraph.Runs, forRendering: true))
                {
                    shapingLevel = run.BidiLevel;
                    if (run.Image != null) { minimum = Math.Max(minimum, run.Image.Width); line += run.Image.Width; continue; }
                    int font = resolver.Resolve(run, paragraph.HeadingLevel > 0); double size = run.FontSize ?? 11;
                    context.ReserveFieldWork(run.Text.Length);
                    foreach (string word in ManagedText(DocumentFields.Render(run, model, 1, totalPages, totalPages, context), font).Split(' ', '\n', '\t'))
                    {
                        double width = Measure(word, font, size); minimum = Math.Max(minimum, word.Length == 0 ? 0 : word.Max(c => Measure(c.ToString(), font, size)));
                        line += width + Measure(" ", font, size);
                    }
                }
                preferred = Math.Max(preferred, line + paragraph.Layout.Left + paragraph.Layout.Right);
            }
            CellPadding padding = cell.Padding ?? new CellPadding { Right = 12 };
            return (minimum + padding.Left + padding.Right, preferred + padding.Left + padding.Right);
        }

        double CellOffset(CellLayout cell)
        {
            double slack = Math.Max(0, cell.Extent - cell.Height);
            return cell.Padding.Top + (cell.Source.VerticalAlignment == "bottom" ? slack : cell.Source.VerticalAlignment == "center" ? slack / 2 : 0);
        }

        IEnumerable<FixedShape> CellShapes(CellLayout cell, double top, double height)
        {
            string? background = cell.Source.Background ?? cell.Table?.CellBackground(cell.Row, cell.CellIndex);
            if (background != null) yield return new FixedShape(cell.Left, top, cell.Width, height, background);
            IEnumerable<KeyValuePair<string, CellBorder>> borders = cell.Table == null ? cell.Source.Borders : cell.Table.CellBorders(cell.Row, cell.CellIndex);
            foreach (var border in borders.Where(b => b.Value.Width > 0))
            {
                bool vertical = border.Key is "left" or "right";
                yield return new FixedShape(cell.Left + (border.Key == "right" ? cell.Width : 0), top + (border.Key == "bottom" ? height : 0),
                    vertical ? 0 : cell.Width, vertical ? height : 0, border.Value.Color, border.Value.Width, border.Value.Style);
            }
        }

        List<Line> NestedLines(List<RowLayout> rows)
        {
            var result = new List<Line>();
            for (int r = 0; r < rows.Count;)
            {
                context.CheckCancellation();
                int endRow = r + 1;
                for (int member = r; member < endRow; member++)
                    foreach (CellLayout cell in rows[member].Cells) endRow = Math.Max(endRow, member + cell.RowSpan);
                var content = new List<(Line Line, double Top)>();
                var shapes = new List<FixedShape>();
                double rowTop = 0;
                for (int member = r; member < endRow; member++)
                {
                    foreach (CellLayout cell in rows[member].Cells.Where(c => c.Merge != VerticalMerge.Continue))
                    {
                        shapes.AddRange(CellShapes(cell, rowTop, cell.Extent));
                        double top = rowTop + CellOffset(cell);
                        foreach (Line line in cell.Lines) { content.Add((line, top)); top += line.Height; }
                    }
                    rowTop += rows[member].Height;
                }
                var intervals = content.OrderBy(c => c.Top).ToArray();
                var cuts = new List<double>();
                double boundary = 0;
                foreach (var item in intervals)
                {
                    if (item.Top >= boundary - 0.001 && boundary > 0) cuts.Add(boundary);
                    boundary = Math.Max(boundary, item.Top + item.Line.Height);
                }
                cuts.Add(Math.Max(boundary, rowTop));
                if (endRow > r + 1 || rows[r].NonSplitting)
                {
                    if (rowTop > settings.Height - settings.Top - settings.Bottom) throw new NotSupportedException("Layout.NestedTableGroupHeight: A nested non-splitting row or merged group exceeds one page.");
                    cuts.Clear(); cuts.Add(rowTop);
                }
                int offset = 0;
                double start = 0;
                foreach (double end in cuts.Distinct())
                {
                    if (end <= start) continue;
                    var band = new Line { Origin = rows[r].Cells[0].Left, PageLeft = settings.Left, Descent = end - start };
                    while (offset < intervals.Length && intervals[offset].Top < end - 0.001)
                    {
                        var item = intervals[offset++];
                        band.Items.AddRange(item.Line.Items.Select(t => t.At(t.X, item.Top - start + item.Line.Ascent + t.Y)));
                        band.Shapes.AddRange(item.Line.Shapes.Select(t => t.At(t.X, t.Y + item.Top - start)));
                    }
                    foreach (FixedShape shape in shapes)
                    {
                        if (shape.Height == 0)
                        { if (shape.Y >= start - 0.001 && (shape.Y < end - 0.001 || end == cuts[cuts.Count - 1] && shape.Y <= end)) band.Shapes.Add(shape.At(shape.X, shape.Y - start)); }
                        else
                        {
                            double top = Math.Max(start, shape.Y), bottom = Math.Min(end, shape.Y + shape.Height);
                            if (bottom > top) band.Shapes.Add(new FixedShape(shape.X, top - start, shape.Width, bottom - top, shape.Color, shape.Stroke, shape.Style));
                        }
                    }
                    result.Add(band); start = end;
                }
                r = endRow;
            }
            return result;
        }

        List<Line> NoteLines(DocumentNote note)
        {
            var lines = new List<Line>();
            int labelFont = resolver.Resolve(new TextRun(""), false);
            string label = note.Label + (note.ExplicitReferenceMark ? "" : " ");
            double labelWidth = Measure(label, labelFont, 9);
            ListCounter bodyCounter = counter; counter = new ListCounter();
            foreach (DocumentBlock block in note.Blocks)
            {
                if (block is not ParagraphBlock paragraph) { context.Loss("Notes.TableLayout", "Tables inside notes use flattened paragraphs."); }
                IEnumerable<ParagraphBlock> content = block is ParagraphBlock p ? new[] { p } : ((TableBlock)block).Rows.SelectMany(r => r).SelectMany(c => c.Paragraphs);
                double noteLeft = sourceSettings.ExplicitColumns.Count == 0 ? sourceSettings.Left : settings.Left;
                double noteWidth = sourceSettings.ExplicitColumns.Count == 0 ? sourceSettings.Width - sourceSettings.Left - sourceSettings.Right : settings.Width - settings.Left - settings.Right;
                foreach (ParagraphBlock noteParagraph in content) lines.AddRange(Lines(noteParagraph, noteLeft + labelWidth, noteWidth - labelWidth, 9, false));
            }
            if (lines.Count == 0) lines.Add(new Line { PageLeft = settings.Left, Ascent = 9, Descent = 4.5 });
            Line first = lines[0];
            first.Items.Insert(0, new PositionedText(label, sourceSettings.ExplicitColumns.Count == 0 ? sourceSettings.Left : settings.Left, 0, 9, labelFont));
            counter = bodyCounter; return lines;
        }

        double ReservedNoteHeight() => pageNotes.TryGetValue(pages[pages.Count - 1], out List<Line>? lines) ? lines.Sum(l => l.Height) + 12 : 0;

        void DrainNotes()
        {
            FixedPage page = pages[pages.Count - 1];
            double maximum = (page.Settings.Height - page.Settings.Top - page.Settings.Bottom) * 0.4;
            if (!pageNotes.TryGetValue(page, out List<Line>? lines)) { lines = new List<Line>(); pageNotes.Add(page, lines); }
            double height = lines.Sum(l => l.Height);
            while (pendingNotes.Count > 0 && (height + pendingNotes.Peek().Height <= maximum || lines.Count == 0))
            {
                Line line = pendingNotes.Dequeue();
                if (line.Height > maximum) throw new NotSupportedException("Layout.NoteHeight: A note line exceeds the bounded note area.");
                lines.Add(line); height += line.Height;
            }
            if (lines.Count == 0) pageNotes.Remove(page);
            settings.Bottom = page.Settings.Bottom + ReservedNoteHeight();
        }

        void ReserveNotes(Line line, double requiredHeight = 0, Action? advance = null)
        {
            DocumentNote[] notes = line.Items.Where(i => i.Field?.Note != null && !i.Field.Note.Endnote).Select(i => i.Field!.Note!).Distinct().Where(n => !assignedNotes.Contains(n)).ToArray();
            if (notes.Length == 0) return;
            var additional = notes.SelectMany(NoteLines).ToList();
            double maximum = (pages[pages.Count - 1].Settings.Height - settings.Top - pages[pages.Count - 1].Settings.Bottom) * 0.4;
            double reserve = Math.Min(maximum, additional.Sum(l => l.Height)) + (ReservedNoteHeight() == 0 ? 12 : 0);
            if (Math.Max(y + Math.Max(line.Height, requiredHeight), pages[pages.Count - 1].BodyBottom) + reserve > settings.Height - settings.Bottom && y > settings.Top) (advance ?? AddPage)();
            int pageNoteCount = pages[pages.Count - 1].NoteCount;
            foreach (DocumentNote note in notes.Where(n => n.Policy.Restart == "eachPage")) note.Number = note.Policy.Start + pageNoteCount++;
            additional = notes.SelectMany(NoteLines).ToList();
            pages[pages.Count - 1].NoteCount += notes.Length;
            foreach (DocumentNote note in notes) assignedNotes.Add(note);
            foreach (Line noteLine in additional) pendingNotes.Enqueue(noteLine);
            DrainNotes();
        }

        List<Line> Prepare(ParagraphBlock paragraph)
        {
            using var location = context.At(paragraph.Location);
            bool found = prepared.TryGetValue(paragraph, out List<Line>? lines);
            bool anchored = hasAnchors && (paragraph.Runs.Any(r => r.Image?.Anchor != null || r.Box != null) || pages[pages.Count - 1].Exclusions.Count != 0);
            if (!found || hasAnchors && (anchored || preparedOrigins[paragraph].Anchored)
                && (preparedOrigins[paragraph].Page != pages.Count || preparedOrigins[paragraph].Y != y
                    || preparedOrigins[paragraph].Left != settings.Left || preparedOrigins[paragraph].Width != settings.Width - settings.Left - settings.Right))
            {
                ListCounter advanced = counter;
                if (found) counter = preparedOrigins[paragraph].Counter.Copy();
                if (hasAnchors) preparedOrigins[paragraph] = (pages.Count, y, settings.Left, settings.Width - settings.Left - settings.Right, anchored, counter.Copy());
                double savedY = y;
                try
                {
                    y += paragraph.Layout.Before ?? 0;
                    lines = Lines(paragraph, settings.Left, settings.Width - settings.Left - settings.Right,
                        paragraph.HeadingLevel > 0 ? 26 - paragraph.HeadingLevel * 2 : 12, false);
                }
                finally { y = savedY; }
                prepared[paragraph] = lines;
                if (found) counter = advanced;
            }
            return lines!;
        }

        (double X, double Y, double Width, double Height, string Wrap) DrawingBounds(DrawingAnchor anchor, double width, double height, double paragraphTop, bool validate = true)
        {
            double left = anchor.HorizontalOrigin == "page" ? 0 : anchor.HorizontalOrigin == "column" ? settings.Left : sourceSettings.Left;
            double top = anchor.VerticalOrigin == "page" ? 0 : anchor.VerticalOrigin == "paragraph" ? paragraphTop : sourceSettings.Top;
            double areaWidth = anchor.HorizontalOrigin == "page" ? settings.Width : anchor.HorizontalOrigin == "column" ? settings.Width - settings.Left - settings.Right : settings.Width - sourceSettings.Left - sourceSettings.Right;
            double areaHeight = anchor.VerticalOrigin == "page" ? settings.Height : settings.Height - sourceSettings.Top - sourceSettings.Bottom;
            double x = left + anchor.X + (anchor.HorizontalAlignment == "center" ? (areaWidth - width) / 2 : anchor.HorizontalAlignment == "right" ? areaWidth - width : 0);
            double topY = top + anchor.Y + (anchor.VerticalAlignment == "center" ? (areaHeight - height) / 2 : anchor.VerticalAlignment == "bottom" ? areaHeight - height : 0);
            if (validate && (x < 0 || topY < 0 || x + width > settings.Width || topY + height > settings.Height))
            {
                if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.AnchorBounds: A positioned drawing extends outside the physical page.");
                context.ReportStrictRejection("Layout.AnchorBounds", "A positioned drawing extends outside the physical page and is clipped at the page edge.");
            }
            return (x, topY, width, height, anchor.Wrap);
        }

        void RememberExclusion(DrawingAnchor anchor, (double X, double Y, double Width, double Height, string Wrap) bounds)
        {
            if (anchor.Wrap != "none") pages[pages.Count - 1].Exclusions.Add((bounds.X - anchor.Distance.Left, bounds.Y - anchor.Distance.Top,
                bounds.Width + anchor.Distance.Left + anchor.Distance.Right, bounds.Height + anchor.Distance.Top + anchor.Distance.Bottom, anchor.Wrap));
        }

        static (double X, double Y, double Width, double Height, string Wrap) RotatedBounds((double X, double Y, double Width, double Height, string Wrap) bounds, int rotation)
            => rotation is 90 or 270 ? (bounds.X + (bounds.Width - bounds.Height) / 2, bounds.Y + (bounds.Height - bounds.Width) / 2, bounds.Height, bounds.Width, bounds.Wrap) : bounds;

        void Place(Line line, double top)
        {
            pages[pages.Count - 1].MaximumAscent = Math.Max(pages[pages.Count - 1].MaximumAscent, line.Ascent);
            pages[pages.Count - 1].MaximumDescent = Math.Max(pages[pages.Count - 1].MaximumDescent, line.Descent);
            pages[pages.Count - 1].BodyBottom = Math.Max(pages[pages.Count - 1].BodyBottom, top + line.Height);
            PlaceOn(pages[pages.Count - 1], line, top);
        }

        void PlaceOn(FixedPage destination, Line line, double top)
        {
            void CheckDrawingOrder(DrawingAnchor anchor, (double X, double Y, double Width, double Height, string Wrap) bounds, bool guarded)
            {
                if (!guarded && !destination.HasGuardedDrawing)
                { destination.DrawingBounds.Add((anchor, bounds.X, bounds.Y, bounds.Width, bounds.Height, false)); return; }
                destination.HasGuardedDrawing |= guarded;
                foreach (var previous in destination.DrawingBounds)
                {
                    context.CheckCancellation();
                    if (!(guarded || previous.Guarded)) continue;
                    context.ReserveBoxFitWork(1);
                    if (anchor.RelativeHeight == 0 && previous.Anchor.RelativeHeight == 0) continue;
                    if (bounds.X < previous.X + previous.Width && previous.X < bounds.X + bounds.Width
                        && bounds.Y < previous.Y + previous.Height && previous.Y < bounds.Y + bounds.Height)
                        context.Loss("Shape.DrawingOrder", "Overlapping drawings with explicit layers are outside the managed drawing profile.");
                }
                destination.DrawingBounds.Add((anchor, bounds.X, bounds.Y, bounds.Width, bounds.Height, guarded));
            }
            context.PageNumberFormat = sourceSettings.PageNumberFormat;
            destination.Shapes.AddRange(line.Shapes.Select(s => s.At(s.X + settings.Left - line.PageLeft, s.Y + top)));
            foreach (PositionedText item in line.Items)
            {
                if (item.Field?.Bookmark is string bookmark)
                {
                    if ((bookmark.Length == 0 || bookmark[0] != '/')) model.BookmarkPages[bookmark] = (pages.IndexOf(destination) + 1, ListCounter.Number(destination.PageNumber, sourceSettings.PageNumberFormat), item.X + settings.Left - line.PageLeft, settings.Height - top);
                    continue;
                }
                if (item.Box != null)
                {
                    PositionedBox box = item.Box;
                    if (box.Geometry != null)
                    {
                        bool taggedVector = !context.RenderingPreview && !context.Typography.Enabled && context.Destination == DocumentFormat.Pdf
                            && (context.Options.Pdf.Conformance is PdfConformance.Pdf17 or PdfConformance.PdfA2a) && context.Options.Pdf.Accessibility != PdfAccessibility.None
                            && (context.Options.Pdf.VectorSemantics is PdfVectorSemantics.DescribedAtAnchor or PdfVectorSemantics.Decorative);
                        if (!ContentProfiles.ManagedFixedOutput(context) && !taggedVector)
                            throw new NotSupportedException("Shape.VectorProfile: Vector shapes require managed ordinary fixed output.");
                        var vectorBounds = DrawingBounds(box.Anchor, box.Width, box.Height, top + item.Y);
                        double halfStroke = (box.Border?.Width ?? 0) / 2;
                        CheckDrawingOrder(box.Anchor, (vectorBounds.X - halfStroke, vectorBounds.Y - halfStroke,
                            vectorBounds.Width + 2 * halfStroke, vectorBounds.Height + 2 * halfStroke, vectorBounds.Wrap), true);
                        if (vectorBounds.X - halfStroke < 0 || vectorBounds.Y - halfStroke < 0
                            || vectorBounds.X + vectorBounds.Width + halfStroke > settings.Width || vectorBounds.Y + vectorBounds.Height + halfStroke > settings.Height)
                            throw new NotSupportedException("Layout.AnchorBounds: Vector strokes must fit inside the physical page.");
                        if (box.Fill != null) destination.Shapes.Add(new FixedShape(vectorBounds.X, vectorBounds.Y, vectorBounds.Width, vectorBounds.Height, box.Fill, geometry: box.Geometry, foreground: !box.Anchor.Behind, sourceRun: item.SourceRun));
                        if (box.Border != null) destination.Shapes.Add(new FixedShape(vectorBounds.X, vectorBounds.Y, vectorBounds.Width, vectorBounds.Height, box.Border.Color, box.Border.Width, geometry: box.Geometry, foreground: !box.Anchor.Behind, sourceRun: item.SourceRun));
                        RememberExclusion(box.Anchor, vectorBounds); continue;
                    }
                    if ((box.Rotation != 0 || box.ShrinkToFit) && (!ContentProfiles.ManagedFixedOutput(context)))
                        throw new NotSupportedException("Shape.TextBoxProfile: Rotated or fitted boxes require managed ordinary fixed output.");
                    if ((box.Rotation != 0 || box.ShrinkToFit) && (box.Blocks.Any(b => b is not ParagraphBlock p || !ContentProfiles.OrdinaryParagraph(p, textHistory: true, formatHistory: true, comments: true, decorations: false, structuralHistory: true))))
                        throw new NotSupportedException("Shape.TextBoxContent: Rotated or fitted boxes support ordinary managed paragraphs only.");
                    var bounds = DrawingBounds(box.Anchor, box.Width, box.Height, top + item.Y, validate: box.Rotation == 0);
                    var visualBounds = RotatedBounds(bounds, box.Rotation);
                    CheckDrawingOrder(box.Anchor, visualBounds, box.Rotation != 0 || box.ShrinkToFit || model.HasLinkedTextBoxes && box.StoryId != null);
                    double boxStroke = (box.Border?.Width ?? 0) / 2;
                    if (box.Rotation != 0 && (visualBounds.X - boxStroke < 0 || visualBounds.Y - boxStroke < 0 || visualBounds.X + visualBounds.Width + boxStroke > settings.Width || visualBounds.Y + visualBounds.Height + boxStroke > settings.Height))
                        throw new NotSupportedException("Layout.AnchorBounds: A rotated text box extends outside the physical page.");
                    CellPadding boxPadding = box.Padding;
                    if (box.Width <= boxPadding.Left + boxPadding.Right || box.Height <= boxPadding.Top + boxPadding.Bottom)
                    {
                        if (box.ShrinkToFit) throw new NotSupportedException("Layout.TextBoxPadding: Shrink-to-fit requires a usable content area.");
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TextBoxPadding: Text-box padding leaves no usable content area.");
                        context.ReportStrictRejection("Layout.TextBoxPadding", "Text-box padding is omitted to retain usable content space.");
                        boxPadding = new CellPadding();
                    }
                    var cell = new CellLayout
                    {
                        Left = visualBounds.X,
                        Width = visualBounds.Width,
                        Extent = visualBounds.Height,
                        Padding = boxPadding,
                        Source = new TableCell { Background = box.Fill }
                    };
                    if (box.Border != null) foreach (string side in new[] { "left", "top", "right", "bottom" }) cell.Source.Borders.Add(side, box.Border);
                    destination.Shapes.AddRange(CellShapes(cell, visualBounds.Y, visualBounds.Height));
                    double boxTop = bounds.Y + boxPadding.Top;
                    var boxLines = new List<Line>();
                    double chainOffset = 0;
                    if (boxChains.TryGetValue(box, out List<PositionedBox>? chain))
                    {
                        if (!ContentProfiles.ManagedFixedOutput(context))
                            throw new NotSupportedException("Shape.TextBoxChain: Linked boxes require managed ordinary fixed output.");
                        if (!chainLines.TryGetValue(box, out var cached))
                        {
                            if (!ReferenceEquals(box, chain[0])) throw new InvalidDataException("Shape.TextBoxChain: Continuation precedes its head.");
                            double origin = bounds.X + boxPadding.Left;
                            double commonWidth = chain.Min(b => b.Width - b.Padding.Left - b.Padding.Right);
                            var all = new List<Line>();
                            foreach (ParagraphBlock paragraph in box.Blocks.Cast<ParagraphBlock>()) AddParagraph(paragraph, origin, commonWidth, all);
                            foreach (Line boxLine in all)
                                foreach (PositionedText text in boxLine.Items)
                                {
                                    context.ReserveBoxFitWork(checked(text.Text.Length + 1));
                                    if (text.Text.Length != 0 && (text.X < origin || text.X + Measure(text.Text, text.Font, text.Size) > origin + commonWidth + 0.001))
                                        throw new NotSupportedException("Layout.TextBoxChain: A line exceeds the shared chain width.");
                                }
                            int next = 0;
                            foreach (PositionedBox member in chain)
                            {
                                double remaining = member.Height - member.Padding.Top - member.Padding.Bottom;
                                var segment = new List<Line>();
                                while (next < all.Count && all[next].Height <= remaining + 0.0001)
                                { context.CheckCancellation(); segment.Add(all[next]); remaining -= all[next++].Height; }
                                chainLines.Add(member, (segment, origin));
                            }
                            if (next != all.Count) throw new NotSupportedException("Layout.TextBoxChainOverflow: Text exceeds the terminal linked box.");
                            cached = chainLines[box];
                        }
                        boxLines = cached.Lines; chainOffset = bounds.X + boxPadding.Left - cached.Origin;
                    }
                    else if (box.Blocks.Any(b => b is not ParagraphBlock) || DocumentModel.Expand(box.Blocks).OfType<ParagraphBlock>().Any(p => p.Runs.Any(r => r.Box != null)))
                    {
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TextBoxContent: Positioned text boxes support paragraphs only.");
                        context.ReportStrictRejection("Layout.TextBoxContent", "Tables and nested boxes in a positioned text box are flattened to formatted text in reading order.");
                        var flattened = new ParagraphBlock(); flattened.Runs.AddRange(FidelityChecks.FlattenBox(box));
                        AddParagraph(flattened, bounds.X + boxPadding.Left, box.Width - boxPadding.Left - boxPadding.Right, boxLines);
                    }
                    else foreach (ParagraphBlock boxParagraph in box.Blocks.Cast<ParagraphBlock>())
                        AddParagraph(boxParagraph, bounds.X + boxPadding.Left, box.Width - boxPadding.Left - boxPadding.Right, boxLines);
                    double available = box.Height - boxPadding.Top - boxPadding.Bottom;
                    double contentHeight = boxLines.Sum(l => l.Height);
                    if (box.Rotation != 0 && !box.ShrinkToFit)
                        foreach (Line boxLine in boxLines)
                            foreach (PositionedText text in boxLine.Items)
                            {
                                context.ReserveBoxFitWork(checked(text.Text.Length + 1));
                                if (text.Text.Length != 0 && (text.X < bounds.X + boxPadding.Left
                                    || text.X + Measure(text.Text, text.Font, text.Size) > bounds.X + box.Width - boxPadding.Right + 0.001))
                                    throw new NotSupportedException("Layout.TextBoxOverflow: Rotated text exceeds its frame width.");
                            }
                    if (box.ShrinkToFit)
                    {
                        double factor = contentHeight > 0 ? Math.Min(1, available / contentHeight) : 1;
                        double minimum = double.PositiveInfinity;
                        foreach (Line boxLine in boxLines)
                            foreach (PositionedText text in boxLine.Items)
                            {
                                context.ReserveBoxFitWork(checked(text.Text.Length + 1));
                                if (text.Text.Length == 0) continue;
                                double left = bounds.X + boxPadding.Left, right = bounds.X + box.Width - boxPadding.Right;
                                if (text.X < left || boxLine.Origin >= right) throw new NotSupportedException("Layout.TextBoxFit: Text indentation is outside the frame.");
                                double extent = text.X - boxLine.Origin + Measure(text.Text, text.Font, text.Size);
                                if (extent > 0) factor = Math.Min(factor, (right - boxLine.Origin) / extent);
                                minimum = Math.Min(minimum, text.Size);
                            }
                        if (factor <= 0 || minimum * factor < 6) throw new NotSupportedException("Layout.TextBoxMinimumSize: Text cannot fit at the six-point minimum.");
                        if (factor < 1) boxLines = boxLines.Select(l => ScaleLine(l, factor)).ToList();
                    }
                    else if (contentHeight > available)
                    {
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.TextBoxOverflow: Text does not fit inside the positioned box.");
                        context.ReportStrictRejection("Layout.TextBoxOverflow", "Text-box content is proportionally reduced to fit its frame.");
                        boxLines = boxLines.Select(l => ScaleLine(l, available / contentHeight)).ToList();
                    }
                    foreach (Line boxLine in boxLines)
                    {
                        foreach (PositionedText boxItem in boxLine.Items)
                        {
                            PositionedText boxPlaced = boxItem.At(boxItem.X + chainOffset, boxTop + boxLine.Ascent + boxItem.Y);
                            destination.Text.Add(box.Rotation == 0 ? boxPlaced : boxPlaced.Rotated(box.Rotation, bounds.X + box.Width / 2, bounds.Y + box.Height / 2));
                        }
                        boxTop += boxLine.Height;
                    }
                    RememberExclusion(box.Anchor, visualBounds); continue;
                }
                if (item.Visual is ObjectVisual visual)
                {
                    double origin = item.X + settings.Left - line.PageLeft, baseline = top + line.Ascent + item.Y;
                    destination.Text.AddRange(visual.Text.Select(t => t.At(origin + t.X, baseline + t.Y)));
                    destination.Shapes.AddRange(visual.Shapes.Select(s => s.At(origin + s.X, baseline + s.Y)));
                    if (context.Destination == DocumentFormat.Pdf && !context.PreviewIntermediate && context.Options.Pdf.Accessibility != PdfAccessibility.None)
                    {
                        string alternative = ObjectAlternatives.Describe(item.SourceRun!.Object!, context);
                        context.Objects.Render(alternative.Length, 1);
                        var carrier = new ShapedWord(new[] { new ShapedGlyph(-1, 0, Math.Max(1, visual.Width) / 12 * 1000, 0, 0) }, 1000);
                        destination.Text.Add(new PositionedText(alternative, origin, baseline, 12, item.Font,
                            sourceParagraph: item.SourceParagraph, sourceRun: item.SourceRun, nativeShape: carrier));
                    }
                    continue;
                }
                PositionedText placed = item.At(item.X + settings.Left - line.PageLeft, top + line.Ascent + item.Y);
                if (item.Image?.Anchor is DrawingAnchor anchor)
                {
                    var bounds = DrawingBounds(anchor, item.Image.Width, item.Image.Height, top + item.Y);
                    CheckDrawingOrder(anchor, bounds, false);
                    placed = item.At(bounds.X, bounds.Y + item.Image.Height); RememberExclusion(anchor, bounds);
                }
                if (item.Field != null)
                    placed = placed.WithText(DocumentFields.Render(item.Field, model, destination.PageNumber, totalPages,
                        sectionTotals != null && sectionTotals.TryGetValue(sourceSettings, out int count) ? count : totalPages, context));
                destination.Text.Add(placed);
                if (placed.SourceRun is TextRun displayRun && displayRun.ControlDisplay && displayRun.Appearance.Underline && placed.Text.Length != 0 && !context.Typography.Enabled)
                {
                    double advance = Measure(placed.Text, placed.Font, placed.Size);
                    if (advance > 0) destination.Shapes.Add(new FixedShape(placed.X, placed.Y + placed.Size * 0.1, advance, 0, placed.Color ?? "000000", Math.Max(0.5, placed.Size / 16)));
                }
            }
        }

        Line FitLine(Line line, double maximum)
        {
            if (line.Height <= maximum || context.EffectiveMode != FidelityMode.Lenient || balancedBottoms != null) return line;
            if (line.Items.Any(t => t.Visual != null)) throw new NotSupportedException("Object.Frame: Object lines are never silently scaled.");
            context.ReportStrictRejection("Layout.TextScaledToFit", "An oversized line is reduced to fit printable space.");
            return ScaleLine(line, maximum / line.Height);
        }

        Line ScaleLine(Line line, double factor)
        {
            if (line.Items.Any(t => t.Visual != null)) throw new NotSupportedException("Object.Frame: Object geometry is never silently scaled.");
            var scaled = new Line { PageLeft = line.PageLeft, FlowWidth = line.FlowWidth, BreakAfter = line.BreakAfter, Origin = line.Origin, Ascent = line.Ascent * factor, Descent = line.Descent * factor };
            scaled.Shapes.AddRange(line.Shapes.Select(s => new FixedShape(line.Origin + (s.X - line.Origin) * factor, s.Y * factor, s.Width * factor, s.Height * factor, s.Color, s.Stroke * factor, s.Style)));
            foreach (PositionedText item in line.Items)
                scaled.Items.Add(new PositionedText(item.Text, line.Origin + (item.X - line.Origin) * factor, item.Y * factor,
                    item.Size * factor, item.Font, item.Color, item.Image?.Scaled(factor), item.Link, item.Field, item.Box, item.SourceParagraph, item.SourceRun, item.NativeShape, item.IsLabel,
                    logicalCarrier: item.LogicalCarrier, carrierWidth: item.CarrierWidth * factor));
            return scaled;
        }

        void NewPageIfNeeded(double lineHeight)
        {
            if (lineHeight > settings.Height - settings.Top - settings.Bottom) throw new NotSupportedException("Layout.ItemHeight: An image, line or table row exceeds one page.");
            if (y + lineHeight <= settings.Height - settings.Bottom) return;
            AdvanceColumn();
        }

        void ApplyColumn()
        {
            double width = sourceSettings.ColumnWidth(columnIndex);
            settings = settings.Copy(); settings.Left = sourceSettings.ColumnLeft(columnIndex); settings.Right = settings.Width - settings.Left - width;
            if (balancedBottoms != null && balancedBottoms.TryGetValue(sourceSettings, out var frame)
                && pages.Count(p => p.Sections.Contains(sourceSettings)) == frame.Page) settings.Bottom = settings.Height - frame.Bottom;
        }

        void AdvanceColumn()
        {
            if (columnIndex + 1 >= sourceSettings.Columns) { AddPage(); return; }
            columnIndex++; ApplyColumn(); y = columnTop;
        }

        void AddPage()
        {
            if (pages.Count >= context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: Output exceeds the page limit.");
            bool first = !pages.Any(p => p.Sections.Contains(sourceSettings));
            int number = first && sourceSettings.PageNumberStart.HasValue ? sourceSettings.PageNumberStart.Value : checked(pages[pages.Count - 1].PageNumber + 1);
            settings = ReserveStories(sourceSettings);
            var page = new FixedPage { Settings = settings, SourceSettings = sourceSettings, PageNumber = number }; page.Sections.Add(sourceSettings);
            pages.Add(page); columnIndex = 0; columnTop = settings.Top; ApplyColumn(); y = settings.Top;
            if (pendingNotes.Count > 0) DrainNotes();
        }

        List<Line> Lines(ParagraphBlock paragraph, double left, double width, double baseSize, bool header, Action<Line>? ready = null)
        {
            baseSize = paragraph.FallbackFontSize ?? baseSize;
            context.PageNumberFormat = sourceSettings.PageNumberFormat;
            var result = new List<Line>();
            double tabOrigin = left;
            left += paragraph.Layout.Left;
            width -= paragraph.Layout.Left + paragraph.Layout.Right;
            List<TextRun> runs = context.Typography.Enabled || context.Options.Pdf.Accessibility != PdfAccessibility.None ? new List<TextRun>(paragraph.Runs) : RunCoalescer.Combine(paragraph.Runs, forRendering: true);
            TextRun? listLabel = null;
            if (paragraph.List != null)
            {
                ListLevel? level = paragraph.List.Definition?[paragraph.List.Level];
                double indent = level?.LayoutProperties.Contains("ind") == true ? 0 : 18 * paragraph.List.Level; left += indent; width -= indent;
                List<TextRun> content = runs;
                string suffix = level == null ? " " : level.Suffix == "tab" ? " \t" : level.Suffix == "space" ? " " : "";
                listLabel = (level?.Appearance ?? new TextRun("")).WithText(counter.Label(paragraph.List) + suffix);
                runs = new List<TextRun> { listLabel }; runs.AddRange(content);
            }
            double continuationLeft = left, continuationWidth = width;
            double continuationInset = continuationLeft - settings.Left;
            double continuationRightInset = settings.Width - settings.Right - continuationLeft - continuationWidth;
            left += paragraph.Layout.FirstLine;
            width -= paragraph.Layout.FirstLine;
            if (left < 0 || continuationLeft < 0 || width <= 0 || continuationWidth <= 0 || left + width > settings.Width || continuationLeft + continuationWidth > settings.Width)
            {
                if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.Indent: Paragraph indentation leaves no usable content space or extends beyond the page.");
                context.ReportStrictRejection("Layout.IndentApproximated", "Unusable indentation is constrained to printable space.");
                left = continuationLeft = Math.Max(settings.Left, Math.Min(left, settings.Width - settings.Right - 12));
                width = continuationWidth = Math.Max(1, settings.Width - settings.Right - left);
            }
            if (width <= 0) throw new NotSupportedException("Layout.ColumnWidth: No space remains for paragraph content.");
            var tabText = new Queue<string>(string.Concat(runs.Select(r => DocumentFields.Render(r, model,
                renderPage == 0 ? pages[pages.Count - 1].PageNumber : renderPage, totalPages, renderSectionPages, context))).Split('\t').Skip(1).Select(t => t.Split('\n')[0]));
            double consumedHeight = 0;
            var exclusions = renderPage == 0 ? new List<(double X, double Y, double Width, double Height, string Wrap)>(pages[pages.Count - 1].Exclusions) : new List<(double X, double Y, double Width, double Height, string Wrap)>();
            foreach (TextRun drawing in runs.Where(r => r.Image?.Anchor != null || r.Box != null))
            {
                DrawingAnchor anchor = drawing.Image?.Anchor ?? drawing.Box!.Anchor;
                var bounds = DrawingBounds(anchor, drawing.Image?.Width ?? drawing.Box!.Width, drawing.Image?.Height ?? drawing.Box!.Height, renderPage == 0 ? y : 0, validate: false);
                if (drawing.Box != null) bounds = RotatedBounds(bounds, drawing.Box.Rotation);
                if (anchor.Wrap != "none") exclusions.Add((bounds.X - anchor.Distance.Left, bounds.Y - anchor.Distance.Top, bounds.Width + anchor.Distance.Left + anchor.Distance.Right, bounds.Height + anchor.Distance.Top + anchor.Distance.Bottom, anchor.Wrap));
            }
            int consecutiveHyphens = 0;
            double x = left;
            Line line = NewLine(); x = left;
            foreach (TextRun run in runs)
            {
                context.CheckCancellation(); shapingLevel = run.BidiLevel;
                if (run.Object is DocumentObjectData retained)
                {
                    if (paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed || sourceSettings.ExplicitColumns.Count != 0)
                        throw new NotSupportedException("Object.Flow: Justification/distribution and explicit multi-column object flow are outside this profile.");
                    ObjectVisual visual = retained.Chart != null ? ChartLayout.Create(retained, resolver.Resolve(new TextRun(""), false), Measure, context)
                        : new MathLayout(resolver.Math!, resolver.MathIndex, data[resolver.MathIndex], context).Create(retained);
                    if (visual.Width > width || visual.Ascent + visual.Descent > settings.Height - settings.Top - settings.Bottom)
                        throw new NotSupportedException("Object.Frame: Object geometry exceeds printable space.");
                    if (retained.Display && line.Items.Count != 0 || x > left && x + visual.Width > left + width) Finish(false);
                    line.Items.Add(new PositionedText("", x, 0, 0, 0, sourceParagraph: paragraph, sourceRun: run, visual: visual));
                    x += visual.Width; line.Ascent = Math.Max(line.Ascent, visual.Ascent); line.Descent = Math.Max(line.Descent, visual.Descent);
                    if (retained.Display) Finish();
                    continue;
                }
                if (run.Bookmark != null) { line.Items.Add(new PositionedText("", x, 0, 0, 0, field: run)); continue; }
                if (run.Box != null || run.Image?.Anchor != null)
                {
                    line.Items.Add(new PositionedText("", left, -consumedHeight, 0, 0, image: run.Image, box: run.Box, sourceParagraph: paragraph, sourceRun: run)); continue;
                }
                if (run.Image is DocumentImage image)
                {
                    if (context.EffectiveMode == FidelityMode.Lenient)
                    {
                        double scale = Math.Min(1, Math.Min(width / image.Width, (settings.Height - settings.Top - settings.Bottom) / image.Height));
                        if (scale < 1) { image = image.Scaled(scale); context.ReportStrictRejection("Image.ScaledToFit", "An oversized image is proportionally scaled to fit printable space."); }
                    }
                    if (image.Width > width) throw new NotSupportedException("Layout.ImageWidth: Image exceeds the content width; dimensions are never silently reduced.");
                    if (x > left && x + image.Width > left + width) Finish(false);
                    line.Items.Add(new PositionedText("", x, 0, 0, 0, image: image, link: run.Link, sourceParagraph: paragraph, sourceRun: run)); x += image.Width;
                    line.Ascent = Math.Max(line.Ascent, image.Height); continue;
                }
                int font = resolver.Resolve(run, paragraph.HeadingLevel > 0);
                double size = run.FontSize ?? baseSize;
                if (balancedBottoms == null && context.EffectiveMode == FidelityMode.Lenient && size * 1.5 > settings.Height - settings.Top - settings.Bottom)
                {
                    size = (settings.Height - settings.Top - settings.Bottom) / 1.5;
                    context.ReportStrictRejection("Layout.TextScaledToFit", "Oversized text is reduced to fit printable space.");
                }
                string runText = DocumentFields.Render(run, model, renderPage == 0 ? pages[pages.Count - 1].PageNumber : renderPage, totalPages, renderSectionPages, context);
                if (context.Hyphenation.Dictionary != null && runText.Any(c => c == '\u00ad'))
                    throw new NotSupportedException("Hyphenation.SoftHyphen: Authored soft hyphens require separate qualification.");
                runText = ManagedText(runText, font);
                foreach (char character in runText)
                    if (!(context.Typography.ExtendedBidi && (UnicodeBidi.Format(character) || character is '\u200c' or '\u200d')) && character is not '\n' and not '\t' and not '\f' and not '\v' && (maps[font] == null || !maps[font]!.TryGetGlyphIndex(character, out int glyph) || glyph == 0))
                        throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.");
                int tocLine = 0;
                string? activeLink = run.Link;
                if (run.FieldInstruction != null && DocumentFields.Kind(run.FieldInstruction) is "REF" or "PAGEREF")
                {
                    var parts = run.FieldInstruction.Trim().Split(' ');
                    if (parts.Length > 1 && parts.Contains("\\h")) activeLink = "#" + parts[1].Trim('"');
                }
                if (model.TocTargets.TryGetValue(run, out List<string>? targets) && targets.Count > 0) activeLink = "#" + targets[0];
                var token = new StringBuilder();
                foreach (char c in runText)
                {
                    if (c == '\t')
                    {
                        Flush();
                        string following = tabText.Count > 0 ? tabText.Dequeue() : "";
                        TabStop? stop = paragraph.Layout.Tabs.FirstOrDefault(t => tabOrigin + t.Position > x + 0.01);
                        double position = stop == null ? tabOrigin + (Math.Floor((x - tabOrigin) / 36) + 1) * 36 : tabOrigin + stop.Position;
                        double advance = Measure(following, font, size);
                        if (stop?.Alignment == "center") position -= advance / 2;
                        else if (stop?.Alignment == "right") position -= advance;
                        else if (stop?.Alignment == "decimal") position -= Measure(following.Split('.')[0], font, size);
                        position = Math.Min(left + width, Math.Max(x, position));
                        if (stop != null && stop.Leader != "none")
                        {
                            string leader = stop.Leader == "dot" ? "." : stop.Leader == "hyphen" ? "-" : "_";
                            double leaderWidth = Measure(leader, font, size);
                            int count = leaderWidth > 0 ? (int)Math.Min(10000, (position - x) / leaderWidth) : 0;
                            if (count > 0) line.Items.Add(new PositionedText(new string(leader[0], count), x, 0, size, font, run.Color));
                        }
                        x = position;
                    }
                    else if (c is '\n' or '\f' or '\v') { Flush(); line.BreakAfter = c == '\n' ? '\0' : c; Finish(); tocLine++; if (targets != null && tocLine < targets.Count) activeLink = "#" + targets[tocLine]; }
                    else { token.Append(c); if (c == ' ') Flush(); }
                }
                Flush();

                void Flush()
                {
                    if (token.Length == 0) return;
                    string value = token.ToString(); token.Clear(); double advance = Measure(value, font, size);
                    if (TryHyphenate(value, advance)) return;
                    if (context.Hyphenation.Dictionary != null && advance > width)
                    {
                        string word = value.TrimEnd(' '); double wordWidth = Measure(word, font, size);
                        if (wordWidth > width) throw new NotSupportedException("Hyphenation.WordWidth: The token has no qualified break that fits the line.");
                        if (word.Length != 0) Add(word, wordWidth);
                        string spaces = value.Substring(word.Length);
                        if (spaces.Length != 0) Add(spaces, Measure(spaces, font, size));
                        return;
                    }
                    if (native != null && value.Length > 1 && value[value.Length - 1] == ' ')
                    {
                        string word = value.TrimEnd(' '); if (word.Length > 0) Add(word, Measure(word, font, size));
                        string spaces = value.Substring(word.Length); Add(spaces, Measure(spaces, font, size));
                    }
                    else if (advance <= width) Add(value, advance);
                    else if (native != null) throw new NotSupportedException("Typography.WordWidth: A shaped word cannot fit the column; hyphenation is outside this profile.");
                    else foreach (char c in value) Add(c.ToString(), Measure(c.ToString(), font, size));
                }
                bool TryHyphenate(string value, double advance)
                {
                    HyphenationLexicon? dictionary = context.Hyphenation.Dictionary;
                    if (dictionary == null || x + advance <= left + width || consecutiveHyphens >= context.Hyphenation.MaximumConsecutiveLines) return false;
                    string word = value.TrimEnd(' ');
                    if (word.Length < context.Hyphenation.MinimumPrefix + context.Hyphenation.MinimumSuffix || word.Length > 128
                        || word.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))) return false;
                    context.ReserveHyphenationWork();
                    if (x + Measure(word, font, size) <= left + width) return false;
                    if (!dictionary.TryGetBreaks(word, out int[] points)) return false;
                    int selected = 0; double firstWidth = 0; string suffix = "", prefix = "", spaces = value.Substring(word.Length);
                    for (int i = points.Length - 1; i >= 0; i--)
                    {
                        context.ReserveHyphenationWork();
                        int point = points[i];
                        if (point < context.Hyphenation.MinimumPrefix || word.Length - point < context.Hyphenation.MinimumSuffix) continue;
                        string first = word.Substring(0, point); first = string.Concat(first, "-"); string rest = word.Substring(point);
                        double firstAdvance = Measure(first, font, size);
                        if (x + firstAdvance <= left + width && Measure(rest + spaces, font, size) <= continuationWidth)
                        { selected = point; firstWidth = firstAdvance; suffix = rest; prefix = first; break; }
                    }
                    if (selected == 0) return false;
                    context.Loss("Hyphenation.SearchGeometry", "The logical word remains searchable, but its search highlight is confined to the first line of a broken word.");
                    if (paragraph.Alignment == ParagraphAlignment.Justified)
                        context.Loss("Hyphenation.JustificationApproximation", "Justified word positions and following-line spacing differ from an independent reference.");
                    context.HyphenationUsed = true;
                    line.Items.Add(new PositionedText(word, x, 0, size, font, run.Color, sourceParagraph: paragraph, sourceRun: run,
                        logicalCarrier: true, carrierWidth: firstWidth));
                    AddVisual(prefix); Finish(false, hyphenated: true);
                    AddVisual(suffix);
                    if (spaces.Length != 0) Add(spaces, Measure(spaces, font, size));
                    return true;
                }
                void AddVisual(string value)
                {
                    MeasuredText shaped = Shape(value, font, size); int units = checked((int)kerning[font].UnitsPerEm);
                    var glyphs = new ShapedGlyph[value.Length];
                    for (int i = 0; i < value.Length; i++)
                    {
                        if (maps[font] == null || !maps[font]!.TryGetGlyphIndex(value[i], out int glyph) || glyph == 0)
                            throw new NotSupportedException("Hyphenation.Glyph: A required word or hyphen glyph is unavailable.");
                        glyphs[i] = new ShapedGlyph(glyph, i, 0, shaped.X[i] * units / size, -shaped.Y[i] * units / size);
                    }
                    line.Items.Add(new PositionedText("", x, 0, size, font, run.Color, nativeShape: new ShapedWord(glyphs, units), carrierWidth: shaped.Width));
                    x += shaped.Width;
                    line.Ascent = Math.Max(line.Ascent, size * kerning[font].Ascent / units);
                    line.Descent = Math.Max(line.Descent, size * kerning[font].Descent / units);
                }
                void Add(string value, double advance)
                {
                    if (advance > width)
                    {
                        if (context.EffectiveMode != FidelityMode.Lenient) throw new NotSupportedException("Layout.ColumnWidth: A glyph cannot fit within the column.");
                        size *= width / advance; advance = width;
                        context.ReportStrictRejection("Layout.TextScaledToFit", "Oversized text is reduced to fit printable space.");
                    }
                    if (x > left && x + advance > left + width && (native == null || !string.IsNullOrWhiteSpace(value))) Finish(false);
                    MeasuredText shaped = Shape(value, font, size);
                    if (shaped.Native != null)
                    {
                        line.Items.Add(new PositionedText(value, x, 0, size, font, run.Color, link: activeLink, sourceParagraph: paragraph, sourceRun: run, nativeShape: shaped.Native, label: ReferenceEquals(run, listLabel)));
                        x += advance; line.Ascent = Math.Max(line.Ascent, size * 1.5); line.Descent = Math.Max(line.Descent, size * 0.65); return;
                    }
                    int begin = 0;
                    for (int i = 1; i < value.Length; i++)
                    {
                        if (Math.Abs((shaped.X[i] - shaped.OriginalX[i]) - (shaped.X[begin] - shaped.OriginalX[begin])) < 0.000001
                            && Math.Abs(shaped.Y[i] - shaped.Y[begin]) < 0.000001) continue;
                        line.Items.Add(new PositionedText(value.Substring(begin, i - begin), x + shaped.X[begin], shaped.Y[begin], size, font, run.Color, link: activeLink, sourceParagraph: paragraph, sourceRun: run, label: ReferenceEquals(run, listLabel)));
                        begin = i;
                    }
                    string kind = run.FieldInstruction == null ? "" : DocumentFields.Kind(run.FieldInstruction);
                    TextRun? pageField = run.Note != null || begin == 0 && value == runText && kind is "PAGE" or "NUMPAGES" or "SECTIONPAGES" ? run : null;
                    line.Items.Add(new PositionedText(value.Substring(begin), x + shaped.X[begin], shaped.Y[begin], size, font, run.Color, link: activeLink, field: pageField, sourceParagraph: paragraph, sourceRun: run, label: ReferenceEquals(run, listLabel))); x += advance;
                    line.Ascent = Math.Max(line.Ascent, size * kerning[font].Ascent / kerning[font].UnitsPerEm);
                    line.Descent = Math.Max(line.Descent, size * kerning[font].Descent / kerning[font].UnitsPerEm);
                }
            }
            Finish();
            return result;

            Line NewLine()
            {
                double top = ready != null ? y : (renderPage == 0 ? y : 0) + consumedHeight;
                foreach (var area in exclusions.Where(e => e.Y < top + baseSize * 1.5 && e.Y + e.Height > top && (e.Wrap == "topAndBottom" || e.X < left + width && e.X + e.Width > left)))
                {
                    if (area.Wrap == "topAndBottom" || area.X <= left && area.X + area.Width >= left + width)
                    {
                        double skip = area.Y + area.Height - top;
                        if (skip > 0)
                        {
                            var spacer = new Line { PageLeft = settings.Left, Descent = skip };
                            if (ready == null) { result.Add(spacer); consumedHeight += skip; } else ready(spacer);
                            top += skip;
                        }
                    }
                    else
                    {
                        double end = left + width;
                        if (area.X - left >= end - area.X - area.Width) width = Math.Max(1, area.X - left);
                        else { left = Math.Max(left, area.X + area.Width); width = Math.Max(1, end - left); }
                    }
                }
                return new Line
                {
                    FlowWidth = ready == null ? null : settings.Width - settings.Left - settings.Right,
                    PageLeft = settings.Left,
                    Origin = tabOrigin,
                    Ascent = native == null ? 0 : baseSize,
                    Descent = native == null ? 0 : baseSize * 0.5
                };
            }
            void Finish(bool lastLine = true, bool hyphenated = false)
            {
                consecutiveHyphens = hyphenated ? consecutiveHyphens + 1 : 0;
                if (line.Height == 0)
                {
                    TextRun prototype = runs.FirstOrDefault(r => r.Image == null && r.Box == null) ?? paragraph.Mark ?? new TextRun("");
                    int face = resolver.Resolve(prototype, paragraph.HeadingLevel > 0); double pointSize = prototype.FontSize ?? baseSize;
                    line.Ascent = pointSize * kerning[face].Ascent / kerning[face].UnitsPerEm;
                    line.Descent = pointSize * kerning[face].Descent / kerning[face].UnitsPerEm;
                }
                if (native != null && line.Items.Count > 0 && !(line.Items.Count == 1 && line.Items[0].Visual != null))
                    NativeTypography.Reorder(line.Items, left, NativeTypography.BaseRtl(paragraph, context.Typography), native, context.Typography.ExtendedBidi);
                if (paragraph.Alignment is ParagraphAlignment.Center or ParagraphAlignment.Right)
                {
                    PositionedText? last = line.Items.LastOrDefault(VisibleEnd);
                    double end = last == null ? left : last.X + EndWidth(last);
                    double shift = Math.Max(0, left + width - end) / (paragraph.Alignment == ParagraphAlignment.Center ? 2 : 1);
                    for (int i = 0; i < line.Items.Count; i++) line.Items[i] = line.Items[i].At(line.Items[i].X + shift, line.Items[i].Y);
                }
                else if (!lastLine && paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed)
                {
                    if (paragraph.Alignment == ParagraphAlignment.Distributed)
                    {
                        var characters = new List<PositionedText>();
                        foreach (PositionedText item in line.Items)
                        {
                            if (item.Image != null) { characters.Add(item); continue; }
                            double position = item.X;
                            foreach (char c in item.Text)
                            { characters.Add(new PositionedText(c.ToString(), position, item.Y, item.Size, item.Font, item.Color, link: item.Link)); position += Measure(c.ToString(), item.Font, item.Size); }
                        }
                        line.Items.Clear(); line.Items.AddRange(characters);
                    }
                    PositionedText? last = line.Items.LastOrDefault(VisibleEnd);
                    if (last != null)
                    {
                        double end = last.X + EndWidth(last);
                        int lastIndex = line.Items.IndexOf(last);
                        bool distributed = paragraph.Alignment == ParagraphAlignment.Distributed;
                        int gaps = distributed ? lastIndex : line.Items.Take(lastIndex).Sum(item => item.Text.Count(c => c == ' '));
                        if (gaps > 0)
                        {
                            double extra = Math.Max(0, left + width - end) / gaps, offset = 0;
                            for (int i = 0; i < line.Items.Count; i++)
                            {
                                PositionedText item = line.Items[i]; line.Items[i] = item.At(item.X + offset, item.Y);
                                if (i < lastIndex) offset += (distributed ? 1 : item.Text.Count(c => c == ' ')) * extra;
                            }
                        }
                    }
                }
                if (paragraph.Layout.LineSpacing is double spacing)
                {
                    double requested = paragraph.Layout.MultipleLineSpacing ? line.Height * spacing : spacing;
                    if (paragraph.Layout.ExactLineSpacing && paragraph.Runs.All(r => r.Text.Length == 0 && r.Image == null && r.Box == null && r.Note == null && r.Object == null))
                    { line.Ascent = 0; line.Descent = requested; }
                    if (requested < line.Height && (paragraph.Layout.ExactLineSpacing || paragraph.Layout.MultipleLineSpacing))
                        context.Loss("Layout.LineSpacing", "Line spacing smaller than content height is expanded to avoid clipping or overlap.");
                    line.Descent += Math.Max(0, requested - line.Height);
                }
                if (ready == null) { result.Add(line); consumedHeight += line.Height; }
                else
                {
                    ready(line); tabOrigin = settings.Left;
                    continuationLeft = settings.Left + continuationInset;
                    continuationWidth = settings.Width - settings.Left - settings.Right - continuationInset - continuationRightInset;
                }
                left = continuationLeft; width = continuationWidth; line = NewLine(); x = left;
            }
            bool VisibleEnd(PositionedText item) => !item.LogicalCarrier && (item.Visual != null || item.Image != null
                || item.NativeShape != null && item.CarrierWidth > 0 || item.Text.TrimEnd(' ').Length > 0);
            double EndWidth(PositionedText item) => item.Visual?.Width ?? item.Image?.Width
                ?? (item.NativeShape != null && item.CarrierWidth > 0 ? item.CarrierWidth : Measure(item.Text.TrimEnd(' '), item.Font, item.Size));
        }

        double Measure(string value, int font, double size) => Shape(value, font, size).Width;

        MeasuredText Shape(string value, int font, double size)
        {
            context.CheckCancellation();
            var key = (value, font, size);
            if (measurements.TryGetValue(key, out MeasuredText? cached)) return cached;
            try
            {
                if (native != null)
                {
                    ShapedWord word = native.Shape(value, font, shapingLevel.HasValue ? shapingLevel.Value >= 0 && (shapingLevel.Value & 1) != 0 : NativeTypography.Rtl(value));
                    // Charge every shaping call; the ordinary measurement cache remains managed-only.
                    return new MeasuredText { Width = word.Width * size, Native = word };
                }
                var letters = metrics.MeasureText(value, size, new PdfPoint(0, 0), fonts[font]);
                var glyphs = new int[value.Length];
                for (int i = 0; i < glyphs.Length; i++)
                    if (maps[font] != null) _ = maps[font]!.TryGetGlyphIndex(value[i], out glyphs[i]);
                GlyphAdjustment[] adjusted = kerning[font].Position(glyphs, size);
                var result = new MeasuredText { X = new double[value.Length], Y = new double[value.Length], OriginalX = new double[value.Length] };
                double advanceX = 0, advanceY = 0;
                for (int i = 0; i < letters.Count; i++)
                {
                    result.OriginalX[i] = letters[i].StartBaseLine.X;
                    result.X[i] = letters[i].StartBaseLine.X + advanceX + adjusted[i].OffsetX;
                    result.Y[i] = -advanceY - adjusted[i].OffsetY;
                    advanceX += adjusted[i].AdvanceX; advanceY += adjusted[i].AdvanceY;
                }
                result.Width = Math.Max(0, (letters.Count == 0 ? 0 : letters[letters.Count - 1].EndBaseLine.X) + advanceX);
                if (measurements.Count < 2048 && value.Length <= 131072 - cachedCharacters)
                { measurements.Add(key, result); cachedCharacters += value.Length; }
                return result;
            }
            catch (InvalidOperationException error) { throw new NotSupportedException("Layout.Glyph: The embedded font cannot represent a character.", error); }
        }
    }
}
