# ADR B5-REVIEW: revisions, comments and comparison

Historical design checkpoint: published 1.5.0 later added bounded opt-in review
preservation, decisions, recording and comparison. The [review profile](../review-workflows.md)
and [capability matrix](../roadmap-102-capability-matrix.json) define current limits.

Design proposal for [#29](https://github.com/kirakosyan/word.net/issues/29),
2026-10-17. The editing foundation (#11, PR #35) is merged. Its immutable paragraph
snapshots and clone-before-commit sessions do not retain arbitrary review markup.
At that checkpoint `Revisions.Accepted` / `Comments.Ignored` losses and Strict rejection applied.
Implementation then required an explicit change to the deferred-feature policy in
[AGENTS.md](../../AGENTS.md), through a separately scoped implementation PR.

## Decision: four separate capabilities

| Stage | Proposed first contract | Explicit exclusions |
| --- | --- | --- |
| R1: preservation | Opt-in DOCX load/save of plain-text body/cell run insertions/deletions and classic anchored comments, retaining author, initials, optional date and valid IDs/ranges. | DOC/ODT/RTF review markup, modern threaded/resolved comments, people/contact metadata, review markup in fields/controls, cross-story ranges and arbitrary opaque XML. |
| R2: accept/reject | Atomic explicit decisions on R1 edits, and explicit comment removal. Supported current/previous run-format property snapshots can follow in R2b. | Moves, paragraph-mark/row/cell/section structural revisions and unrecognized property changes; these need independent future tasks. |
| R3: recording | Explicit text replacement API emits insert/delete revisions with caller author and clock. | Automatically recording all current editing/template/composition methods, tracking styles/layout/fields/images or synthesizing attribution. |
| R4: comparison | Bounded deterministic text comparison of two supported, revision-free semantic snapshots, generating R1 insert/delete changes. | Word-identical diffs, inferred moves, formatting/structure comparison, legal redline certification or interpreting existing history. |

Do not implement R4 by first flattening both arbitrary documents to plain text. Reject
unsupported structural differences: paragraphs and table-cell topology must align,
with identical supported non-text objects/properties. Compare text only inside matching
paragraph/cell containers. Added/removed paragraphs, merged cells and story changes
require later structural comparison. Output contains the original supported formatting
for retained/deleted text and revised formatting for inserted text.

Use specification-authored managed logic; no diff engine or new runtime dependency
is selected. This keeps all five TFMs and avoids opaque algorithm/resource contracts.
Open XML SDK stays a test validator. External LibreOffice/Word can inspect semantics;
neither is a required service or production dependency.

## Model and identifier validity

Introduce internal `ReviewStore` and stable node identities, copied/remapped by
`DocumentCloner` and composition. Store deleted runs as real bounded content rather
than strings outside admission. A revision has internal ID, kind, author, optional
timestamp and a segment list. Comment bodies have ordinary supported text runs plus
their own IDs and author/date/initials. No author lookup or network contact resolution.
Imported missing dates stay missing; caller clocks apply only to new changes.

An anchor is `(story ID, node ID, UTF-16 offset, affinity)` in a particular session
version. Offsets cannot split surrogate pairs or combining/grapheme sequences when
mutating. Public paragraph indexes are snapshots, not persistent anchors. Expose
opaque immutable handles scoped to a session/version; stale or foreign handles fail
before mutation. Rebase unaffected internal anchors after a commit, invalidate public
handles and return fresh snapshots. Never silently retarget a comment to nearby text.

Validate unique IDs in their OOXML domains, one matching start/end/reference where
required, resolvable bodies and monotonic in-story ranges. Crossing comment ranges
within one paragraph are represented as independent intervals (not a forced XML tree).
R1 revision edits must be nonoverlapping run ranges within a paragraph; nested edits,
orphaned move markers, duplicate IDs, missing comments and unsupported revision forms
fail preservation admission. Unsupported valid forms are feature failures, not
corruption; malformed ranges are invalid data. Composition allocates fresh IDs and
rewrites every reference, or rejects the operation atomically if it cannot remap.

Retain source IDs only as private provenance; writer allocates collision-free IDs.
Serialize deletions with the specified deleted-text markup, insertions with current
text markup, and supported format history with prior property snapshots. Validate with
Open XML SDK plus independent range/text assertions; schema validity alone does not
prove the accepted/rejected text is correct.

## Proposed additive API and behavior

Names below are proposed, unavailable APIs; no existing signature changes:

```text
WordDocument.Load(..., ReviewLoadOptions { Mode = PreserveSupported }, ...)
document.Review.Revisions / Comments          // immutable, bounded snapshots
document.Review.Apply(handles, Accept|Reject, cancellationToken)
document.Review.RemoveComments(handles, cancellationToken)
document.Review.ReplaceText(range, text, ReviewAuthor, cancellationToken)
DocumentComparison.Compare(original, revised, ComparisonOptions, cancellationToken)
    -> new WordDocument + diagnostics; input sessions unchanged
```

Use a distinct overload/options type rather than changing the existing constructor
or default load interpretation. `Apply` validates the entire selection before commit;
accept insertion keeps inserted text, reject insertion removes it; accept deletion
removes deleted text, reject deletion restores it. For R2b accept keeps current format,
reject restores the supported prior snapshot. Moves remain unsupported until paired
move-range semantics are implemented. Accept/reject never removes comments implicitly.
If a decision would consume a comment anchor, reject that decision until the caller
explicitly removes/reanchors the comment in the same transaction.

Preservation loads reject unsupported review forms in every mode, rather than yielding
a partially retained history labelled complete. Existing default loads retain their
current permissive accepted-state behavior. Rendering/conversion from a preserved
session requires an explicit disposable accepted or rejected **view**; it cannot
silently make that decision. Export to a history-incapable destination reports review
loss and Strict rejects; DOCX save preserving all supported history has no such loss.
Ordinary edits touching retained review ranges, repeats/conditions, bookmarks spanning
them and composition through unsupported anchors fail until explicitly review-aware.
Untouched ranges remain valid; no stale overlay can survive an edit silently.

## Comparison semantics and work budget

Default existing-history policy is **RejectIfPresent** for either input, including
comments. Callers can explicitly supply accepted/rejected copies and remove comments
first, with diagnostics; never reinterpret histories inside `Compare`. New comparison
author and timestamp must be caller supplied. Do not reuse the last imported author.

Offer `Word` and `Grapheme` granularity, not code-unit character diffs. Use Unicode 17.0.0 grapheme/word boundary rules with pinned,
reviewed Unicode data and its notices with identical tables on all TFMs rather
than runtime-dependent `StringInfo`. Word mode keeps whitespace/punctuation tokens;
no case folding, Unicode normalization or whitespace collapse by default. Grapheme
mode keeps combining sequences and emoji together. A deterministic bounded sequence
algorithm uses deletion-before-insertion tie breaks and never reports moves.

Proposed defaults: 10,000 reviews/comments combined, 1 MiB review-body text,
1,024 characters per author, 100,000 tokens per comparison and 4,000,000 visited
comparison cells/frontier steps. Each is also capped by aggregate expanded-byte,
character and node budgets across *both* inputs, deleted content and output. No
unbounded quadratic fallback: reaching the work budget fails with a proposed
`Comparison.WorkLimit`, leaving both sessions and output unchanged. Check cancellation
between token batches and algorithm steps; host isolation still owns hard deadlines.

## Privacy, diagnostics and acceptance

History can reveal removed text, names and dates. Preserving it is explicit and is
not a redaction operation. Add an explicit metadata-sanitization transaction (remove
comments, resolve revisions, optionally replace author/date) with a report of actions;
do not claim secure deletion of original bytes/backups/managed memory. Strip unrelated
people/contact/rsid metadata from newly serialized supported history, with a loss
diagnostic if retaining it was requested. Diagnostics carry codes, counts and structural
locations, never deleted text, comment bodies or author values by default.

The [acceptance plan](acceptance.md) separates preservation, accept/reject, recording
and comparison. It includes insert/delete/move/format controls, tables, fields, stories,
overlapping anchors, IDs, surrogate/grapheme boundaries and work exhaustion. The
existing MIT-authored comparison `contract-revisions.docx` is a negative/default
behavior seed, not proof of preservation. A future fixture generator must produce
additional supported and rejected cases independently of production serialization.

Follow-ups are R1 (model, admission, loss-free supported DOCX preservation), R2/R2b
(decisions and format snapshots), R3 (recorded text edits), then R4 (segmentation and
comparison). Each needs its own policy/API approval, regression/independent tests,
five-target package checks, diagnostics, sample, privacy guidance and updated fidelity
documentation before support is advertised. No stage depends on completing all others
except these stated prerequisites.

## References

- [Unicode 17.0 segmentation specification](https://www.unicode.org/versions/Unicode17.0.0/core-spec/chapter-3/) and its UAX #29/data conformance files.
- [Published revision semantics](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-accept-all-revisions-in-a-word-processing-document).
- [MoveFrom](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.movefrom) and [run-property history](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.runpropertieschange).
- [Current container interpretation](../../src/wordnet/Formats/DocxReader.Containers.cs), [editing session](../../src/wordnet/WordDocument.cs), [editing design](../editing-template-design.md).
