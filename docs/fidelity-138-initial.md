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
   broader control cases and requested feature combinations remain outstanding.

The separate [machine-readable failure ledger](measurements/roadmap-138-failure-ledger.json)
indexes every failed verdict in the retained initial and contrast defect reports,
separately for source and public-package runs. It records 312 verdicts across the
four recorded cohorts: 216 remain `unknown`, 82 are the specifically verified A4
rounding policy difference and 14 are documented unsupported theme/table controls.
The six separate fragment/review probes remain explicitly classified as unsupported
for release 1.5.0. These counts include distinct repeated runs and do not count
unique defects. A pagination difference is never reclassified as page-size rounding;
when the repeated contrast's extracted reference snapshot was not retained, its
geometry verdict remains unknown. The ledger keeps input/reference identities,
original findings and result-file pointers and does not change any thresholds.
The [generator](../eng/generate-roadmap-138-ledger.py) and
[tests](../eng/test-roadmap-138-ledger.py) reproduce this index from 65 hashed
evidence files and reject missing failures or changed paired observations. The
ledger does not add the remaining requested combination controls or establish
that current unreleased source behaves like the 1.5.0 baseline.
The ledger tests passed six cases, the existing baseline-tool tests passed 20,
and the comparison-tool controls passed 13 after a locked restore and local
benchmark build. The first comparison-tool attempt failed its prerequisite check
because that benchmark executable had not yet been built; no comparison result
was produced in that attempt. These checks do not rerun the historical reference.

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

## Theme-font and table-style combination controls — 2026-11-01

Four independently authored, schema-valid DOCX controls vary a theme font versus
an explicit Liberation Mono font and conditional first-row table shading versus
direct shading. The pinned LibreOffice 24.2.7.2 reference renders all four with
identical pixels, including the intended blue shading. The clean release source
`bf5ab00` and independently restored public 1.5.0 package produce identical
glyph snapshots, raster pixels and verdicts for these inputs. All four retain
authored text/order, one authored page and independently read editable DOC text.

| Release input | Unexpected warning codes | Reference glyph placement | Reference raster |
| --- | --- | --- | --- |
| Explicit font, direct shading | None | Pass | Pass |
| Explicit font, conditional shading | `Docx.TableFormatting` | Pass | Fail |
| Theme font, direct shading | `Docx.ThemeFont` | Fail | Fail |
| Theme font, conditional shading | Both codes above | Fail | Fail |

The theme cases reach 8.449 pt maximum aligned-glyph displacement against the
unchanged 0.25 pt threshold. In the styled cases the reference has blue first-row
pixels while the release output has none; the direct-shading contrast retains
them. All four also fail the unchanged 0.01 pt page-geometry tolerance because
the reference A4 height is 841.889764 pt and the release output is 841.9 pt; this
specific height difference is the previously identified reference-unit policy
difference. Both comparison commands exited 1, and all eight failed case results
and their individual verdicts remain in the ranked reports.

The [machine-readable matrix](measurements/roadmap-138-combination-baseline/factor-matrix.json)
and [receipt](measurements/roadmap-138-combination-baseline/receipt.json) retain
the input, open reference and output files, hashes, raw measurements, ranked
findings, package identities and 18 altered-report/image controls. The public fonts
archive was verified but not loaded; these cases supply the pinned fonts explicitly.
Licensed desktop Word and Aspose were not provisioned. These are observations of
the released baseline, not proof of the behavior of newer source or untested combinations;
#139 remains open for its remaining controls and qualification.

To recheck the retained corpus, use Pillow 12.3.0 and run the verifier with the
four subdirectories under `docs/measurements/roadmap-138-combination-baseline`,
followed by the clean release build's `wordnet.dll` and the downloaded public
`wordnet.1.5.0.nupkg`. The verifier checks their hashes and detects altered
verdicts and images. Regeneration uses
`python eng/generate-roadmap-138-baseline-combinations.py --output <new-directory>`;
the reference image digest and executable identities are pinned in the receipt.

## Nested-table structure controls — 2026-11-01

The original `invoice-nested` case has a 134.503 pt maximum aligned-glyph
displacement against the pinned LibreOffice reference. Four schema-valid controls
vary the nested grid width and a terminal paragraph in the first outer cell. The
outer grid remains 5,000 twips. The clean 1.5.0 source and independently restored
public package produce identical glyph snapshots, raster pixels and reference
verdicts; the wide/no-paragraph control reproduces the original observation.
Both builds retain the authored text and independently readable editable DOC text,
without diagnostics.

| Nested grid | Terminal paragraph | Reference `Total` position, pt | Aligned-glyph result |
| --- | --- | --- | --- |
| 5,000 twips | Absent | (72.1, 116.992) | 134.503 pt maximum difference |
| 2,500 twips | Absent | (72.1, 116.992) | 134.503 pt maximum difference |
| 5,000 twips | Present | (197.1, 86.992) | Text and glyph sequences differ |
| 2,500 twips | Present | (197.1, 86.992) | 9.503 pt maximum difference |

Positions use Poppler's top-origin coordinates. The release output positions
`Total` at (206.603, 86.982) in all four controls. In the wide/paragraph reference,
Poppler does not extract the nested-cell text `80`; the reported zero maximum
displacement for that mismatched sequence is not a passing measurement. All four
comparisons fail the existing page-geometry and visual thresholds. Both comparison
commands exited 1 and their failures remain retained. The A4 height difference
includes the reference-unit policy described above.

The [receipt](measurements/roadmap-138-nested-table-baseline/receipt.json) indexes
the inputs, open references, release outputs, independent schema and Poppler
observations, original verdicts and 23 altered-observation controls. Regenerate
the inputs with `python eng/generate-roadmap-138-nested-table-controls.py --output
<new-directory>`; the verifier accepts the retained fixture, office, source,
package and Poppler directories, schema log, release and package workers, release
core assembly and public package archive. The receipt records their identities.
These contrasts show LibreOffice sensitivity to the structural changes; licensed
Word was not provisioned, so they do not establish which renderer matches Word or
attribute the original discrepancy. Other baseline differences remain unresolved.

## Additional fragment and review controls — 2026-11-01

Four independently authored, schema-valid controls exercise a plain body-bookmark
fragment, a field-bearing fragment and a paragraph with an insertion and deletion.
The clean 1.5.0 release source and independently restored public package both insert
the plain fragment, reject the field-bearing fragment without changing either
document, and reject PDF output of retained review history without changing the
caller stream. Explicit accepted and rejected review views contain `Before new
After` and `Before old After`. Open XML SDK validates all six output DOCX files;
Poppler independently confirms each output's text and one-page geometry. The source
and package renderings have matching glyph coordinates and pixels for these
synthetic controls. Their PDF bytes can differ because creation times are sampled
separately.

The [receipt](measurements/roadmap-138-workflow-baseline/receipt.json) and its
hash-indexed files retain the source and package identities, inputs, outputs,
schema and tool transcripts, the initial compiler warning and corrected
warning-free probe, and 16 altered-observation controls. The PDF comparisons are
between the release builds; they do not measure a licensed reference or implement
a redline renderer. #139 still needs its remaining combination evidence and final
acceptance checks.
