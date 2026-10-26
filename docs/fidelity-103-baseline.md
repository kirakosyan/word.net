# Fidelity baseline refresh - issue #103

Measured on 2026-10-25 for roadmap [#102](https://github.com/kirakosyan/word.net/issues/102).
This evidence-only delivery merged in [PR #131](https://github.com/kirakosyan/word.net/pull/131)
on 2026-10-26 as `ab3843880572f811d84b410e437c86b80f8ff59f` (reviewed head `09825b6`). No converter, API, dependency,
target framework, fidelity tolerance or published package is changed.

The selected source is clean commit `ec7816bf4eabe7a7784d2fb11cb0e9b8e1d6fabd`.
The public flat-container indexes reported **1.4.0** as the highest stable core
version at capture time, and matching core/fonts archives were downloaded. Those
indexes include unlisted versions; the corrected runner now selects the latest
**listed** stable release from NuGet registration metadata instead. The historical
package worker was restored into a fresh cache, then restored again in locked mode.
It inherited central transitive pins; the recorded versions happen to match the
1.4.0 public nuspec minimums. That receipt does not prove isolation from repository
pins for future versions. The corrected runner writes minimal consumer props with
transitive pinning disabled and only BenchmarkDotNet pinned. It restores the public
dependency graph separately. OCR package availability is recorded, but OCR is not exercised
by this managed DOCX-export comparison. This task did not publish those packages.

The [machine-readable receipt](measurements/fidelity-103-baseline.json) contains
public indexes/archive hashes and entries, the package dependency lock, worker and
loaded-assembly hashes, reference/input/font identities, every original verdict,
raw timing/allocation/RSS/output-size samples, commands and log hashes. Embedded-font
assemblies present beside a worker are not claimed as executed when caller-supplied
fonts avoid loading them. The final audit checked actual loaded core/optional-font
hashes and matched package assets to the downloaded archives.

## Results

Both modes ran sequentially on the same Linux x64 Docker desktop VM: SDK 10.0.401,
.NET 10.0.12, Ubuntu 24.04, LibreOffice 24.2.7.2 `420(Build:2)`, pinned writer/core
packages `4:24.2.7-0ubuntu0.24.04.7`, Poppler 24.02.0, Pillow 12.3.0 and Apache POI
5.4.1. Reference capture used the manifest's verified font bytes in a private
fontconfig catalog. Source and public-package page/text/glyph snapshots and reference
verdicts are identical across all 36 documents; their binary identities remain separate.

| Check, per mode | Existing 24-document corpus | 12 new reduced contrasts |
| --- | ---: | ---: |
| Measured with independent reference | 24/24 | 12/12 |
| Authored required text/order | 24/24 | 12/12 |
| Independent editable DOC text/order | 24/24 | 12/12 |
| Authored page count | 24/24 | 12/12 |
| Expected warning-code set | 24/24 | 9/12 |
| Reference page count | 24/24 | 11/12 |
| Reference per-page normalized text/order | 19/24 | 10/12 |
| Strict reference glyph placement | 0/24 | 3/12 |
| Reference page geometry | 0/24 | 0/12 |
| Reference raster policy | 0/24 | 8/12 |
| Any strict reference failure retained | 24/24 | 12/12 |

All four comparison commands correctly exited **1** because differences remain.
Completeness/retention validation passed; a nonzero fidelity result was not changed
to a passing verdict. The original 24 inputs, manifest, expectations and tolerances
are unchanged. The new contrasts also retain their original zero-warning expectations,
so newly observed losses remain failures rather than being accepted after measurement.

## Ranked handoff for L1-L4

Ranks below prioritize investigation; they do not broaden the accepted scope of the
next issue. The full machine receipt also retains every original ranked finding.

| Rank / issue | Reproduction and current finding | Status and next starting point |
| --- | --- | --- |
| 1 / L1 [#104](https://github.com/kirakosyan/word.net/issues/104) | `mixed-fonts-short` still differs by 22.666 pt where glyph sequences match. `defaults-omitted` visibly differs; explicit 11 pt/zero-spacing, document-default and based-on-style controls have identical source snapshots and pass the independent raster policy. Their `first`/`fi` glyph sequences do not align, so none supplies a reduced glyph-displacement measurement for the 22.666 pt finding. | **Raster evidence reproduces a fallback-policy difference; an inheritance defect is unproven.** Before attributing a metric defect in #104, author and independently measure a separate ligature-free `one`/`two`/`three` cohort. Preserve this historical cohort and its failed verdicts. Inspect resolution/default policy only after obtaining that quantitative control. |
| 2 / L2 [#105](https://github.com/kirakosyan/word.net/issues/105) | Original left-logo displacement remains 60.100 pt. `wrap-explicit11` reduces it to 1.324 pt, still above 0.25 pt. Explicit and inherited 6 pt spacing controls agree and pass raster checks. | **Residual placement difference reproduced; general spacing-inheritance failure is unproven.** Inspect anchor-paragraph mark formatting, then `FixedLayout.cs` line placement and `DocxReader.Drawings.cs` exclusions. The explicit anchor mark produces `Docx.ParagraphFormatting`; do not claim that input is fully supported. |
| 3 / L3 [#106](https://github.com/kirakosyan/word.net/issues/106) | Long-table ledger/inventory retain matching per-page text with 7.793/9.586 pt maximum displacement. The small implicit/autofit/fixed controls have agreeing explicit table/grid/cell widths and only two short rows; their convergence (0.100 pt displacement, passing rasters) is expected by construction. | **These controls cannot distinguish width-demanding autofit or row splitting.** The old manifest hypothesis overstates that purpose; new scratch manifests call them convergence controls. Obtain a width-demanding counterexample and a genuinely splitting row before attributing a `TableSizing.cs` defect in #106. |
| 4 / L4 [#107](https://github.com/kirakosyan/word.net/issues/107) | `notes-marked` supplies markers but omits the conventional preserved-space run after `footnoteRef` and the separator/continuationSeparator notes. Its label/text spacing is therefore confounded by unusual source markup. The 60-line continuation retains every line once: source pages 2/3 start at lines 21/41; the reference's page 2 starts at line 41. | **No converter-caused label-spacing defect is established, and no dropped/duplicated note text is reproduced.** #107 needs a separately captured control with conventional spacing/separator markup before attribution. The source's documented 40% note reservation explains three pages versus the reference's two; do not infer a reservation bug from that policy difference. |

Additional findings outside these four slices remain visible, including the nested
invoice (124.900 pt) and column report (172.393 pt), and exact text/glyph differences
in list/revision/text-box/story cases. These are candidate follow-ups, not new support
claims or silently added implementation scope.

## Do not confuse measurement causes

- **Reference-side page-unit rounding:** 595.3 x 841.9 pt exactly matches the authored
  `11906` x `16838` twips divided by 20. LibreOffice quantizes to 1/100 mm, producing
  210.01 x 297.00 mm (approximately 595.303937 x 841.889764 pt). The reference-side
  height change exceeds the unchanged 0.01 pt tolerance and fails all documents.
  The source matches its input; this is not a wordnet page-size defect for #104–#107.
- **Glyph tokenization:** the direct/default/style and spacing reductions contain
  `first`. The source extracts separate `f`, `i` glyphs while the reference extracts
  one `fi` glyph. The existing comparison deliberately refuses to align unlike glyph
  sequences. Its reported maximum of zero is **not** a placement pass; text and raster
  results remain separately visible. Those controls do not quantify the original
  22.666 pt discrepancy. Ligature-free quantitative controls belong to a new #104
  measurement, not a rewrite of the current evidence. No tolerance was changed.
- **Source omissions:** the original short-font inputs omit size/spacing, and the
  original short-note parts omit explicit note markers. These are distinct from
  corruption or a failed inheritance algorithm. The reduced controls expose them.
  The marked-note controls also omit the conventional space after the reference mark
  and separator/continuationSeparator notes, so their label-spacing result does not
  isolate converter behavior from source markup and office footnote-area policy.
- **Diagnosed losses:** `wrap-explicit11` reports `Docx.ParagraphFormatting` on the
  anchor-only paragraph. Both note controls report `Formatting.RunAppearance` for
  superscript appearance. Their expected-warning checks fail visibly in both modes.
- **Different layout policy:** the continuation contrast's authored three-page
  expectation follows the bounded reservation profile. It is not an independent
  office expectation; the two-page reference result is retained as a failure.

## Timing observations

One fresh process per document records one cold and three warm conversions. These
exclude process startup, font-catalog setup, reference extraction, rasterization and
editable DOC export. Worker wall time includes extra work and is stored separately.

| Original 24-document cohort | Cold min / median / max (ms) | Warm min / median / max (ms) |
| --- | --- | --- |
| Selected source | 230.9 / 262.5 / 346.9 | 49.2 / 66.5 / 144.3 |
| Public 1.4.0 | 286.7 / 316.5 / 399.1 | 48.8 / 67.4 / 199.6 |

There was no CPU pinning or controlled host isolation. Source and package worker
directories also use different container/bind-mount paths. Other development and
tool-validation work may run on this shared host. These are observations, not a
speed comparison, regression gate or platform qualification. All individual samples,
managed allocations, process RSS/high-water RSS and output sizes remain in the receipt.

## Reproduce and validate

Use a clean checkout at the chosen source commit, SDK/runtime and dependencies from
[the comparison procedure](comparison.md). The new runner is read-only with respect
to converter sources and refuses an initially dirty or different selected checkout.
Run the following **from the runner checkout** (shown as `/runner/wordnet`), which
contains the new scripts. `/clean/wordnet` is a separate clean measured checkout at
`ec7816b`; it does not contain these scripts. Prepare the POI jars in the runner
checkout and pass their location explicitly. Use an empty evidence output directory:

```sh
python eng/generate-fidelity-reductions.py --output artifacts/fidelity-103/reductions
python eng/prepare-comparison-poi.py artifacts/comparison-poi
python eng/test-fidelity-baseline.py
python eng/refresh-fidelity-baseline.py --repository /clean/wordnet \
  --commit ec7816bf4eabe7a7784d2fb11cb0e9b8e1d6fabd \
  --output /evidence/new-run --iterations 3 \
  --poi-classpath /runner/wordnet/artifacts/comparison-poi \
  --reductions /runner/wordnet/tests/wordnet.Conversion.Tests/Fixtures/Fidelity103/manifest.json
```

The optional generation step writes scratch copies only. The measurement command
above deliberately uses the **committed measured** manifest and archives, not the
scratch copies. The generator now refuses fixture-directory output, defaults to
`artifacts/fidelity-103/reductions`, and uses stored ZIP parts with fixed producer
metadata. New scratch archives are byte-identical across OS/zlib versions; their
hashes and generator identity differ from the historical deflated archives. The
guards compare exact part bytes and check each manifest's own hashes without
changing the measured receipt. New scratch table hypotheses also state the actual
convergence-only scope. Use a scratch manifest for a new cohort/receipt when
changing probes; do not substitute it into this historical evidence.

The runner validates every pinned POI jar **before** network or build work. It selects
the latest listed stable public package from paginated NuGet registration metadata,
retains the listing responses/hashes, and restores a consumer without source
transitive pins. A future run may select a newer package; use this receipt's immutable
archive hashes for the historical identity. It captures all 24 originals plus the 12
contrasts using unchanged comparison machinery, and independently restores the public
consumer. It does not modify the production dependency lock files.

Historical executed validation: clean source and fresh package-worker Release builds with no
warnings/errors; fresh and locked public-consumer restore; all 13 existing comparison
controls; six evidence-guard/generator tests; 12/12 reduced DOCX packages validated
with Open XML SDK 3.5.1, Office 2013 schema; reference/loaded-binary/public-asset/hash
and complete-failure-retention audits. Representative original/reduced renderings were
visually inspected. The first executed runner was archived by its exact hash; added
binary-identity guards were then exercised against every retained raw result. CI now
runs twenty evidence-guard tests alongside the thirteen comparison controls.

Reproduce every published summary count and timing minimum/median/maximum without
rerunning conversions or changing the receipt:

```sh
python eng/refresh-fidelity-baseline.py --summarize docs/measurements/fidelity-103-baseline.json
```

The current runner also writes `summary`, verifies retained raw-report/reference/
package-asset hashes for `audit`, and records observed host/runtime information in
`environment` on every new measurement. It explicitly reports the separate Open XML
schema validator as **not run** instead of claiming the historical manual validation.
The historical `runnerSha256` identifies the original executed script; the separate
`finalVerifierImplementationSha256` identifies the later verifier used for that audit.
Those historical bytes, timestamps and manual schema/environment receipts remain
unchanged. The summary command reproduces derived numbers, not an earlier host or
timing sample; a fresh run records its own current tool identity and observations.

The local evidence root is `artifacts/issue103/`: it retains raw reports, PDFs, editable
DOCs, rasters, public indexes/archives, exact executed tooling, validation logs and
source/package worker binaries. These generated artifacts are excluded from Git and
packages. The committed compact receipt retains all verdicts and raw performance
samples; full glyph snapshots and pixels remain in that evidence root. Full library
conversion/OCR suites and other runtimes were not rerun locally for this evidence-only
change; PR CI supplies its own exact-head results. No new runtime support, production
feature, release or package publication is claimed.
