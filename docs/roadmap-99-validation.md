# Roadmap #99 validation — 2026-10-25

This receipt qualifies the bounded source changes at
`063cbbd7fc78d876a33ec3a1556db3887faa3bef`. It does not describe a newly published
package. The [implementation profile](roadmap-99-implementation.md) defines the
admitted combinations and rejection boundaries. The [machine-readable receipt](measurements/roadmap-99-final.json)
retains source/assembly/font hashes, raw conversion samples, all comparison verdicts,
targeted before/after/reference coordinates, package identities and test results.

## Executed checks

| Check | Result |
| --- | --- |
| Locked restore; all five library targets | Pass; build has zero warnings/errors |
| Linux x64 .NET 10.0.12 complete suite | 1,111 passed, zero skipped |
| Linux x64 .NET 8.0.32 complete suite | 1,111 passed, zero skipped |
| Isolated local package .NET Standard 2.0 assets on .NET 10 | 75 targeted tests passed, zero skipped |
| Format verification | Pass; the workspace loader emitted its notice, with no formatting errors |
| Core, Fonts and OCR packing/API compatibility | Pass; five core/OCR targets retained; package entries and assembly hashes inspected |
| Four concurrent workers, 32 iterations, all 40 directions | 1,280 conversions with content assertions; pass; peak worker RSS 306,229,248 bytes |
| Malformed-input smoke campaign | 160 isolated cases passed; hosted CI retains its larger 128-per-seed campaign |
| Existing independent chart/MATH/native geometry checks | Pass |
| New independent native table/story and object semantic checks | Pass |
| Pinned PDF/UA-1 validator | Five positive files pass; missing-language negative control fails as intended |
| Pinned PDF/A-2b validator | One positive file passes; missing-metadata negative control fails as intended |

The complete suites used the pinned English OCR data and the Linux x64 native OCR
library, without skipped OCR cases. Tests read DOCX/XLSX with an independent package
reader and validate structural revision markup against the Open XML schema. PDF text,
positions, images and structure are read independently; native shaping/bidi vectors
retain their existing independent controls. The new semantic verifier uses the pinned
P2 Python requirements, including PyMuPDF 1.27.2.2. Raw validator reports are retained
for [PDF/UA](measurements/roadmap-99-ua-verification.xml) and
[PDF/A](measurements/roadmap-99-pdfa-verification.xml).

The local packed files still use the repository's 1.3.0 identity and are explicitly
unpublished build artifacts, separate from the verified public 1.3.0 archives. The
fallback check copied the test output into an isolated directory and replaced the core,
Fonts and OCR assemblies with the exact `netstandard2.0` assets from these local packages.
It exercised every new profile plus PDF reconstruction under the .NET 10 test host.
Windows, ARM64 and .NET Framework execution remain hosted-CI checks, not local claims.

## Comparison results and remaining differences

The final 24-document run used the same Ubuntu 24.04 image, pinned fonts, reference
manifest, editable reader and rasterizer as the [separate source/package baseline](comparison-1.3.0.md).
All 24 authored content/order, page-count, expected-loss and editable-content checks
pass. All 24 independent comparisons still fail one or more original strict checks.
Glyph, page-geometry and raster tolerances were not changed.

| Targeted corpus case | Before | After |
| --- | --- | --- |
| Long-table ledger/inventory | Different per-page text/order | Per-page text/order matches; residual maximum glyph displacements 7.793 / 9.586 pt |
| Mixed-font short/report | Maximum glyph displacement 32.189 pt | 22.666 pt; unspecified font size/spacing still use the existing fallback policy |
| Compatible continuous sections | Maximum glyph displacement 18.239 pt | 15.755 pt; default spacing policy remains different |
| Floating logo, right | Maximum glyph displacement 29.589 pt | 25.449 pt |
| Floating logo, left | Maximum glyph displacement 60.100 pt | 60.100 pt; the under-specified fallback size/spacing still moves following text out of the wrap region |
| Footnote corpus | Different reference-label/text sequence | Difference retained; no universal note-layout equivalence claim |

A zero displacement value when glyph sequences do not match is not a geometry pass.
The receipt retains that distinction. The reference A4 dimensions also differ by more
than the existing 0.01 pt geometry allowance; matching page counts do not remove it.
All other corpus results, including any regressions, remain in the receipt.

The minimal reductions isolate the behavior more precisely:

- `row-note-reservation.docx` uses ten filler lines and two references in an indivisible
  row. Before, the first note remained on page 1 while both references moved to page 2.
  After, both references and notes share page 1, as in the independent reference.
  The separate thirteen-line case verifies the declared policy of moving the row and
  reserving its notes together. The reference instead continues the second note onto
  another page; that policy difference is retained.
- The explicit-size mixed-font reduction removes fallback-size/after-spacing ambiguity.
  Before top baselines are 84, 102, 120 and 138 pt. Actual-font line boxes now produce
  83.759, 98.741, 112.000 and 123.771 pt. The independent office baselines are 83.751,
  98.751, 112.251 and 123.801 pt. The bold line's roughly 0.251 pt residual still exceeds
  the comparison runner's 0.25 pt threshold; PDF/XPS tests separately verify the declared
  font-metric policy at their existing 0.03 pt coordinate allowance.
- An empty compatible continuous final section previously added a second physical page.
  Source and reference now both have one page.
- A kept logo previously left following text at x=30 pt on its destination page; it now
  wraps at x=150 pt, the image's right edge. When following text moves away from the
  logo to a clear page, its stale x=150 pt indentation now resets to x=30 pt. Reference
  coordinates are retained separately (including different default wrap distances).
- Equal/unequal two-column reductions contain six and forty lines. The opt-in balancing
  policy preserves physical page counts and earlier pages and balances the last column
  bottoms within one line. The independent office capture uses sequential final filling;
  it is a policy comparison, not a claimed matching rendering.

## Reproduction and performance limits

Use `eng/run-comparison.py --cohort all --iterations 3` with the frozen final worker and
the baseline reference manifest. The command exits nonzero because the retained fidelity
failures are intentional evidence. The receipt preserves one cold and three warm samples,
allocations, process RSS/high-water RSS, output size and separate worker orchestration time.
The final run had no concurrent builds or tests, but the baseline was collected on a shared
development host during other work. These measurements are observations, not a controlled
speed comparison or a replacement for the hosted single-core performance gate.

Full local reports and outputs are under `artifacts/roadmap-99`, including `final-source`,
`final-worker`, `reductions-before`, `new-features`, `new-references`, `qualification` and
`tests`. CI captures the new native/object qualification outputs and validator reports.
No package publication, release tag, deployment or main-branch merge was performed.
