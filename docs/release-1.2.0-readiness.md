# 1.2.0 release readiness

Status: **release preparation merged; not published**. The additive business-document,
editing/template and interoperability work is a suitable minor-version milestone.
Production publication remains conditional on the gates below. The latest verified
published version is 1.1.0; bucket 5 is merged design work, not runtime feature delivery.

The live [publication record on roadmap #32](https://github.com/kirakosyan/word.net/issues/32)
identifies the selected current main SHA and CI run. The verified snapshots below
remain tied to their named commits. Any later main change, including README/package
documentation, needs its own successful exact-main CI and immutable-package evidence
before publication; an older successful run cannot qualify the new main artifact.

## Gates

| Gate | Evidence / state |
| --- | --- |
| Roadmap reconciliation | Buckets 1–4 implement bounded source profiles; bucket 5 closes only its four design issues. PR #39 merge SHA, checked criteria and roadmap reconciliation are recorded in the implementation plan. |
| Release metadata/API | Candidate 1.2.0 for all three packages, unchanged AssemblyVersion/five core/OCR TFMs, API baseline 1.1.0. Current validation is recorded on the release PR linked from roadmap #32. |
| Local current-source and package checks | 829 required cases; release PR evidence records locked restore/build/format/audit/pack and fresh-cache modern/Standard package runs. Earlier PR #38 results remain historical. |
| Exact release PR/main CI | PR #40 and documentation PR #41 merged with verified tree equality and 24 successful checks on each final head. Main `91838eb` passed all 12 checks in run `39124192159`; see its dated snapshot below. Use the live record for any newer main. |
| Actual Framework 4.7.2 source | **Passed** on named candidate/main revisions, including `91838eb`: all 829 cases, release 461814, x64 and observed legacy `compatjit.dll`; reports and cleanup independently checked. |
| Actual Framework 4.7.2 immutable main package | **Passed for `91838eb`**. Its clean consumer matches the immutable CI archives and passes all 829 cases. Retain package/library/test/config hashes, runtime report and TRX. A newer main requires its own package evidence. |
| Trusted publication | Not dispatched. Use existing `publish.yml`, `main`, exact successful `ci_run_id` and the `nuget` environment; do not expose/copy credentials. |
| Public NuGet verification | Not published. After processing, restore all three packages into a clean consumer and compare DLLs with the tested CI artifact; NuGet signing can change ZIP hashes. |

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

Record exact release commit, main CI run/artifact IDs, package/DLL hashes, current
minimum-runtime reports, publication run and public verification here when completed.
Do not describe this candidate or older qualification as a published/qualified 1.2.0.
