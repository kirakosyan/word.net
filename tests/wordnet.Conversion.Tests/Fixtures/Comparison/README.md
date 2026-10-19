# Authored comparison corpus

Twenty-four distinct synthetic business documents, independently authored under the repository MIT license by `eng/generate-comparison-corpus.py`. No customer source, Office/Aspose code or proprietary resources were used. ZIP timestamps are fixed.

The original twelve cases cover invoices, contracts, reports and letterheads. The `p0-2026-10` cohort adds two cases each for mixed font families/faces, long tables, footnotes, continuous sections, floating logos and multi-page reports. Each fixture isolates one requested feature; short variants are triage reproducers for the larger cases.

The manifest records input/generator hashes, provenance, text in reading order, authored page expectations, pinned font hashes and exact expected diagnostics. Authored expectations are never office measurements. Noto Sans 2.008 and Liberation 2.1.5 retain their existing SIL OFL 1.1 notices/provenance. Every input is independently schema checked with Open XML SDK in the normal suite.

`baseline.json` retains the original dated twelve-case free run. The [P0 report](../../../../docs/comparison-p0.md) records the expanded cohort and independent LibreOffice reference results. Raw office PDFs, references, glyph snapshots, rasters and ranked defects are archived in CI's `business-comparison` artifact. Missing licensed desktop Word/Aspose references remain explicit, without parity claims.

See [reproduction and interpretation](../../../../docs/comparison.md).
