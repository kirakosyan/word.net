# Initial 1.5.0 fidelity measurements — roadmap #138

This is **partial evidence for #139**, not acceptance of that issue or the roadmap.
On 2026-10-30 (Europe/Oslo), the existing 24-document corpus and 12 existing
reduced contrasts were measured against clean release source
`bf5ab004d1622e66084aedf4bbc83e3cf8d9c2c7` (tree
`dfd9b1f33a239bc9527c64e1f63d9743b8958b3b`) and independently restored, latest
listed stable public **1.5.0** packages. The
[prior 1.4.0 baseline](fidelity-103-baseline.md) is unchanged and remains dated
historical evidence. The [initial combination matrix](roadmap-138-initial-matrix.json)
records documented 1.5.0 boundaries, not new support.

The [receipt](measurements/roadmap-138-initial/receipt.json) retains every finding,
raw timing/allocation/RSS/output sample, original tolerances, selected source and
package identities, loaded assemblies, font identities and reference provenance. Its
SHA-256 is `22f3749de361c7d4bae928c0eaf23c5ab4cf47b9d7bf36a2d6ea4e81049cb692`.
The adjacent evidence directory also retains full result/defect JSON, extracted
reference glyph snapshots, reference manifests and command transcripts; `.txt`
transcripts correspond byte-for-byte to the `.log` names in the receipt. Reference
and output PDF bytes are not committed. Their hashes were verified during the run;
the full local capture is needed to repeat that byte-level audit.

## Identity and method

The source and public package workers were built separately on Linux x64, .NET SDK
10.0.401 and runtime 10.0.12. The package consumer was restored into a fresh cache,
then restored in locked mode without inheriting source transitive dependency pins.
Core archive SHA-256 was
`5a7c20bb5b4271482d8756c44f756542365fddfb519151444398b071b0e0c1b9`;
fonts archive SHA-256 was
`11740af88988313ab7d6f0620c9fcdeabbfee94e29d7ee3b9970776652808631`.
The public OCR 1.5.0 archive was separately downloaded and hashed as
`47c9e24142b019b192c8337e103df730382207a0159a3066acc71519b26835d7`;
it was not loaded or exercised by this managed DOCX export comparison. A separate
[registration check](measurements/roadmap-138-initial/public-registration.json)
identified 1.5.0 as the latest listed stable version for all three packages.
The receipt verifies the public core/fonts asset hashes against the consumer and the
loaded worker assemblies against their respective builds; the two binary builds have
different identities even when their measured output agrees.

The independent reference producer was LibreOffice 24.2.7.2 `420(Build:2)`, in an
Ubuntu 24.04 Linux x64 container with private fontconfig and manifest-pinned open
fonts. It used Poppler 24.02.0, Pillow 12.3.0 and pinned Apache POI 5.4.1. The image
used for capture had ID
`sha256:a3aaef646226d406fc2b511462beb0359f4cd8859d775f404bd8f137dfd5f644`;
the receipt records executable, binary, font, tool and PDF hashes. The reference is
an independent open-source office rendering, not a licensed desktop reference.
Licensed reference execution was not provisioned; no evaluation output is used as a
golden. A preliminary host attempt stopped before measurement because its development
office build was rejected by the unchanged capture guard; it produced no verdicts.

The runner retained unchanged thresholds: 0.25 pt glyph displacement, 0.01 pt page
geometry, significant pixel channel sum 96, changed-pixel fraction 0.002 and mean
channel error 0.25. One fresh worker ran each document with one cold and three warm
conversions. The runner's 13 comparison controls and 20 baseline-tool tests passed.
Its audit verified the clean checkout, retained reference hashes, raw/compact report
consistency and loaded binaries. The runner itself does not run schema validation.
A separate Open XML SDK 3.5.1 check validated all 36 actual input archives without
errors; [source](measurements/roadmap-138-initial/schema-check-source.txt),
[project](measurements/roadmap-138-initial/schema-check-project.txt) and
[transcript](measurements/roadmap-138-initial/schema-check.txt) are retained.
The schema validator DLL SHA-256 was
`ff3ed8318388d84fa5bbc5736df6144ecfa3165c0b95f6b45b5330f463edf7ae`.

