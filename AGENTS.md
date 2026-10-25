# Working on wordnet

## Scope and current milestone

This repository hosts an independent MIT-licensed .NET document-conversion library.
Implementation is authorized. All 28 original directions and English OCR have active baseline
tests; the merged bucket 4 profile extends the matrix to 36 with bounded HTML/RTF exchanges. Deliver independently tested fidelity increments and keep unsupported features
explicit. Never substitute fake conversion behavior or overstate platform qualification.

Read `README.md`, `docs/architecture.md`, and `docs/testing.md` before changing scope.
The root namespace is `WordNet` (PascalCase). The assembly name and provisional NuGet
package ID are `wordnet`. Keep namespace spelling consistent across all targets and examples.

## Non-negotiable boundaries

- Use independently authored code, published format specifications, and appropriately
  licensed dependencies. Do not copy proprietary implementation code or resources into
  source, tests, tools, or packages. Preserve dependency notices and fixture provenance.
- Preserve the MIT license and existing user changes.
- Preserve `net462;net472;netstandard2.0;net8.0;net10.0` unless the user changes support policy.
- The 1.0 release scope is Windows/Linux x64 and ARM64. Android ARM64 and macOS are
  deferred by the user; preserve portability without making release claims for them.
  Do not introduce a Windows-only dependency into the portable core.
- Word means DOC and DOCX. PDF/MD/TXT/AZW3/EPUB/MOBI are bidirectional with Word.
  XPS is output-only. Scanned-PDF OCR is required for the first converter release.
- First-release OCR is English printed text only; handwriting and additional OCR
  languages are deferred. Preserve Unicode support for ordinary document text.
