# Production qualification work

This work preserves `net472;netstandard2.0;net8.0;net10.0`, the `WordNet` API namespace,
independent MIT implementation, and Windows/Linux x64/ARM64 plus Android ARM64 goals.
macOS is deferred. This checklist is evidence tracking, not a production-readiness claim.

## Release gates

| Workstream | Required evidence | Current work |
| --- | --- | --- |
| Page/paragraph layout | Independent section, geometry, spacing, indentation and pagination fixtures; editable structure and PDF/XPS coordinates | Implemented next-page sections, empty final sections, inherited paragraph layout and keep controls; regression suite passes locally |
| Tables | Multiple paragraphs, horizontal/vertical merges, repeating headers, long-row pagination and explicit rejection of unrepresentable cases | Implemented shared cells and Word/EPUB adapters; PDF/XPS pagination and malformed-grid regressions pass locally |
| Independent corpus | Reproducible, licensed producer fixtures; independent output readers and visual comparisons with fixed fonts/tolerances | Apache POI input/export checks, schema validation, two-page ReportLab reference with 0.03-point glyph tolerance, Poppler visual inspection and independently supplied OCR scan |
| Security/resources | Deterministic malformed-input campaigns, process deadlines, concurrency/soak checks and recorded memory/throughput budgets | 1,024 isolated mutations and 2,800 concurrent measured conversions pass locally; provider error regressions added; native x64 CI repeats the campaigns |
| English OCR | Character/word-error and reading-order measurements on rotation, skew, noise and columns; regression fixtures for improvements | Eight scenarios × two Word targets pass CER ≤3% / WER ≤8%; optional orientation/deskew, conservative column order, cancellation monitor and isolated resource checks implemented |
| Deployment/API | Exact-package tests on claimed platforms; oldest Framework and Android APIs; native dependency setup; compatibility policy | Four targets retained; alpha.9 API baseline enabled; CI tests one immutable NuGet plus explicit Standard fallback; minimum-runtime gates remain open |

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

The active implementation is not yet release-qualified. The expanded 301-case suite
passes on local Windows ARM64 .NET 8/10 and the installed x64 Framework 4.8 host.
Linux ARM64 passed the preceding 296-case revision on both runtimes. Native CI and
Android device execution are being repeated for the completed candidate; see
[testing](testing.md) for final build-specific evidence.
An actual .NET Framework 4.7.2 environment and Android API 21 environment are requested
to verify the existing declared minimums; newer runtime execution is not a substitute.
For an additional Windows test PC, check its Framework release key: an installed 4.8
runtime does not close the 4.7.2 gate. The standalone runner records this distinction.

## Measurements and limits

The local ARM64 corpus run recorded zero normalized character/word errors in all 16 OCR
outputs. Isolated OCR processes completed in 0.69–2.36 seconds and stayed below 185 MiB
sampled working set. The 2,800-conversion, four-worker run completed in 18.01 seconds
with approximately 181 MiB peak working set. These measurements describe small fixed
fixtures on this machine, not production throughput or broad OCR accuracy. Machine-readable
reports under ignored `artifacts/qualification/windows-arm64/` record library hashes,
runtime, seeds and outcomes. CI uploads equivalent reports for native x64 platforms.

The mutation campaign found malformed DOC/PDF container exceptions and corrupt PDF font
resources that previously escaped the public input-error contract. Regression tests now
require useful `InvalidDataException` failures and unchanged destinations. Corpus passing
is not proof that every parser/native dependency is safe: use the isolation and admission
budgets in [hosting](hosting.md), and expand representative documents before deployment.

Do not publish a stable version or change the production status while any required gate
has missing evidence. Package publication remains a separate action.
