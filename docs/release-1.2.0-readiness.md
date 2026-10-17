# 1.2.0 release readiness

Status: **candidate preparation; not published**. The additive business-document,
editing/template and interoperability work is a suitable minor-version milestone.
Production publication remains conditional on the gates below. The latest verified
published version is 1.1.0; bucket 5 is merged design work, not runtime feature delivery.

## Gates

| Gate | Evidence / state |
| --- | --- |
| Roadmap reconciliation | Buckets 1–4 implement bounded source profiles; bucket 5 closes only its four design issues. PR #39 merge SHA, checked criteria and roadmap reconciliation are recorded in the implementation plan. |
| Release metadata/API | Candidate 1.2.0 for all three packages, unchanged AssemblyVersion/five core/OCR TFMs, API baseline 1.1.0. Current validation is recorded on the release PR linked from roadmap #32. |
| Local current-source and package checks | 829 required cases; release PR evidence records locked restore/build/format/audit/pack and fresh-cache modern/Standard package runs. Earlier PR #38 results remain historical. |
| Exact release PR/main CI | Pending final release revision and successful exact-main push CI. A PR run or pre-version-change artifact cannot qualify publication. |
| Actual Framework 4.7.2 source | **Missing** for the 829-case release candidate. Must observe release 461808/461814 and legacy `compatjit.dll`; a newer hosted Framework is insufficient. |
| Actual Framework 4.7.2 immutable main package | **Missing**. Build a clean consumer from the exact final main CI archive; retain package/library/test/config hashes, runtime report and complete 829-case TRX. |
| Trusted publication | Not dispatched. Use existing `publish.yml`, `main`, exact successful `ci_run_id` and the `nuget` environment; do not expose/copy credentials. |
| Public NuGet verification | Not published. After processing, restore all three packages into a clean consumer and compare DLLs with the tested CI artifact; NuGet signing can change ZIP hashes. |

## Minimum-runtime handoff

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
