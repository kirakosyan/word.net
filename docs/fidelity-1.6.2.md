# Released 1.6.2 fidelity baseline

On 20 November 2026, all 40 inputs retained their authored text, reading order
and page count in managed PDF. Apache POI also recovered the required text in
order from all 40 editable DOC outputs. For each input, the clean source build and
the public package produced identical PDF bytes and comparison verdicts. Every
input had at least one difference from the independent LibreOffice rendering.

| Passing checks per build | Original corpus (24) | Reduced inputs (12) | Theme/table combinations (4) |
| --- | ---: | ---: | ---: |
| Expected diagnostic set | 24 | 10 | 2 |
| Reference page count | 24 | 11 | 4 |
| Reference text and page order | 19 | 11 | 4 |
| Reference glyph placement | 7 | 2 | 4 |
| Reference page geometry | 0 | 0 | 0 |
| Reference raster | 12 | 9 | 4 |

The [ranked ledger](measurements/fidelity-1.6.2/findings.tsv) lists all 186 failed
verdicts across the two builds. All six comparison commands returned 1. The
[capture](measurements/fidelity-1.6.2/capture.zip) contains the input manifests,
complete result and defect reports, independent glyph snapshots, raw performance
samples, tool and assembly identities, commands and separate schema/workflow
checks. The [profile matrix](roadmap-138-source-matrix.json) maps individual
feature combinations and exclusions to executable tests.

## What changed since 1.5.0

- **Theme fonts and conditional shading.** All four combinations of explicit/theme
  font and direct/first-row shading now pass the glyph and raster checks. Maximum
  aligned-glyph displacement was 0.100 pt in each case. The historical theme
  controls failed glyph placement, and three of the four inputs failed raster
  comparison. Those specific reference differences are resolved for these fixtures.
  The two theme inputs still emit `Docx.ThemeFont` and fail their authored
  zero-warning expectation. The ledger classifies these four source/package warning
  verdicts as documented unsupported theme semantics. [Original four-way control](measurements/roadmap-138-combination-baseline/factor-matrix.json)
- **Fixed table.** The `table-fixed` reduction passed both glyph and raster
  comparison in 1.5.0 and fails both in 1.6.2. This is a reproduced change in the
  reference comparison. Its cause and behavior in desktop Word remain unknown.
  [Historical 1.5.0 baseline](fidelity-138-initial.md)
- **Note continuation.** `notes-continuation` produced three pages against the
  reference's two. It and `notes-marked` also produced an unexpected
  `Formatting.RunAppearance` warning. The ledger keeps these findings individually.
- **Page geometry.** For 39 inputs per build, all paired pages match the historical
  A4 rounding pattern of 841.900000 pt versus 841.889764 pt in LibreOffice. The
  0.010236 pt difference exceeds the original 0.01 pt tolerance. Only these 78
  verdicts are classified as reference-policy differences. The 104 remaining
  failed verdicts have unknown causes, including geometry when page counts differ.

## Office-derived controls, fragments and review

A separate 40-case Linux .NET 10 run passed against release source and again
against public 1.6.2. These tests inspect actual DOCX parts with the Open XML
SDK and PDF text with PdfPig. The archive includes named TRX results and test
output assembly hashes for both runs. The MIT [Word-derived control parts](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants/README.md)
have a pinned upstream document and extraction provenance.

| Input and operation | Observed result in both runs |
| --- | --- |
| Word-derived plain control with its original glossary run defaults | Load reports a theme-font loss. Fragment insertion rejects with `Fragment.SourceLoss` and preserves both documents. |
| Same control with just the glossary run-default element removed | Two insertions yield distinct control IDs. Editing one and restoring the other placeholder preserves both text values, four local glossary resources and the source. |
| Word-derived date placeholder with a materialized character style | Load reports `Control.CharacterStyle`. Fragment insertion rejects before changing either document. |
| Field fragment with local references, body and cell destinations | The default field policy rejects. Explicit cached-field acceptance preserves instructions and remaps local targets under the admitted style policies. |
| Paragraph with a deletion and insertion | Retained history rejects fragment insertion. Each detached decision inserts just its chosen text into a schema-valid DOCX and searchable PDF. The separately captured redline still displays both versions. |

The first plain-control experiment expected a successful insertion with the
original glossary defaults. Source execution exposed `Docx.ThemeFont` and
`Docx.RunFormatting`. The final controls explicitly cover that rejection and the
minimal positive contrast. These cases use Office-derived XML and independent
readers. Licensed Word and Aspose rendering were not provisioned for this capture.

## Source, packages and reference

