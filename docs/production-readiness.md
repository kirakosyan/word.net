# Production qualification — 1.0 history

## 1.4.0 release qualification

Version 1.4.0 includes the bounded P1/P2 and roadmap #99 profiles documented in the
[release notes](releases/1.4.0.md). Production publication requires successful
exact-main CI and immutable-package/minimum-runtime qualification, followed by the
existing Trusted Publishing workflow. PR checks do not select the release artifact.
The required suite is 1,050 cases without skips. Cryptographic features are absent;
advanced object/native/accessibility combinations and new platforms remain excluded.
The dated 1.0/1.2.0 evidence below applies to those releases only.

This document records the 1.0 qualification work and its historical evidence. For
current source capabilities, all five target assets, roadmap status and build-specific
test counts, use the [implementation plan](implementation-plan.md),
[fidelity profile](document-fidelity.md) and [testing evidence](testing.md).
The section/layout limits below describe that earlier release, not subsequent PRs.

The 1.0 work preserved `net472;netstandard2.0;net8.0;net10.0`, the `WordNet` API namespace,
independent MIT implementation, and Windows/Linux x64/ARM64 desktop scope.
Android and macOS are deferred by the maintainer for 1.0. This checklist records the
release profile and evidence; it does not imply unrestricted Word fidelity.

## Release gates

| Workstream | Required evidence | Current work |
| --- | --- | --- |
| Page/paragraph layout | Independent section, geometry, spacing, indentation and pagination fixtures; editable structure and PDF/XPS coordinates | Implemented next-page sections, empty final sections, inherited paragraph layout and keep controls; regression suite passes locally |
| Tables | Multiple paragraphs, horizontal/vertical merges, repeating headers, long-row pagination and explicit rejection of unrepresentable cases | Implemented shared cells and Word/EPUB adapters; PDF/XPS pagination and malformed-grid regressions pass locally |
| Independent corpus | Reproducible, licensed producer fixtures; independent output readers and visual comparisons with fixed fonts/tolerances | Apache POI input/export checks, schema validation, two-page ReportLab reference with 0.03-point glyph tolerance, Poppler visual inspection and independently supplied OCR scan |
| Security/resources | Deterministic malformed-input campaigns, process deadlines, concurrency/soak checks and recorded memory/throughput budgets | 1,024 isolated mutations and 2,800 concurrent measured conversions pass locally; provider error regressions added; native x64 CI repeats the campaigns |
| English OCR | Character/word-error and reading-order measurements on rotation, skew, noise and columns; regression fixtures for improvements | Eight scenarios × two Word targets pass CER ≤3% / WER ≤8%; optional orientation/deskew, conservative column order, cancellation monitor and isolated resource checks implemented |
| Deployment/API | Exact-package tests on claimed desktop platforms; actual Framework 4.7.2 with its explicit compatibility configuration; native setup and API policy | Four targets retained; alpha.9 API baseline enabled; all nine source and 17 exact-package/Standard lanes pass the 348-case suite; actual minimum-runtime evidence is recorded below |

## Scope boundaries

The stable profile must be explicit about supported features and preserve strict-mode
destination protection. Password protection, tracked changes/comments, editable
charts/equations, handwriting and additional OCR languages remain deferred. Original
Word pagination, arbitrary fonts, nested tables and unrestricted EPUB/CSS compatibility
are not implied by passing the supported-profile tests. Unsupported data must be rejected
or reported, never silently dropped.

Current page geometry supports next-page sections up to 22 inches with positive content
space. Other section-break modes produce a loss diagnostic. Fixed rendering retains
the pinned Noto Sans family. Vertically merged groups and non-splitting rows that cannot
fit one page fail explicitly. Oversized ordinary rows can split between text lines;
cell paragraphs retain their editable structure in Word and EPUB.

## Environment evidence

