# Editing and template API decisions

The [P2 control contract](content-controls-design.md) and [implemented source profile](content-controls.md)
add an opt-in typed-control path in PR #134. Existing template-only loading below
keeps its contract; typed tags never silently acquire executable template meaning.
The bounded implementation is included in the published 1.5.0 packages.

PR #135 extends the existing `Merge` contract to bounded mixed and one-level
master-detail body regions and adds cross-tab and explicit fragment insertion APIs.
See the [usage profile](editing-and-templates.md#mixed-report-regions--merged-source)
for scope lookup, cloning, atomicity, ownership and limits.

This records the implementation contract for roadmap #32, bucket 3 (#11–#15),
before implementation. The existing converter and all five targets remain compatible.

`WordDocument` is a document session over the internal semantic model. It supports
create/load, metadata, immutable paragraph/run snapshots, appending styled paragraphs,
simple tables and inline images, find/replace, bookmark edits, document append and
template execution. Public value types describe supported text and images; internal
XML, layout and provider types never cross the public boundary. Loading supports the
existing readable formats; saving supports the existing writers, including DOCX to
DOCX editing. This is a bounded editing profile, not a lossless OOXML DOM.

Every operation on one session is serialized. Inputs and public snapshots are copied;
documents never share mutable model state. Mutations work on a deep copy and commit
only after cancellation and aggregate character/image/structure admission checks.
Saving uses another copy because fidelity checks and pagination may mutate their
working model. Caller streams remain open, and output is staged before copying.
Original load warnings remain visible on later results; Strict save rejects earlier
detected losses. Separate sessions can be used concurrently. Caller data/options
must remain stable during a call. Session operations are synchronous; the existing
converter's asynchronous I/O contract is unchanged.

For report generation, `AddParagraphs` batches caller text paragraphs in one copied
transaction, with incremental admission during enumeration and one final validation.
This avoids quadratic whole-model copying from repeated individual appends while
retaining cancellation, serialized callbacks and atomic failure. Individual edits
keep their existing copy-on-edit semantics. Literal replacement has cancellation
and resource bounds without a fixed wall-clock deadline, and traverses run slices
once; regex operations retain their explicit finite matching budget.

Literal and timeout-bounded regex replacements are leftmost, non-overlapping,
paragraph-local and story-local. Fields, bookmarks, images, notes and boxes are
barriers. Replacement text is literal, inherits the first matched run's formatting,
and retains untouched prefix/suffix styles. Empty searches and zero-length regex
matches are rejected. Matches cannot bisect UTF-16 surrogate pairs. Bookmarks expose
named ranges; text/image insertion or replacement retains valid boundary markers.
Unsupported cross-story or structurally ambiguous bookmark edits fail atomically.

Composition deep-copies source sections/stories and remaps list identities, bookmark
names/references and note identifiers. Keep-source retains resolved run/paragraph
styles. Destination-style maps appended paragraphs to the destination's first
paragraph with the same heading role, while preserving explicit images/fields and
source section geometry. Appends start a new section/page. No source document is
modified; unsupported source features keep their diagnostics.

Editing loads retain MERGEFIELD instructions and tagged content controls, while
ordinary conversion continues to use their cached content. Template tags are
`merge:Path`, `image:Path`, `if:Path` (or `if:!Path`) and `repeat:Path` on one table
row. Scalar controls occupy one paragraph or an inline run group. Conditions may
wrap blocks or inline content. Repeats may nest in nested tables within configured
depth and row/character/resource limits. Empty collections remove the marked row.
Horizontal cell spans are preserved; vertically merged repeat rows are explicitly
unsupported. Cloned bookmarks/notes are remapped, and numbering continues within a
repeated region. Evaluation order is repeat, condition, then scalar/image binding.

Data is a caller-supplied dictionary, an explicit snapshot of public object
properties, or bounded JSON. Paths use case-sensitive identifiers separated by dots;
item scope precedes root scope. No method calls, indexers, code expressions, implicit
files, databases or network reads are supported. Object property getters execute
only while taking the caller-requested snapshot. Scalar types are strings, booleans,
finite numbers, dates, GUIDs and null (empty text). Culture is explicit and defaults
to invariant. MERGEFIELD supports quoted names and date/number switches, plus
MERGEFORMAT. Missing data defaults to an error, with explicit keep-cache/empty
policies. Image values contain copied caller bytes and physical dimensions.
Diagnostics identify feature codes/locations without including data values.

Template syntax, JSON, regex execution, nesting and expansion have finite bounds.
The evaluator is deterministic and has no scripting engine. Invalid syntax,
ambiguous ranges, resource failures or cancellation leave the session unchanged.
Verification uses independently authored OOXML, Open XML schema validation, PDF
content/geometry, the existing conversion/OCR suites and a standalone invoice and
composition sample. New APIs must match across framework assets during packing.
