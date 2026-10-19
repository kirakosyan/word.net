using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Explicit opt-in to bounded DOCX text-history preservation.</summary>
public sealed class ReviewLoadOptions
{
    /// <summary>Gets or sets whether supported revisions and classic comments must be preserved. Defaults to true.</summary>
    public bool PreserveSupported { get; set; } = true;
}
/// <summary>An explicit supported text-revision decision.</summary>
public enum ReviewDecision
{
    /// <summary>Keep insertions and remove deletions.</summary>
    Accept,
    /// <summary>Remove insertions and restore deletions.</summary>
    Reject,
}
/// <summary>An immutable session/version-scoped review record handle.</summary>
public sealed class DocumentReviewItem
{
    internal DocumentReviewItem(Guid session, long version, ReviewRecord record, bool comment, string? revisionText = null)
    {
        Session = session; Version = version; Id = record.Id; Author = record.Author; Date = record.Date; IsDeletion = record.Deletion; IsComment = comment;
        Initials = record.Initials; Text = revisionText ?? string.Join("\n", record.Body.Select(p => p.Text));
    }
    internal Guid Session { get; }
    internal long Version { get; }
    internal string Id { get; }
    /// <summary>Gets the imported/caller author without attribution inference.</summary>
    public string Author { get; }
    /// <summary>Gets the optional imported date; absence remains absence.</summary>
    public DateTimeOffset? Date { get; }
    /// <summary>Gets whether this revision deletes text.</summary>
    public bool IsDeletion { get; }
    /// <summary>Gets whether the item is a classic comment.</summary>
    public bool IsComment { get; }
    /// <summary>Gets comment initials, empty for revisions without initials.</summary>
    public string Initials { get; }
    /// <summary>Gets retained revision text or the comment body. History snapshots can contain private removed text.</summary>
    public string Text { get; }
}
/// <summary>An immutable UTF-16 body/cell paragraph range scoped to one session version.</summary>
public sealed class DocumentReviewRange
{
    internal DocumentReviewRange(Guid session, long version, Guid paragraph, int start, int length)
    { Session = session; Version = version; Paragraph = paragraph; Start = start; Length = length; }
    internal Guid Session { get; }
    internal long Version { get; }
    internal Guid Paragraph { get; }
    /// <summary>Gets the UTF-16 start offset.</summary>
    public int Start { get; }
    /// <summary>Gets the UTF-16 length.</summary>
    public int Length { get; }
}

