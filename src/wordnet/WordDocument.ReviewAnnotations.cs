using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Selects whether supported review annotations are included in a detached display.</summary>
public enum ReviewAnnotationPolicy
{
    /// <summary>Reject comments, moves and formatting history.</summary>
    Reject = 0,
    /// <summary>Use numbered text anchors and end annotations in source order.</summary>
    NumberedEndNotes = 1,
}

/// <summary>Identifies the kind of a numbered review annotation.</summary>
public enum ReviewDisplayAnnotationKind
{
    /// <summary>A classic comment and its body.</summary>
    Comment = 0,
    /// <summary>A supported run-format revision.</summary>
    Formatting = 1,
    /// <summary>A supported paired whole-paragraph move.</summary>
    Move = 2,
}

/// <summary>Immutable, display-local metadata for a numbered review annotation.</summary>
public sealed class ReviewDisplayAnnotation
{
    internal ReviewDisplayAnnotation(string label, ReviewDisplayAnnotationKind kind, ReviewRecord record, string text, string? previous = null, string? current = null)
    { Label = label; Kind = kind; Author = record.Author; Initials = record.Initials; Date = record.Date; Text = text; PreviousFormatting = previous; CurrentFormatting = current; }
    /// <summary>Gets the display-local anchor, such as C1, F1 or M1.</summary>
    public string Label { get; }
    /// <summary>Gets the supported annotation kind.</summary>
    public ReviewDisplayAnnotationKind Kind { get; }
    /// <summary>Gets the recorded author.</summary>
    public string Author { get; }
    /// <summary>Gets the recorded initials, which may be empty.</summary>
    public string Initials { get; }
    /// <summary>Gets the recorded date, or null if absent.</summary>
    public DateTimeOffset? Date { get; }
    /// <summary>Gets comment text or the original text of a marked format change or move.</summary>
    public string Text { get; }
    /// <summary>Gets the previous formatting summary for a format change, or null.</summary>
    public string? PreviousFormatting { get; }
    /// <summary>Gets the current formatting summary for a format change, or null.</summary>
    public string? CurrentFormatting { get; }
}

