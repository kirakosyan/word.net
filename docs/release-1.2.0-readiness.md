# 1.2.0 release readiness

Status: **1.2.0 published and publicly verified**. The
[publish run 39129534970](https://github.com/kirakosyan/word.net/actions/runs/39129534970)
accepted all three 1.2.0 package and symbol pushes. All three public packages are
listed; a fresh public-only consumer restores/builds successfully, and all 11 public
DLLs match the qualified CI artifact at release commit `3202a1f`.
Bucket 5 is merged design work, not runtime feature delivery.

The live [publication record on roadmap #32](https://github.com/kirakosyan/word.net/issues/32)
identifies release and maintenance commits separately. The verified snapshots below
remain tied to their named commits. Later main changes, including README/package
documentation, do not replace the published 1.2.0 artifact. A future release needs
a new version, successful exact-main CI and its own immutable-package evidence.

## Gates

| Gate | Evidence / state |
| --- | --- |
| Roadmap reconciliation | Buckets 1–4 implement bounded source profiles; bucket 5 closes only its four design issues. PR #39 merge SHA, checked criteria and roadmap reconciliation are recorded in the implementation plan. |
| Release metadata/API | Published 1.2.0 for all three packages, unchanged AssemblyVersion/five core/OCR TFMs, API baseline 1.1.0. |
| Local current-source and package checks | 829 required cases; release PR evidence records locked restore/build/format/audit/pack and fresh-cache modern/Standard package runs. Earlier PR #38 results remain historical. |
| Exact release PR/main CI | PR #42's final head passed all 24 checks with verified merge-tree equality. Release main `3202a1f` passed all 12 checks in run `39126846669`. |
| Actual Framework 4.7.2 source | **Passed for `3202a1f`**: all 829 cases, release 461814, x64 and observed legacy `compatjit.dll`; reports and cleanup independently checked. |
| Actual Framework 4.7.2 immutable main package | **Passed for `3202a1f`**. Its clean consumer matches the immutable CI archives and passes all 829 cases. Package/library/test/config hashes, runtime report and TRX retained. |
| Trusted publication | **Succeeded**, run `39129534970`, using commit `3202a1f` and exact-main CI `39126846669`. All three package/symbol pushes were accepted after nine full 829-case suites passed. |
| Public NuGet verification | **Passed**, all three public packages: fresh public-only restore/build, matching nuspec commit/version and all 11 DLL hashes. NuGet signing changes ZIP hashes; signed archives are retained separately. Do not rerun publication for an already accepted version. |

## Published artifact selection — 2026-10-18

PR #42 merged as `3202a1fcfed9b1210b1eadb8a5e3509d7c2adba9`. Its tree matches
reviewed head `939e7bd660c404e1f403ded17dec924ed0dd2bc4`, which passed all 24 checks.
Main CI [39126846669](https://github.com/kirakosyan/word.net/actions/runs/39126846669)
passed all 12 checks. The immutable NuGet artifact is `12135704761`, archive SHA-256
`3538666ebc0474a424432b4fc956a4f852cc62f4684d063c0471e5ed979606b0`.
Actual-4.7.2 source/package artifacts `12137480072` / `12137277225` each passed
829/829 on release 461814/x64 with observed legacy JIT and verified cleanup of their
containers, networks, VM state and newly pulled images. All 18 Windows guests through
this release revision completed with independently verified cleanup.

The [publication run](https://github.com/kirakosyan/word.net/actions/runs/39129534970)
passed all six jobs. Nine downloaded TRX reports each contain 829 individual passes
without failures/skips. NuGet accepted all three `.nupkg` and `.snupkg` pushes.
Public `wordnet`, `wordnet.Fonts` and `wordnet.Ocr` 1.2.0 packages were downloaded
from NuGet, CRC-checked and compared against all 11 immutable CI DLL hashes.
Their nuspecs identify the exact release commit; all three carry NuGet signatures.
A new isolated package cache with only the public feed restored and built the
package-mode consumer successfully. These checks use public packages, separate
from the local candidate archives and earlier caches.
Later CI/documentation maintenance commits are distinct from this release artifact;
they must not overwrite version 1.2.0. Historical preparation snapshots below retain
their original commit scope and publication status.

## Verified main snapshot — 2026-10-18, before OS-icon documentation

PR #41 merged as `91838ebfa9c5eeef38716b72757b3b017d1ad347`, with tree equality to
`4b600e96f778f4f9252ba9beef37eb3d0ae1b4f3` and all 24 final-head checks passed.
Its [main CI run 39124192159](https://github.com/kirakosyan/word.net/actions/runs/39124192159)
passed **12/12** checks. The following artifacts were downloaded, digest/CRC-checked
and independently verified against their manifests and individual passing results:

| Artifact | ID | SHA-256 |
| --- | --- | --- |
| Immutable NuGet, six archives | [12136330023](https://github.com/kirakosyan/word.net/actions/runs/39124192159/artifacts/12136330023) | `8a6a0bf05c81df9528dd8e887845d0288106f20625820702e7ed715b4d85f851` |
| Actual Framework 4.7.2 source | [12135594415](https://github.com/kirakosyan/word.net/actions/runs/39124192159/artifacts/12135594415) | `fe42343b506f86a69026ba21e87373af921d1404f383048752598a1f06c1913b` |
| Actual Framework 4.7.2 package | [12136047049](https://github.com/kirakosyan/word.net/actions/runs/39124192159/artifacts/12136047049) | `39161b0cf98cfd5481fa077a41f9ab27e6333858bda6e4a4b87abd573116759d` |

Both minimum-runtime suites passed **829/829**, without failures/skips, on release
461814/x64 with observed legacy `compatjit.dll`, exact commit/binary provenance and
configuration hash `e23400f6651addee1c9b5947621babd2fbe38b44347376f98fdb2c51823b2067`.
All three executed package DLLs match the immutable archives. Both cleanup receipts
verify removal of containers, network, VM state and newly pulled image. Across the
release candidate and main revisions through this snapshot, all 12 Windows guests
completed and verified cleanup. This evidence is historical once main changes.

Publication dispatch was attempted for this qualified snapshot and returned
`Forbidden`; no publication run was created and no 1.2.0 package was published.
For the newly selected main, wait for its successful **Build, test, and pack** run
and verified minimum-runtime reports. Then open **Publish NuGet → Run workflow**,
select **main**, and enter that run's numeric ID as `ci_run_id`. See the live record
for the current value; do not reuse `39124192159` after the README merge.

## Verified candidate Docker execution — 2026-10-18

The [candidate push run 39119397507](https://github.com/kirakosyan/word.net/actions/runs/39119397507)
and [PR run 39119400351](https://github.com/kirakosyan/word.net/actions/runs/39119400351)
completed all **24** checks on head `242488b9399717044b1b9583b10b9d73b2cd5c7e`.
The following push reports were downloaded, checked against GitHub's archive digest,
CRC-checked and independently validated against their manifests and TRX counters:

| Actual Framework 4.7.2 evidence | Source | Immutable candidate package |
| --- | --- | --- |
| Artifact | [12134158092](https://github.com/kirakosyan/word.net/actions/runs/39119397507/artifacts/12134158092) | [12133854770](https://github.com/kirakosyan/word.net/actions/runs/39119397507/artifacts/12133854770) |
| Archive SHA-256 | `d2c3fbecae676e23ef12270589edc63c514ffcbe020b582de4d0cc1ac7e75dd9` | `04baa6d39129ea4211d8d06ed2526cc5071ea272461dd4f4670c08c2f3bcf236` |
| Required/passed | 829/829, zero failures/skips | 829/829, zero failures/skips |
| Core DLL SHA-256 | `824a0ef1e36fad8454e00bff786dedf276468d0bb5c195e59b7330b1bcba35fe` | `64593c3ed2240bdb679792b5a58152c75c40f1a472473feeaa933dbdfe33f1d7` |
| Runtime/JIT | Release 461814, x64, observed `compatjit.dll` | Release 461814, x64, observed `compatjit.dll` |
| Cleanup | Containers, network, VM state and newly pulled image removed | Containers, network, VM state and newly pulled image removed |

Both reports have the exact candidate commit/asset, library/test/configuration hashes,
no inherited diagnostic runtime overrides, and only the intended child
`COMPlus_UseLegacyJit=1`. Package DLLs are tied to all three immutable 1.2.0 archives.
The separate PR merge-ref runs also passed 829/829 each; both cleanup receipts were
downloaded and verified. Thus **four** real Windows guests completed qualification
and verified cleanup. No personal PC, host Framework installation or manual Docker
execution was required. Evidence remains available; disks/credentials were not uploaded.

These candidate results satisfy source-before-merge qualification. They do **not**
qualify publication of the different final-main artifact. After all release/documentation
merges, verify that commit's successful push CI and fresh immutable-package report
before dispatching publication. Historical 1.1.0 and pre-automation results remain separate.

## Minimum-runtime execution

The [automated hosted VM lane](hosted-minimum-runtime.md) now runs both prepared
bundles on fresh Windows Server 2019 guests using a dedicated Linux KVM host.
It captures actual runtime/JIT/hash/full-suite evidence and verifies removal of
its containers, network, disks and newly pulled image. This removes the need for
manual PC execution when the hosted lane succeeds. Named candidate/main execution
and cleanup are verified above; selecting the current exact-main artifact and
completing publication remain separate requirements.

The [Windows Docker instructions](release-1.2.0-windows.md) first use the successful
release-candidate push run's ready-to-run `framework-source-vm` artifact. Complete
that current-source run before merging the release PR, following releasing step 2.
After merge, use `framework-package-vm` from the successful exact-main push run.
CI assembles prebuilt source and clean package consumer outputs separately;
the package bundle checks DLL hashes against all three immutable NuGet archives.
No SDK or publishing credential is needed on the test PC or inside the guest.

For an existing actual-4.7.2 Windows host, the inner `tests.zip` can instead be
extracted into a separate directory and run without Docker:

```powershell
./run.ps1 -RequireFramework472 -JitMode legacy
```

Return `results/net472.trx` and `results/environment.json`. The report must contain
all 829 cases with no failures/skips and match the intended DLL/test hashes. Repeat
with a clean package-mode consumer restored from the **immutable exact-main CI**
archive before publishing. Keep source/package bundles and results separate.
The Docker path additionally returns `status.json` and `setup.log`; retain both
`bundle.json` manifests and their commit/asset/package/binary hash provenance.
See [the runbook](minimum-runtime-qualification.md) and [release procedure](releasing.md).
Do not replace a shared machine's Framework or substitute Mono/Wine/newer Framework
execution for the declared Windows minimum.

## Publication access

The configured repository workflow uses NuGet Trusted Publishing; no long-lived key
is required or requested. The current cloud shell's `gh api` request returns
`Forbidden`, and the available GitHub connector has no workflow-dispatch operation.
Publication dispatch therefore needs the supported GitHub Actions UI or an authorized
runtime with Actions dispatch access after the gates pass. Repository read/write and
PR-merge access do not establish workflow-dispatch access. Do not modify triggers,
credentials or release gates to work around this limitation.

The maintainer dispatched the successful publication through GitHub Actions UI.
Retain the selected release commit and public verification above. Tag/GitHub release
records must also identify that commit, even when main advances. Older qualification
does not qualify another artifact.