The measured checkout was clean at `d95225937b49dc3b0507fc87928b92fe86dd78ee`,
tree `008c73b087e68f99252221dd5edf3f6f0dde5533`. NuGet registrations for all
three IDs independently selected 1.6.2 as the latest listed stable version. A
fresh, source-mapped consumer restored the public dependency graph and repeated
the restore in locked mode. The managed export used explicit pinned fonts. The OCR
archive was downloaded and hashed separately. Locally packed validation artifacts
were separate from the measured public packages. [Release and exact-runtime qualification](https://github.com/kirakosyan/word.net/releases/tag/v1.6.2)

| Public archive | SHA-256 |
| --- | --- |
| wordnet | `7bf0fb7578530216188c0bd3f0172f85da9eb8efa4a6d9eeb18b6897d51f0bad` |
| wordnet.Fonts | `f783ab03f20eca6a31c1be66c6be517883366529e8d06cbea83489f74fa45fac` |
| wordnet.Ocr | `d83892eb37e1c41511b8c2c39efb18b3bb427532e21838ccf86474cc98c6213b` |

Ubuntu 24.04 x64 ran SDK 10.0.401 and .NET 10.0.12. The SDK image digest was
`sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317`.
LibreOffice Writer 24.2.7.2 used private fontconfig with the manifest's font
bytes. Poppler 24.02.0 and Pillow 12.3.0 performed the raster comparison. Glyph
tolerance was 0.25 pt. Significant pixel channel sum, changed-pixel fraction and
mean channel error limits remained 96, 0.002 and 0.25. Open XML SDK 3.5.1
independently validated all 40 measured DOCX inputs by filename and SHA-256.

Each input ran once cold and three times warm in its own worker. Medians for the
original 24 inputs are below. RSS is the process working set after conversion.
Host load varied during capture, so these timings describe this run.

| Build / conversion | Time (ms) | Allocated bytes | RSS (bytes) | PDF bytes |
| --- | ---: | ---: | ---: | ---: |
| Source, cold | 257.5 | 22,662,876 | 95,524,864 | 10,675 |
| Source, warm | 59.5 | 20,143,348 | 101,601,280 | 10,675 |
| Public, cold | 261.7 | 22,662,876 | 94,511,104 | 10,675 |
| Public, warm | 59.4 | 20,141,288 | 100,345,856 | 10,675 |

## Reproduction

Prepare the [comparison tools](comparison.md#reproduce-the-free-baseline) on
Linux. Use a separate clean checkout of the release for `--repository` and a
fresh output directory. Run the refresh script from the checkout containing the
new evidence tools. Package selection queries the live NuGet registrations, so
a future run can select a different published version.

```sh
python eng/refresh-fidelity-baseline.py \
  --repository "$release" --commit d95225937b49dc3b0507fc87928b92fe86dd78ee \
  --output "$output" --poi-classpath "$jars" \
  --reductions "$release/tests/wordnet.Conversion.Tests/Fixtures/Fidelity103/manifest.json" \
  --combinations "$release/docs/measurements/roadmap-138-combination-baseline/fixtures/manifest.json"
```

The original receipt records SHA-256
`b01f744f7f02a67dee4c696c48cd77cd7c95a8c99c90a45f26672fc2cabf925a` for
the executed CRLF copy in `qualification/executed-runner.py`. Converting those
line endings to LF reproduces the runner at commit
`24cc58a2559f7d88b459a582e020d996e2132535` with SHA-256
`cbeb9d15b80c6fcb368748a6152bd2b1d1f8dc4b123717d2aa7a963f80ab7c53`.
Future checkouts use LF. In this capture, the registration files numbered 0, 1
and 2 belong to wordnet, wordnet.Fonts and wordnet.Ocr respectively, as recorded
by URL in `packageSelection.registration`. New captures include the package ID
in each filename.

The original `receipt.json` and all measured files retain their captured bytes.
A supplemental `qualification/reference-snapshot-hashes.json` records SHA-256
hashes for all 80 reference snapshots. Replay pins both the original receipt and
the supplement. Each snapshot was checked against the raw
Linux capture (archive SHA-256
`080a5c1fbf81c4673998cd24c2cb7295c997ac12e2e0afb89cb4a0b9d79146dd`).
New captures record these hashes during measurement.

To reproduce the published ledger offline, run the indexer against the capture
ZIP. It verifies the raw report and reference snapshot hashes, checks input and
assembly identities against the capture, and applies the versioned replay rules
for geometry, glyph placement and ranking. Original output/reference
PDFs and raster images are excluded from the Git archive. Their hashes remain in
the capture. Recreating the pixel comparisons requires the render step above.

```sh
python eng/index-fidelity-capture.py docs/measurements/fidelity-1.6.2/capture.zip findings.tsv
python eng/test-fidelity-baseline.py
```