A separate Word-authored control probe used the unmodified public Open XML SDK
[`SDT.docx`](https://github.com/dotnet/Open-XML-SDK/blob/431ab05cf160248cc3885a4a766026d4f8243792/test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/SDT/SDT.docx)
(SHA-256 `8d297bee1383f87d0fa77e7b2e5573e88517297a88f36f569ec5eaa0f8201878`).
Open XML SDK 3.5.1 reported no schema errors. Both the clean `bf5ab00` source and
public 1.5.0, using Lenient typed-control loading, rejected its glossary resource
relationships with `Control.Unsupported`; neither produced a display result. Their
executed core DLL hashes were respectively
`64abb7af54f26814223ecfd5614b26d0032afda538088710844433134e9faab0` and
`4c4c1d2c83c4466bce2e0b4da5502b3f513b9d78d4babd7774eade136a2e690c`, matching the
earlier source/package workers. The extracted, metadata-minimized
[negative-control parts](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordParts/README.md)
and their provenance are retained separately. This probe establishes a documented
unsupported boundary, not a rendering defect or successful Office qualification.

## Results

Source and package produced identical page/text/glyph snapshots, output hashes and
verdicts for each of the 36 inputs. The table shows each mode separately only when
the values differ; here they are equal.

| Check, per mode | Existing 24 | Reduced 12 |
| --- | ---: | ---: |
| Authored text/order and authored pages | 24/24 | 12/12 |
| Independent editable DOC text/order | 24/24 | 12/12 |
| Expected diagnostic set | 24/24 | 10/12 |
| Reference page count | 24/24 | 11/12 |
| Reference per-page text/order | 19/24 | 11/12 |
| Reference glyph placement | 7/24 | 3/12 |
| Reference page geometry | 0/24 | 0/12 |
| Reference raster policy | 12/24 | 10/12 |
| Any reference failure retained | 24/24 | 12/12 |

All four comparison commands correctly exited 1 because reference differences
remain. A nonzero comparison is retained, not reclassified as a passing fidelity
result. All compared page pairs in this cohort measured 595.3 × 841.9 pt in the
source output and approximately 595.303937 × 841.889764 pt in the reference.
The reference's unit rounding changes height by about 0.010236 pt, just beyond the
unchanged 0.01 pt geometry threshold. This is a **reference-policy difference** for
that page-size check, not a finding that source page size is wrong. Other failed
verdicts remain independent and are not explained away by that rounding.

The existing 24 source runs had cold latency min/median/max 288.1/352.8/532.9 ms
and warm latency 31.5/97.8/290.3 ms; public-package runs measured
301.2/347.3/519.9 ms cold and 30.4/98.6/229.7 ms warm. Across all cold and warm
samples for those 24 inputs, source allocations were 17,950,112–48,142,488 bytes,
observed RSS 93,450,240–116,248,576 bytes and output 9,636–26,301 bytes. The
receipt retains every sample, including process peak RSS and the 12 reductions.
There was no exclusive host or CPU isolation; these observations do not establish
performance superiority, a regression gate or a hard memory bound.

## Ranked follow-up and classifications

1. **Unknown: text and reading-order differences.** Five original cases
   (`contract-legal`, `contract-revisions`, `letterhead-textbox`,
   `footnotes-contract`, `footnotes-report`) fail the per-page reference text/order
   check. Their authored-content and independent editable-DOC checks pass. Inspect
   each reference and source structure before attributing a cause.
2. **Unknown: geometry and raster differences beyond page-size rounding.** For
   example, `invoice-nested` has a 134.503 pt maximum aligned-glyph displacement;
   `long-table-ledger` and `long-table-inventory` each have 4.454 pt. Matching glyph
   sequences permit those measurements but do not establish their causes. Other
   failures, including nonmatching glyph sequences, remain in the raw findings.
3. **Documented diagnostics and reference-policy difference:** both note reductions
   report `Formatting.RunAppearance`, outside their original zero-warning expectation.
   `notes-continuation` has three source pages and two reference pages, with a
   per-page text mismatch. The bounded note-reservation policy and the fixture's
   unusual marker/spacing are documented confounds; this alone does not establish
   dropped text or a converter defect.
4. **Reference-policy difference:** page-unit rounding exceeds the fixed geometry
   threshold as described above. Do not weaken the threshold or discard the verdict.
5. **Reproduced and documented unsupported: themes and table styles.** Separate
   paired controls below reproduce theme-font and conditional table-shading losses.
   The theme-color contrast is inconclusive against this reference producer.
6. **Documented unsupported: composition and portable review.** Separate API
   probes below confirm rejection of field-bearing fragments, cell destinations
   and direct PDF export with retained revisions, without mutation.
7. **Unknown: remaining roadmap combinations.** The old 12 reductions cover
   defaults, spacing, wrapping, simple table sizing and notes. The Word-authored
   negative control and new probes capture some editor and workflow boundaries;
   broader control cases, requested feature combinations and a complete ranked
   ledger remain outstanding.

The existing fixtures and scratch-generation procedure are documented in the
[comparison guide](comparison.md) and
[reduction provenance](../tests/wordnet.Conversion.Tests/Fixtures/Fidelity103/README.md).
The 1.5.0 release qualification and these measurements are distinct evidence.
This run adds no new converter capability, platform qualification or public package.

## Additional independent controls

Six theme/table contrasts and five workflow packages were independently authored
by the [theme/table generator](../eng/generate-roadmap-138-controls.py) and
[workflow generator](../eng/generate-roadmap-138-workflows.py), without a wordnet
serializer. Their inputs, generator hashes and pinned open fonts are recorded in
the [theme/table manifest](measurements/roadmap-138-contrasts/theme-table-manifest.json)
and [workflow manifest](measurements/roadmap-138-contrasts/workflow-manifest.json).
All eleven archives passed [Open XML SDK 3.5.1](measurements/roadmap-138-contrasts/schema-check.txt).

The same clean source commit and public 1.5.0 package were measured using the
same Ubuntu/LibreOffice/Poppler/POI toolchain and unchanged thresholds described
above. The [receipt](measurements/roadmap-138-contrasts/receipt.json), full
result and defect JSON, reference snapshots and command transcripts retain all
six new verdicts and the repeated 24-case run. Source/package snapshots and
verdicts agree. All six authored text, page and independently read editable text
checks passed; all six retain a page-geometry reference failure from the rounding
described above. Three also report unexpected diagnostics because the authored
manifest deliberately accepts no losses. The measurements ran alongside other
tests without CPU isolation: their timing samples are observations only. Reference
and output PDF/raster bytes are not committed; hashes, raster contrasts, glyph
snapshots and reference manifests are retained.

- **Theme font — reproduced loss:** LibreOffice's theme and explicit Liberation
  Mono controls have identical rendered pixels. Release source/package diagnose
  `Docx.ThemeFont`; their theme output differs from its explicit counterpart, and
  the theme/reference aligned-glyph maximum displacement is 31.329 pt. The explicit
  control is within 0.1 pt. This is evidence for the specific theme-font case,
  not a claim about every theme or font.
- **Conditional table shading — reproduced loss:** LibreOffice renders the same
  blue fill in both conditional-style and directly shaded first rows; the two full
  reference pages differ slightly in other pixels. Source/package report
  `Docx.TableFormatting` and omit the blue fill for the styled table while retaining
  it in the direct control. Exact color counts and pairwise pixel differences are
  in [pair contrasts](measurements/roadmap-138-contrasts/pair-contrasts.json).
- **Theme color — reference boundary:** the theme stores red as its fallback and
  blue as its accent. Both LibreOffice and the release render the stored red while
  the explicit contrast is blue. The release diagnoses `Docx.ThemeColor`, but this
  reference cannot establish the intended theme-color result; it remains unknown.

The separate [workflow probe](../eng/roadmap-138-workflow-probe.cs) executed the
clean source and separately restored public package against the five authored
inputs. Both reject a field-bearing fragment with `Fragment.Structure` and a
table-cell bookmark destination with `Fragment.Placeholder`; target and source
remain byte-identical to their pre-operation saves. Both reject direct PDF export
of retained insertion/deletion history with `Review.ExplicitViewRequired`, leaving
the caller stream and source history unchanged. Explicit accepted and rejected
views contain `Total 20` and `Total 10`; Poppler independently extracts those
values from the generated PDFs. The [source](measurements/roadmap-138-contrasts/workflow-source.json)
and [package](measurements/roadmap-138-contrasts/workflow-package.json) records
include exact inputs, assembly identities, diagnostics and outcomes. Different
PDF hashes do not imply different text or review decisions.
