# P0 independent reference report — 2026-10-19

Issue #46 adds twelve independently authored MIT fixtures to the original twelve-case comparison corpus. Two cases each isolate mixed font families/faces, long tables, footnotes, continuous sections, floating logos and multi-page reports. No converter implementation changes are part of this work.

This is local Linux x64 working-tree evidence based on `7a09ef6b5d7dfcec8ee9507741b2b78016f14761`, pending PR and separate from published-package qualification. `Comparison/p0-baseline.json` records manifest, input, font, reference PDF and raw report hashes. The generator reproduces all inputs; existing original fixture bytes are unchanged.

## Independent measurements

All **24/24** cases pass authored required text in reading order, exact authored page counts and exact expected warning codes. Apache POI 5.4.1 independently recovers the required content from all 24 editable DOC exports. The five comparison controls pass, including independently authored PDFs with removed text, an extra page and a 12 pt glyph shift; each mutation fails the corresponding verdict.

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
| `continuous-two-columns` | pass | 243.750 |
| `floating-logo-left` | pass | 60.100 |
| `floating-logo-right` | pass | 29.589 |
| `multipage-operations` | pass | 19.363 |
| `multipage-audit` | pass | 19.363 |

## Ranked triage

1. **Reference text/order differs: long tables and footnotes.** All authored required fragments survive, but exact per-page office text/glyph sequences differ. Start with `long-table-ledger.docx` and `footnotes-contract.docx`; their larger companions retain the same feature boundary. Inspect `src/wordnet/Layout/TableSizing.cs`, `src/wordnet/Layout/FixedLayout.cs` and `src/wordnet/Formats/DocxReader.Notes.cs`. Glyph displacement is deliberately not measured across mismatched sequences; a displayed zero in the raw snapshot metric is not a placement pass.
2. **Glyph geometry differs: mixed fonts, continuous sections, floating logos and multi-page reports.** Eight cases retain identical per-page normalized text/glyph sequences but exceed 0.25 pt. The largest displacement is in `continuous-two-columns.docx`. Each fixture isolates its named feature; begin with the short variants where available. Inspect `src/wordnet/Layout/FixedLayout.cs`, `src/wordnet/Layout/FontResolver.cs` and `src/wordnet/Formats/DocxReader.Drawings.cs`.
3. **Page dimensions and raster differences remain visible.** All engine pages are 595.3 × 841.9 pt; the captured office pages are 595.303937 × 841.889764 pt. The height difference of about 0.010236 pt exceeds the locked 0.01 pt tolerance. Raster verdicts fail independently. Neither threshold was relaxed to accommodate this result.

These paths are candidate source locations for investigation, not confirmed root causes. The machine-readable ranked `defects.json` keeps every fixture/hash, reference verdict and available diagnostic occurrence. `minimalFixture` identifies the actual single-feature input that produced the finding; `smallerRelatedFixture` offers a shorter related case, without asserting an automatically minimized root cause. Converter fixes belong to separately scoped work.

## Reproduce and archive

Follow [the existing comparison instructions](comparison.md) to build the worker, install the pinned test-only Pillow/POI tools and run the full authored corpus. Then capture LibreOffice references and compare the `p0-2026-10` cohort. Expect a nonzero comparison exit when differences remain; `results.json` and `defects.json` retain all verdicts.

CI archives raw office PDFs, reference manifests, engine outputs, snapshots, rasters and ranked findings in **business-comparison**. It requires all twelve office measurements to complete; the comparison step remains explicitly nonblocking because #46 excludes converter fixes. Missing outputs or incomplete reference comparisons fail the completion gate. Exact producer/build hashes are recorded for every capture; a different CI office build is separate evidence from this local run.

No commercial reference binaries, keys or customer data are committed. The original dated `Comparison/baseline.json` remains historical evidence for its twelve-case manifest.
