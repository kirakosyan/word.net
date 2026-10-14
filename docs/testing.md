# Conversion tests and qualification

## Active matrix

All **28 directions** run real acceptance tests:

- DOC ↔ DOCX: two.
- Each DOC/DOCX ↔ PDF, Markdown, TXT, AZW3, EPUB and MOBI: 24.
- DOC/DOCX → XPS: two.

Four more cases cover **scanned and mixed PDF → DOC and DOCX**.
They assert editable text and reading order. Embedding the page image cannot pass.
Missing OCR prerequisites fail with guidance, rather than silently skipping.

The current PR-review suite contains **348 active cases**. The dated 301-case platform
reports below describe the earlier converter revision, not the current review fixes.

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