- Prioritize core content. Password encryption/decryption and DOCX/PDF signatures
  are cancelled by the owner (#74–#81) and remain unsupported. The owner-authorized
  P2 implementation permits the bounded chart/OMML preservation, static rendering
  and immutable public editing stages in #64–#70, and qualified ordinary native
  bidi/Devanagari/tagged paragraphs in #71–#73. Each stage needs its independent
  acceptance evidence before a support claim; unsupported object types, native
  stories, conformance combinations and platforms remain rejected. The P1 implementation permits bounded ODF 1.3
  DOCX exchange and opt-in DOCX body/cell text revisions with classic comments,
  explicit decisions/recording and matching-structure text comparison. Moves,
  property/structural revisions, modern comments and history in other stories/formats
  remain unsupported. Require explicit diagnostics or failure, never silent data loss.
- Declare support only for implemented and verified capabilities. Compatibility metadata,
  successful compilation, and skipped tests are not proof of working conversions.

## Roadmap #99 source extension

The owner authorized implementation of numbered issues #87–#98, initially with a PR and no package
publication, then authorized merging and 1.4.0 publication after exact-commit release
gates. The 1.4.0 source profile in `docs/roadmap-99-implementation.md` extends
some of the released 1.3.0 boundaries above: plain structural paragraph/row revisions,
bounded native tables/stories, separate managed-object accessibility/archive output,
scalar fields, chart binding and direct HTML/ODT PDF export. Keep the new subset limits
explicit; do not treat this as permission to expand other combinations or platforms.

## Engineering quality

- Keep the public API small, documented, and independent of platform/provider types.
  Preserve caller stream ownership and make cancellation/resource behavior explicit.
- Keep all targets' public APIs compatible. Do not break the `netstandard2.0` fallback
  by accidentally using newer runtime APIs in shared code.
- Keep nullable analysis, SDK analyzers, deterministic builds, and warnings-as-errors enabled.
  Address warnings; use narrowly scoped suppressions only with a specific reason.
- Put dependency versions in `Directory.Packages.props`; commit updated lock files.
  Review license, maintenance, native assets, security, and all target frameworks before
  selecting runtime dependencies. Do not add graphics/OCR engines speculatively.
- Prefer independent format adapters and a shared document model when implementing.
  Introduce additional projects only when a working boundary justifies them.
- Parse external documents defensively: resource bounds, ZIP expansion limits, safe XML,
  no implicit macro/script execution or remote resource fetching, and useful diagnostics.
- Keep generated packages, reference binaries, customer data, license keys, and secrets out of Git.

## Testing and fidelity

- Maintain a test for every agreed conversion direction and scanned-PDF OCR scenario.
- Keep unimplemented cases visibly skipped with a reason. Never replace real assertions
  with always-passing placeholders, file-existence checks, or a renamed input file.
- Enabling a test requires real conversion output, meaningful assertions, and fixtures.
  Validate using an independent reader or specification where practical; a round trip alone
  can hide matching bugs in a reader and writer.
- Pin fonts and environment for visual tests. Record fixture provenance and expected losses.
  OCR tests need editable text, accuracy, reading order, and layout checks.
- Android compilation is not device execution. Test native assets, fonts, OCR, and resource
  limits on actual target runtimes before claiming platform support.
- Add regression tests when fixing implementation bugs; do not write tests that merely
  mirror implementation details. Update docs when support or observable behavior changes.

## Validation and reporting

After every pull-request merge, reconcile the delivered work before starting the next
bucket. Verify the merged commit and final checks, update each linked issue's acceptance
criteria with evidence and remaining limits, close completed issues, and update roadmap
#32. Keep partially completed issues open with the outstanding requirement stated.
Refresh affected README, capability/API/usage, diagnostic and testing documentation,
including `docs/implementation-plan.md`; distinguish merged source, pending PRs and
published packages. Preserve historical qualification as dated evidence. Include issue
closing references in PR descriptions when their full acceptance scope is delivered.
This reconciliation is part of the authorized implementation workflow and does not
require another confirmation. See the implementation plan's post-merge checklist.

For code, dependency, or project changes, use the relevant checks:

```sh
rtk dotnet restore wordnet.sln --locked-mode
rtk dotnet build wordnet.sln --configuration Release --no-restore
rtk proxy powershell -NoProfile -Command '& ./eng/prepare-ocr-tests.ps1; rtk dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore; exit $LASTEXITCODE'
rtk proxy powershell -NoProfile -Command '& ./eng/prepare-ocr-tests.ps1; rtk dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore; exit $LASTEXITCODE'
rtk dotnet format wordnet.sln --verify-no-changes --no-restore
rtk dotnet pack src/wordnet/wordnet.csproj --configuration Release --no-build --no-restore --output artifacts/packages
```

On Windows with a compatible test host, also run `net472`. On non-Windows hosts do not
run the Framework target. For Android-facing changes, build the separate Android
device test app with its required workload, then execute the shared tests through
`eng/run-android-tests.ps1`. Do not install workloads unnecessarily.
Provision English OCR before running the complete suite, in the same process that launches
tests; match the native library architecture to the test host. On Windows ARM64, provision
`-Architecture x64` for Framework tests and select `RunConfiguration.TargetPlatform=x64`.
OCR tests must fail with setup guidance when prerequisites are missing, never silently skip.

When deliberately changing dependencies, restore once without `--locked-mode`, review
the lock-file changes, and validate the final state in locked mode. Do not ignore NuGet
audit warnings or reset lock files solely to make CI green.

Before reporting completion, inspect the diff and package contents as appropriate.
Report what built, what ran, what was skipped, and platform/tooling limits. No required
conversion/OCR cases may remain pending in the first functional converter release.
Publishing packages and changing release credentials are separate from creating a scaffold.

<!-- headroom:rtk-instructions -->
## RTK (Rust Token Killer)

When running shell commands, **always prefix with `rtk`**. If RTK has no filter for a
command it passes it through unchanged. Use `rtk proxy <cmd>` for unfiltered output
while tracking usage. For debugging, a raw command is allowed.

- Git: `rtk git status`, `rtk git diff`, `rtk git log`.
- Files/search: `rtk read <file>`, `rtk rg <pattern>`, `rtk rg --files`.
- .NET: `rtk dotnet build`, `rtk dotnet test`, `rtk dotnet pack`.
- GitHub: `rtk gh pr view <number>`, `rtk gh run list`.
- Prefix each command separately in a command chain.
- On Windows, use PowerShell filesystem operations through `rtk proxy powershell`
  when a Unix command is unavailable. Verify exact target paths before recursive removal.

This rule applies to agent shell use. RTK is not required by consumers or hosted CI.
<!-- /headroom:rtk-instructions -->

## Bucket 5 design status

The [later-design decisions](docs/later-design-decisions.md) merged in PR #39 as design
deliverables for #28–#31. They do not change the deferred-feature
boundaries above or authorize their implementation. Read the relevant ADR and
independent acceptance plan before selecting a separately scoped implementation slice.
