# Released 1.3.0 and selected-source measurements — 2026-10-25

Roadmap #99 / issue #87 baseline: the public 1.3.0 core/fonts packages and selected
source `c723fb8aa4ed70fda5562da7f472e6fef54740b7` were measured separately on Linux x64,
.NET 10.0.12, using SDK 10.0.401. The source worker was built and snapshotted before
any engine edits. The report's dirty-worktree flag records documentation/work-in-progress
changes in the runner checkout; it does not label those changes as the selected worker.
The [machine-readable receipt](measurements/roadmap-99-baseline.json) identifies every
loaded assembly, pinned font, input, output and reference hash and retains raw timing,
allocation, process RSS, high-water RSS and output-size samples.

All **24/24** documents pass authored text/order, editable-content and exact expected
loss-code checks in each mode. All authored page counts match. All **24/24** independent
reference comparisons retain one or more failures under the existing tolerances.
No thresholds were relaxed. Source and public-package fidelity verdicts agree.
Reference software is test-only; no runtime dependency or platform claim is added.

The subsequent implementation measurements are recorded separately in the
[roadmap validation receipt](roadmap-99-validation.md); this baseline remains unchanged.

## Current findings

| Minimal fixture | Current observation in both modes | Candidate implementation |
| --- | --- | --- |
| `long-table-ledger.docx` | Same two-page count; different per-page text. First continuation starts at entry 030 versus reference 045. | FixedLayout row measurement, font metrics and paragraph spacing; not proof of a row-splitting error. |
| `footnotes-contract.docx` | Same one-page count; different text/glyph sequence and note-reference baseline. | FixedLayout note label/reservation and run appearance. |
| `mixed-fonts-short.docx` | Same text/page count; maximum glyph displacement 32.189 pt. | Font defaults, baseline/line metrics and paragraph spacing. |
| `continuous-same-width.docx` | Same text/page count; maximum glyph displacement 18.239 pt. | FixedLayout section transition and paragraph spacing. |
| `floating-logo-left.docx` | Same text/page count; maximum glyph displacement 60.100 pt; following paragraph starts outside the reference's wrap region. | Drawing anchor origins and exclusion lifetime. |
| `floating-logo-right.docx` | Same text/page count; maximum glyph displacement 29.589 pt. | Drawing anchor and surrounding paragraph metrics. |

The complete ranked list retains every failed verdict, including original cohort
failures and missing optional licensed references. These are measured symptoms and
candidate locations, not accepted fixes. In particular, the short mixed-font fixture
omits font size and paragraph spacing. Reference advances correspond to 11-point
text; the source uses its 12-point fallback and six-point after-spacing. Those default
policy differences must be isolated with explicit-format reproducers before attributing
all displacement to font selection or continuous-section logic.

The reference page's 595.303937 × 841.889764 pt dimensions also differ from the
595.3 × 841.9 pt source page, retaining the 0.01 pt geometry failure. A matching page
count does not erase that result or establish matching placement.

## Performance observations

One fresh worker process per document measured one cold conversion and three warm
conversions, excluding process startup, explicit provider loading and font catalog
setup. `workerWallMilliseconds` separately includes orchestration and extraction.
Raw samples, allocations and process RSS are retained rather than reduced to a score.
These observations were collected on a shared development host while other development
commands ran; they are not a controlled speed comparison or a performance regression gate.

| Worker | Cold conversion range (ms) | Warm conversion range (ms) |
| --- | --- | --- |
| source | 334.1–892.7 | 57.3–296.7 |
| package | 331.2–776.6 | 65.7–302.1 |

## Reproduction and provenance

Use the [existing comparison procedure](comparison.md), the unchanged 24-document
manifest and exact font bytes. Capture all references with `--cohort all` in a private
fontconfig catalog. This run used the recorded office 24.2.7.2 build and Poppler 24.02.0
inside Ubuntu 24.04; Python/Pillow versions and executable hashes are in the receipt.
The local container image identity is
`sha256:a03abadbcfcc881cb6658a821e3008eb97539261fea88556fd8636f35f931972`.

The baseline source worker's complete output directory was copied before edits.
For the second worker only `wordnet.dll` and `wordnet.Fonts.dll` were replaced with
`lib/net10.0/wordnet.dll` and `lib/netstandard2.0/wordnet.Fonts.dll` from the verified
public archives. This is valid for this selected release revision, whose dependency
versions match those packages. The receipt records both loaded binaries; it is not
a generic recipe for substituting future releases with different dependency graphs.
The signed package hashes are recorded in the [release record](releases/1.3.0.md).

Each worker used `eng/run-comparison.py --cohort all --iterations 3`, the captured
reference manifest and the pinned independent editable reader. Both commands returned
nonzero because reference differences remain. Raw source/package reports, PDFs,
editable documents, references and rasters remain under
`artifacts/roadmap-99/{baseline-source,baseline-package,office}`. The compact committed
receipt preserves all verdicts and raw performance samples; full glyph snapshots and
raster pixels remain in that local evidence directory. Existing regression goldens
remain distinct from these independent references.
