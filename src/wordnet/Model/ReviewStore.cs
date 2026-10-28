using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Layout;

namespace WordNet.Model;

internal sealed class ReviewRecord
{
    internal string Id { get; set; } = "";
    internal string Author { get; set; } = "";
    internal DateTimeOffset? Date { get; set; }
    internal bool Deletion { get; set; }
    internal string Structure { get; set; } = "";
    internal string MoveSide { get; set; } = "";
    internal string? MovePartner { get; set; }
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
        var storyParagraphs = new HashSet<ParagraphBlock>();
        foreach (List<DocumentBlock> story in model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Where(s => s != null).Distinct()
            .SelectMany(s => s!.Stories.Values).Distinct())
        {
            context.CheckCancellation();
            bool history = DocumentModel.Expand(story).OfType<ParagraphBlock>().Any(p => p.Runs.Any(r => r.Revision != null));
            if (!history) continue;
            if (story.Any(b => b is not ParagraphBlock) || story.Cast<ParagraphBlock>().Any(p => p.Control != null || p.NestedControl != null || p.TypedControlId != null || p.List != null || p.StructuralRevision != null
                || p.Runs.Any(r => r.Object != null || r.Image != null || r.Box != null || r.Note != null || r.FieldInstruction != null || r.Control != null
                    || r.TypedControlId != null || r.Bookmark != null || r.Comment != null || r.Link != null || r.FormatRevision != null)))
                throw new NotSupportedException("Review.Story: Story history requires ordinary plain-text paragraphs.");
            storyParagraphs.UnionWith(story.Cast<ParagraphBlock>());
        }
        var moveOwners = new Dictionary<string, ParagraphBlock>(StringComparer.Ordinal);
        var moveText = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocumentBlock block in model.Blocks)
        {
            if (block is ParagraphBlock p && p.StructuralRevision != null)
            {
                Structural(p.StructuralRevision, "paragraph"); ReviewRecord owner = Revisions[p.StructuralRevision];
                if (p.Control != null || p.List != null || p.Runs.Any(r => r.Image != null || r.Box != null || r.Object != null || r.Note != null || r.Control != null || r.FieldInstruction != null || r.Bookmark != null || r.Link != null || r.Comment != null
                    || r.Text.Length != 0 && (r.Revision == null || !Revisions.TryGetValue(r.Revision, out ReviewRecord? child) || child.Deletion != owner.Deletion || child.MoveSide != owner.MoveSide)))
                    throw new NotSupportedException("Review.Structure: Only whole plain paragraphs with matching tracked text support paragraph-mark decisions; split/join revisions are unsupported.");
                if (owner.MoveSide.Length != 0)
                {
                    if (p.Text.Length == 0 || p.Runs.Any(r => r.FormatRevision != null)) throw new NotSupportedException("Review.Move: Moves require nonempty ordinary paragraphs.");
                    moveOwners.Add(owner.Id, p);
                    foreach (TextRun run in p.Runs) if (run.Revision != null)
                    {
                        ReviewRecord child = Revisions[run.Revision];
                        if (child.Author != owner.Author || child.Date != owner.Date || child.MovePartner != null)
                            throw new NotSupportedException("Review.Move: Moved text attribution must match its paragraph mark.");
                        moveText.Add(run.Revision);
                    }
                }
            }
            if (block is TableBlock t) foreach (var pair in t.RowRevisions)
            {
                if (pair.Key < 0 || pair.Key >= t.Rows.Count || t.Rows[pair.Key].Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None || c.Blocks.Any(b => b is not ParagraphBlock)))
                    throw new NotSupportedException("Review.Structure: Only simple body rows support structural revisions.");
                Structural(pair.Value, "row");
            }
        }
        void Structural(string id, string kind)
        {
            if (!Revisions.TryGetValue(id, out ReviewRecord? record) || record.Structure != kind || !revisions.Add(id))
                throw new InvalidDataException("Review.Structure: Invalid structural revision owner.");
        }
        foreach (var pair in moveOwners)
        {
            context.CheckCancellation(); ReviewRecord mark = Revisions[pair.Key];
            if (mark.MoveSide is not "from" and not "to" || !mark.Date.HasValue || mark.Deletion != (mark.MoveSide == "from") || mark.MovePartner == null
                || !moveOwners.TryGetValue(mark.MovePartner, out ParagraphBlock? other) || !Revisions.TryGetValue(mark.MovePartner, out ReviewRecord? partner)
                || partner.MovePartner != mark.Id || partner.MoveSide == mark.MoveSide || partner.Author != mark.Author || partner.Date != mark.Date
                || !ReferenceEquals(pair.Value.Section, other.Section) || pair.Value.Text != other.Text || !EquivalentParagraph(pair.Value, other))
                throw new InvalidDataException("Review.Move: Missing, inconsistent or cross-section move pair.");
            List<TextRun> a = RunCoalescer.Combine(pair.Value.Runs.Select(r => { TextRun copy = r.WithText(r.Text); copy.Revision = null; return copy; }).ToList());
            List<TextRun> b = RunCoalescer.Combine(other.Runs.Select(r => { TextRun copy = r.WithText(r.Text); copy.Revision = null; return copy; }).ToList());
            if (a.Count != b.Count || a.Where((run, i) => run.Text != b[i].Text || !RunCoalescer.Same(run, b[i])).Any())
                throw new NotSupportedException("Review.Move: Moved text and formatting must match exactly.");
        }
        foreach (ReviewRecord record in Revisions.Values)
            if (record.MoveSide.Length != 0 && !moveOwners.ContainsKey(record.Id) && !moveText.Contains(record.Id) || record.MoveSide.Length == 0 && record.MovePartner != null)
                throw new InvalidDataException("Review.Move: Orphaned move record.");
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation(); int offset = 0; var closed = new HashSet<string>(StringComparer.Ordinal); string? current = null;
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.FormatRevision != null)
                {
                    if (!body.Contains(paragraph) || run.Revision != null || run.PreviousFormat == null
                        || !Revisions.TryGetValue(run.FormatRevision, out ReviewRecord? format) || format.Structure != "format"
                        || !revisions.Add(run.FormatRevision)) throw new InvalidDataException("Review.Format: Invalid or duplicate formatting revision owner.");
                    if (run.Text.Length == 0 || run.Object != null || run.Image != null || run.Box != null || run.FieldInstruction != null || run.Note != null || run.Control != null || run.Bookmark != null || run.Comment != null || run.Link != null)
                        throw new NotSupportedException("Review.Format: Formatting revisions support ordinary body/cell text only.");
                    text += run.Text.Length;
                }
                else if (run.PreviousFormat != null) throw new InvalidDataException("Review.Format: Previous formatting has no revision owner.");
                if (run.Revision != current)
                {
                    if (current != null) closed.Add(current);
                    current = run.Revision;
                    if (current != null && closed.Contains(current)) throw new InvalidDataException("Review.Range: Disjoint revision segments are unsupported.");
                }
                if (run.Revision != null)
                {
                    if ((!body.Contains(paragraph) && !storyParagraphs.Contains(paragraph)) || !Revisions.TryGetValue(run.Revision, out ReviewRecord? revision)) throw new InvalidDataException("Review.Range: Unresolved or cross-story revision.");
                    if (run.Object != null || run.Image != null || run.Box != null || run.FieldInstruction != null || run.Note != null || run.Control != null || run.Bookmark != null || run.Comment != null || run.Link != null)
                        throw new NotSupportedException("Review.Range: Revisions support plain body/cell text only.");
                    if (revision.Structure.Length != 0) throw new InvalidDataException("Review.Structure: A structural identifier cannot own text runs.");
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

    private static bool EquivalentParagraph(ParagraphBlock a, ParagraphBlock b)
    {
        ParagraphLayout x = a.Layout, y = b.Layout;
        return a.StyleId == b.StyleId && a.HeadingLevel == b.HeadingLevel && a.Alignment == b.Alignment && a.RightToLeft == b.RightToLeft
            && a.IsCodeBlock == b.IsCodeBlock && a.FallbackFontSize == b.FallbackFontSize && a.FallbackAfter == b.FallbackAfter
            && (a.Mark == null ? b.Mark == null : b.Mark != null && RunCoalescer.Same(a.Mark, b.Mark))
            && x.Before == y.Before && x.After == y.After && x.Left == y.Left && x.Right == y.Right && x.FirstLine == y.FirstLine
            && x.LineSpacing == y.LineSpacing && x.MultipleLineSpacing == y.MultipleLineSpacing && x.ExactLineSpacing == y.ExactLineSpacing
            && x.PageBreakBefore == y.PageBreakBefore && x.KeepTogether == y.KeepTogether && x.KeepWithNext == y.KeepWithNext
            && x.Tabs.Count == y.Tabs.Count && !x.Tabs.Where((tab, i) => tab.Position != y.Tabs[i].Position || tab.Alignment != y.Tabs[i].Alignment || tab.Leader != y.Tabs[i].Leader).Any();
    }
}
