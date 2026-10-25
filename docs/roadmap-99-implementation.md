# Roadmap #99: 1.4.0 profile

These 1.4.0 changes extend the released 1.3.0 profiles. See the
[1.4.0 release notes](releases/1.4.0.md) for compatibility and release qualification. The original release receipt remains in [1.3.0](releases/1.3.0.md).
The release/source baseline, including unsuccessful fidelity checks, is retained in
[comparison-1.3.0.md](comparison-1.3.0.md).

## Delivered issue slices

| Issue | Source change and acceptance evidence |
| --- | --- |
| #87 | Separate 24-document public-package and frozen-source measurements; exact identities, raw samples and all failed reference verdicts in the baseline receipt. |
| #88 | Contiguous row geometry and aggregate note reservation; table/merged-row/note-continuation regressions, independent per-page comparison and multiple-note reproduction. |
| #89 | Actual resolved-face line metrics; pinned regular/bold/italic PDF/XPS reduction, explicit spacing/wrapping/cell regression coverage and retained raster measurements. |
| #90 | Compatible empty continuous section fix; bounded opt-in equal/unequal final-column balancing with short/multipage geometry and explicit rejection controls. |
| #91 | Position-dependent wrap remeasurement after page/column moves; kept-logo and cleared-next-page geometry, existing anchor/crop/wrap/paint-order controls. |
| #92 | Immutable grouped-report chart binding; independent DOCX/XLSX data, PDF text and SVG proportions, atomic invalid-input controls. |
| #93 | Arabic/Hindi simple table cells and paragraph stories; independent shaping/bidi vectors retained, multipage logical ownership and PDF/UA validation for all four language/story combinations. |
| #94 | Managed chart/equation tagged/UA and separate archival profiles; semantic alternatives, independent PDF validators and deliberately malformed negative controls. |
| #95 | Bounded DOCPROPERTY/IF/arithmetic evaluation in editable DOCX and PDF/XPS; cached-result/error controls. STYLEREF is separately specified below as requested by the staged issue. |
| #96 | Bounded paragraph/row alignment and genuine structural DOCX markup; independent schema validation, reload/accept/reject, identifier remapping and protected-section controls. |
| #97 | Direct HTML/ODT PDF readers-to-model pipeline; independent text, geometry, image and atomicity checks, all 40 directions in concurrency qualification. |
| #98 | Verified 1.3.0 release/tag/public archives and reconciled current-facing documentation without changing historical qualification. |

## Layout (#88–#91)

Managed line boxes use the actual resolved OpenType face. `USE_TYPO_METRICS` selects
OS/2 typographic ascender, descender and line gap; otherwise the horizontal header
metrics apply. Regular, bold and italic runs contribute their own metrics to a shared
baseline. Empty lines use the paragraph's effective run size. Explicit spacing still
cannot silently clip content. Native shaping retains its separately qualified line-box
policy. This changes pagination: consumers that compare generated pages with a 1.3.0
snapshot should expect different baselines and row counts.

Adjacent table rows share an edge. The former six-point inter-row gap was not a source
cell margin and has been removed. Actual cell padding, minimum/exact row heights,
vertical merges and repeated headers remain part of the layout. Notes in an indivisible
row/group are reserved together so moving the row cannot leave an earlier note behind.
Splittable rows retain line-band continuation. Oversized indivisible groups remain
explicitly unsupported; lenient fallbacks keep their existing diagnostics.

Position-dependent paragraph preparation is refreshed after a keep group or page move.
The original list counter is reused during remeasurement. Square/top-bottom exclusions
continue to include the declared wrap distances; top-bottom spacers also participate in
streamed unequal-column flow. Page/margin/column/paragraph origins, cropping, image bytes,
paint order and strict physical-page bounds retain their existing contracts.

`ConversionOptions.BalanceFinalColumns = true` opts into balancing final multicolumn
pages. It searches for a shorter final frame without increasing physical page count, using
at most 16 whole-document bisection probes across all sections and one final layout,
a 0.01-point stopping interval, at most 16 column sections, 1,000 body blocks and
200,000 characters. Probes retain full line sizes in every fidelity mode. The search
uses unscaled line metrics as its lower bound; coupled section/page limits can produce
a conservative result, and the original layout is retained if final page allocation changes.
Earlier full pages retain their frames.
Equal and explicitly unequal widths are supported. The option rejects tables, notes,
keeps, explicit page/column breaks, body fields/links/lists/controls, images/objects, tagged
output, native typography and continuous
sections sharing a multicolumn physical page. The default preserves sequential filling.
Those combinations still use the ordinary existing layout when balancing is disabled.
Compatible continuous sections retain a shared page and start below the preceding body;
an empty compatible continuous final section no longer introduces a blank physical page;
incompatible physical geometry retains its explicit diagnostic/new-page policy. Odd/even
sections and page labels retain their existing behavior.

