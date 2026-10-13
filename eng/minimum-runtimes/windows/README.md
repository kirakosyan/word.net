# Actual .NET Framework 4.7.2 VM

Run the prepared `wordnet-framework472-vm-setup.zip` on the Windows x64 test PC.
This provisions a Windows Server 2019 evaluation VM inside Docker's Linux backend;
the container runs QEMU. It does not replace the host's Framework installation.

Requirements: Windows x64, Docker Desktop running Linux x64 containers, at least
6 GiB assigned to Docker, 60 GiB physical free storage and internet access. KVM in
the Docker backend is preferred. Without it, explicitly select software emulation,
which can make installation and OCR substantially slower.

Extract the bundle into a dedicated directory and run from PowerShell:

```powershell
./start.ps1 -CheckOnly -WorkDirectory C:/wordnet-framework472-vm
./start.ps1 -WorkDirectory C:/wordnet-framework472-vm
# Only if the KVM preflight fails:
./start.ps1 -WorkDirectory C:/wordnet-framework472-vm -SoftwareEmulation
```

The pinned Dockur 6.06 image downloads and verifies Windows Server 2019 media.
The guest must report Framework release 461808 or 461814 before any tests run.
It verifies the test ZIP, installs a signature-checked Microsoft x64 C++ runtime,
provisions pinned English OCR prerequisites and executes the complete standalone
suite. Missing cases, skips, failures or a newer Framework cannot pass qualification.
The C++ installer hash is recorded; it is a VM prerequisite, not a library dependency.

Watch installation at <http://localhost:8006>. Only the local viewer port is published;
RDP and SMB are not exposed on the host. The guest password is randomly generated
in the private `state/secrets.env` (or chosen work directory). Keep this file and the
VM disk out of Git. Do not share credentials when returning results.

Return `shared/results/environment.json`, `net472.trx`, `status.json` and `setup.log`
from the work directory. `status.json` must say `passed: true` and `exactFramework472: true`.
If setup fails before tests, `status.json` and `setup.log` explain why. If the private
share fails, reports remain in the guest at `C:\wordnet-results`.

Startup prints a scoped Docker Compose stop command. Stop the VM after collecting
results. Restarting the same work directory preserves its disk and evidence. Use a
new work directory for a fresh qualification attempt. No host disks are passed through.

Repository scripts alone do not include test binaries. From the repository root,
run `./eng/minimum-runtimes/prepare-windows-vm.ps1` to build the tests and assemble
the setup directory with `tests.zip` and its checksum manifest. See the complete
[AK-PC runbook](../../../docs/minimum-runtime-qualification.md) in the repository.
The prepared transfer bundle already includes these inputs. Setup/configuration validation is not proof
that the VM booted or that Framework tests passed.

Sources: [Dockur](https://github.com/dockur/windows/tree/v6.06),
[its environment options](https://github.com/dockur/windows/blob/v6.06/docs/environment.md),
[Microsoft Framework versions](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#net-framework-472).
