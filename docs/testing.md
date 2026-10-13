# Conversion tests and qualification

## Active matrix

All **28 directions** run real acceptance tests:

- DOC ↔ DOCX: two.
- Each DOC/DOCX ↔ PDF, Markdown, TXT, AZW3, EPUB and MOBI: 24.
- DOC/DOCX → XPS: two.

Four more cases cover **scanned and mixed PDF → DOC and DOCX**.
They assert editable text and reading order. Embedding the page image cannot pass.
Missing OCR prerequisites fail with guidance, rather than silently skipping.

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

## PR review regressions — 2026-10-13

The suite now contains **236 cases**, including all 28 directions, the four original OCR
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

Exact OCR ground-truth matches imply zero character/word errors on these small fixtures.
They do not measure noise, columns, tables, rotation or real-world quality. Define
corpus-based numerical thresholds before production claims.

## Further qualification

Current regressions cover ZIP limits/paths/duplicates, XML entities/depth, encryption,
corrupt DOC/Kindle offsets, truncation, unsupported styling, resource limits, glyphs
and cancellation. Native OCR checks cancellation before/after the uninterruptible call.

Expand the corpus for images/styles/sections/lists, metadata, Unicode/shaping, noisy OCR
and independent ebook producers. Pin fonts/renderers and numerical tolerances.
Round trips alone can hide matching reader/writer bugs.

Packing checks API compatibility. Inspect assemblies/XML docs, README/notices/licenses
and dependency groups. Execute representative packed consumers and explicitly load
the .NET Standard asset. Android device tests now exercise native loading, fonts/models
and conversions; broaden devices/API levels, memory checks and trimming/AOT coverage.
