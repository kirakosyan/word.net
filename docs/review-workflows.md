# Bounded DOCX review workflows

## Unreleased source: detached text-revision display

The pending roadmap #138 work adds an explicit display for retained text insertions
and deletions in body paragraphs and simple body-table cells. It does not decide any
revision or alter the source session:

```csharp
using var input = File.OpenRead("reviewed.docx");
WordDocument document = WordDocument.Load(input, DocumentFormat.Docx,
    new ReviewLoadOptions(), out _, new ConversionOptions(FidelityMode.BestEffort));
DocumentReviewDisplay display = document.CreateReviewDisplay();
using var pdf = File.Create("review-display.pdf");
display.SavePdf(pdf);
DocumentPreview svgPages = display.RenderSvg();
```

Each display-local `D` or `I` label surrounds the corresponding original deleted
or inserted text, and its opening marker identifies the recorded author and
optional date. The labels identify change kinds without color; deleted text is
also red and inserted text blue, replacing the original colors of changed text.
Bold, italic and the other admitted run properties continue to use the ordinary
managed layout profile. An attribution legend is appended by default; set
`ReviewDisplayOptions.IncludeLegend` to false to omit it. Marker authors escape
backslashes, brackets, colons, `@` and control characters. `display.Revisions`
exposes immutable display-local labels and the original author, optional date and
revision text. PDF keeps the displayed deletion text selectable and searchable.
SVG uses glyph outlines and text titles, as in the existing preview profile.
Source text itself is not escaped and can resemble a marker; these labels are
visual cues, not an authenticated interchange format. Both output and the snapshot
may contain sensitive deleted text and attribution.

The display is detached, so subsequent source edits or decisions do not change it.
Its ordinary managed PDF and SVG share the source conversion options, limits and
diagnostics; configure those options before concurrent use. Native typography,
tagged or archival PDF, PNG, comments, moves, structural or format history,
revisions in headers/footers and structured content are rejected. Structured
content includes fields, bookmarks, links, images, notes, drawings and controls;
tables must have ordinary unmerged, unnested cells. Source load warnings also
prevent display creation. Invalid profiles and cancellation are checked before
the staged PDF is copied to the caller's stream. The normal final-copy I/O limits
still apply. The original accepted/rejected view APIs and regular export behavior
remain available. This is unreleased source work; issue #148 remains open pending
its complete acceptance and exact-runtime qualification.

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.


## Roadmap #102 P4 review profile

Version 1.5.0 extends the earlier profile with bounded run-format history,
exact unambiguous whole-paragraph moves, matching multiple sections, and plain
header/footer text revisions. See the [P4/P5 profile](roadmap-102-p4-p5.md) for
ownership, decisions, limits and excluded histories. The 1.4.0 descriptions below
remain the published-package behavior.

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

The P1 implementation merged in PR #85 and ships in 1.3.0.
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
Clone/save/append remap identifiers. Version 1.4.0 additionally preserves whole-body-
paragraph and simple body-table-row insertion/deletion marks in its bounded profile.
Moves, property revisions, complex structural revisions, modern comments, field/control
ranges, text boxes and other stories reject preservation.
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

Retained history may be saved to DOCX. Other ordinary exports require an explicit
accepted/rejected view with comments removed; the unreleased display above is a
separate opt-in view of supported text history. Removing history is not secure redaction of original bytes,
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
creates a third session. Inputs must be history-free with supported non-text properties.
Matching paragraph/cell topology uses the existing text comparison; 1.4.0 also admits
bounded ordinary paragraph and simple table-row alignment in one section. Equivalent
adjacent run boundaries are coalesced. Word or Grapheme granularity uses pinned Unicode 17 tables, preserving
punctuation, whitespace and logical text. Equal tokens require matching formatting and retain it;
deleted tokens use original formatting and inserted tokens revised formatting.
Deletion wins ambiguous ties. Unqualified structural/property changes and unsupported
structured stories/ranges fail explicitly; there is no move inference or formatting
comparison. Selecting the mark or text of a whole-paragraph insertion/deletion decides
that complete unit. Consuming the final tracked row removes its empty table. See the
[structural profile](roadmap-99-implementation.md#structural-comparison-96).

Limits include 10,000 review records, 1 MiB review text, 1,024-character authors,
100,000 combined tokens across changed paragraphs and 4,000,000 comparison/range steps, further
constrained by conversion resource limits. Exhaustion/cancellation never commits a
partial session. The comparison has no unbounded quadratic fallback.

The executable [P1 recipes](../examples/WordNet.Automation/P1Recipes.cs) save reviewed
DOCX, accepted/rejected PDFs, a comparison and a grouped report.
