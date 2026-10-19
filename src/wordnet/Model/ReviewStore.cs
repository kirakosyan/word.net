using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;

namespace WordNet.Model;

internal sealed class ReviewRecord
{
    internal string Id { get; set; } = "";
    internal string Author { get; set; } = "";
    internal DateTimeOffset? Date { get; set; }
    internal bool Deletion { get; set; }
    internal string Initials { get; set; } = "";
    internal List<ParagraphBlock> Body { get; } = new List<ParagraphBlock>();
}

internal sealed class ReviewStore
{
    internal Dictionary<string, ReviewRecord> Revisions { get; } = new Dictionary<string, ReviewRecord>(StringComparer.Ordinal);
    internal Dictionary<string, ReviewRecord> Comments { get; } = new Dictionary<string, ReviewRecord>(StringComparer.Ordinal);
    internal bool HasHistory => Revisions.Count != 0 || Comments.Count != 0;
    internal static IEnumerable<ParagraphBlock> SupportedParagraphs(IEnumerable<DocumentBlock> blocks)
    {
        foreach (DocumentBlock block in blocks)
            if (block is ParagraphBlock paragraph) yield return paragraph;
            else foreach (ParagraphBlock child in SupportedParagraphs(((TableBlock)block).Rows.SelectMany(r => r).SelectMany(c => c.Blocks))) yield return child;
    }
    internal void Validate(DocumentModel model, ConversionContext context)
    {
        if (Revisions.Count + Comments.Count > 10000) throw new InvalidDataException("Review.ItemLimit: Too many review records.");
        long text = 0;
        foreach (ReviewRecord record in Revisions.Values.Concat(Comments.Values))
        {
            context.CheckCancellation();
            if (record.Author.Length > 1024 || record.Initials.Length > 1024) throw new InvalidDataException("Review.AuthorLimit: Review metadata exceeds its bound.");
            text += record.Author.Length + record.Initials.Length + record.Body.Sum(p => p.Text.Length);
        }
        var revisions = new HashSet<string>(StringComparer.Ordinal);
        var revisionOwners = new Dictionary<string, ParagraphBlock>(StringComparer.Ordinal);
        var comments = new Dictionary<string, (ParagraphBlock Paragraph, int Offset, bool Ended, bool Referenced)>(StringComparer.Ordinal);
        var body = new HashSet<ParagraphBlock>(SupportedParagraphs(model.Blocks));
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation(); int offset = 0; var closed = new HashSet<string>(StringComparer.Ordinal); string? current = null;
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Revision != current)
                {
                    if (current != null) closed.Add(current);
                    current = run.Revision;
                    if (current != null && closed.Contains(current)) throw new InvalidDataException("Review.Range: Disjoint revision segments are unsupported.");
                }
                if (run.Revision != null)
                {
                    if (!body.Contains(paragraph) || !Revisions.TryGetValue(run.Revision, out ReviewRecord? revision)) throw new InvalidDataException("Review.Range: Unresolved or cross-story revision.");
                    if (run.Image != null || run.Box != null || run.FieldInstruction != null || run.Note != null || run.Control != null || run.Bookmark != null || run.Comment != null || run.Link != null)
                        throw new NotSupportedException("Review.Range: Revisions support plain body/cell text only.");
                    if (revisionOwners.TryGetValue(run.Revision, out ParagraphBlock? owner) && !ReferenceEquals(owner, paragraph))
                        throw new InvalidDataException("Review.Range: Revision ranges cannot cross paragraphs.");
                    revisionOwners[run.Revision] = paragraph;
                    revisions.Add(run.Revision); text += run.Text.Length;
                }
                if (run.Comment != null)
                {
                    if (!body.Contains(paragraph) || !Comments.ContainsKey(run.Comment)) throw new InvalidDataException("Review.CommentRange: Unresolved or cross-story comment.");
                    bool found = comments.TryGetValue(run.Comment, out var range);
                    if (run.CommentBoundary == 1)
                    { if (found) throw new InvalidDataException("Review.CommentRange: Duplicate start."); comments.Add(run.Comment, (paragraph, offset, false, false)); }
                    else
                    {
                        if (!found || !ReferenceEquals(range.Paragraph, paragraph)) throw new InvalidDataException("Review.CommentRange: Orphan or cross-paragraph marker.");
                        if (run.CommentBoundary == 2)
                        { if (range.Ended || range.Referenced) throw new InvalidDataException("Review.CommentRange: Duplicate or unordered end."); range.Ended = true; }
                        else if (run.CommentBoundary == 3)
                        { if (!range.Ended || range.Referenced) throw new InvalidDataException("Review.CommentRange: Missing end or duplicate reference."); range.Referenced = true; }
                        else throw new InvalidDataException("Review.CommentRange: Invalid marker.");
                        comments[run.Comment] = range;
                    }
                }
                offset = checked(offset + run.Text.Length);
            }
        }
        if (!revisions.SetEquals(Revisions.Keys) || comments.Count != Comments.Count || comments.Values.Any(c => !c.Ended || !c.Referenced)) throw new InvalidDataException("Review.Range: Orphaned review records.");
        if (text > 1024 * 1024 || text > context.Options.MaximumCharacters || text * 2 > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Review.TextLimit: Review text exceeds its aggregate allowance.");
    }
}
