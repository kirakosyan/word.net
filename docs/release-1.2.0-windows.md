# Run the 1.2.0 minimum-runtime checks with Docker on a Windows PC

This is a release qualification handoff. It runs real Microsoft .NET Framework
4.7.2 in an isolated Windows Server 2019 evaluation VM, leaving the host Framework
unchanged. No SDK or NuGet publishing credential is needed inside the guest.

Use the successful **release-candidate push CI run** linked from the release PR
for the source run first. The release PR is linked from [roadmap #32](https://github.com/kirakosyan/word.net/issues/32).
Check that its head SHA matches `bundle.json`. Complete this run before the release
PR merges, as required by [releasing step 2](releasing.md#prepare-and-publish).

After the source reports are verified and the release PR merges, use the **final
main push CI run** for package qualification. Its commit must match current main;
a PR run's package artifact cannot qualify production publication. Download:

- `framework-source-vm` from the candidate push run: source binaries for the reviewed revision.
- `framework-package-vm` from the final main push run: binaries restored from that run's immutable NuGet artifact.

Extract each artifact into a separate directory, for example
`C:\wordnet-1.2.0-source-setup` and `C:\wordnet-1.2.0-package-setup`.
Each directory must contain `start.ps1`, `install.bat`, `qualify.ps1`, `README.md`,
`tests.zip` and `bundle.json`. Do not mix files from different artifacts/runs.

## Prerequisites

Use a Windows x64 PC with Docker Desktop's Linux x64 backend, at least 6 GiB assigned
to Docker, internet access and at least 60 GiB physical free space for each fresh VM.
Keeping the first VM while creating the second requires additional disk space.
The launcher checks these requirements. Windows ARM64, a newer host Framework or
installing the 4.7.2 targeting pack does not replace the required guest/runtime.

From PowerShell on the PC:

```powershell
docker info --format '{{.OSType}}/{{.Architecture}}'
cd C:\wordnet-1.2.0-source-setup
./start.ps1 -CheckOnly -WorkDirectory C:/wordnet-1.2.0-source-vm
./start.ps1 -WorkDirectory C:/wordnet-1.2.0-source-vm
```

If preflight explicitly reports unavailable KVM, repeat startup with
`-SoftwareEmulation`. This is slower and does not waive the actual runtime checks.
Watch installation at `http://localhost:8006`. Only a loopback viewer is exposed;
no public RDP/SMB or host-disk passthrough is configured. VM credentials stay in its
private state; do not share or commit `secrets.env` or VM disks.

The guest verifies release key 461808/461814, validates the test ZIP hash, installs
the signature-checked Microsoft C++ prerequisite, provisions local English OCR and
runs all **829** cases with observed legacy JIT. Success requires `passed: true`
and `exactFramework472: true` in `shared/results/status.json`, complete passing TRX
and matching hashes/JIT evidence in `environment.json`.

After the source reports finish exporting, stop and remove its owned container and
network with the command below. The explicit compose file scopes this to the test
project; no volumes, images or bind-mounted VM disk/results are deleted. Keep its
results and disk until the evidence is verified. Both setups use the same compose
project name, so finish this step before starting the package VM:

```powershell
docker compose --project-name wordnet-framework472 --env-file C:/wordnet-1.2.0-source-vm/secrets.env -f C:/wordnet-1.2.0-source-vm/compose.json down
```

Then run the package setup with a separate work directory:

```powershell
cd C:\wordnet-1.2.0-package-setup
./start.ps1 -CheckOnly -WorkDirectory C:/wordnet-1.2.0-package-vm
./start.ps1 -WorkDirectory C:/wordnet-1.2.0-package-vm
```

Do not overwrite the source evidence or infer a package pass from a source pass.
Never remove unrelated Docker containers or volumes. Retain qualified results before
any cleanup. If space is insufficient, return the source results first so its owned
VM can be cleaned up before the package run.

## Return evidence

For **both** work directories, return these files from `shared/results/`:

```text
environment.json
net472.trx
status.json
setup.log
```

Also retain each setup's `bundle.json`. It records exact commit, source/package
identity and hashes. The final package DLL hashes must match the immutable main CI archive.
Return the source results first; package results follow after merge and final main CI.
A setup failure, timeout, newer runtime, missing case or skip is useful diagnostic
evidence and does not satisfy the release gate. Do not downgrade the PC's installed
Framework to make the runner pass.

After these reports are verified, dispatch `publish.yml` from `main` with the exact
successful main `ci_run_id` using the existing authorized GitHub Actions UI/workflow.
If main has moved, its newer exact artifact and minimum-runtime evidence are required;
never publish an old artifact under a new commit or rebuild the tested version at push time.
