# Bounded DOCX review workflows

The P1 implementation merged in PR #85 and is not in the published packages.
It adds separate opt-in APIs; ordinary converter loads keep their existing
accepted-state diagnostics and Strict rejection of review loss.

```csharp
using var input = File.OpenRead("reviewed.docx");
WordDocument document = WordDocument.Load(input, DocumentFormat.Docx,
    new ReviewLoadOptions(), out ConversionResult result,
    new ConversionOptions(FidelityMode.BestEffort));

// Snapshots contain private authors, comment bodies and deleted text.
IReadOnlyList<DocumentReviewItem> revisions = document.Revisions;
IReadOnlyList<DocumentReviewItem> comments = document.Comments;
document.RemoveComments(comments); // Explicit; never implicit in a review decision.
document.ApplyRevisions(document.Revisions, ReviewDecision.Accept);
```

Preservation supports plain run insertions/deletions in body paragraphs and table
cells, plus classic comment intervals within one paragraph, including crossing
intervals. Authors, initials and optional dates survive; absent dates remain absent.
DOCX contains actual `w:ins`, `w:del`/`w:delText`, comment parts and marker references.
Clone/save/append remap identifiers. Moves, property/structural revisions, modern
comments, field/control ranges, text boxes and other stories reject preservation.
Malformed duplicate/orphan identifiers and ranges are invalid data.
Classic comment paragraphs resolve numbering and list-linked styles. Extended
author-identity parts can be safely omitted with an informational diagnostic;
their presence alone does not turn ordinary tracked changes into modern comments.

Records and ranges are immutable handles tied to a session and version. Any
committed edit invalidates existing handles. All selected handles are checked
before a transaction commits. Accept keeps insertions/removes deletions; reject
removes insertions/restores deletions. A decision that consumes an anchored comment
fails until that comment is explicitly removed. Ordinary editing/templates require
history to be resolved; append and the review APIs preserve supported history.

To render a detached view without changing the original history:

```csharp
WordDocument accepted = document.CreateReviewView(ReviewDecision.Accept,
    removeComments: true);
using var pdf = File.Create("accepted.pdf");
accepted.Save(pdf, DocumentFormat.Pdf);
```

Retained history may be saved to DOCX. Other exports require an explicit view with
comments removed. Removing history is not secure redaction of original bytes,
backups or managed memory. Diagnostics do not include authors or review text.

Recording is an explicit range operation; existing Replace/Merge methods do not
record changes:

```csharp
WordDocument draft = WordDocument.Create();
draft.AddParagraph("Total 10");
DocumentReviewRange range = draft.GetReviewRange(0, 6, 2);
draft.RecordReplacement(range, "20", "Caller author",
    new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
```

Ranges use UTF-16 offsets but cannot split Unicode 17 extended grapheme clusters.
Recording requires a history-free, unstructured paragraph; other paragraphs may
retain history. New attribution/time always come from the caller.

`DocumentComparison.Compare(original, revised, new ComparisonOptions(author, time))`
creates a third session. Inputs must be history-free, with matching paragraph/cell
topology and supported non-text properties; equivalent adjacent run boundaries are
coalesced. Word or Grapheme granularity uses pinned Unicode 17 tables, preserving
punctuation, whitespace and logical text. Equal tokens require matching formatting and retain it;
deleted tokens use original formatting and inserted tokens revised formatting.
Deletion wins ambiguous ties. Structural/property changes and unsupported structured
stories/ranges fail explicitly; there is no move inference or formatting comparison.

Limits include 10,000 review records, 1 MiB review text, 1,024-character authors,
100,000 combined tokens across changed paragraphs and 4,000,000 comparison/range steps, further
constrained by conversion resource limits. Exhaustion/cancellation never commits a
partial session. The comparison has no unbounded quadratic fallback.

The executable [P1 recipes](../examples/WordNet.Automation/P1Recipes.cs) save reviewed
DOCX, accepted/rejected PDFs, a comparison and a grouped report.
