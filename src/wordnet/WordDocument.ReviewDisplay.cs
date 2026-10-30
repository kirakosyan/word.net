using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Controls attribution and annotation behavior in an explicit review display.</summary>
public sealed class ReviewDisplayOptions
{
    /// <summary>Gets or sets whether an attribution legend is appended. Defaults to true.</summary>
    public bool IncludeLegend { get; set; } = true;
    /// <summary>Gets or sets the policy for supported comments, moves and formatting history. Defaults to rejection.</summary>
    public ReviewAnnotationPolicy AnnotationPolicy { get; set; }
}

/// <summary>Immutable attribution and original text for one marked text revision.</summary>
public sealed class ReviewDisplayRevision
{
    internal ReviewDisplayRevision(string label, ReviewRecord record, string text)
    { Label = label; Author = record.Author; Date = record.Date; IsDeletion = record.Deletion; Text = text; }
    /// <summary>Gets the display-local marker, such as D1 or I2.</summary>
    public string Label { get; }
    /// <summary>Gets the recorded author without attribution inference.</summary>
    public string Author { get; }
    /// <summary>Gets the recorded date, or null when none was supplied.</summary>
    public DateTimeOffset? Date { get; }
    /// <summary>Gets whether the revision removes text.</summary>
    public bool IsDeletion { get; }
    /// <summary>Gets the original revision text, including text deleted from the accepted view.</summary>
    public string Text { get; }
}

/// <summary>A detached, read-only display of retained text revisions.</summary>
/// <remarks>Markers and the optional legend contain author metadata and deleted text. PDF text remains selectable;
/// SVG previews use glyph outlines with text titles. The source session and its revision decisions are unchanged.
/// Configure the source conversion options before concurrent use.</remarks>
public sealed class DocumentReviewDisplay
{
    private readonly WordDocument snapshot;
    private readonly ConversionOptions conversion;
    private readonly IReadOnlyList<ReviewDisplayRevision> revisions;
    private readonly IReadOnlyList<ReviewDisplayAnnotation> annotations;
    internal DocumentReviewDisplay(WordDocument snapshot, ConversionOptions conversion, IEnumerable<ReviewDisplayRevision> revisions, IEnumerable<ReviewDisplayAnnotation> annotations)
    {
        this.snapshot = snapshot; this.conversion = conversion;
        this.revisions = new ReadOnlyCollection<ReviewDisplayRevision>(revisions.ToArray());
        this.annotations = new ReadOnlyCollection<ReviewDisplayAnnotation>(annotations.ToArray());
    }
    /// <summary>Gets immutable attribution and text in display-marker order.</summary>
    public IReadOnlyList<ReviewDisplayRevision> Revisions => revisions;
    /// <summary>Gets immutable numbered annotations in their first source-anchor order.</summary>
    public IReadOnlyList<ReviewDisplayAnnotation> Annotations => annotations;

    /// <summary>Writes an ordinary managed PDF from the detached display, leaving the destination open.</summary>
    public ConversionResult SavePdf(Stream destination, CancellationToken cancellationToken = default)
    {
        CheckProfile(conversion);
        return snapshot.Save(destination, DocumentFormat.Pdf, cancellationToken: cancellationToken);
    }

    /// <summary>Creates SVG page previews of the detached display; PNG and nonordinary profiles are unsupported.</summary>
    public DocumentPreview RenderSvg(PreviewOptions? preview = null, CancellationToken cancellationToken = default)
    {
        CheckProfile(conversion); preview ??= new PreviewOptions();
        if (preview.Format != PreviewFormat.Svg) throw new NotSupportedException("Review.DisplayProfile: Review previews support managed SVG only.");
        return snapshot.RenderReviewSvg(preview, cancellationToken);
    }

    internal static void CheckProfile(ConversionOptions options)
    {
        Guard.NotNull(options.Typography, nameof(options.Typography)); Guard.NotNull(options.Pdf, nameof(options.Pdf));
        if (options.Typography.Enabled || options.Pdf.Accessibility != PdfAccessibility.None || options.Pdf.Conformance != PdfConformance.Pdf17)
            throw new NotSupportedException("Review.DisplayProfile: Review display requires ordinary managed PDF/SVG output.");
    }
}

