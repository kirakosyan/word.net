# P0 independent reference report — 2026-10-19

Issue #46 adds twelve independently authored MIT fixtures to the original twelve-case comparison corpus. Two cases each isolate mixed font families/faces, long tables, footnotes, continuous sections, floating logos and multi-page reports. No converter implementation changes are part of this work.

PR [#83](https://github.com/kirakosyan/word.net/pull/83) delivered this corpus as merged commit `6cf54fd2a406880feb18abb1ee495b8c77bcc3e8`. The measurements below are local Linux x64 working-tree evidence captured during its review, based on candidate `948863bd539e27a3205b86bf1f182ad4449a8f72`; they remain separate from merged-commit or published-package qualification. `Comparison/p0-baseline.json` records manifest, input, font, reference PDF and raw report hashes. The generator reproduces all inputs; existing original fixture bytes are unchanged.

## Independent measurements

All **24/24** cases pass authored required text in reading order, exact authored page counts and exact expected warning codes. Apache POI 5.4.1 independently recovers the required content from all 24 editable DOC exports. The thirteen comparison/capture controls pass, including independently authored PDFs with removed text, an extra page and a 12 pt glyph shift; each mutation fails the corresponding verdict.

LibreOffice Writer **25.2.3.2 520(Build:2)** independently exported all twelve new inputs using an isolated fontconfig catalog with the manifest's Noto Sans 2.008 and Liberation 2.1.5 bytes. Poppler **26.05.0**, Pillow **12.3.0** and the existing .NET benchmark snapshot reader measured both sets. The office PDF files are genuine captured outputs, separate from authored page expectations. Licensed desktop Word and licensed Aspose.Words were unavailable and are not qualified by this run.

Reference page counts match on **12/12**, while **12/12** full office comparisons fail the unchanged strict fidelity policy. This is a defect baseline, not a parity claim. The fixed thresholds remain 0.25 pt glyph baselines, 0.01 pt page dimensions, exact per-page normalized text and glyph sequences, and the existing 96-DPI raster thresholds (0.2% significantly changed pixels, mean channel error 0.25/255).

| Fixture / isolated reproducer | Reference per-page text/order | Maximum glyph displacement (pt) |
| --- | --- | --- |
| `mixed-fonts-short` | pass | 32.189 |
| `mixed-fonts-report` | pass | 32.189 |
| `long-table-ledger` | differs | not comparable |
| `long-table-inventory` | differs | not comparable |
| `footnotes-contract` | differs | not comparable |
| `footnotes-report` | differs | not comparable |
| `continuous-same-width` | pass | 18.239 |
| `continuous-two-columns` | pass | 18.239 |
| `floating-logo-left` | pass | 60.100 |
| `floating-logo-right` | pass | 29.589 |
| `multipage-operations` | pass | 19.363 |
| `multipage-audit` | pass | 19.363 |

## Ranked triage

1. **Reference text/order differs: long tables and footnotes.** All authored required fragments survive, but exact per-page office text/glyph sequences differ. Start with `long-table-ledger.docx` and `footnotes-contract.docx`; their larger companions retain the same feature boundary. Inspect `src/wordnet/Layout/TableSizing.cs`, `src/wordnet/Layout/FixedLayout.cs` and `src/wordnet/Formats/DocxReader.Notes.cs`. Glyph displacement is deliberately not measured across mismatched sequences; a displayed zero in the raw snapshot metric is not a placement pass.
2. **Glyph geometry differs: mixed fonts, continuous sections, floating logos and multi-page reports.** Eight cases retain identical per-page normalized text/glyph sequences but exceed 0.25 pt. The largest displacement is in `floating-logo-left.docx`. The corrected `continuous-two-columns` input changes from one column before the continuous break to two afterward; the initial 243.750 pt measurement used two columns on both sides and does not qualify that transition. Its corrected measurement is 18.239 pt. Each fixture isolates its named feature; begin with the short variants where available. Inspect `src/wordnet/Layout/FixedLayout.cs`, `src/wordnet/Layout/FontResolver.cs` and `src/wordnet/Formats/DocxReader.Drawings.cs`.
3. **Page dimensions and raster differences remain visible.** All engine pages are 595.3 × 841.9 pt; the captured office pages are 595.303937 × 841.889764 pt. The height difference of about 0.010236 pt exceeds the locked 0.01 pt tolerance. The authored `w:h="16838"` twips is exactly 841.9 pt, so the engine matches the input. LibreOffice rounds it to 297 mm (841.889764 pt); this particular verdict reflects reference unit conversion, not a demonstrated converter page-size defect. The tolerance remains unchanged. Raster verdicts fail independently. Neither threshold was relaxed to accommodate this result.

These paths are candidate source locations for investigation, not confirmed root causes. The machine-readable ranked `defects.json` identifies each engine and keeps every fixture/hash, warning-expectation/editable-content/reference verdict and available diagnostic occurrence. Only wordnet findings receive candidate wordnet source paths; external-engine findings have none. `minimalFixture` identifies the actual single-feature input that produced the finding; `smallerRelatedFixture` offers a shorter related case, without asserting an automatically minimized root cause. Converter fixes belong to separately scoped work.

## Reproduce and archive

Follow [the existing comparison instructions](comparison.md) to build the worker, install the pinned test-only Pillow/POI tools and run the full authored corpus. Then capture LibreOffice references and compare the `p0-2026-10` cohort. Expect a nonzero comparison exit when differences remain; `results.json` and `defects.json` retain all verdicts.

CI archives raw office PDFs, reference manifests, engine outputs, snapshots, rasters and ranked findings in **business-comparison**. It requires all twelve office measurements to complete; the comparison step remains explicitly nonblocking because #46 excludes converter fixes. Missing outputs, incomplete reference comparisons, missing/duplicate findings or omitted failed verdicts fail the completion gate; finding counts track actual failures instead of freezing twelve failures as a desired outcome. CI pins its Ubuntu office writer/core package versions to `4:24.2.7-0ubuntu0.24.04.7`, separate from this recorded Debian 25.2.3.2 producer. Exact producer/build hashes are recorded for every capture.

No commercial reference binaries, keys or customer data are committed. The original dated `Comparison/baseline.json` remains historical evidence for its twelve-case manifest.
