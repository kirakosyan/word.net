using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
}
/// <summary>Deterministic bounded text and plain paragraph/row comparison of history-free documents.</summary>
public static partial class DocumentComparison
{
    /// <summary>Creates a reviewed copy without changing either input. Plain paragraph/row insertion and deletion are supported; other structural/property differences and existing history fail.</summary>
    public static WordDocument Compare(WordDocument original, WordDocument revised, ComparisonOptions comparisonOptions,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(original, nameof(original)); Guard.NotNull(revised, nameof(revised)); Guard.NotNull(comparisonOptions, nameof(comparisonOptions));
        int tokens = comparisonOptions.MaximumTokens, steps = comparisonOptions.MaximumSteps;
        ComparisonGranularity granularity = comparisonOptions.Granularity;
        if (tokens is < 1 or > 100000 || steps is < 1 or > 4000000 || granularity is not ComparisonGranularity.Word and not ComparisonGranularity.Grapheme) throw new ArgumentOutOfRangeException(nameof(comparisonOptions));
        options ??= new ConversionOptions(); var context = new ConversionContext(options, cancellationToken) { Editing = true };
        DocumentModel left = original.ComparisonSnapshot(context), right = revised.ComparisonSnapshot(context);
        foreach (DocumentModel snapshot in new[] { left, right })
            foreach (ParagraphBlock paragraph in snapshot.BodyParagraphs)
            { List<TextRun> combined = RunCoalescer.Combine(paragraph.Runs); paragraph.Runs.Clear(); paragraph.Runs.AddRange(combined); }
        long characters = (long)left.CharacterCount + right.CharacterCount;
        if (characters > options.MaximumCharacters || characters * 4 > options.MaximumExpandedBytes) throw new InvalidDataException("Comparison.ResourceLimit: Combined inputs exceed the comparison allowance.");
        byte[] leftStructure = Structure(left), rightStructure = Structure(right);
        if (!leftStructure.SequenceEqual(rightStructure)) return CompareStructure(left, right, comparisonOptions, options, context);
        ParagraphBlock[] a = left.BodyParagraphs.ToArray(), b = right.BodyParagraphs.ToArray(); int tokenCount = 0, work = 0;
        for (int paragraph = 0; paragraph < a.Length; paragraph++)
        {
            context.CheckCancellation(); string oldText = a[paragraph].Text, newText = b[paragraph].Text;
            if (oldText == newText)
            {
                if (!a[paragraph].Runs.Select(r => r.Text.Length).SequenceEqual(b[paragraph].Runs.Select(r => r.Text.Length)))
                    throw new NotSupportedException("Comparison.Formatting: Formatting boundaries must match on unchanged text.");
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
                    if (oldRuns.Count != newRuns.Count || oldRuns.Where((run, i) => run.Text.Length != newRuns[i].Text.Length || !RunCoalescer.Same(run, newRuns[i])).Any())
                        throw new NotSupportedException("Comparison.Formatting: Formatting on unchanged tokens must match.");
                    output.AddRange(oldRuns); revision = null; deleting = null; u++; v++; continue;
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
            var body = new HashSet<ParagraphBlock>(ReviewStore.SupportedParagraphs(copy.Blocks));
            foreach (ParagraphBlock paragraph in copy.Paragraphs)
            {
                if (!body.Contains(paragraph) || paragraph.Control != null || paragraph.Runs.Any(r => r.FieldInstruction != null || r.Control != null
                    || r.Bookmark != null || r.Box != null || r.Note != null || r.Image != null || r.Link != null || r.Object != null)) continue;
                for (int i = 0; i < paragraph.Runs.Count; i++) paragraph.Runs[i] = paragraph.Runs[i].WithText("");
            }
            using var stream = new BoundedMemoryStream(options.MaximumOutputBytes); DocxWriter.Write(copy, stream, context); return stream.ToArray();
        }
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
