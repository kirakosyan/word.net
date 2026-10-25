# Releasing wordnet

The historical **1.2.0** desktop release was qualified at commit `3202a1f`.
See the [release ledger](release-1.2.0-readiness.md). Later maintenance builds of main
are separate artifacts and must not overwrite this version. Pack and verify
`wordnet.Fonts`, `wordnet`, and `wordnet.Ocr` together with `eng/pack.ps1`; they share
one package version. The workflow publishes fonts before core and the optional
provider. New companion package IDs need their own authorized NuGet ownership/
Trusted Publishing configuration before any release. Exact-commit CI and
minimum-runtime gates apply to every release.

The active release line is [1.4.0](releases/1.4.0.md), covering the bounded roadmap #99
profile on top of [published 1.3.0](releases/1.3.0.md). All three project versions
advance together. Select successful CI from the exact final-main commit for the new
version; verify public indexes before dispatch and never republish an existing version.
Historical receipts remain unchanged. Publication is a separate explicit action after
all source, immutable-package and actual-minimum-runtime gates pass.

Package README images must use Markdown syntax and a NuGet-allowed image host.
Avoid HTML sizing/alignment tags: NuGet's renderer disables HTML. Validate the
packed README with HTML disabled and inspect the package upload preview before
publishing. Keep badge labels and support claims tied to actual qualification.
Use absolute HTTP(S) links for repository documentation in the package README;
NuGet clears relative file links. Internal `#section` links are supported.

For the fresh actual-4.7.2 gate, use the candidate push's source evidence before
merge and the final main push's immutable-package evidence before publication.
The [hosted VM jobs](hosted-minimum-runtime.md) can execute these bundles automatically;
inspect their actual-runtime reports and cleanup receipts, not only their configuration.
The [Windows Docker handoff](release-1.2.0-windows.md) is the manual fallback when
hosted execution is unavailable.

The bounded roadmap implementation is included in 1.2.0; bucket 5 remains design-only.
Merging does not publish packages. After every merge, complete the [issue, roadmap and documentation
reconciliation](implementation-plan.md#after-every-merge) before taking the next bucket;
keep release notes and package claims tied to the version that actually ships them.

The first stable release is **1.0.0**, for the documented Windows/Linux x64 and ARM64
desktop profile. Android and macOS are deferred by the maintainer for this release.
Publishing alone does not establish quality: retain the corpus, resource, API and
exact-package evidence in [production qualification](production-readiness.md).
Actual Framework 4.7.2 x64 requires the documented legacy JIT configuration.

## One-time setup

Use [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
No long-lived NuGet API key belongs in the repository or GitHub secrets.

- NuGet account/package owner: `kirakosyan`.
- Repository owner/name: `kirakosyan` / `word.net`.
- Workflow filename: `publish.yml` (without `.github/workflows/`).
- GitHub environment: `nuget`, restricted to the `main` branch.
- Allowed scope: push new packages and versions. Authorize the exact package IDs
  `wordnet`, `wordnet.Fonts`, and `wordnet.Ocr` through their Trusted Publishing
  policies before dispatching the three-package release.
  Do not allow unlisting/relisting or a wildcard for unrelated packages.
- GitHub environment variable `NUGET_USER`: `kirakosyan` (the profile name, not email).

Only the final publishing job has `id-token: write`; validation jobs cannot request a
NuGet credential. Actions are pinned to commit SHAs. Protect changes to the workflow
and release scripts through normal repository review.

All workflows use actions whose manifests declare Node.js 24: checkout 7.0.1,
setup-dotnet 6.0.0, upload-artifact 7.0.2, download-artifact 8.0.2 and NuGet/login
1.2.0. Keep the full commit pins when updating them and check their `action.yml`
runtime and release notes. Node.js runs the actions; it does not change the library's
.NET targets. Hosted runners provide the required Node.js runtime. Artifact uploads
retain the default ZIP format and downloads fail on a digest mismatch.
`global.json` selects SDK 10.0.401 without roll-forward so setup-dotnet cannot silently
advance the SDK and its implicit Android dependencies beyond the committed lock files.
Upgrade the SDK and affected locks together in a separately validated change.

## Prepare and publish

1. Choose a new SemVer in `src/wordnet/wordnet.csproj`. Update the README,
   package release notes and `docs/releases/<version>.md`. Never overwrite a published
   version. Local candidate builds must be identified by full source SHA and package hash;
   use a new version or isolated package cache and lock-file path when testing a different
   unpublished artifact. Preserve earlier evidence and verify the intended artifact's hash.
2. Run locked restore, Release build, the real conversion/OCR suites, formatting,
   dependency audit and pack. Review package contents, licenses, API compatibility
   and remaining limitations. Execute the complete suite on actual Framework 4.7.2
   with `-RequireFramework472 -JitMode legacy` when changing the converter, dependencies
   or test profile. Record the release key, observed JIT and library/test hashes. Android
   compilation remains a portability check; it is outside the desktop publication gate.
3. Merge the reviewed change to `main`. Wait for **Build, test, and pack** to succeed
   on that exact main commit. Record the run's numeric ID. Download its immutable NuGet,
   restore a fresh Framework consumer, and repeat the actual-4.7.2 suite against that
   package before publication. Preserve the package hash and reports for the release.
4. Run **Publish NuGet** from `main`, passing `ci_run_id`. CLI equivalent:

   ```sh
   gh workflow run publish.yml --ref main -f ci_run_id=<successful-main-CI-run-id>
   ```

5. The workflow rejects other repositories/branches, unsuccessful or mismatched CI,
   missing notes, ambiguous version spellings and expired artifacts. Stable and
   prerelease SemVer identities are accepted. It downloads the immutable
   CI package artifact and verifies its ID, version, repository commit, the declared
   framework assets, symbols and notices.
6. The complete suite runs against that downloaded NuGet on native Windows/Linux
   x64/ARM64 (.NET 8/10 and Windows x64 Framework). A clean package cache and source
   mapping prevent a stale/public copy from replacing the candidate. Any missing,
   failed or skipped case blocks publication. NuGet restore auditing remains enabled.
7. After every lane succeeds, the `nuget` environment exchanges GitHub OIDC for a
   short-lived NuGet key and pushes the same `.nupkg` and companion `.snupkg`.
   It does not rebuild or increment the version at publishing time.
8. Verify NuGet processing completes and restore the public package into a clean
   consumer. Verify the public archive's DLLs match the tested package (NuGet signing
   can change the archive hash). Create Git tag `v<version>` and a GitHub release at the
   tested commit, using committed notes, package files, minimum-runtime evidence and
   links to CI/publication runs. Set the GitHub prerelease flag only for prerelease versions.

Ordinary commits, PRs and tags do not publish. An explicit workflow dispatch is required.
If `main` changes before dispatch, use the new commit's successful CI run; do not reuse
an older artifact. New releases need new versions. Duplicate pushes fail deliberately;
after a partial publication, inspect NuGet and finish only the missing symbol/release
step instead of blindly rerunning or rebuilding an already published version.

## Production readiness

The 1.2.0 desktop release profile has 36 working directions and real English printed OCR, independent
corpus thresholds, mutation/resource campaigns and packed consumer checks. Actual
Framework 4.7.2 evidence is required for its explicit compatibility configuration;
default-JIT 4.7.2 operation is unsupported. Android API 21 remains unresolved, outside
the release scope alongside macOS. Review representative deployment documents too.
Current resource limits do not provide a hard process sandbox. See
[production gates](production-readiness.md) and [test evidence and boundaries](testing.md).