internal static class ReviewAnnotations
{
    internal static IReadOnlyList<ReviewDisplayAnnotation> Prepare(DocumentModel model, ConversionContext context)
    {
        if (model.Review.Revisions.Values.Any(r =>
            !(r.MoveSide.Length == 0 && (r.Structure.Length == 0 || r.Structure == "format")
                || r.MoveSide is "from" or "to" && (r.Structure.Length == 0 || r.Structure == "paragraph"))))
            throw new NotSupportedException("Review.DisplayProfile: Structural history other than paired whole-paragraph moves is unsupported.");

        var annotations = new List<ReviewDisplayAnnotation>();
        var comments = new Dictionary<string, ReviewDisplayAnnotation>(StringComparer.Ordinal);
        var commentBodies = new Dictionary<ReviewDisplayAnnotation, ReviewRecord>();
        var moves = new Dictionary<string, ReviewDisplayAnnotation>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var listIds = new HashSet<string>(model.Paragraphs.Where(p => p.List != null).Select(p => p.List!.Id), StringComparer.Ordinal);
        int commentCount = 0, moveCount = 0, formatCount = 0;
        int listCount = 0;
        long characters = model.CharacterCount;
        foreach (ParagraphBlock paragraph in ReviewStore.SupportedParagraphs(model.Blocks))
        {
            context.CheckCancellation();
            var output = new List<TextRun>(); ReviewDisplayAnnotation? move = null;
            if (paragraph.StructuralRevision is string markId)
            {
                ReviewRecord mark = model.Review.Revisions[markId];
                if (mark.MoveSide is not "from" and not "to" || mark.MovePartner == null)
                    throw new NotSupportedException("Review.DisplayProfile: Only paired whole-paragraph moves are supported.");
                if (!moves.TryGetValue(markId, out move))
                {
                    move = new ReviewDisplayAnnotation("M" + (++moveCount).ToString(CultureInfo.InvariantCulture), ReviewDisplayAnnotationKind.Move, mark, paragraph.Text);
                    moves.Add(markId, move); moves.Add(mark.MovePartner, move); annotations.Add(move);
                }
                Mark("[" + move.Label + ":" + mark.MoveSide + "]");
                removed.Add(markId); paragraph.StructuralRevision = null;
            }
            foreach (TextRun run in paragraph.Runs)
            {
                context.CheckCancellation();
                if (run.Comment is string commentId)
                {
                    if (run.CommentBoundary == 1)
                    {
                        ReviewRecord record = model.Review.Comments[commentId];
                        var item = new ReviewDisplayAnnotation("C" + (++commentCount).ToString(CultureInfo.InvariantCulture), ReviewDisplayAnnotationKind.Comment,
                            record, string.Join("\n", record.Body.Select(p => p.Text)));
                        comments.Add(commentId, item); commentBodies.Add(item, record); annotations.Add(item); Mark("[" + item.Label + ":start]");
                    }
                    else if (run.CommentBoundary == 2) Mark("[" + comments[commentId].Label + ":end]");
                    else if (run.CommentBoundary != 3) throw new InvalidDataException("Review.DisplayProfile: Invalid comment anchor.");
                    if (run.Text.Length == 0) continue;
                }
                if (run.FormatRevision is string formatId)
                {
                    ReviewRecord record = model.Review.Revisions[formatId];
                    if (run.PreviousFormat == null) throw new InvalidDataException("Review.DisplayProfile: Missing previous formatting.");
                    var item = new ReviewDisplayAnnotation("F" + (++formatCount).ToString(CultureInfo.InvariantCulture), ReviewDisplayAnnotationKind.Formatting,
                        record, run.Text, Formatting(run.PreviousFormat), Formatting(run));
                    annotations.Add(item); removed.Add(formatId); Mark("[" + item.Label + "]");
                    output.Add(Clean(run)); Mark("[/" + item.Label + "]");
                }
                else if (move != null)
                {
                    if (run.Revision != null) removed.Add(run.Revision);
                    output.Add(Clean(run));
                }
                else if (run.Comment != null) output.Add(Clean(run));
                else output.Add(run);
            }
            if (move != null) Mark("[/" + move.Label + "]");
            paragraph.Runs.Clear(); paragraph.Runs.AddRange(output);
            void Mark(string text) { Count(text.Length); output.Add(new TextRun(text)); }
        }
        if (comments.Count != model.Review.Comments.Count) throw new InvalidDataException("Review.DisplayProfile: A comment has no admitted anchor.");
        foreach (string id in removed) model.Review.Revisions.Remove(id);
        if (annotations.Count != 0)
        {
            PageSettings? section = model.FinalSection ?? model.Blocks.LastOrDefault()?.Section;
            Note("Review annotations", section);
            foreach (ReviewDisplayAnnotation annotation in annotations)
            {
                context.CheckCancellation();
                string kind = annotation.Kind == ReviewDisplayAnnotationKind.Comment ? "comment" : annotation.Kind == ReviewDisplayAnnotationKind.Move ? "move" : "formatting";
                string header = annotation.Label + ": " + kind + " by " + Escape(annotation.Author)
                    + (annotation.Initials.Length == 0 ? "" : " (" + Escape(annotation.Initials) + ")")
                    + (annotation.Date.HasValue ? " at " + annotation.Date.Value.ToString("O", CultureInfo.InvariantCulture) : " (date not supplied)");
                if (annotation.Kind == ReviewDisplayAnnotationKind.Formatting)
                    header += "; previous " + annotation.PreviousFormatting + "; current " + annotation.CurrentFormatting;
                Note(header, section);
                if (annotation.Kind == ReviewDisplayAnnotationKind.Comment)
                {
                    ReviewRecord original = commentBodies[annotation];
                    var commentListIds = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (ParagraphBlock body in original.Body)
                    {
                        context.CheckCancellation(); Count(body.Text.Length);
                        if (body.List is ListItem list)
                        {
                            if (!commentListIds.TryGetValue(list.Id, out string? id))
                            {
                                do { id = "wordnet-review-list-" + (++listCount).ToString(CultureInfo.InvariantCulture); } while (!listIds.Add(id));
                                commentListIds.Add(list.Id, id);
                            }
                            list.Id = id;
                        }
                        body.Section = section; model.Blocks.Add(body);
                    }
                }
            }
        }
        model.Review.Comments.Clear();
        return annotations.AsReadOnly();

        void Count(int count)
        {
            characters = checked(characters + count);
            if (characters > context.Options.MaximumCharacters || characters * 2 > context.Options.MaximumExpandedBytes)
                throw new InvalidDataException("Limit.ReviewDisplay: Review annotations exceed the destination bounds.");
        }
        void Note(string text, PageSettings? section)
        {
            context.CheckCancellation();
            Count(text.Length); var paragraph = new ParagraphBlock { Section = section };
            paragraph.Runs.Add(new TextRun(text)); model.Blocks.Add(paragraph);
        }
    }

    private static TextRun Clean(TextRun source)
    {
        TextRun result = source.WithText(source.Text);
        result.Revision = null; result.FormatRevision = null; result.PreviousFormat = null; result.Comment = null; result.CommentBoundary = 0;
        return result;
    }
    private static string Formatting(TextRun run) => "bold=" + (run.Bold ? "true" : "false") + ", italic=" + (run.Italic ? "true" : "false")
        + ", size=" + (run.FontSize?.ToString("R", CultureInfo.InvariantCulture) ?? "default") + ", color=" + (run.Color ?? "auto");
    private static string Escape(string value)
    {
        var output = new System.Text.StringBuilder();
        foreach (char ch in value)
            if (ch is '\\' or '[' or ']' or '@' or ':') output.Append('\\').Append(ch);
            else if (char.IsControl(ch)) output.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            else output.Append(ch);
        return output.ToString();
    }
}