public sealed partial class WordDocument
{
    internal DocumentPreview RenderReviewSvg(PreviewOptions preview, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            using var operation = BeginOperation();
            ConversionContext context = Context(options, cancellationToken); context.Destination = DocumentFormat.Pdf;
            DocumentModel copy = new DocumentCloner(context).Copy(model); Admit(copy, context);
            return WordConverter.RenderModel(copy, preview, context);
        }
    }

    /// <summary>Creates a detached, immutable marked display for supported body/cell text insertions and deletions.</summary>
    /// <remarks>Deletion text is retained and searchable in PDF. Each colored change is also surrounded by a
    /// textual I/D marker carrying author and optional date; original colors of changed text are replaced.
    /// The optional numbered annotation policy admits supported comments, moves and format history;
    /// other structural history, story revisions and structured content are unsupported.
    /// The source session and its retained history are never modified.</remarks>
    public DocumentReviewDisplay CreateReviewDisplay(ReviewDisplayOptions? displayOptions = null, CancellationToken cancellationToken = default)
    {
        bool legend = displayOptions?.IncludeLegend ?? true;
        ReviewAnnotationPolicy annotationPolicy = displayOptions?.AnnotationPolicy ?? ReviewAnnotationPolicy.Reject;
        if (annotationPolicy is not ReviewAnnotationPolicy.Reject and not ReviewAnnotationPolicy.NumberedEndNotes)
            throw new ArgumentOutOfRangeException(nameof(displayOptions));
        lock (gate)
        {
            using var operation = BeginOperation();
            DocumentReviewDisplay.CheckProfile(options);
            ConversionContext context = Context(options, cancellationToken);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning))
                throw new NotSupportedException("Review.DisplaySourceLoss: Resolve source load losses before creating a review display.");
            if (annotationPolicy == ReviewAnnotationPolicy.Reject
                && (model.Review.Comments.Count != 0 || model.Review.Revisions.Values.Any(r => r.Structure.Length != 0 || r.MoveSide.Length != 0)))
                throw new NotSupportedException("Review.DisplayProfile: Comments, moves and structural/format revisions require a separate display profile.");
            if (model.Controls.HasControls || model.HasControlDisplay || model.ObjectCount != 0 || model.Notes.Count != 0
                || model.Paragraphs.Any(p => p.Control != null || p.NestedControl != null)
                || model.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Image != null || r.Box != null || r.Note != null || r.FieldInstruction != null
                    || r.Bookmark != null || r.Link != null || r.Control != null || r.Object != null)
                || model.Tables.Any(t => t.Control != null || t.NestedControl != null || t.RowControls.Count != 0
                    || t.Rows.SelectMany(r => r).Any(cell => cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None || cell.Blocks.Any(b => b is TableBlock))))
                throw new NotSupportedException("Review.DisplayProfile: Review display currently requires ordinary text and simple tables without structured content.");
            var body = new HashSet<ParagraphBlock>(ReviewStore.SupportedParagraphs(model.Blocks));
            if (model.Paragraphs.Any(p => !body.Contains(p) && p.Runs.Any(r => r.Revision != null)))
                throw new NotSupportedException("Review.DisplayProfile: Revision display is limited to body and table-cell text.");

            DocumentModel copy = new DocumentCloner(context).Copy(model);
            IReadOnlyList<ReviewDisplayAnnotation> annotations = annotationPolicy == ReviewAnnotationPolicy.NumberedEndNotes
                ? ReviewAnnotations.Prepare(copy, context) : Array.Empty<ReviewDisplayAnnotation>();
            var ordered = new List<string>(); var texts = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            foreach (ParagraphBlock paragraph in ReviewStore.SupportedParagraphs(copy.Blocks))
                foreach (TextRun run in paragraph.Runs)
                {
                    context.CheckCancellation();
                    if (run.Revision is not string id) continue;
                    if (!texts.TryGetValue(id, out StringBuilder? text)) { text = new StringBuilder(); texts.Add(id, text); ordered.Add(id); }
                    text.Append(run.Text);
                }
            if (ordered.Count != copy.Review.Revisions.Count) throw new InvalidDataException("Review.DisplayProfile: A revision has no admitted text owner.");
            var items = new List<ReviewDisplayRevision>(); var byId = new Dictionary<string, ReviewDisplayRevision>(StringComparer.Ordinal);
            for (int index = 0; index < ordered.Count; index++)
            {
                context.CheckCancellation(); string id = ordered[index]; ReviewRecord record = copy.Review.Revisions[id];
                string label = (record.Deletion ? "D" : "I") + (index + 1).ToString(CultureInfo.InvariantCulture);
                var item = new ReviewDisplayRevision(label, record, texts[id].ToString()); items.Add(item); byId.Add(id, item);
            }
            long characters = copy.CharacterCount;
            foreach (ReviewDisplayAnnotation annotation in annotations)
                AddCharacters(checked(annotation.Label.Length + annotation.Author.Length + annotation.Initials.Length + annotation.Text.Length
                    + (annotation.PreviousFormatting?.Length ?? 0) + (annotation.CurrentFormatting?.Length ?? 0)));
            foreach (ReviewDisplayRevision item in items)
            {
                context.CheckCancellation();
                AddCharacters(checked(item.Text.Length + item.Author.Length + item.Label.Length));
            }
            foreach (ParagraphBlock paragraph in ReviewStore.SupportedParagraphs(copy.Blocks))
            {
                var output = new List<TextRun>(); string? current = null;
                foreach (TextRun run in paragraph.Runs)
                {
                    context.CheckCancellation();
                    if (run.Revision != current)
                    {
                        if (current != null) Mark("[/" + byId[current].Label + "]", byId[current]);
                        current = run.Revision;
                        if (current != null)
                        {
                            ReviewDisplayRevision item = byId[current];
                            string date = item.Date.HasValue ? "@" + item.Date.Value.ToString("O", CultureInfo.InvariantCulture) : "";
                            Mark("[" + item.Label + ":" + Escape(item.Author) + date + "]", item);
                        }
                    }
                    if (current == null) output.Add(run);
                    else
                    {
                        ReviewDisplayRevision item = byId[current];
                        output.Add(new TextRun(run.Text, run.Bold, run.Italic, run.Code, fontSize: run.FontSize,
                            color: item.IsDeletion ? "B00020" : "0062B1", appearance: run.Appearance.Copy()));
                    }
                }
                if (current != null) Mark("[/" + byId[current].Label + "]", byId[current]);
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(output);
                void Mark(string text, ReviewDisplayRevision item)
                {
                    AddCharacters(text.Length); output.Add(new TextRun(text, color: item.IsDeletion ? "B00020" : "0062B1"));
                }
            }
            copy.Review.Revisions.Clear();
            if (legend && items.Count != 0)
            {
                PageSettings? section = copy.FinalSection ?? copy.Blocks.LastOrDefault()?.Section;
                AddLegend("Revision legend", section);
                foreach (ReviewDisplayRevision item in items)
                    AddLegend(item.Label + ": " + (item.IsDeletion ? "deletion" : "insertion") + " by " + Escape(item.Author)
                        + (item.Date.HasValue ? " at " + item.Date.Value.ToString("O", CultureInfo.InvariantCulture) : " (date not supplied)"), section);
            }
            Admit(copy, context); context.CheckCancellation();
            var snapshot = new WordDocument(copy, options) { diagnostics = context.Result(0).Diagnostics };
            return new DocumentReviewDisplay(snapshot, options, items, annotations);

            void AddCharacters(int count)
            {
                characters = checked(characters + count);
                if (characters > context.Options.MaximumCharacters || characters * 2 > context.Options.MaximumExpandedBytes)
                    throw new InvalidDataException("Limit.ReviewDisplay: Review markers and legend exceed the destination bounds.");
            }
            void AddLegend(string text, PageSettings? section)
            {
                AddCharacters(text.Length); var paragraph = new ParagraphBlock { Section = section };
                paragraph.Runs.Add(new TextRun(text)); copy.Blocks.Add(paragraph);
            }
        }
    }

    private static string Escape(string value)
    {
        var output = new StringBuilder();
        foreach (char ch in value)
        {
            if (ch is '\\' or '[' or ']' or '@' or ':') output.Append('\\').Append(ch);
            else if (char.IsControl(ch)) output.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            else output.Append(ch);
        }
        return output.ToString();
    }
}
