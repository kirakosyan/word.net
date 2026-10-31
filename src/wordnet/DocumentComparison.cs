using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

/// <summary>Unicode 17 segmentation for bounded matching-structure text comparison.</summary>
public enum ComparisonGranularity
{
    /// <summary>Default Unicode word boundaries retaining punctuation and whitespace.</summary>
    Word,
    /// <summary>Extended grapheme clusters.</summary>
    Grapheme,
}
/// <summary>Caller attribution and finite comparison budgets. Configure before use.</summary>
public sealed class ComparisonOptions
{
    /// <summary>Creates options with explicit attribution; no author or clock is inferred.</summary>
    public ComparisonOptions(string author, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(author) || author.Length > 1024) throw new ArgumentException("Comparison.Author: A bounded author is required.", nameof(author));
        Author = author; Timestamp = timestamp;
    }
    /// <summary>Gets the caller author.</summary>
    public string Author { get; }
    /// <summary>Gets the caller timestamp.</summary>
    public DateTimeOffset Timestamp { get; }
    /// <summary>Gets or sets word/grapheme granularity, default Word.</summary>
    public ComparisonGranularity Granularity { get; set; }
    /// <summary>Gets or sets the combined token cap, default 100,000.</summary>
    public int MaximumTokens { get; set; } = 100000;
    /// <summary>Gets or sets the total visited-cell cap, default 4,000,000.</summary>
    public int MaximumSteps { get; set; } = 4000000;
    /// <summary>Gets or sets whether admitted ordinary header/footer text is compared. When false the original story text remains.</summary>
    public bool CompareStoryText { get; set; } = true;
    /// <summary>Gets or sets whether supported bold, italic, size and color changes are tracked. When false matching text keeps the original formatting.</summary>
    public bool CompareRunFormatting { get; set; } = true;
    /// <summary>Gets or sets whether admitted paragraph alignment, spacing, indentation and pagination changes are tracked. When false the original values remain.</summary>
    public bool CompareParagraphFormatting { get; set; } = true;
}
/// <summary>Deterministic bounded text and plain paragraph/row comparison of history-free documents.</summary>
public static partial class DocumentComparison
{
    /// <summary>Creates a reviewed copy without changing either input. Plain paragraph/row changes and bounded paragraph-property history are supported; unsupported combinations and existing history fail.</summary>
    public static WordDocument Compare(WordDocument original, WordDocument revised, ComparisonOptions comparisonOptions,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(original, nameof(original)); Guard.NotNull(revised, nameof(revised)); Guard.NotNull(comparisonOptions, nameof(comparisonOptions));
        int tokens = comparisonOptions.MaximumTokens, steps = comparisonOptions.MaximumSteps;
        ComparisonGranularity granularity = comparisonOptions.Granularity;
        bool compareStoryText = comparisonOptions.CompareStoryText, compareRunFormatting = comparisonOptions.CompareRunFormatting;
        bool compareParagraphFormatting = comparisonOptions.CompareParagraphFormatting;
        if (tokens is < 1 or > 100000 || steps is < 1 or > 4000000 || granularity is not ComparisonGranularity.Word and not ComparisonGranularity.Grapheme) throw new ArgumentOutOfRangeException(nameof(comparisonOptions));
        options ??= new ConversionOptions(); var context = new ConversionContext(options, cancellationToken) { Editing = true };
        DocumentModel left = original.ComparisonSnapshot(context), right = revised.ComparisonSnapshot(context);
        if (!(left.Theme == null && right.Theme == null) && !(left.Theme?.Same(right.Theme) ?? false))
            throw new NotSupportedException("Comparison.ThemeConflict: Documents with different or missing themes cannot be compared without resolving their document-wide theme.");
        TableStyle?[] leftStyles = left.Tables.Select(table => table.Style).ToArray(), rightStyles = right.Tables.Select(table => table.Style).ToArray();
        if ((leftStyles.Any(style => style != null) || rightStyles.Any(style => style != null))
            && (leftStyles.Length != rightStyles.Length || leftStyles.Where((style, index) => !SameTableStyle(style, rightStyles[index], context)).Any()))
            throw new NotSupportedException("Comparison.TableStyle: Table-style changes are outside the comparison profile.");
        foreach (DocumentModel snapshot in new[] { left, right })
            foreach (ParagraphBlock paragraph in snapshot.BodyParagraphs)
            { List<TextRun> combined = RunCoalescer.Combine(paragraph.Runs); paragraph.Runs.Clear(); paragraph.Runs.AddRange(combined); }
        long characters = (long)left.CharacterCount + right.CharacterCount;
        if (characters > options.MaximumCharacters || characters * 4 > options.MaximumExpandedBytes) throw new InvalidDataException("Comparison.ResourceLimit: Combined inputs exceed the comparison allowance.");
        List<(ParagraphBlock Old, ParagraphBlock New)> stories = MatchingStories(left, right, context);
        if (!compareStoryText) stories.Clear();
        int tokenCount = 0, work = 0, bodyCount = 0;
        byte[] leftStructure = Structure(left), rightStructure = Structure(right);
        bool structural = !leftStructure.SequenceEqual(rightStructure) || ReorderedPlainParagraphs(left, right);
        if (structural) CompareStructure(left, right, comparisonOptions, options, context, !compareRunFormatting, compareParagraphFormatting, work, out work, out tokenCount);
        ParagraphBlock[] bodyA = structural ? Array.Empty<ParagraphBlock>() : left.BodyParagraphs.ToArray();
        ParagraphBlock[] bodyB = structural ? Array.Empty<ParagraphBlock>() : right.BodyParagraphs.ToArray();
        HashSet<ParagraphBlock> formatOwnersA = ReviewStore.ParagraphFormatOwners(left), formatOwnersB = ReviewStore.ParagraphFormatOwners(right);
        bodyCount = bodyA.Length;
        ParagraphBlock[] a = bodyA.Concat(stories.Select(p => p.Old)).ToArray(), b = bodyB.Concat(stories.Select(p => p.New)).ToArray();
        for (int paragraph = 0; paragraph < a.Length; paragraph++)
        {
            context.CheckCancellation(); bool storyMode = paragraph >= bodyCount; string oldText = a[paragraph].Text, newText = b[paragraph].Text;
            if (!storyMode && !ParagraphFormatSnapshot.Same(a[paragraph], b[paragraph]))
            {
                if (!formatOwnersA.Contains(a[paragraph]) || !formatOwnersB.Contains(b[paragraph])
                    || !ReviewStore.OrdinaryParagraphFormat(a[paragraph]) || !ReviewStore.OrdinaryParagraphFormat(b[paragraph]))
                    throw new NotSupportedException("Comparison.ParagraphFormat: Paragraph-property changes require ordinary body or simple-cell paragraphs.");
                if (compareParagraphFormatting)
                {
                    string id = Guid.NewGuid().ToString("N"); var previous = new ParagraphFormatSnapshot(a[paragraph]);
                    new ParagraphFormatSnapshot(b[paragraph]).Apply(a[paragraph]);
                    a[paragraph].ParagraphFormatRevision = id; a[paragraph].PreviousParagraphFormat = previous;
                    left.Review.Revisions.Add(id, new ReviewRecord { Id = id, Author = comparisonOptions.Author, Date = comparisonOptions.Timestamp, Structure = "paragraph-format" });
                }
                else a[paragraph].ExplicitParagraphFormat = true;
            }
            if (oldText == newText)
            {
                if (a[paragraph].Runs.Any(Structured) || b[paragraph].Runs.Any(Structured))
                {
                    if (!a[paragraph].Runs.Select(r => r.Text.Length).SequenceEqual(b[paragraph].Runs.Select(r => r.Text.Length)))
                        throw new NotSupportedException("Comparison.Formatting: Structured run boundaries must match.");
                    continue;
                }
                List<TextRun> formatted = CompareFormats(a[paragraph].Runs, b[paragraph].Runs, storyMode);
                a[paragraph].Runs.Clear(); a[paragraph].Runs.AddRange(formatted);
                continue;
            }
            List<int> x = UnicodeBoundaries.Find(oldText, granularity == ComparisonGranularity.Word, context, tokens);
            List<int> y = UnicodeBoundaries.Find(newText, granularity == ComparisonGranularity.Word, context, tokens);
            tokenCount = checked(tokenCount + Math.Max(0, x.Count - 1) + Math.Max(0, y.Count - 1));
            if (tokenCount > tokens) throw new InvalidDataException("Comparison.TokenLimit: Combined token allowance exceeded.");
            if (a[paragraph].Runs.Any(r => r.Image != null)) throw new NotSupportedException("Comparison.ObjectBoundary: Text changes in object paragraphs are unsupported.");
            int m = x.Count - 1, n = y.Count - 1;
            long cells = (m + 1L) * (n + 1L);
            if (cells > steps - work || cells * 4 + characters * 4 > options.MaximumExpandedBytes) throw new InvalidDataException("Comparison.WorkLimit: Comparison work or storage allowance exceeded.");
            var lengths = new int[(int)cells];
            for (int i = m - 1; i >= 0; i--) for (int j = n - 1; j >= 0; j--)
            {
                context.CheckCancellation(); if (++work > steps) throw new InvalidDataException("Comparison.WorkLimit: Comparison work allowance exceeded.");
                lengths[i * (n + 1) + j] = Equal(i, j) ? 1 + lengths[(i + 1) * (n + 1) + j + 1]
                    : Math.Max(lengths[(i + 1) * (n + 1) + j], lengths[i * (n + 1) + j + 1]);
            }
            var output = new List<TextRun>(); int u = 0, v = 0; string? revision = null; bool? deleting = null;
            while (u < m || v < n)
            {
                context.CheckCancellation();
                if (u < m && v < n && Equal(u, v))
                {
                    List<TextRun> oldRuns = RunCoalescer.Combine(Slice(a[paragraph], x[u], x[u + 1] - x[u], context, ref work, steps));
                    List<TextRun> newRuns = RunCoalescer.Combine(Slice(b[paragraph], y[v], y[v + 1] - y[v], context, ref work, steps));
                    output.AddRange(CompareFormats(oldRuns, newRuns, storyMode)); revision = null; deleting = null; u++; v++; continue;
                }
                bool deletion = u < m && (v == n || lengths[(u + 1) * (n + 1) + v] >= lengths[u * (n + 1) + v + 1]);
                if (deleting != deletion)
                {
                    deleting = deletion; revision = Guid.NewGuid().ToString("N");
                    left.Review.Revisions.Add(revision, new ReviewRecord { Id = revision, Author = comparisonOptions.Author, Date = comparisonOptions.Timestamp, Deletion = deletion });
                }
                List<TextRun> changed = deletion ? Slice(a[paragraph], x[u], x[u + 1] - x[u], context, ref work, steps)
                    : Slice(b[paragraph], y[v], y[v + 1] - y[v], context, ref work, steps);
                foreach (TextRun run in changed) { run.Revision = revision; output.Add(run); }
                if (deletion) u++; else v++;
            }
            a[paragraph].Runs.Clear(); a[paragraph].Runs.AddRange(output);
            bool Equal(int i, int j) => x[i + 1] - x[i] == y[j + 1] - y[j]
                && string.CompareOrdinal(oldText, x[i], newText, y[j], x[i + 1] - x[i]) == 0;
        }
        return WordDocument.ComparisonResult(left, options, context);

        byte[] Structure(DocumentModel model)
        {
            DocumentModel copy = new DocumentCloner(context).Copy(model);
            NormalizePlainStories(copy, context, !compareRunFormatting, ReserveAdditionalWork);
            var body = new HashSet<ParagraphBlock>(ReviewStore.SupportedParagraphs(copy.Blocks));
            HashSet<ParagraphBlock> formatOwners = ReviewStore.ParagraphFormatOwners(copy);
            foreach (ParagraphBlock paragraph in copy.Paragraphs)
            {
                if (formatOwners.Contains(paragraph) && ReviewStore.OrdinaryParagraphFormat(paragraph))
                { ReserveAdditionalWork(1); NeutralParagraphFormat(paragraph); }
                if (!body.Contains(paragraph) || paragraph.Control != null || paragraph.Runs.Any(r => r.FieldInstruction != null || r.Control != null
                    || r.Bookmark != null || r.Box != null || r.Note != null || r.Image != null || r.Link != null || r.Object != null)) continue;
                var normalized = RunCoalescer.Combine(paragraph.Runs.Select(Neutral).ToList());
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(normalized.Select(r => r.WithText("")));
            }
            using var stream = new BoundedMemoryStream(options.MaximumOutputBytes); DocxWriter.Write(copy, stream, context); return stream.ToArray();
        }
        void ReserveAdditionalWork(int count)
        {
            context.CheckCancellation();
            if (count > steps - work) throw new InvalidDataException("Comparison.WorkLimit: Formatting normalization exceeds its work allowance.");
            work += count;
        }
        List<TextRun> CompareFormats(List<TextRun> oldRuns, List<TextRun> newRuns, bool storyMode)
        {
            var result = new List<TextRun>(); int i = 0, j = 0, aOffset = 0, bOffset = 0;
            while (i < oldRuns.Count && j < newRuns.Count)
            {
                context.CheckCancellation(); if (++work > steps) throw new InvalidDataException("Comparison.WorkLimit: Formatting alignment exceeds its work allowance.");
                TextRun oldRun = oldRuns[i], newRun = newRuns[j]; int count = Math.Min(oldRun.Text.Length - aOffset, newRun.Text.Length - bOffset);
                if (count <= 0 || !RunCoalescer.Same(Neutral(oldRun), Neutral(newRun))) throw new NotSupportedException("Comparison.Formatting: Unsupported formatting differences or structured runs.");
                TextRun current = compareRunFormatting ? newRun.WithText(newRun.Text.Substring(bOffset, count)) : oldRun.WithText(oldRun.Text.Substring(aOffset, count));
                if (compareRunFormatting && (oldRun.Bold != newRun.Bold || oldRun.Italic != newRun.Italic || oldRun.FontSize != newRun.FontSize || oldRun.Color != newRun.Color))
                {
                    if (storyMode) throw new NotSupportedException("Comparison.StoryFormatting: Story formatting changes are unsupported.");
                    string id = Guid.NewGuid().ToString("N");
                    left.Review.Revisions.Add(id, new ReviewRecord { Id = id, Author = comparisonOptions.Author, Date = comparisonOptions.Timestamp, Structure = "format" });
                    current.FormatRevision = id; current.PreviousFormat = oldRun.WithText(current.Text);
                }
                result.Add(current); aOffset += count; bOffset += count;
                if (aOffset == oldRun.Text.Length) { i++; aOffset = 0; }
                if (bOffset == newRun.Text.Length) { j++; bOffset = 0; }
            }
            if (i != oldRuns.Count || j != newRuns.Count) throw new NotSupportedException("Comparison.Formatting: Formatting alignment failed.");
            return result;
        }
        static bool Structured(TextRun r) => r.Text.Length == 0 || r.Object != null || r.Image != null || r.Box != null || r.Note != null || r.Bookmark != null
            || r.FieldInstruction != null || r.Control != null || r.Link != null || r.TypedControlId != null;
        bool ReorderedPlainParagraphs(DocumentModel originalModel, DocumentModel revisedModel)
        {
            string[] BlockKeys(DocumentModel model)
            {
                int table = 0;
                return model.Blocks.Select(block => block is ParagraphBlock paragraph ? "p:" + paragraph.Text
                    : "t:" + (table++).ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            string[] oldBlocks = BlockKeys(originalModel), newBlocks = BlockKeys(revisedModel);
            if (oldBlocks.SequenceEqual(newBlocks)) return false;
            long scan = (long)oldBlocks.Length + newBlocks.Length;
            if (scan > steps - work)
                throw new InvalidDataException("Comparison.WorkLimit: Paragraph move candidate search exceeds its allowance.");
            work += (int)scan;
            var oldGroups = oldBlocks.Select((key, i) => (Text: key, Index: i)).GroupBy(x => x.Text, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => new Queue<int>(g.Select(value => value.Index)), StringComparer.Ordinal);
            var oldCounts = oldGroups.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
            var newCounts = newBlocks.GroupBy(key => key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            int previous = -1;
            foreach (string key in newBlocks)
            {
                context.CheckCancellation();
                if (!oldGroups.TryGetValue(key, out Queue<int>? positions) || positions.Count == 0 || !newCounts.TryGetValue(key, out int count)) continue;
                if (oldCounts[key] != count) continue;
                int position = positions.Dequeue();
                if (position < previous) return true;
                previous = position;
            }
            return false;
        }
    }
    private static bool SameTableStyle(TableStyle? left, TableStyle? right, ConversionContext context)
    {
        while (left != null && right != null)
        {
            context.CheckCancellation();
            if (left.Id != right.Id || left.Name != right.Name || left.Background != right.Background
                || left.RowBandSize != right.RowBandSize || left.ColumnBandSize != right.ColumnBandSize
                || left.Regions.Count != right.Regions.Count || left.Borders.Count != right.Borders.Count)
                return false;
            foreach (var region in left.Regions)
                if (!right.Regions.TryGetValue(region.Key, out string? color) || region.Value != color) return false;
            foreach (var border in left.Borders)
                if (!right.Borders.TryGetValue(border.Key, out CellBorder? other) || border.Value.Style != other.Style
                    || border.Value.Color != other.Color || border.Value.Width != other.Width) return false;
            left = left.BasedOn; right = right.BasedOn;
        }
        return left == null && right == null;
    }
    private static TextRun Neutral(TextRun r)
    {
        RunAppearance appearance = r.Appearance.Copy();
        if (appearance.ReviewCompanions != null)
        {
            XElement companions = XElement.Parse(appearance.ReviewCompanions);
            companions.Elements().Where(e => e.Name == W + "bCs" || e.Name == W + "iCs" || e.Name == W + "szCs").Remove();
            appearance.ReviewCompanions = companions.HasElements ? companions.ToString(SaveOptions.DisableFormatting) : null;
        }
        TextRun result = new TextRun(r.Text, code: r.Code, link: r.Link, image: r.Image, appearance: appearance,
            fieldInstruction: r.FieldInstruction, note: r.Note, box: r.Box, bookmark: r.Bookmark, control: r.Control, retainedObject: r.Object).RetainMetadata(r);
        result.ExplicitFormat = false; return result;
    }
    private static void NeutralParagraphFormat(ParagraphBlock paragraph)
    {
        paragraph.Alignment = ParagraphAlignment.Left; paragraph.FallbackAfter = null; paragraph.ExplicitParagraphFormat = false;
        ParagraphLayout layout = paragraph.Layout;
        layout.Before = null; layout.After = null; layout.Left = 0; layout.Right = 0; layout.FirstLine = 0;
        layout.LineSpacing = null; layout.MultipleLineSpacing = true; layout.ExactLineSpacing = false;
        layout.PageBreakBefore = false; layout.KeepTogether = false; layout.KeepWithNext = false;
    }
    private static List<TextRun> Slice(ParagraphBlock paragraph, int start, int length, ConversionContext context, ref int work, int maximum)
    {
        var result = new List<TextRun>(); int offset = 0;
        foreach (TextRun run in paragraph.Runs)
        {
            context.CheckCancellation(); if (++work > maximum) throw new InvalidDataException("Comparison.WorkLimit: Text-range work allowance exceeded.");
            int from = Math.Max(0, start - offset), to = Math.Min(run.Text.Length, start + length - offset);
            if (to > from) result.Add(run.WithText(run.Text.Substring(from, to - from))); offset += run.Text.Length;
        }
        return result;
    }
}

public sealed partial class WordDocument
{
    internal DocumentModel ComparisonSnapshot(ConversionContext context)
    {
        lock (gate)
        {
            using var operation = BeginOperation();
            if (model.Controls.HasControls) throw new NotSupportedException("Control.ProtectedOwner: Create an explicit display view before comparing typed-control sessions.");
            if (model.Review.HasHistory || diagnostics.Any(d => d.Code is "Revisions.Accepted" or "Comments.Ignored")) throw new NotSupportedException("Comparison.ExistingHistory: Supply explicit history-free snapshots.");
            Admit(model, context);
            return new DocumentCloner(context).Copy(model);
        }
    }
    internal static WordDocument ComparisonResult(DocumentModel model, ConversionOptions options, ConversionContext context)
    { Admit(model, context); context.CheckCancellation(); return new WordDocument(model, options) { diagnostics = context.Result(0).Diagnostics }; }
}
