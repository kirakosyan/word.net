# Bucket 4 implementation handoff

Prepared 2026-10-17 for continuation in the originating Codex chat. This is a work
handoff, not an implementation or release claim. All seven bucket-4 issues remain
open. The architecture contract is [interoperability-design.md](interoperability-design.md).

## Verified starting state

- PR #35 was squash-merged as `0b2af843a40b8c866119594a887e8dac1f5c7340` after all
  15 review findings were fixed, replied to and resolved. All 20 CI checks passed
  on expected head `383ea15f1bb21d96f38a61a2eaee42980cae0124`; its tree equals the
  merge tree. The 32 review regressions bring the editing profile to 86 and the
  complete required suite to 696 cases. Both local Linux x64 .NET 8/10 suites pass
  without skips, as do all-target builds, formatting and package/API validation.
- Issues #11–#15 and #17 have all verified acceptance criteria checked and completed
  closure confirmed. Roadmap #32 records completed buckets 1–3. The seven earlier
  bucket-2 issues stay closed; do not repeat their implementation.
- Documentation reconciliation is in PR [#36](https://github.com/kirakosyan/word.net/pull/36),
  branch `codex/reconcile-editing-delivery`, head
  `109df080c3a273d1e7aaf4ecc8604f64081796b9`. It updates README, architecture,
  diagnostics, usage, implementation plan, revision-specific testing and the public
  sample instructions. All 93 relative documentation links resolve, diff/consistency
  checks pass, and no code/dependencies change. Refresh its review/CI and merge state
  before continuing; do not infer a completed review from an empty review list.
- `codex/interoperability-typography-import` starts at the #35 merge and contains
  the architecture contract plus this handoff. Fetch/fast-forward main and incorporate
  #36's documentation when merged. No bucket-4 source code or tests have been added.
  `eng/required-tests.txt` remains 696. Package 1.1.0 is separate from these unreleased
  source increments; nothing here publishes packages or changes credentials.

## Ordered work and acceptance

| Issues | Implementation and independent acceptance |
| --- | --- |
| #20 | Safe HTML/CSS input, normal DOC/DOCX directions and WordDocument loading; explicit CSS cascade/units/whitespace, caller/data images, merged/nested tables. Independent web-editor fixtures, schema validation and hostile no-fetch/size/depth controls. |
| #21 | Own bounded specification-based RTF reader/writer, code pages/Unicode fallback, formatting/lists/basic tables/raster pict. Independent producer fixtures and reader validation, malformed bin/group/expansion tests, enum/CanConvert/provenance/docs. |
| #22 | Optional shaping/provider bridge, bounded Arabic/mixed bidi, grapheme fallback, line breaks and searchable logical Unicode. Compare real glyph placement to the independent shaper; extraction/search is an unresolved prototype gate. Preserve the managed Latin path; no broad script/platform qualification from compilation. |
| #23 | Source semantics through placement, tagged PDF structure/parent trees/MCIDs/OBJR, headings/lists/tables/headers/alt/language and artifacts. Separate tagged and bounded UA-1 modes; pinned independent validator and documented manual reading-order review. Preserve PDF/A, links, embedding and determinism. |
| #25 | Physical-page selection after full layout, preserved total/source page fields and labels, remapped destinations. Managed safe SVG and optional-provider PNG; independent content/pixel/aspect/crop checks, limits/cancellation/concurrency. |
| #26 | Optional-provider opt-in digital reconstruction: known columns, paragraph grouping and simple ruled tables, supported images once, ambiguity fallbacks. Independent ground-truth PDFs, editable cells/order/geometry, always reconstruction-loss disclosure; default unchanged. |
| #27 | Opt-in bounded immutable OCR regions/confidence/UTF-16 spans; selected-candidate TSV, exact inverse orientation/deskew transforms to original top-left point coordinates. Actual clean/noisy/multipage/rotated/deskew/mixed fixtures plus Unicode/empty/numeric/ownership controls. English-only and default output remain. |

Read the live issue bodies and AGENTS/README/architecture/fidelity/testing again.
Implement and validate all seven bounded profiles before the feature PR is declared
ready. Keep partial acceptance open. Bucket 5 (#28–#31) is design-only. After every
merge perform the standing issue/roadmap/documentation reconciliation.

## Provider and tool findings

These are research observations on Linux x64, not qualification of implemented
bucket-4 functionality. Temporary probe files are outside the repository and must
not become the only reproduction/evidence for a delivered feature.

- AngleSharp 1.8.4 already parses without loaders/scripts. EbookContent supports
  many semantic primitives but currently flattens nested tables, supports limited
  inline CSS and has no normal HTML input dispatch. Do not silently inherit those
  losses while advertising a wider import profile.
- Optional wordnet.Ocr already resolves SkiaSharp/SkiaSharp.HarfBuzz 4.153.1 and
  HarfBuzzSharp/native assets 14.2.1.301, including a net462 managed asset. Existing
  THIRD_PARTY_NOTICES records binding licenses and native-notice obligations.
  The new native typography/raster work must stay explicit and optional; core
  must not load host fonts or native engines implicitly.
- HarfBuzzSharp shapes `مَرْحَبًا` with NotoSansArabic-Regular at 1000 units/em.
  Test-only uharfbuzz 0.53.0 / native HarfBuzz 12.1.0 independently returned the same
  nine glyph IDs, clusters, advances and offsets. Font SHA-256:
  `504d7407d86875acf7d04dfaa0fd7524d0b8797723bc4aa18022f29db25b0b6e`.
  This host font is not committed; add a licensed pinned fixture/provenance before
  relying on it in tests. The observed tuples `(glyph, cluster, xAdvance, xOffset,
  yOffset)` were `(47,8,291,0,0)`, `(299,6,0,-7,-174)`, `(104,6,343,0,0)`,
  `(291,4,0,4,-106)`, `(417,4,549,0,0)`, `(1217,2,0,35,-107)`,
  `(979,2,396,0,0)`, `(291,0,0,142,-100)`, `(771,0,525,0,0)`.
- Skia exposes actual glyph outlines; a temporary PdfPig path prototype renders
  them. Its invisible logical text/ActualText mapping did **not** pass MuPDF
  extraction/search: mark ordering and geometric reassignment remain wrong.
  A valid PDF string must use escaped UTF-16BE/hex, not raw unescaped UTF-16LE
  StringToken data. Neither a successful shape comparison nor a visible outline
  satisfies searchable Arabic acceptance. Rework the mapping before adoption.
- PdfPig 0.1.16's public TokenWriter extensions include virtual object writing and
  cross-reference writing. They permit a bounded managed tagged-output strategy
  without private reflection; actual object IDs/pages must be discovered, never
  guessed. No tagged-output implementation has been validated yet.
- Pinned veraPDF CLI 1.30.2 image is locally available:
  `verapdf/cli:v1.30.2@sha256:d5ee329657cf9bc4b2400392dd54c7d0a0ce9980ff6fa2da5590eebeec007cdb`.
  Offline help/UA-1 profile availability was checked. No bucket-4 PDF has passed it.
  Use the local Docker socket explicitly and clear stale Docker endpoint/TLS
  selectors per runtime instructions; preserve registry configuration. No root or
  daemon restart is required. Existing PDF/A reproduction is eng/validate-pdfa.ps1.
- Independent RTF production/reading is available through LibreOfficeDev
  26.8.0.0.alpha0, build `2c87e51eeaa2b413ff4ae097b2705eea1995d8e5`.
  Use unique temporary UserInstallation profiles; record that this is a development
  build. MuPDF 1.26.11/PyMuPDF 1.26.6 are separately installed qualification tools,
  not production package dependencies. Review tool licensing before redistribution.

## Execution guidance

The originating workspace is `/workspace/word.net`. RTK is
`/workspace/.tools/bin/rtk`; .NET SDK 10.0.401 is under `/workspace/.tools/dotnet`;
installed test runtimes are 8.0.32 and 10.0.12. Add those two tool directories to PATH.
Global NuGet packages are under `/home/agent/.nuget/packages`; use `rg --files --hidden`
when inspecting that cache. PowerShell is installed. No subagents were requested.

Use `-m:1` for the all-target build to avoid memory pressure. Provision OCR and run
each complete suite in the same PowerShell process with OMP_THREAD_LIMIT=1. Preserve
all five API assets, perform locked restore and package/API inspection, and inspect
the final diff. Tests must assert real independent output rather than file existence
or matching round-trip bugs. Current platform evidence does not qualify actual
Framework 4.6.2, desktop Word/Aspose, Android, macOS or trimming/AOT.

Create and attach the bucket-4 PR only when implementation and independent validation
are ready; describe all residual limits and use closing references only for fully
delivered issues. PR #36 is documentation reconciliation, not that feature PR.