The 1.0 suite has 348 cases. [PR CI run 38629510750](https://github.com/kirakosyan/word.net/actions/runs/38629510750)
passes all nine native source and 17 exact-package runtime lanes: 9,048 passing cases,
zero failed or skipped. Platforms are Windows Server 2022 x64, Windows 11 ARM64 and
Ubuntu 24.04 x64/ARM64, on .NET 8/10 plus Windows x64 Framework. Explicit Standard-asset
runs verify runtime identity on both modern runtimes. The dated 301-case reports below
are historical and do not substitute for current-build minimum-runtime evidence.
OCR column reordering now requires the separate `ReadTextColumns` opt-in; ordinary OCR
retains native line and paragraph order. See [review regression evidence](testing.md#pr-3-review-regressions--2026-10-14).

The 348-case source and locally packed 1.0.0 suites also pass on actual Framework 4.7.2 x64, release
461814, with the explicit legacy JIT configuration and observed `compatjit.dll` version
4.7.3460.0. No cases fail or skip. The release process additionally requires the exact
main CI package to pass there before publishing. Default-JIT 4.7.2 is unsupported;
Android and macOS are outside the 1.0 release scope. See the
[current minimum-runtime evidence](minimum-runtime-qualification.md#current-10-desktop-qualification--2026-10-14).

The earlier 301-case suite passed on all nine native desktop runtime lanes: Windows/Linux
x64 and ARM64 .NET 8/10, plus the installed Windows x64 Framework. The same CI package
passes 17 desktop runs, including its explicit Standard asset on both modern runtimes
on all four platforms. Android 16 ARM64 passes all 301 source, locally packed and downloaded
CI-package cases. See [testing](testing.md) for build-specific evidence and artifact hashes.
The following historical 301-case investigation explains the runtime compatibility
configuration. Newer runtime execution never substitutes for actual-minimum evidence.
The returned standalone-bundle results from an additional Windows x64 PC pass all
301 cases with no failures/skips in 15.08 seconds. Assembly hashes and case names match
the supplied bundle. Its Framework release key is 533509 (4.8.1), so the actual 4.7.2
gate was still open for that report. Report hashes are recorded in [testing](testing.md).
The [minimum-runtime runbook](minimum-runtime-qualification.md) provides a Windows
Server 2019 Docker VM setup for an x64 test PC and an isolated API 21 ARM64 emulator
probe. These setup tools preserve the gates until actual complete reports pass.
On 2026-10-14 AK-PC passed all 301 source cases directly on installed Framework 4.8.1,
.NET 8 and .NET 10. Actual Framework 4.7.2 executed 301 cases with 33 failures under
both software emulation and hardware-accelerated Hyper-V. The same binaries pass on
4.8.1. Inspecting generated code confirmed repeated span accesses incorrectly reuse
one indexed byte. Selecting Microsoft's legacy x64 JIT application setting resolves
all 33 failures: both source and locally packaged assets pass all 301 cases on actual
4.7.2 with no skips. The final runner confirms `compatjit.dll` in the child process
and records the requested mode and configuration hash. Default-JIT 4.7.2 operation
remains unqualified; the explicit compatibility setting is required for this baseline.

The API 21 ARM64 probe reproduces the default JIT startup signal 11 on x64 AK-PC,
including in a minimal .NET Android app without WordNet dependencies. An interpreter
build completes all 301 source conversion/OCR cases with no failures/skips in the
software emulator. Its initial host runner timed out; the eventual report was collected
separately after verifying the installed APK hash. Interpreter evidence does not
qualify the default JIT or a physical API 21 device. The minimum gate remains open.
Subsequent interpreter runs with Android workloads 36.1.69 and 36.1.2 crashed in native
graphics after 141 and 106 completed cases, respectively. The earlier complete report
does not establish a repeatable baseline. Further Android investigation stopped at
the user's request; experimental runtime pins and default-mode changes were removed.
Temporary VMs and downloaded system images are removed after preserving reports;
their setup scripts remain reproducible.

## Measurements and limits

The 348-case PR CI run above passes these isolated campaigns on .NET 10.0.13:

| Native x64 campaign | Windows Server 2022 | Ubuntu 24.04 |
| --- | --- | --- |
| 1,024 deterministic mutations | Pass; maximum sampled process 59.5 MiB | Pass; maximum 84.3 MiB |
| 896 conversions, concurrency 4 | Pass; 13.82 s / 180.5 MiB | Pass; 9.68 s / 253.8 MiB |
| 16 isolated OCR conversions | Pass; maximum 2.13 s / 181.2 MiB | Pass; maximum 2.45 s / 222.9 MiB |

These are fixed-fixture measurements, not production throughput guarantees. The earlier
301-case campaign results are retained below for comparison.

The local and native desktop corpus runs recorded zero normalized character/word errors
in all 16 OCR outputs. Isolated native x64 OCR processes stayed below 232 MiB and 2.7 seconds
per case. The latest local 2,800-conversion, four-worker run completed in 16.28 seconds
with approximately 217 MiB peak working set. These measurements describe small fixed
fixtures on the tested machines, not production throughput or broad OCR accuracy. Machine-readable
reports under ignored `artifacts/qualification/windows-arm64/` record library hashes,
runtime, seeds and outcomes. CI uploads equivalent reports for native x64 platforms:

| Native x64 campaign | Windows Server 2022 | Ubuntu 24.04 |
| --- | --- | --- |
| 1,024 isolated mutations | Pass; maximum sampled process 59.2 MiB | Pass; maximum 99.9 MiB |
| 896 measured conversions, concurrency 4 | Pass; 11.06 s / 189.2 MiB peak | Pass; 8.23 s / 346.6 MiB peak |
| 16 isolated OCR conversions | Pass; maximum 2.25 s / 180.1 MiB | Pass; maximum 2.65 s / 231.1 MiB |

The mutation campaign found malformed DOC/PDF container exceptions and corrupt PDF font
resources that previously escaped the public input-error contract. Regression tests now
require useful `InvalidDataException` failures and unchanged destinations. Corpus passing
is not proof that every parser/native dependency is safe: use the isolation and admission
budgets in [hosting](hosting.md), and expand representative documents before deployment.

Do not publish a stable version or change the production status while any required gate
has missing evidence. Package publication remains a separate action.

## Published 1.2.0

This page retains 1.0 history. The [1.2.0 readiness ledger](release-1.2.0-readiness.md)
records the release commit's 829-case source/package, actual-4.7.2 and publication evidence. Historical
successful runs and hosted newer-Framework tests cannot satisfy a missing current
minimum-runtime run. Bucket 5 is merged design work, not additional runtime support.