The independent office capture uses sequential final-column filling and may continue
notes onto the following page. Those policy differences are recorded separately from
metric and anchor errors. Neither the new balancing policy nor keeping a row's notes
together is a claim of identical pagination across renderers.

## Binding chart data (#92)

`WordDocument.BindChart` projects an immutable `ReportData` snapshot into one existing
chart. Each binding names a root collection, a row-relative category string, a root
series-name string, and a row-relative nullable numeric value. Paths are case-sensitive,
dotted ASCII names; they do not execute expressions or fetch resources.

```csharp
var binding = new ChartDataBinding("Groups", "Key", new[]
{
    new ChartSeriesBinding("SeriesName", "Total")
});
DocumentObject updated = document.BindChart(document.Objects[0], report, binding, token);
document.Save(output, DocumentFormat.Docx, cancellationToken: token);
```

Binding retains the chart kind, dimensions, location, description, axes, colours and title.
It atomically rebuilds the immutable definition, chart caches and embedded spreadsheet.
The series count must match the existing chart (1–16); at most 10,000 total points are
admitted. Missing paths, non-string labels, nonnumeric/nonfinite values, stale/foreign
handles, cancellation and resource failures leave the document unchanged. Null is a gap;
zero and negative values remain numbers. The returned handle supersedes the old handle.

Charts inside repeat/control owners remain rejected. Ordinary template `Merge` does not
implicitly bind or duplicate objects: prepare grouped report data, merge an ordinary
text/table document, then add/bind the chart as a separate explicit transaction. See
`ChartBindingTests` for a grouped report with independently read caches, workbook cells,
PDF text and SVG bar proportions.

## Native tables and stories (#93)

The qualified Arabic/Latin and Hindi shaping paths now admit simple, unmerged, nonnested
single-column tables, repeated headers and ordinary paragraph headers/footers. Logical
text remains separate from visible glyph outlines. Table cells own their logical content;
repeated headers and page stories remain artifacts in tagged output. Wrapping does not
split a shaping cluster. Native columns, notes, review history, objects, table controls,
merged cells, nested tables and structured story content remain outside this extension.

The new combinations run in the full source and package desktop suites. Release
qualification requires Windows/Linux x64/ARM64 and the actual Framework 4.7.2
configuration; compilation alone does not qualify an architecture. See the
[release evidence requirements](releases/1.4.0.md).

## Object PDF profiles (#94)

Managed supported charts and bounded equations can be emitted as tagged PDF/PDF/UA-1,
or separately as PDF/A-2b. A nonblank caller description is mandatory for tagged objects.
Chart alternatives include category labels, series names and every value, distinguishing
null gaps from zero. Nonempty title and axis labels are included. Equations include the
expression's logical description, with at most 256 nodes and 2,048 text characters.
Alternatives are bounded to 65,536 characters and the conversion character limit.

A single owned logical carrier represents each object as `Figure` or `Formula`; visible
vector text/shapes are artifacts. This avoids duplicate reading content and preserves
vector rendering. Independent PDF/UA-1 and PDF/A-2b validation supplements semantic,
structure, extraction and rendering checks. Validation alone does not assess whether a
caller description is useful to a reader. Missing required metadata or descriptions,
native-typography/object combinations, and combined archival/accessibility requests
remain rejected before caller output is written.

The source combination matrix is deliberately narrower than the Cartesian product:

| Managed supported objects | Ordinary PDF | Tagged | PDF/UA-1 | PDF/A-2b | PDF/A + accessibility | Native typography |
| --- | --- | --- | --- | --- | --- | --- |
| Clustered column / ordinary line | Yes | Description + complete data alternative | Same + required title/language | Separately validated | Rejected | Rejected |
| Bounded MATH equation | Yes | Description + bounded logical expression | Same + required title/language | Separately validated | Rejected | Rejected |

The released [P2 matrix](p2-capability-matrix.json) remains the 1.3.0 receipt; this table
identifies the 1.4.0 extension instead of retroactively changing that release.

## Scalar fields (#95)

The bounded evaluator supports:

- `DOCPROPERTY Title` and `DOCPROPERTY Author` (names are case-insensitive and may be quoted).
- `IF` with two invariant decimal operands or two quoted strings, one of `= <> < > <= >=`,
  and two quoted result strings. String comparison is ordinal and case-sensitive;
  wildcard patterns are outside this profile.
- `=` formulas containing checked decimal literals, unary signs, parentheses and `+ - * /`.
  Display uses at most 15 significant digits, with insignificant trailing zeros removed;
  this bounded display policy does not implement Word numeric-picture switches.