public sealed partial class WordDocument
{
    private readonly Guid sessionId = Guid.NewGuid();
    private long version;
    /// <summary>Loads DOCX with an explicit supported-review preservation policy.</summary>
    public static WordDocument Load(Stream source, DocumentFormat format, ReviewLoadOptions reviewOptions, out ConversionResult result,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(reviewOptions, nameof(reviewOptions));
        if (!reviewOptions.PreserveSupported) return Load(source, format, out result, options, cancellationToken);
        Guard.NotNull(source, nameof(source));
        if (format != DocumentFormat.Docx) throw new NotSupportedException("Review.InputFormat: Preservation requires DOCX.");
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        options ??= new ConversionOptions();
        var context = new ConversionContext(options, cancellationToken) { Editing = true, PreserveReview = true, Destination = DocumentFormat.Docx };
        using var input = new BoundedMemoryStream(options.MaximumInputBytes); Transfer(source, input, context, options.MaximumInputBytes);
        DocumentModel model = WordConverter.ReadDocument(input, format, context); Admit(model, context); result = context.Result(0);
        return new WordDocument(model, options) { diagnostics = result.Diagnostics };
    }
    /// <summary>Gets immutable revision handles; any committed mutation invalidates prior handles.</summary>
    public IReadOnlyList<DocumentReviewItem> Revisions
    {
        get
        {
            lock (gate)
            {
                var texts = model.BodyParagraphs.SelectMany(p => p.Runs).Where(r => r.Revision != null).GroupBy(r => r.Revision!, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => string.Concat(g.Select(r => r.Text)), StringComparer.Ordinal);
                return model.Review.Revisions.Values.Select(r => new DocumentReviewItem(sessionId, version, r, false, texts[r.Id])).ToList().AsReadOnly();
            }
        }
    }
    /// <summary>Gets immutable classic-comment handles including their private text/metadata.</summary>
    public IReadOnlyList<DocumentReviewItem> Comments
    { get { lock (gate) return model.Review.Comments.Values.Select(r => new DocumentReviewItem(sessionId, version, r, true)).ToList().AsReadOnly(); } }
    /// <summary>Captures a grapheme-aligned range in one unstructured, history-free body/cell paragraph.</summary>
    public DocumentReviewRange GetReviewRange(int paragraphIndex, int start, int length, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); ParagraphBlock[] paragraphs = model.BodyParagraphs.ToArray();
            if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Length) throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
            ParagraphBlock paragraph = paragraphs[paragraphIndex]; ValidateReviewParagraph(paragraph);
            if (!ReviewStore.SupportedParagraphs(model.Blocks).Contains(paragraph)) throw new NotSupportedException("Review.Story: Recording is limited to body/cell text.");
            if (start < 0 || length < 0 || start > paragraph.Text.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
            List<int> boundaries = UnicodeBoundaries.Find(paragraph.Text, false, Context(options, cancellationToken), options.MaximumCharacters);
            if (!boundaries.Contains(start) || !boundaries.Contains(start + length)) throw new ArgumentException("Review.Range: Range splits a grapheme sequence.");
            return new DocumentReviewRange(sessionId, version, paragraph.Identity, start, length);
        }
    }
    private static void ValidateReviewParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Control != null || paragraph.Runs.Any(r => r.Revision != null || r.Comment != null || r.Bookmark != null || r.Control != null
            || r.FieldInstruction != null || r.Box != null || r.Note != null || r.Image != null || r.Link != null))
            throw new NotSupportedException("Review.Range: Recording requires a history-free paragraph without structured ranges.");
    }
    /// <summary>Records a literal replacement with caller author/time; existing replacement APIs do not record history.</summary>
    public DocumentEditResult RecordReplacement(DocumentReviewRange range, string text, string author, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(range, nameof(range)); Guard.NotNull(text, nameof(text)); Guard.NotNull(author, nameof(author));
        lock (gate)
        {
            if (range.Session != sessionId || range.Version != version) throw new ArgumentException("Review.StaleHandle: Foreign or stale range.", nameof(range));
            return Edit((m, c) =>
            {
                CheckText(text, c); CheckText(author, c);
                if (string.IsNullOrWhiteSpace(author) || author.Length > 1024) throw new ArgumentException("Review.Author: Author is empty or exceeds its bound.", nameof(author));
                _ = UnicodeBoundaries.Find(text, false, c, c.Options.MaximumCharacters);
                ParagraphBlock paragraph = m.BodyParagraphs.Single(p => p.Identity == range.Paragraph); ValidateReviewParagraph(paragraph);
                if (range.Length == 0 && text.Length == 0) return 0;
                string deletion = Guid.NewGuid().ToString("N"), insertion = Guid.NewGuid().ToString("N");
                var output = new List<TextRun>(); int position = 0; TextRun? prototype = null; bool inserted = false;
                foreach (TextRun run in paragraph.Runs)
                {
                    c.CheckCancellation(); int end = position + run.Text.Length;
                    if (end <= range.Start) output.Add(run);
                    else if (position >= range.Start + range.Length)
                    {
                        prototype ??= run; Insert(); output.Add(run);
                    }
                    else
                    {
                        prototype ??= run;
                        int from = Math.Max(0, range.Start - position), to = Math.Min(run.Text.Length, range.Start + range.Length - position);
                        if (from > 0) output.Add(run.WithText(run.Text.Substring(0, from)));
                        if (to > from) { TextRun removed = run.WithText(run.Text.Substring(from, to - from)); removed.Revision = deletion; output.Add(removed); }
                        if (to < run.Text.Length) { Insert(); output.Add(run.WithText(run.Text.Substring(to))); }
                    }
                    position = end;
                }
                Insert();
                if (range.Length > 0) m.Review.Revisions.Add(deletion, new ReviewRecord { Id = deletion, Author = author, Date = timestamp, Deletion = true });
                if (text.Length > 0) m.Review.Revisions.Add(insertion, new ReviewRecord { Id = insertion, Author = author, Date = timestamp });
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(output); return 1;
                void Insert()
                {
                    if (inserted) return; inserted = true;
                    if (text.Length != 0) { TextRun added = (prototype ?? paragraph.Runs.LastOrDefault() ?? new TextRun("")).WithText(text); added.Revision = insertion; output.Add(added); }
                }
            }, cancellationToken, true);
        }
    }
    /// <summary>Atomically accepts/rejects selected text revisions. Consumed comment anchors require explicit removal first.</summary>
    public DocumentEditResult ApplyRevisions(IEnumerable<DocumentReviewItem> selection, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(selection, nameof(selection));
        lock (gate)
        {
            string[] ids;
            using (BeginOperation()) ids = Selection(selection, false, cancellationToken);
            ValidateDecision(decision);
            return Edit((m, c) => { Decide(m, ids, decision, c); return ids.Length; }, cancellationToken, true);
        }
    }
    /// <summary>Explicitly removes selected comments and every associated marker in one transaction.</summary>
    public DocumentEditResult RemoveComments(IEnumerable<DocumentReviewItem> selection, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(selection, nameof(selection));
        lock (gate)
        {
            string[] ids;
            using (BeginOperation()) ids = Selection(selection, true, cancellationToken);
            return Edit((m, c) => { Remove(m, new HashSet<string>(ids, StringComparer.Ordinal), c); return ids.Length; }, cancellationToken, true);
        }
    }
    private string[] Selection(IEnumerable<DocumentReviewItem> selection, bool comments, CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocumentReviewItem item in selection)
        {
            token.ThrowIfCancellationRequested();
            if (item == null || item.Session != sessionId || item.Version != version || item.IsComment != comments || !result.Add(item.Id)
                || !(comments ? model.Review.Comments : model.Review.Revisions).ContainsKey(item.Id)) throw new ArgumentException("Review.StaleHandle: Invalid, duplicate, foreign or stale selection.", nameof(selection));
            if (result.Count > 10000) throw new InvalidDataException("Review.ItemLimit: Selection exceeds its bound.");
        }
        return result.ToArray();
    }
    private static void ValidateDecision(ReviewDecision decision)
    { if (decision is not ReviewDecision.Accept and not ReviewDecision.Reject) throw new ArgumentOutOfRangeException(nameof(decision)); }
    private static void Remove(DocumentModel model, HashSet<string> ids, ConversionContext context)
    {
        foreach (ParagraphBlock paragraph in model.Paragraphs) { context.CheckCancellation(); paragraph.Runs.RemoveAll(r => r.Comment != null && ids.Contains(r.Comment)); }
        foreach (string id in ids) model.Review.Comments.Remove(id);
    }
    private static void Decide(DocumentModel model, IEnumerable<string> ids, ReviewDecision decision, ConversionContext context)
    {
        var selected = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation(); var open = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Comment != null)
                { if (run.CommentBoundary == 1) open.Add(run.Comment); else if (run.CommentBoundary == 2) open.Remove(run.Comment); }
                if (run.Revision == null || !selected.Contains(run.Revision)) continue;
                bool remove = model.Review.Revisions[run.Revision].Deletion == (decision == ReviewDecision.Accept);
                if (remove && open.Count > 0) throw new NotSupportedException("Review.CommentIntersection: Remove intersecting comments explicitly before consuming their text.");
            }
            for (int i = paragraph.Runs.Count - 1; i >= 0; i--)
            {
                TextRun run = paragraph.Runs[i]; if (run.Revision == null || !selected.Contains(run.Revision)) continue;
                if (model.Review.Revisions[run.Revision].Deletion == (decision == ReviewDecision.Accept)) paragraph.Runs.RemoveAt(i);
                else run.Revision = null;
            }
        }
        foreach (string id in selected) model.Review.Revisions.Remove(id);
    }
    /// <summary>Creates a detached explicit accepted/rejected snapshot for rendering. Original history remains unchanged.</summary>
    public WordDocument CreateReviewView(ReviewDecision decision, bool removeComments = false, CancellationToken cancellationToken = default)
    {
        ValidateDecision(decision);
        lock (gate)
        {
            using var operation = BeginOperation(); ConversionContext context = Context(options, cancellationToken);
            DocumentModel copy = new DocumentCloner(context).Copy(model);
            if (removeComments) { Remove(copy, new HashSet<string>(copy.Review.Comments.Keys, StringComparer.Ordinal), context); context.Report("Review.CommentsRemoved", "The caller explicitly removed comments from this detached view."); }
            Decide(copy, copy.Review.Revisions.Keys.ToArray(), decision, context); Admit(copy, context); context.CheckCancellation();
            return new WordDocument(copy, options) { diagnostics = context.Result(0).Diagnostics };
        }
    }
}
