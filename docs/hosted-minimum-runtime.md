# Automated actual Framework 4.7.2 qualification

The release workflow can use a fresh GitHub-hosted Ubuntu x64 runner to host the
existing pinned Windows Server 2019 evaluation VM. This supplies actual Microsoft
Framework 4.7.2; the Linux host is not presented as a Framework runtime. Hardware
KVM, at least 6 GiB Docker memory and 60 GiB free disk are mandatory. No software
emulation fallback or newer-Framework substitution is accepted in this CI lane.

The reusable `framework472.yml` consumes the same run's already-tested
`framework-source-vm` or `framework-package-vm` artifact. The source job follows
desktop qualification; the package job follows the clean immutable-package lanes.
No new provider/dependency, publication trigger, credential or converter API is
introduced. Manual [Windows PC qualification](release-1.2.0-windows.md) remains
available if the hosted runner cannot meet the requirements.

Each dedicated runner needs no host .NET SDK, Android workload or browser. Only
on a disposable `github-hosted` Linux runner, its unused preinstalled SDK/browser/
Android/tool-cache directories are removed to reclaim VM storage. The checkout and
other CI jobs are separate. The launcher then admits the actual free disk, memory,
architecture and KVM device; an unsuitable runner fails rather than skipping tests.

The launcher verifies the commit/asset, ZIP and binary hashes, creates a fresh
private work directory and Compose project, and starts the pinned image without
publishing viewer/RDP/SMB ports. VM setup and tests have a 60-minute host deadline;
the child suite retains its own bounded deadline. Guest completion is signaled
only after reports finish exporting to the private container share.

The host independently requires Framework release 461808/461814, x64, observed
`compatjit.dll`, the intended scoped legacy-JIT configuration, matching library/test
hashes and the complete manifest case count without failures/skips. Negative controls
reject foreign/stale bundles, newer runtimes, absent JIT/configuration evidence,
binary mismatches, incomplete exports and partial/failing/skipped reports. These
controls do not themselves establish an actual runtime pass.

Normal exit, guest failure, deadline and catchable interruption enter cleanup.
Only this run's Compose containers/network, fresh VM disk/ISO/staging directory and
newly pulled image are removed. Preexisting images and caller evidence are retained.
`cleanup.json` records verified disposal; cleanup failure also fails the job. The
disposable runner is reclaimed by GitHub after job completion, including hard
cancellation where application cleanup cannot finish. No VM disk, credentials or
secret environment file is uploaded.

Artifacts `minimum-framework472-source` and `minimum-framework472-package` contain
the manifest, TRX, environment/status/setup reports, independent verification,
redacted VM diagnostics and cleanup receipt. A candidate package run is preliminary;
publication still requires the successful package run from the exact final main
push CI and the existing explicitly dispatched Trusted Publishing workflow.

This is qualification infrastructure. A configured workflow or successful host
preflight is not a Framework pass; current results must be inspected and recorded
in the [release ledger](release-1.2.0-readiness.md).