- Optional trailing `\* MERGEFORMAT`; no other formatting switches are inferred.

Evaluation uses the current document metadata and the existing managed body, cell and
ordinary story field pipeline; it does not enable fields in native typography. Results
remain plain scalar text. No date type or date-format switch is admitted.

Instructions are limited to 4,096 characters, 256 tokens, and expression depth 16 or the
smaller configured nesting limit. Missing properties, unsupported syntax, overflow and
division by zero use the informational `Field.UnsupportedUsedCachedResult` diagnostic
and retain the source cache in every fidelity mode, including Strict. No script, macro,
spreadsheet engine, cell reference, external lookup, date inference or culture-sensitive number conversion runs.
Nested instructions and formatted or multiparagraph scalar caches remain cached text.
Nested instructions are flattened without emitting a synthetic live field or hyperlink;
their discarded instruction fragments are never partially evaluated. Existing page, reference,
sequence and table-of-contents evaluation is retained.

`STYLEREF` remains a separate staged design: an initial profile would resolve an explicit
paragraph style identifier within the current section, select the nearest preceding
matching ordinary paragraph (then a following match only under an explicit policy), and
re-evaluate page-sensitive header/footer uses after pagination. It must define style
identity preservation, no-match caches, scope, switches, cycles and convergence before
admission. This change does not silently approximate `STYLEREF` as a title or heading.

## Structural comparison (#96)

The existing matching-structure text comparison path is retained. Structural alignment
extends it to ordinary body-paragraph and simple table-row insertions/deletions in a
single section. Protected stories, metadata and section geometry must match. Table count,
properties and repeated headers must match; merged/nested/controlled/individually sized
rows, object-bearing content, lists and formatting revisions remain rejected.

Alignment admits at most 10,000 combined blocks/rows (or the smaller token allowance),
serializes each document once for its block/row signatures, interns exact signatures
for constant-time cell comparisons, and uses a bounded
longest-common-subsequence table. It preserves the earliest equal
matches, and chooses deletion first on equal costs. Structural items share the configured
token/work allowance and the expanded-memory budget. Unmatched content appearing on both
sides is rejected as an ambiguous move. Equal text with changed properties is rejected as
a formatting revision. Empty and repeated paragraphs have deterministic placement.

DOCX uses paragraph-mark `w:ins`/`w:del` inside `w:pPr/w:rPr`, tracked paragraph text with
separate unique identifiers, and row markers inside `w:trPr`. `DocumentReviewItem.Structure`
is `paragraph`, `row`, or empty for a text record/comment. The importer preserves only
whole-paragraph structural changes; partial paragraph split/join semantics remain rejected.
Accept/reject views restore the supported revised/original structures. Consuming a
structural owner also consumes its owned text records. Selecting its text revision
likewise decides the whole paragraph, and consuming the final row removes the empty
table. Intersecting comments require an explicit removal decision. Without review
preservation, BestEffort conversion retains its structural-mark omission diagnostic;
with preservation, unsupported story/nested owners fail explicitly as `Review.Unsupported`.
Input sessions are unchanged and edits invalidate old handles.

## Direct PDF conversion (#97)

`CanConvert(Html, Pdf)` and `CanConvert(Odt, Pdf)` are enabled. Both use their existing
bounded reader, the shared document model, and the PDF writer directly. No temporary DOCX
or external conversion process is used. Lists, supported tables, page settings, ordinary
stories and bounded embedded/caller-resolved images follow the existing input profiles.
Unsupported source semantics keep their diagnostic codes/locations and Strict atomicity.
PDF import lazily resolves and parses its four managed font metric faces once per
import, sharing them across pages without loading kerning tables.
Preflight still distinguishes predicted losses from actual conversion and independent
validation. Stream ownership, current stream position, cancellation and byte/page/text
limits are unchanged. The conversion matrix now has 40 enabled directions.

## Evidence and release boundary

See the [validation receipt](roadmap-99-validation.md) for exact executed checks,
before/after reductions and the complete retained comparison failures. The
[review corrections and release assessment](roadmap-99-review.md) supplement that
historical receipt.

The measurement receipt distinguishes the published package, frozen pre-change source,
and modified source. All failures remain visible at the original comparison tolerances.
The fixture generator's independent line-box arithmetic was updated from the former
fixed-ratio policy to the pinned font's actual metrics; unchanged glyph and raster
thresholds continue to apply. Raster baseline updates are reviewed artifacts, not evidence
that arbitrary documents match an independent renderer.

The implementation covers numbered child issues #87–#98. The owner subsequently
authorized merging and 1.4.0 publication after successful release gates. The unnumbered
P3 proposal in #99 remains a later planning item and is not part of 1.4.0.
