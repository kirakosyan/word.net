# Conversion tests and qualification

## Evidence by revision

PR [#35](https://github.com/kirakosyan/word.net/pull/35) was squash-merged on
2026-10-17 as `0b2af843a40b8c866119594a887e8dac1f5c7340`, with **696** required
conversion/OCR cases, including **86** authored editing/template cases. All **15**
review findings were addressed, replied to and resolved. All **20** CI check runs
passed on the final reviewed head `383ea15f1bb21d96f38a61a2eaee42980cae0124`
([push CI](https://github.com/kirakosyan/word.net/actions/runs/39034264216),
[PR CI](https://github.com/kirakosyan/word.net/actions/runs/39034268071)).
The expected-head merge was verified and its tree equals that reviewed head.
Issues #11–#15 and #17 have checked acceptance criteria, merge evidence and verified
closure; roadmap #32 records completed buckets 1–3 and the next bucket 4.

PR [#34](https://github.com/kirakosyan/word.net/pull/34), commit
`1c44d97f7378401ed8ee67a9ae50b2197499c4bd`, had **610** required cases and 20 successful
pre-merge checks. PR #35's initial 664-case head
`811d6a26af91ebbd95249f4189230be99ddb483e` also passed 20 checks; those are historical
results, separate from the final review-regression evidence below.

The PR CI checks include native Windows/Linux x64/ARM64 source and packed-asset
lanes, compile checks and the single-core PDF performance/memory gate. These are
PR-head results; they do not replace exact-main-package release qualification,
actual minimum-Framework execution, desktop Word or licensed Aspose references.
Local runs and independent-reader evidence are detailed below. Historical release
sections retain their original case counts. See the [delivery status and post-merge
checklist](implementation-plan.md) before interpreting an old count as current.

## Bucket 4 candidate qualification

The unreleased implementation covers #20–#23 and #25–#27; see the
[bounded profiles/API](interoperability.md) and [decisions](interoperability-design.md).
The required suite expands from 696 to **828** cases (795 initial plus 33 review regressions): 15 HTML, 21 RTF, 12 typography,
10 accessibility, 16 previews, 14 geometric PDF reconstruction and 16 OCR metadata/control
cases plus 28 cross-feature review cases are described by their actual test classes (the aggregate count is authoritative).
Historical PR #35 counts above remain historical, not a claim about these additions.

Independent checks on Linux x64 use LibreOfficeDev 26.8 alpha build
`2c87e51eeaa2b413ff4ae097b2705eea1995d8e5`, ReportLab 4.4.9,
PyMuPDF 1.26.6/MuPDF 1.26.11, Pillow 12.3.0, uharfbuzz 0.53.0/HarfBuzz 12.1.0,
FontTools 4.61.1 and FriBidi 1.0.16. Noto Arabic font hash/license and PDF ground truth accompany
the fixtures. No commercial reference engine is used. The production optional shaper
uses HarfBuzzSharp/native 14.2.1.301; the independent binding/version is deliberately
separate. Actual Arabic glyph IDs, UTF-16 clusters, advances and mark offsets match;
independent glyph-mask IoU is **1.0**, and both base directions match FriBidi token order.
MuPDF extraction retains logical Unicode/marks after removing extractor-inserted layout
line breaks; word search spans measure **63.12 pt** for the 30 pt Arabic golden.

The own outline decoder fixes negative signed Y deltas and implied half-unit points;
a dedicated Arabic-dot regression and cyclic-composite rejection protect that path.
Independent SVG/PNG ink-mask IoU is **0.9333** for the Latin preview control. Direct
pixels verify cropped/rotated/flipped rasters and clipping bounds, alongside page labels
and original PAGE/NUMPAGES. LibreOffice reads emitted RTF into real editable OOXML,
including exact Unicode/emphasis, one paragraph per ordinary table cell, preserved
multi-paragraph/explicit-empty cells and a **60 × 30 pt** PNG frame.

The 33 review regressions cover generated/artifact link degradation, sorted ParentTree
page/annotation mappings, Word-style RTF repeated rows and picture containers, special
charset losses, Strict cross-page-link PNG previews, 80-page outline-budget reuse,
continued unique-glyph admission, alt/decorative precedence, nested highlights and
2,000-run list semantics. Independent ReportLab additions check first-line/hanging
indents, compact style runs, 32-line prose and measured baselines after tables/inline
images. The qualification benchmark now includes and validates all 36 directions;
the previous 30-direction guard caused both x64 CI failures despite passing source suites.

Pinned offline veraPDF **1.30.2**, image digest
`sha256:d5ee329657cf9bc4b2400392dd54c7d0a0ce9980ff6fa2da5590eebeec007cdb`,
accepts three UA-1 fixtures (invoice, multi-page lists/images/table and an early-page link), rejects the
untagged negative control, and retains **38/38 PDF/A-2b** corpus results. Separate
MuPDF inspection verifies structure roles, parent trees, unique MCIDs, actual page
references, annotations/OBJRs, Unicode text, embedded TrueType programs and source
reading order. Repeated table headers remain artifacts; one semantic Figure retains
alt text while the explicitly decorative second image is an artifact. Fixed-clock
PDF tagging is deterministic. Ordinary PDF/PDF-A defaults remain separate profiles.

Manual reading-order review on 2026-10-17 inspected the emitted hierarchy/content
mappings against source and viewed the invoice raster: H1 Invoice → P Summary first →
row-wise Product/Price, Service/42.50 table → Terms link. The two-page sample orders
heading → two labelled list items → final instruction → described Figure → table
rows 1–49, with column header semantics only once despite visual repetition on page 2.
The Terms descriptions/destinations and image alt were checked in actual objects.
This is a bounded manual structure/visual review, not a claimed NVDA/JAWS session or
arbitrary-document accessibility guarantee.

```sh
# Prepare OCR in the same PowerShell process as both complete suites (see AGENTS).
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore -m:1
# Set WORDNET_INTEROP_ARTIFACTS to an absolute output directory for authored tests.
python3 eng/verify-interoperability.py artifacts/bucket4/interop
python3 eng/verify-typography-accessibility.py artifacts/bucket4/interop
python3 eng/validate-pdf-accessibility.py artifacts/bucket4/interop
# Generate the existing archival corpus with wordnet.ManagedExport.Probe, then:
python3 eng/validate-pdf-accessibility.py artifacts/bucket4/pdfa --flavour 2b --output artifacts/bucket4/pdfa-report.xml
```

Complete suite results and exact final candidate revision are recorded with the PR;
all source targets compile, and local suites execute on .NET 8.0.32 and 10.0.12.
The review-fix build passes **828/828 on each runtime**, with zero failed/skipped cases,
a warning-free solution build, locked restore and formatting verification. The repaired
36-direction concurrent qualification benchmark passes 32 iterations (1,152 measured,
content-validated exchanges). These local results precede the refreshed PR-head CI. Its packaged Standard-on-.NET-8
lane then exposed an undeclared CodePages 10 dependency (modern assets passed). The
follow-up explicitly declares the centrally pinned 10.0.12 provider for every core TFM;
archive validation now checks the required legacy/Standard/.NET 8 dependency groups
(.NET 10 legitimately prunes its shared-framework assembly), and packed fallback qualification
runs on both modern runtimes. Source and release evidence remain separate.

The optional Python qualification tools are external development dependencies; the
validator uses Docker's local Unix socket, exact copied bytes and `--network none`,
including in environments where host bind mounts refer to another filesystem.
Run the self-contained `examples/WordNet.Interoperability` sample for editable HTML,
RTF, semantic PDF and managed SVG. Actual execution here is Linux x64 .NET 8/10;
Windows/ARM64 and exact packed/runtime lanes require CI. Framework compilation does
not qualify 4.6.2 execution. No Word/Aspose, Android/macOS or trimming/AOT claim follows.

## PR #35 review regressions

Thirty-two new cases address the 15 review findings, bringing the editing/template
profile to **86** authored cases and the complete suite to **696**. Independent
OOXML fixtures and schema validation check external/local repeat references,
cell block controls and diagnosed unsupported wrappers, text boxes inside cells,
inherited shared headers, multi-block/row control boundaries (including cross-section and empty-region cases), wrapped section/note
paragraphs, cached controls inside fields, rejected flattened nested execution,
item-scoped repeated notes, Word percent/meridiem/literal formats, unrelated Word
controls and protected inline-control edits. Independent EPUB input exercises
scaled-image rounding through public snapshots. Large cases replace 30,000 styled
runs and atomically create a 20,000-paragraph report with the bulk API, including
cancellation and limit rollback.

Locked restore, warning-free all-target Release builds, **696/696** complete
conversion/OCR cases on each local Linux x64 .NET **8.0.32** and **10.0.12**
(zero failures/skips), formatting, five comparison controls, package/API validation
and package-content inspection pass. The added `AddParagraphs` API is documented
in all five core assets; dependencies are unchanged. The public sample also runs.
Reports are under ignored `artifacts/pr35-review`. These local results do not add
Windows/Framework, ARM64, minimum-runtime, Word/Aspose or PDF/UA qualification.

## Document editing and templates — roadmap #32, section 3

The [editing/template guide](editing-and-templates.md) and
[design contract](editing-template-design.md) describe the additive public API and
bounded grammar. The initial **54** authored editing/template cases brought the
required suite to **664** before review. Fixtures independently construct OOXML and Open XML SDK
validates editable output; PDF assertions check actual text, pages, geometry,
images and labels. Cases cover styled cross-run replacement, Unicode/regex barriers,
bookmark pairs, atomic failure, callback reentrancy and serialized sessions;
composition style/list/bookmark/story/image collisions, default page geometry and
unresolved references; and scalar/JSON/culture merge, nested repeats, conditions,
image bytes/dimensions, note identities, row spans/decoration, missing/null data,
syntax/row/depth/expansion bounds and cancellation. Existing conversion behavior
still uses cached merge-field content.

The standalone `examples/WordNet.Automation` project exercises only the public
core API. It creates a letter, fills an independently authored invoice and assembles
an appendix, exporting DOCX and PDF. MuPDF 1.26.11 independently reads the invoice's
populated text and the assembled document's three pages. The sample emits the
expected image alternative-text/accessibility diagnostic. Generated outputs remain
under ignored `artifacts/automation-sample`.

Locked restore, warning-free all-target Release builds, **664/664** complete
conversion/OCR cases on each Linux x64 .NET **8.0.32** and **10.0.12** (no skips),
format verification, five comparison controls, NuGet/API validation and package
inspection pass. Runtime dependencies remain unchanged and all five assets contain
the same documented editing API. Reports are retained in ignored
`artifacts/editing-templates/tests`. This change does not add local Windows,
Framework-runtime, ARM64, desktop Word/Aspose or arbitrary-template qualification.

## Business-document fidelity — roadmap #32, section 2

The bounded table, drawing, section, numbering, note and reference profiles are
specified in [business fidelity decisions](business-fidelity-design.md) and the
[fidelity guide](document-fidelity.md). That increment brought the suite to **610 cases**, including
86 authored business-fidelity cases and removal of the obsolete XPS image-loss case.
Both complete source suites pass **610/610**, with zero failures/skips, on Linux
x64 .NET **8.0.32** and **10.0.12**. Locked restore, warning-free Release builds
of all five core assets, format verification and NuGet package/API validation pass.
The five Python comparison controls also pass; corpus fixture bytes are unchanged
and the four newly supported cases have updated expected-loss manifests.

The new cases independently validate DOCX markup with Open XML SDK and assert PDF
text, precise positions, physical pages versus labels, linked TOCs/references,
footnote placement, nested row splitting, invalid geometry and destination safety.
An asymmetric four-color raster with a corner marker checks cropping and both
mirror axes across right-angle rotations. Skia-rendered PDF samples for nested
cells, text boxes and image transforms were inspected. MuPDF **1.26.11** through
PyMuPDF **1.26.6** independently renders all **12** XPS transform combinations,
checks **48** transform pixel samples, **8** PNG/JFIF/EXIF density cases at 72/144 dpi,
and validates the nested table's text/colors. The review regressions cover all
reflowable break fallbacks, zero-based page labels, negative crop padding,
body/table/cell bookmarks, DOCX refresh without layout, Lenient drawing/row/box
fallbacks, final note labels/appearance/sections, implicit table layout, Word's
multi-paragraph default TOC, rich field caches and nested text-box content.

Reproduce the optional independent XPS check after the required build:

```sh
WORDNET_BUSINESS_ARTIFACTS=/absolute/path/to/artifacts/business-fidelity/rasters \
  dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore \
  --filter FullyQualifiedName~BusinessFidelityTests
python eng/verify-business-xps.py /absolute/path/to/artifacts/business-fidelity/rasters
```

PyMuPDF is a separately installed qualification tool under its own license, not a
wordnet package dependency. Reports and reviewed samples are retained locally in
ignored `artifacts/business-fidelity/`. This run does not qualify Windows,
Framework execution, ARM64, desktop Word/Aspose parity or arbitrary template layout.


## Comparison corpus and preflight

The additive preflight API and twelve-document comparison corpus are described in
[diagnostics](diagnostics.md) and [comparison reproduction](comparison.md). New tests
independently schema-check the authored invoices/contracts/reports/letterheads,
assert identical parse/destination/layout findings against real conversion, check
stable cell/story locations, bounded grouping, privacy, cancellation, stream ownership,
encryption and resource failures, and distinguish provider/writer-only findings.
The optional commercial runner does not change existing required CI gates.
Local Linux x64 evidence: locked restore and warning-free Release builds of all
five assets, **510/510** conversion/OCR tests on each of .NET 8.0.32 and 10.0.12,
format verification and package/API validation pass. All twelve free comparison
cases pass exact text/page checks and independent POI editable DOC reading order;
three Python control tests pass, including actual displaced/missing-text/extra-page
PDFs and stale-raster rejection. The historical performance harness also builds
against the earlier core API. Raw reports are under ignored `artifacts/comparison*`.
Word/Aspose reference execution and Windows/ARM64/Framework runtime qualification
were not performed here.


## PR #33 review corrections

The review adds fifteen regression cases for Strict inspection through full layout,
model-only fixed-page losses without pagination, caller-font glyph fallback and list
mappings, Lenient rejection prediction, and row/cell/numbering/note source locations.
The complete **525-case** suite passes on Linux x64 .NET 8.0.32 and 10.0.12 with no
failures or skips. Locked restore, all five library targets, formatting and package
API compatibility checks pass. The corpus now requires its exact expected warning
set, in addition to text, page and independent editable-DOC checks.

Five Python controls cover text/displacement/page regressions, stale rasters,
missing/unexpected loss diagnostics, portable manifest hashes and CRLF/LF renderer
identities. PowerShell 7.5.3 locally passes the native-stderr/failure checks and
captures the actual Poppler banner identically to Python. CI additionally executes
the same helper tests under Windows PowerShell 5.1. Desktop Word and licensed Aspose
were not provisioned locally; their reference availability remains explicit.

Raw review reports are under ignored `artifacts/pr33-review`. Location paths are
materialized only for diagnostic observations, with bounded hash-based deduplication.
The single-core ten-page export smoke check remains subject to the existing latency
and memory caps; it is not a replacement for the alternating-revision CI gate.

## Stable 1.1.0 release qualification

The maintainer requested a stable release after the review corrections below.
The stable candidate keeps the same converter implementation and all **487 cases**;
the PDF producer assertion now expects version 1.1.0. Local source runs pass
487/487 without failures/skips on .NET 8, .NET 10 and the installed Framework 4.8.1.
Locked restore, warning-free Release builds of all five assets, API validation
against 1.0.0 and formatting verification pass. Reports are retained under ignored
`artifacts/release-1.1.0`.
The local three-package set also passes all 487 cases on each of five lanes:
.NET 8, .NET 10, Framework 4.8.1, and the explicit netstandard2.0 asset on .NET 8/10.
The transitive dependency audit reports no known vulnerabilities.

Stable publication still requires the complete native desktop CI matrix on the
final main commit, fresh source and exact-main-package execution on actual
Framework 4.7.2 with the documented legacy JIT, and authorization for all three
package IDs. Earlier alpha qualification is historical evidence. Preserve the
final immutable package hashes and runtime reports with the release.

## PR #5 review candidate — 1.1.0-alpha.2

The required suite now has **487 cases**, including **36 new review regressions**.
They check multi-paragraph/cell and nested fields in DOC/DOCX, numbered-heading
font registration, independent HTML reflow, Strict revision rejection, byte-array
result reporting, JPEG termination/trailing data, unsupported-image placeholders,
font weights/inheritance, DOCX schema ordering, cell tab origins, decorated tabs,
XPS retained features, first-page HTML stories and Lenient cell scaling.

All five library targets compile with warnings-as-errors. Locked restore, formatting,
release-version checks and the transitive dependency vulnerability audit pass.
The initial review build (`636621d`) passes **487/487** on each of .NET 8.0.32 and .NET 10.0.12
with zero failures/skips. Independent veraPDF 1.30.2 validation also passes
**38/38** archival corpus files; reports are under `artifacts/pr5-review/pdfa-final`.
The initial Linux x64 source reports are retained under ignored
`artifacts/pr5-review/source-final`; exact-package reports are retained under
`artifacts/pr5-review/package-tests`. Each full run must execute all 487 cases
without failures or skips. `OMP_THREAD_LIMIT=1` caps native OCR worker threads on
this five-CPU host without changing OCR assertions or runtime security settings.

The follow-up scaling correction anchors indented continuation lines to the cell's
text area and scaled header-table rows to the page margin. The existing regression
covers both cases; the 40 focused review/pagination/raster cases pass on .NET 8/10.
The refreshed final-candidate evidence is retained under `artifacts/pr5-final`.

The alpha.1 results below qualify the earlier revision. Publication of alpha.2
requires successful exact-main CI, current actual-Framework-4.7.2 legacy-JIT
source/package evidence, and authorization for all three NuGet package IDs.
Windows/ARM64/Android runtime and minimum-Framework execution are not claimed for
this Linux review task.

## Enterprise acceptance profile — 2026-10-16

The source suite now has **451 cases**, including the original 28 directions and
English OCR, two HTML directions, the 40-document authored enterprise corpus, font
catalog/TTC/CFF/GPOS and image pixel/alpha checks, header/footer/field pagination, aligned
tabs, HTML safety/decoration, Lenient geometry and three raster regression baselines.
The complete source suite runs on Windows 11 x64 .NET 8, .NET 10.0.12 and installed
Framework 4.8.1 (the net472 test asset), with no failures/skips. All five library
targets build with warnings-as-errors. The net462 managed-export executable also
runs on Framework 4.8.1; this does not qualify an actual 4.6.2 installation.

`eng/test-package.ps1` exercises the exact three-package preview on net8/net10/net472,
then explicitly substitutes the core netstandard2.0 DLL on both modern runtimes.
It checks suite completeness, framework identity, package manifests and font notices.
The package gate requires all **451 cases in each of those five lanes** with
zero failures/skips (2,255 package cases). Source verification separately requires all
451 cases in each of the three desktop lanes (1,353 source cases).
API validation compares against 1.0.0; its old provider references are resolved for
comparison without adding native dependencies back to core. No GAC/single-DLL or
strong-name gate applies under the consumer's NuGet-only deployment decision.

The 40 authored files and manifest are described in
[enterprise provenance](../tests/wordnet.Conversion.Tests/Fixtures/Enterprise/README.md).
`word-pages.csv` records independent Microsoft Word 16.0 / build 16.0.20430 counts
and hashes for all 38 valid inputs. **36/36 business cases match exactly**, including
1/5/25-page variants and ten-page text/table and text/table/image benchmarks. The two hostile-size stress inputs
are categorized separately before measurement; they are not business parity cases.
This meets the page-count tolerance on this authored set, not arbitrary template or
line-break parity. `eng/measure-word-reference.ps1` regenerates the Word measurements
with an isolated, read-only Word instance. Office is not a production dependency.
`eng/update-enterprise-goldens.ps1` regenerates
the embedded-font, 72-DPI PDF regression PNGs for review; the tests compare pixels
with a documented antialiasing tolerance. Native rendering remains test-only.

`tools/wordnet.ManagedExport.Probe` exports eight mixed text/story/nested-table/raster
fixtures using one converter. With a fixed Clock, **64 workers × 200 iterations**
produce 12,800 byte-identical PDF/HTML pairs and load no Skia/HarfBuzz/OCR provider.
Run from an isolated copy of the probe output so Windows file locks cannot interfere
with subsequent builds. A 4 GiB GC heap cap bounds the stress process; this is not a
per-document memory guarantee. The probe reports elapsed time and sampled memory.

The BenchmarkDotNet 0.15.8 project supports the authored ten-page text/table/image fixture:

```powershell
$env:WORDNET_BENCHMARK_INPUT = (Resolve-Path tests/wordnet.Conversion.Tests/Fixtures/Enterprise/benchmark-10-pages-images.docx).Path
dotnet run --project tools/wordnet.Benchmarks -c Release -- --filter '*' --job short --warmupCount 3 --iterationCount 5 --artifacts artifacts/enterprise-audit/benchmarks
```

The same project contains a qualification harness using the common 1.0 stream API.
`eng/run-performance-gate.ps1 -ReferenceRevision <commit>` archives the reference,
builds the identical current harness against both revisions, and alternates two warm
workers per variant on the same runner with a verified single-processor affinity.
Each worker warms 20 conversions and measures 31; output must contain ten pages,
ten tables and two images. The comparison pools 62 measured samples per variant.

On Core Ultra 9 185H / Windows 11 x64 / .NET 10.0.12, candidate median was **75.796 ms**
versus **141.330 ms** for 1.0.0 (ratio **0.5363**), on affinity `0000000000000001`.
Maximum sampled managed heap was **25,058,640 bytes**. The more conservative upper
bound, collected heap before conversion plus all managed bytes allocated during it,
was **40,122,840 bytes**. Both are below 100,000,000 bytes. This bound includes
temporary allocations even when collected; it is distinct from process RSS.
Fixture SHA-256: `258aa1dccf93e95b3442b3a642cd3f32e15ec32cb971cc6cccb38b01188e0c11`.
Raw local reports are under ignored `artifacts/performance-completion1/measurements`.
These measurements identify the exact assembly hash, including an uncommitted build.

CI runs the same comparison against the PR base (or previous pushed revision), on
the same hardware/runtime/input, and rejects median ≥300 ms, managed upper bound or
observed peak ≥100 MB, or a regression >25%. Seven guard regressions in
`eng/test-performance-gate.ps1` reject incomplete, mismatched and failing evidence.
Historical BDN text/table-only measurements were 60.887 ms PDF / 1.300 ms HTML;
they had no image or affinity restriction and are not the acceptance measurements.

`eng/validate-pdfa.ps1` exports all 38 valid authored fixtures with PDF/A-2b and
outlines, then validates each with veraPDF 1.30.2, pinned by image digest. **38/38 pass**.
CI repeats this independent check and uploads its XML. Tests also assert metadata
uniqueness, embedded/selectable text, compression controls, actual-page hierarchical
outlines, PNG prediction/alpha and link flags. Tagged PDF remains optional/unimplemented.
PNG admission bounds chunk lengths and zlib expansion to the declared scanline
geometry (including Adam7) before provider allocations, verifies Adler32 and checks
cancellation. Regressions reject excessive IDAT expansion and absent chunk payloads.

Native Ubuntu 24.04 x64 CI also passed the comparable single-core gate: 28.559 ms
median versus 53.861 ms baseline, ratio 0.5302, conservative managed bound 38,258,104
bytes. Run 38820196850 retains the measurements; its separate desktop jobs failed
at the probe RID restore and were corrected independently.

The [final implementation CI](https://github.com/kirakosyan/word.net/actions/runs/38822484354)
passes all 26 Windows/Linux x64/ARM64 source/package lanes: 11,726 cases, no skips,
plus the independent PDF/A, performance, concurrency and resource gates. The older
qualification benchmark now preserves the original 28 directions and DOM-validates
both new output-only HTML directions instead of trying to read HTML back as Word.

Actual Framework 4.7.2 x64 (release 461814) separately passes **451/451**, including
OCR, with the documented legacy JIT (`compatjit.dll` observed). The software-emulated
run took 1479.881 seconds; native prerequisites were copied to the guest's local disk.
The exact net462 package asset also passes managed-only mixed PDF/HTML probes there.
The audit records package hashes, the retained earlier failure and evidence locations.

The Android ARM64 test app compiles in CI. No new Android device execution, actual
4.6.2 runtime, macOS or AOT qualification is claimed. Compilation or VM startup is
never treated as runtime qualification.

## Historical 1.0 matrix

All **28 directions** run real acceptance tests:

- DOC ↔ DOCX: two.
- Each DOC/DOCX ↔ PDF, Markdown, TXT, AZW3, EPUB and MOBI: 24.
- DOC/DOCX → XPS: two.

Four more cases cover **scanned and mixed PDF → DOC and DOCX**.
They assert editable text and reading order. Embedding the page image cannot pass.
Missing OCR prerequisites fail with guidance, rather than silently skipping.

The 1.0 suite contained **348 active cases**. [CI run 38629510750](https://github.com/kirakosyan/word.net/actions/runs/38629510750)
passes all nine native source and 17 exact-package runtime lanes: **9,048 passing cases,
zero failures/skips**. These include native Windows/Linux x64 and ARM64 .NET 8/10,
Windows x64 Framework, and explicit Standard-asset execution on both modern runtimes.
All Windows/Linux x64 mutation, concurrent conversion and OCR resource campaigns pass.
The dated 301-case reports below describe the earlier converter revision.

The 1.0 release scope excludes Android and macOS at the maintainer's request. Historical
Android results remain evidence for those earlier builds; they are not current support claims.

Current 1.0 source and local package qualification also execute all 348 cases on actual Framework 4.7.2
x64 (release 461814), using the explicit legacy JIT application configuration. Every
case passes with no skips. The environment records `compatjit.dll` 4.7.3460.0 and the
unmodified configuration hash. See [minimum-runtime evidence](minimum-runtime-qualification.md#current-10-desktop-qualification--2026-10-14).

The suite also covers Unicode, structural formatting, long ebook records, independent
DOCX validation, PDF wrapping/pagination/glyph bounds, XPS resources, strict loss,
streams, cancellation, limits and malformed/hostile input.

## Reproduce locally

Install the SDK in `global.json` and .NET 8/10 runtimes. In one PowerShell process:

```powershell
# Linux: install libtesseract5, libfontconfig1, fontconfig and fonts (see below).
./eng/prepare-ocr-tests.ps1
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore
```

Setup sets `WORDNET_TESSDATA` and `WORDNET_TESSERACT_LIBRARY` in that process.
Windows prerequisite/model downloads are SHA-256 pinned. Override `-NativeLibraryPath`
for a custom library; Linux/macOS use locally installed Tesseract 5. Conversion itself
never downloads prerequisites.

On Windows ARM64, provision x64 OCR for the installed x64 Framework host:

```powershell
./eng/prepare-ocr-tests.ps1 -Architecture x64
dotnet test tests/wordnet.Conversion.Tests -c Release -f net472 --no-build --no-restore -- RunConfiguration.TargetPlatform=x64
```

Switch setup back to ARM64 before native modern tests. Framework tests run only on
Windows. Agents prefix commands with `rtk` per AGENTS.md.

## PR #3 review regressions — 2026-10-14

Forty-seven new cases cover Word-default tables that overhang text margins, physical-page
rejection, negative table indentation, omitted grid columns, wrapped cells, unspecified
grid widths, absolute DOC row edges, raw DOC orientation operands, signed vertical
margins, HTML span/empty-table compatibility, row headers, and default versus opt-in OCR
column order with paragraph boundaries. Test DOCX/EPUB markup is authored independently
in `ProductionReviewTests`; DOC operands/row boundaries are inspected or modified
according to published binary fields, independently of the production reader.

All **348 cases pass**, without failures/skips, on AK-PC Windows x64 .NET 8, .NET 10
and the installed Framework **4.8.1** host targeting `net472`. Locked solution restore,
warning-free Release builds of all four library targets, and formatting checks pass.
Reports are retained under ignored `artifacts/pr-review/test-results/`.
Actual Framework 4.7.2 and Android device execution were not repeated for these converter
changes. The earlier 301-case minimum-runtime results below do not qualify the new revision.
Android API 21 remains unresolved and further device/emulator work is deferred by the user.

Apache POI 5.4.1 passes both `--check-layout` and `--check-layout-corpus`, including new
assertions for the actual portrait `0x01` and landscape `0x02` operands. Its high-level
`getDmOrientPage()` exposes a Boolean, so the verifier uses POI's section index and sprm
parser on the original section bytes to distinguish the published values. The managed
suite also checks emitted bytes directly and imports explicit `0`, `1`, and `2` operands.

Framework runner checks pass on Windows PowerShell 5.1 and PowerShell 7, including a
simulated transient loader-enumeration error followed by a module appearing after one
second. The polling uses the existing overall deadline. Android process-command checks
cover plain toolbox `ps` on API 21/25 and toybox `ps -A` on API 26/36 without device execution.
These are harness checks, not platform conversion qualification.

The standalone Framework runner also passes all 348 cases on installed Framework 4.8.1,
with actual JIT-module evidence. Its generated ZIP includes the new polling helper and
the current required-case count. Android locked restore and Release APK compilation pass;
the updated harness was not executed on a device.

The locally packed validation version `0.1.0-alpha.10.review.1`, built from converter
commit `0f039b3`, passes all 348 cases on each of five asset/runtime lanes: `net8.0`,
`net10.0`, `net472` on installed Framework 4.8.1, and explicit `netstandard2.0` loading
on .NET 8 and .NET 10. Restore uses an isolated cache and a distinct validation version
to avoid reusing an earlier unpublished alpha.10 package or its lock-file hash.
The preserved archive is `artifacts/pr-review/wordnet.0.1.0-alpha.10.review.1.tested.nupkg`,
SHA-256 `e06105dac7fe8277a3c9f38e2d1cae1a0f2a0c698226984d1cddf8e68a06d63b`.
Package structure, notices and framework/API checks pass; nothing was published.
Reports are under `artifacts/pr-review/package-results/`.

## Production candidate — initial alpha.10, 2026-10-13

The shared suite now has **301 active cases**, preserving all 28 directions and original
scanned/mixed OCR cases. New assertions cover section geometry/empty final sections,
paragraph spacing/indent inheritance, keep controls, merged multi-paragraph tables,
repeating headers, long-row splitting, malformed-provider regressions and runtime asset
identity. `eng/required-tests.txt` is the common minimum used by CI and device/Framework
runners; missing cases and skips fail qualification.

`LayoutCorpusTests` compares every output glyph baseline to an independently positioned
ReportLab reference within 0.03 points. Apache POI independently reads exported DOC
layout/table records. Two Poppler-rendered reference-corpus pages have been visually
inspected. The [fixture manifest](../tests/wordnet.Conversion.Tests/Fixtures/README.md)
records producers, fixed fonts, licenses, source revisions and hashes.

Eight OCR scenarios run to both DOC and DOCX: clean, rotated 90/180 degrees, skewed,
noisy, low-resolution, two-column and an independently supplied Tesseract test scan.
The required normalized CER is ≤3%, WER ≤8%, plus editable text and column reading order.
The local ARM64 run records CER/WER zero for all 16 outputs; desktop TRX contains
`OCR_METRIC` lines. This is a bounded regression corpus, not a general OCR benchmark.

The new qualification tool runs 1,024 deterministic mutations in separate processes,
four-way concurrent conversions across all 28 directions and isolated OCR memory/deadline
checks. See [hosting](hosting.md) for commands, thresholds and limitations.
CI repeats the campaigns on native Windows/Linux x64 and uploads JSON measurements.

`eng/test-package.ps1 -Version 0.1.0-alpha.10 -Commit <full-source-sha>` verifies one
packed artifact and runs the complete .NET 8/10 suite against it. `-IncludeFramework`
adds Windows Framework. It then extracts that archive's `netstandard2.0` DLL and runs
both modern suites again, with a required runtime-identity assertion. CI performs this
on four native desktop platforms using the same package artifact. API validation compares
all four targets and the published alpha.9 baseline. These checks do not publish a package.

Implementation commit `264a29c40ddc74b6c97f198fcb2e6b934527faf9` passes
[native desktop CI](https://github.com/kirakosyan/word.net/actions/runs/38466027553):
all nine source runtime lanes and 17 exact-package lanes pass 301 cases each, with no
failures/skips (7,826 cases total). The package lanes include explicit Standard-asset
execution on both .NET 8 and 10 for every native Windows/Linux architecture. CI also
passes 1,024 isolated mutations, 896 concurrent measured conversions and 16 isolated OCR
cases on each native x64 OS. JSON reports include library hashes, runtime, duration and
sampled memory; see [production measurements](production-readiness.md#measurements-and-limits).

The CI package's SHA-256 is
`5037daf3f5a770c443dcd143b56f701cb9ec37c2135b69a21c4f8115892a3f5d`.
Local Windows source and packed suites also pass. The Samsung SM-S926B/Android 16 ARM64
device passes all 301 source and locally packed cases on .NET 10.0.12. It also passes
all 301 cases against the downloaded CI artifact with the SHA-256 above, using a separate
package cache and candidate lock file. That makes 8,127 passing desktop/device cases for
the CI artifact/source qualification set. Two initial
device failures were test-data integer-to-byte binding differences in the reflection
runner; typed byte constants now execute those malformed-PDF tests. Earlier device
attempts timed out while the activity was suspended. The harness now keeps its visible
screen awake and records per-test progress; unlocking/bringing it forward remains
necessary if the device's keyguard suspends the app.

Local Framework is installed 4.8.1 (release 533509), not actual 4.7.2. Actual Framework
4.7.2 and Android API 21 remain open gates; macOS is deferred. Evidence is under
`artifacts/production-tests/`, including downloaded CI reports, package and assembly hashes.

The returned standalone-bundle report from an additional Windows x64 PC passes all
301 cases with no failures/skips in 15.08 seconds on 2026-10-13. Its test case names and
both assembly hashes match the supplied bundle. The environment records Framework
release `533509`, which is [Framework 4.8.1 on Windows 11](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#net-framework-481),
and `exactFramework472: false`. This adds execution evidence for the installed newer
runtime; it does not close the actual 4.7.2 gate. The retained reports and verification
summary are under `artifacts/production-tests/returned-windows-x64-20261013/`:

- TRX SHA-256: `d8be41134ce654fd1a593127876ad1d6cffd1f20360e15a01cb1a30baa4bf258`.
- Environment SHA-256: `765230beeaaf687dcb08ee0949cb35df959229a68d33b89cd20761983e4d874f`.
- Library SHA-256: `271c239235216397ffa92a061e7f21075e5dd1945f9dea6518f7f40ecea0006a`.
- Test executable SHA-256: `782f9f67cdd971afe170d747f876dd0e6448abff4fec1d21796b5596a3ea1609`.

`eng/run-framework-tests.ps1` can run the complete prebuilt x64 output on another Windows
PC without an SDK and records assembly hashes and the Framework release key. Add
`-RequireFramework472` to enforce the actual minimum-runtime gate. See the
[API/support policy](api-compatibility.md) for copying the runner and prerequisites.

## AK-PC runtime investigation — 2026-10-14

Library and conversion test sources remain at `70bfc5585d4544bac89e7246d88cb5fc0a31c5e0`.
The conversion implementation is unchanged. Compatibility harnesses now select
explicit runtime configurations and record them in qualification evidence.

| Environment | Executed result |
| --- | --- |
| Native AK-PC .NET 8 / .NET 10 | 301 passing cases each, no failures/skips |
| Native AK-PC Framework 4.8.1, release 533509 | 301 passing cases, no failures/skips; final standalone runner: 42.477 seconds |
| Actual Framework 4.7.2, release 461814 | 301 executed, 268 passed, 33 failed, no skips; reproduced under Hyper-V with CLR/JIT 4.7.4150.0 and default JIT settings |
| Actual Framework 4.7.2, explicit legacy x64 JIT | 301 passed, no failures/skips; source run 21.193 seconds, packaged asset run 19.395 seconds on CLR/compatjit 4.7.3460.0 |
| API 21 ARM64 software emulator, .NET 10.0.12 interpreter | 301 passing source cases, no failures/skips; eventual report collected after the original runner timed out |
| API 21 ARM64 default JIT | Startup signal 11 before tests, also reproduced by a minimal app without WordNet/provider references |
| API 21 ARM64 interpreter repeat runs | Native graphics crashes after 141 cases (workload 36.1.69) and 106 cases (36.1.2); no complete report |

The Framework runs use library SHA-256
`b00a533397bc7bc250353f56e6a3644cb722f48b46f4c27d02b63f6eac43ae04`
and test executable SHA-256
`a6805040c166b22a2b4a04ec371948d34e4754f0b01d23bfc3d3f3bd64bb8f79`.
The interpreter APK SHA-256 is
`906aa507d2a0c42f63a1920c2ee8233d5f75e820b54a7353ad6b40a6739cc9e7`.
That APK predates the new harness metadata and timings; its execution mode was
recorded at build time and matched against the installed APK before report collection.
The packaged 4.7.2 run used archive SHA-256
`5e12d8a77c93797f23a081834a4002e8954cc967dcedd31dae6152420ce20b75`.
Its environment report confirms `compatjit.dll` loaded, records the child-only legacy
setting and hashes the unmodified application configuration. The archive's Framework
asset and the restored consumer's library are byte-identical. Default-JIT 4.7.2
operation is still unqualified; consumers must select the documented application setting.
API 21 source results add no exact-package or physical-device qualification. See the
[runbook](minimum-runtime-qualification.md) for reproduction and limits.
The later native graphics crashes prevent treating the earlier interpreter pass as
a repeatable baseline. Further Android investigation stopped at the user's request;
the API 21 failure remains unresolved.

Reports are retained under ignored `artifacts/runtime-diagnosis/`, including
`host-tests/`, `ak-pc-native-final/`, `framework472-hyperv/` and the Android baseline
XML/status/build manifest. The original software-emulated failures remain in
`artifacts/ak-pc-qualification/`. VM disks/system images are disposable after reports
are archived; they are not release artifacts.

The Framework runner now records CLR/JIT file versions and known diagnostic overrides,
rejects those overrides for the actual-minimum gate, enforces an external process
deadline, preserves previous reports and captures both output streams. Direct process
ownership fixes a missing exit code observed with Windows PowerShell 5.1 redirection.
`eng/test-framework-runner.ps1` verifies timeout rejection, termination of the exact
worker, fast failure exit-code preservation and stale-report rejection on Windows
PowerShell 5.1 and PowerShell 7 locally. CI is configured to repeat the checks on
Windows x64. Its negative fixtures are not conversion results.

The Android runner handles a UTF-8 BOM transported by ADB, records the APK hash and
requested execution mode before startup, checks the report's actual mode/API/architecture,
and detects process exit without waiting for the full suite deadline. The app records
case arguments, durations, API and execution mode. `eng/test-android-report.ps1` checks
BOM/plain XML and rejection of incomplete or non-report output; CI includes those
parser checks without requiring an Android workload. The rebuilt JIT app reproduced
the API 21 crash, and the updated runner detected its exit without a complete report.
Removing `am start -W` lets the host own the startup wait; that legacy Android command
had remained blocked after the native crash.

The evidence archive is `artifacts/runtime-diagnosis/qualification-evidence-20261014.zip`,
SHA-256 `b3a969f0ed9d242f5bd4db340edd9f72960eede61523e9cf21b64024cd7e1fbe`.
After verification, both Windows VM disks, the Hyper-V registration, Docker qualification
container/image and isolated Android system images were removed at the user's request,
reclaiming 31.29 GiB on the host. Docker Desktop and unrelated caches remain installed.
`artifacts/runtime-diagnosis/cleanup-status.json` records the completed cleanup.

The final repair evidence is retained in
`artifacts/runtime-repair/qualification-evidence-20261014-final.zip`, SHA-256
`b4aeaf076fbe98cdd72777fe2f0bb2cd06823180027105bb772fb6d5965bd119`.
Its 41 entries include a hash manifest, source/package Framework reports, runner
negative checks, and both later Android native-crash reports. The exact tested NuGet
archive is preserved separately as
`artifacts/runtime-repair/wordnet.0.1.0-alpha.10.tested.nupkg`.
The repair VM and its installation media (38.37 GiB of file lengths), temporary Docker
debugger container, and isolated API 21 emulator directory were removed after preserving
evidence. Shared Android SDKs, Docker Desktop and unrelated caches were retained.

## PR review regressions — alpha.9, 2026-10-13 (prior revision)

That suite contains **236 cases**, including all 28 directions, the four original OCR
acceptance cases, and 39 review regressions. The new tests check DOC image-store records
and nested numbering against binary field definitions, zero starts and list restarts,
linked numbering styles and cycles, style inheritance, moved text, table-header emphasis,
EPUB URL handling and image scaling, PDF text-matrix scaling, Kindle output budgets,
multi-page confidence including blank scans, concurrent OCR and final-write cancellation.

All 236 cases pass without failures or skips on each of these runtime lanes:

| Runtime | Evidence |
| --- | --- |
| Windows x64 | Hosted Windows Server 2022: .NET 8, .NET 10 and Framework |
| Windows ARM64 | Hosted Windows 11 and local ARM64: .NET 8 and .NET 10 |
| Linux x64 | Native hosted Ubuntu 24.04: .NET 8 and .NET 10, default runtime settings |
| Linux ARM64 | Native hosted Ubuntu 24.04: .NET 8 and .NET 10, default runtime settings |
| Android ARM64 | Samsung SM-S926B, Android 16, .NET 10.0.12; 236 shared cases including OCR |

The [desktop CI run](https://github.com/kirakosyan/word.net/actions/runs/38442966251)
tests implementation commit `b39d3ff`; its nine reports contain 2,124 passing cases.
Local Windows reports and the device report are under `artifacts/test-results/review/`,
with downloaded CI reports in its `ci/` directory. The local x64 Framework host also
passes all 236 cases. This closes the earlier native Linux x64 qualification gap for
the documented profile; broader fidelity, minimum OS versions and macOS remain deferred.
The updated OCR loader retains native libraries for process lifetime, and the prior
Linux x64 exit-139 crash did not recur in these complete native runs.

Locked restore, all four library targets, formatting, package API compatibility and
packing pass. Apache POI independently validates the revised DOC image records and
formatting. The Android incremental build initially failed at activity startup; a clean
`dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:Rebuild`,
followed by `-t:SignAndroidPackage`, regenerated the app and all 236 device cases passed.
The Android workflow now provisions its pinned native libraries and language data before
building the shared test application; its [successful CI job](https://github.com/kirakosyan/word.net/actions/runs/38442966308)
remains compilation-only.

## Formatting and images — alpha.8, 2026-10-13 (prior revision)

All **197 cases pass without failures or skips** on each of these eight runtime runs
(1,576 passing cases total):

| Runtime | Execution evidence |
| --- | --- |
| Windows ARM64 | .NET 8 and .NET 10, native processes |
| Windows x64 Framework | Installed Framework, x64 test host on the ARM64 machine |
| Linux ARM64 | Ubuntu 24.04 container, .NET 8.0.31 and .NET 10.0.12, default runtime settings |
| Linux x64 | Ubuntu 24.04 container under QEMU, .NET 8.0.31 and .NET 10.0.12, scoped `DOTNET_EnableWriteXorExecute=0` |
| Android ARM64 | Samsung SM-S926B, Android 16, .NET 10.0.12; shared suite and native English OCR |

The suite grows from 150 to 197 cases: 48 new formatting/image/defensive cases replace
one former Markdown-list-loss case with editable-list coverage. All 28 directions and
four scanned/mixed OCR cases remain active. Assertions cover fonts/colors, style inheritance,
table geometry/padding, editable lists, real PNG/JPEG pixels and bytes, dimensions,
pagination, independent DOC/PDF/EPUB inputs, malformed images and strict destination safety.

All four library targets build without warnings. Locked solution/Android restores,
formatting and package API compatibility checks pass. The inspected alpha.8 package
contains the four DLL/XML pairs, README, MIT/dependency/font notices, and no test-only
dependencies. Separate NuGet consumers build for all four targets and execute representative
formats, font/color/table/list/image assertions and English OCR on Windows Framework and
.NET 8/.NET 10. Explicit .NET Standard 2.0 loading is identified and exercised on .NET 8/10.
Runtime dependencies are unchanged from alpha.7.

Apache POI 5.4.1 independently reads the generated DOC title's 18-point RGB font,
unequal 120/240-point cells, list start/nesting and exact image bytes/physical dimensions.
The composite PDF was rendered with Poppler and visually checked for spacing, image
aspect ratio, clipping and overlap; numeric assertions supplement this single-page review.

Reports are under `artifacts/test-results/alpha8/`; `verification.json` records report
counters/hashes and package/assembly hashes. Linux containers reuse the alpha.7 prerequisite
images, then overlay the current sources (excluding host bin/obj), restore locked
dependencies and rebuild all targets before executing. They do not run the old assemblies.
Android's stopped activity needed the screen awakened and its own activity brought forward
before the suite completed. No conversion/OCR case was bypassed.

Native Linux x64/default-runtime qualification, minimum OS/runtime versions, broad
real-world fidelity and noisy OCR remain separate work. macOS remains deferred.

## Dependency refresh checks — 2026-10-13

Alpha.7 updates the graphics/native dependencies and PDF decoders and migrates the
tests to xUnit v3. See [the dependency audit](dependencies.md) for exact versions.

- All four library targets build with no warnings. Solution and Android locked restores,
  formatting and package API compatibility checks pass.
- All 150 tests pass with no failures or skips on Windows ARM64 .NET 8/.NET 10,
  Windows x64 .NET Framework and Android ARM64 (Samsung SM-S926B, Android 16).
- Linux ARM64 (Ubuntu 24.04 container) passes all 150 tests on each .NET 8.0.31 and
  .NET 10.0.12 with default runtime settings.
- Linux x64 (Ubuntu 24.04 container under QEMU) also passes all 150 tests on each
  .NET 8.0.31 and .NET 10.0.12. It uses the previously documented container-local
  `DOTNET_EnableWriteXorExecute=0` workaround; native x64/default-runtime qualification
  remains separate. Across these eight runtime runs, 1,200 tests pass and none skip.
- A separate consumer of the alpha.7 NuGet builds for all four targets. Representative
  conversions, DOC formatting/tables and real English OCR pass on Windows x64 .NET
  Framework and Windows ARM64 .NET 8/.NET 10,
  including explicit loading and runtime identification of the .NET Standard 2.0 asset.
- Package inspection confirms that updated runtime pins are present in the dependency
  groups, with no test packages or implicit framework reference packages shipped.
- NuGet reports no deprecated packages or known vulnerabilities for the solution or
  the Android project, including transitive dependencies. Reports and runtime results
  for this build are under `artifacts/test-results/alpha7/`.
  `verification.json` records each report's counters/hash and the package/assembly hashes.

## Namespace migration checks — 2026-10-13

Alpha.6 changes the root namespace to `WordNet`, including embedded font resource
names. The NuGet package ID and assembly name remain `wordnet`. Consumers must update
their imports/qualified names and rebuild.

- Locked restore, Release build for all four library targets, formatting and package
  API compatibility checks pass without warnings.
- All 150 tests pass with no failures or skips on Windows ARM64 .NET 8/.NET 10,
  Windows x64 .NET Framework, and Android ARM64 (Samsung SM-S926B, Android 16).
  The Android app completed after relaunching its stopped activity.
- A separate consumer of the alpha.6 NuGet compiles for all four targets with
  `using WordNet;`. Representative conversions, DOC formatting/tables and English OCR
  pass on .NET 8/.NET 10, including explicit loading of the .NET Standard 2.0 asset.
- The package contains the four matching DLL/XML pairs and `WordNet` API documentation.
  Reports are under `artifacts/test-results/alpha6/`.

Linux was not rerun for this namespace migration; the alpha.5 results below describe
the previous build. macOS remains deferred.

## Alpha.5 local evidence — 2026-10-12

| Check | Result |
| --- | --- |
| Release build | All four library targets, no warnings |
| Windows ARM64 .NET 8 / .NET 10 | 150 tests per target, 0 failed/skipped |
| Windows x64 Framework / .NET 10 | 150 tests per target, 0 failed/skipped, x64 processes on the ARM64 host |
| Linux ARM64, Ubuntu 24.04 container | 150 tests on each .NET 8.0.31 / .NET 10.0.12, 0 failed/skipped |
| Linux x64, Ubuntu 24.04 container under QEMU | 150 tests on each .NET 8.0.31 / .NET 10.0.12, 0 failed/skipped; container-local runtime workaround described below |
| Android ARM64, Samsung SM-S926B, Android 16 | Same 150 tests on .NET 10.0.12, 0 failed/skipped, including 28 directions and four OCR cases |
| DOC interoperability | Apache POI 5.4.1 verifies exported headings, emphasis, editable cells/rows, repeating headers and left/center/right alignment; independent input fixtures |
| MOBI / KF8 interoperability | KindleUnpack recovered generated text; no device rendering check |
| PDF visual check | Poppler-rendered page layout and alignment inspected; numeric wrapped-line and table-cell alignment assertions; broad visual corpus still pending |
| Formatting/package | Locked restore, formatting and framework API compatibility checks |
| Packed alpha.5 | Full 150-test suite on Windows Framework/.NET 8/.NET 10, Linux ARM64 and emulated x64 .NET 8/.NET 10, and Android ARM64; separate representative consumers also pass |
| Explicit .NET Standard asset | Runtime identity verified; new format engines and OCR executed on .NET 8 and .NET 10 |
| Package inspection | Four DLL/XML pairs, README, MIT/dependency notices and embedded-font license/provenance present |

The Framework host uses the installed runtime, not necessarily the oldest 4.7.2 runtime.
These results cover the specified profiles, not general rich DOC/KF8 fidelity or real-world
OCR accuracy. Windows x64 .NET 8, the oldest claimed Framework/Android runtimes and broader
device/native-codec coverage remain unqualified. Linux x64 now executes the complete suite
under QEMU with a container-local executable-memory workaround. This does not establish
native x64 performance or default-runtime qualification; a native Linux x64 CI run is still
required for those claims. Four Windows/Linux CI lanes are
configured; configuration is not evidence of remote execution. macOS is deferred.

## Linux and Android runners

`eng/linux-tests.Dockerfile` restores locked dependencies, builds all four library targets,
provisions Tesseract and registers the pinned Noto Sans faces for PDF fallback. Its test
runner executes .NET 8 and .NET 10. For example, from the repository root:

```sh
docker build -f eng/linux-tests.Dockerfile -t wordnet-validation .
docker run --rm --mount type=bind,source=/absolute/path/to/results,target=/results wordnet-validation
```

For **local x64 emulation on the ARM64 host only**, the default .NET executable-memory
aliasing failed inside MSBuild/reflection before tests could run. Disabling hardware
intrinsics or ReadyToRun did not fix it. The following scoped workaround allowed all
four library targets to build and the .NET 8/10 conversion/OCR suites to complete:

```sh
docker build --platform linux/amd64 --build-arg DOTNET_EnableWriteXorExecute=0 -f eng/linux-tests.Dockerfile -t wordnet-validation:x64 .
docker run --rm --platform linux/amd64 -e DOTNET_EnableWriteXorExecute=0 --mount type=bind,source=/absolute/path/to/results,target=/results wordnet-validation:x64
```

The build argument applies only during the validation-image build. The runtime override
is passed to that test container, never to the library, NuGet, host or native CI lanes.
It disables .NET's W^X executable-memory separation; it is a validation workaround, not
a deployment recommendation. The setting is documented in the
[.NET runtime source](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/inc/clrconfigvalues.h).
Keep native tests on runtime defaults. The runner records architecture, runtime, override
and package mode alongside the TRX reports. Alpha.5 reports are under
`artifacts/test-results/alpha5/`; older reports in other directories describe prior builds.
The local `verification.json` records the package/assembly hashes and verifies that each
listed report contains all 150 passing cases, including 28 directions, four OCR cases
and 24 new alignment regressions. No conversion or OCR case is skipped.

On an ordinary Linux host, install `libtesseract5`, `libfontconfig1` and `fontconfig`, copy
`src/wordnet/Resources/Fonts/*.ttf` into `~/.local/share/fonts/wordnet/`, then run `fc-cache -f`.
The fontconfig-enabled Skia asset is required for rasterizing unembedded PDF text; the
former no-dependencies asset failed mixed-page OCR in the minimal Linux environment.

The Android app links the same desktop test sources and fixtures. It discovers every
Fact/InlineData case, awaits async tests and writes a machine-readable XML result.
It has no network permission. Prerequisites and outputs remain under ignored `artifacts/`.

```powershell
./eng/prepare-android-tests.ps1
dotnet restore tests/wordnet.Android.Compatibility --locked-mode
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage
./eng/run-android-tests.ps1 -AdbPath /absolute/path/to/adb
```

The runner installs only `org.wordnet.validation`, clears that app's previous report,
launches its tests and fails on missing results, failures or skipped cases. Add `-Serial`
when several devices are connected. It bundles Tesseract4Android native ARM64 assets and
English data solely as test prerequisites. The app is debuggable so ADB can retrieve its
private report, and disables trimming/AOT; those modes are not qualified by this run.

## Test the actual NuGet

Both test projects accept `WordnetPackageVersion` to replace their project reference
with the local package. Source and package modes keep separate lock files; package-mode
locks are generated under ignored `obj/packaged/`. The validation configuration maps
`wordnet` exclusively to `artifacts/packages` and other dependencies to nuget.org.

```powershell
dotnet restore tests/wordnet.Conversion.Tests -p:WordnetPackageVersion=0.1.0-alpha.9 --configfile eng/NuGet.Packages.Config
dotnet build tests/wordnet.Conversion.Tests -c Release --no-restore -p:WordnetPackageVersion=0.1.0-alpha.9
./eng/prepare-ocr-tests.ps1
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore -p:WordnetPackageVersion=0.1.0-alpha.9
```

Repeat for .NET 8 and Windows Framework with the appropriate OCR architecture. For
Android, pass the same property/configuration during restore and build, then use
`run-android-tests.ps1` normally. For the Linux runner, set `WORDNET_PACKAGE_VERSION`
after restoring/building that package configuration. Restore again without the property
to return to source development. Never treat a previously cached package as a new build;
use a new prerelease version when implementation changes.

## Fixtures and assertions

[Fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/README.md) records sources
and hashes. DOCX XML parts, EPUB/MOBI generators and ReportLab/Pillow PDFs are independent
of production serialization. AZW3 originates with wordnet and was externally unpacked;
it is not an independent writer fixture.

| Output | Current assertions |
| --- | --- |
| DOCX | Open XML SDK schema validation, editable text and supported structure |
| DOC | Compound storage, FIB/text, 17 formatting/table regressions, independent input and external output checks |
| Alignment | 24 additional regressions: independent DOC table input, Word/ebook exchange, DOCX defaults/styles/overrides, Markdown columns, PDF/XPS positions, wrapped lines and cell positions, invalid values and explicit loss |
| Appearance/images | 48 cases for font/color inheritance, editable lists, table geometry/padding, embedded raster bytes/pixels/dimensions, independent DOC/PDF/EPUB input, pagination and hostile resources |
| PDF | Parsed content, page count, glyph bounds, wrapping and baselines |
| XPS | Sequence/pages, dimensions, glyph text/coordinates and font resources |
| EPUB | Mimetype/container and XHTML text/structure |
| MOBI / AZW3 | PalmDB/MOBI headers, actual XHTML and supplemental external unpacking |
| OCR | Exact editable English text in expected reading order on scanned/mixed fixtures |

The original clean fixtures use exact matching. The alpha.10 corpus additionally measures
noise, columns, skew and rotation against numerical CER/WER thresholds. Complex tables,
forms and arbitrary real-world scan quality remain outside this qualification.

## Further qualification

Current regressions cover ZIP limits/paths/duplicates, XML entities/depth, encryption,
corrupt DOC/Kindle offsets, truncation, unsupported styling, resource limits, glyphs
and cancellation. Native recognition receives cancellation through Tesseract's progress
monitor; initialization, rasterization and other provider calls can still require a host deadline.

Expand the corpus for images/styles/sections/lists, metadata, Unicode/shaping, noisy OCR
and independent ebook producers. Pin fonts/renderers and numerical tolerances.
Round trips alone can hide matching reader/writer bugs.

Packing checks API compatibility. Inspect assemblies/XML docs, README/notices/licenses
and dependency groups. Execute representative packed consumers and explicitly load
the .NET Standard asset. Android device tests now exercise native loading, fonts/models
and conversions; broaden devices/API levels, memory checks and trimming/AOT coverage.
