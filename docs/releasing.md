# Releasing wordnet

The current release channel is **alpha**, for evaluation with the documented profiles.
Publishing a package does not establish production readiness. Stable releases remain
blocked on the corpus, security/resource, performance and platform gates in the
[implementation plan](implementation-plan.md#release-gates).

## One-time setup

Use [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
No long-lived NuGet API key belongs in the repository or GitHub secrets.

- NuGet account/package owner: `kirakosyan`.
- Repository owner/name: `kirakosyan` / `word.net`.
- Workflow filename: `publish.yml` (without `.github/workflows/`).
- GitHub environment: `nuget`, restricted to the `main` branch.
- Allowed scope: push new packages and versions, exact package pattern `wordnet`.
  Do not allow unlisting/relisting or a wildcard for unrelated packages.
- GitHub environment variable `NUGET_USER`: `kirakosyan` (the profile name, not email).

Only the final publishing job has `id-token: write`; validation jobs cannot request a
NuGet credential. Actions are pinned to commit SHAs. Protect changes to the workflow
and release scripts through normal repository review.

## Prepare and publish an alpha

1. Choose a new prerelease version in `src/wordnet/wordnet.csproj`. Update the README,
   package release notes and `docs/releases/<version>.md`. Never overwrite a published
   version. Local candidate builds must be identified by full source SHA and package hash;
   use a new version or isolated package cache and lock-file path when testing a different
   unpublished artifact. Preserve earlier evidence and verify the intended artifact's hash.
2. Run locked restore, Release build, the real conversion/OCR suites, formatting,
   dependency audit and pack. Review package contents, licenses, API compatibility
   and remaining limitations. For converter/native changes, run the Android device
   suite too; hosted Android compilation alone is insufficient.
3. Merge the reviewed change to `main`. Wait for **Build, test, and pack** and
   **Android API compilation** to succeed on the exact main commit. Record the former
   run's numeric ID from its URL.
4. Run **Publish NuGet prerelease** from `main`, passing `ci_run_id`. CLI equivalent:

   ```sh
   gh workflow run publish.yml --ref main -f ci_run_id=<successful-main-CI-run-id>
   ```

5. The workflow rejects other repositories/branches, unsuccessful or mismatched CI,
   missing notes, stable versions and expired artifacts. It downloads the immutable
   CI package artifact and verifies its ID, version, repository commit, all four
   framework assets, symbols and notices.
6. The complete suite runs against that downloaded NuGet on native Windows/Linux
   x64/ARM64 (.NET 8/10 and Windows x64 Framework). A clean package cache and source
   mapping prevent a stale/public copy from replacing the candidate. Any missing,
   failed or skipped case blocks publication. NuGet restore auditing remains enabled.
7. After every lane succeeds, the `nuget` environment exchanges GitHub OIDC for a
   short-lived NuGet key and pushes the same `.nupkg` and companion `.snupkg`.
   It does not rebuild or increment the version at publishing time.
8. Verify NuGet processing completes and restore the public package into a clean
   consumer. Create Git tag `v<version>` and a GitHub **prerelease** at the tested commit,
   using the committed release notes, package files and links to both CI/release runs.

Ordinary commits, PRs and tags do not publish. An explicit workflow dispatch is required.
If `main` changes before dispatch, use the new commit's successful CI run; do not reuse
an older artifact. New releases need new versions. Duplicate pushes fail deliberately;
after a partial publication, inspect NuGet and finish only the missing symbol/release
step instead of blindly rerunning or rebuilding an already published version.

## Production readiness

The alpha has 28 working directions and real English printed OCR. The candidate adds
independent corpus thresholds, mutation/resource campaigns and packed consumer checks.
Actual Framework 4.7.2 and Android API 21 evidence remains required before a stable claim;
review the supported profile against representative deployment documents too. Current
resource limits do not provide a hard process sandbox. macOS remains deferred. See
[production gates](production-readiness.md) and [test evidence and boundaries](testing.md).
