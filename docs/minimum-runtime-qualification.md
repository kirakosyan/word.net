# Finish minimum-runtime qualification on AK-PC

This runbook preserves `net472;netstandard2.0;net8.0;net10.0` and the `WordNet` API.
macOS remains deferred. The two outstanding gates are **actual Framework 4.7.2** and
**Android API 21 ARM64** execution of every shared test. A VM/emulator starting is not
a passing qualification report.

## 1. Get the branch

On AK-PC, open PowerShell in the repository and run:

```powershell
git fetch origin
git switch codex/production-readiness
git pull --ff-only
```

Keep any local work; do not reset or clean the checkout to run these instructions.
Commands below assume the repository root. RTK is optional for a human running them.

## 2. Prepare the Windows VM from the checkout

AK-PC needs Windows x64, the .NET SDK selected by `global.json` (10.0.401 or an allowed
patch), and Docker Desktop using Linux x64 containers. Assign Docker at least 6 GiB
RAM. Choose a VM work directory on a disk with at least **60 GiB free**; account for
the physical disk containing Docker Desktop's virtual disk too.

```powershell
dotnet --version
docker info --format '{{.OSType}}/{{.Architecture}}'
./eng/minimum-runtimes/prepare-windows-vm.ps1
./artifacts/framework472-vm-setup/start.ps1 -CheckOnly -WorkDirectory C:/wordnet-framework472-vm
./artifacts/framework472-vm-setup/start.ps1 -WorkDirectory C:/wordnet-framework472-vm
```

The preparation script restores locked dependencies, builds the actual net472 tests,
and creates the test ZIP and its checksum locally. No separate ZIP transfer or .NET
SDK inside the guest is needed. If preparing again, pass a new `-OutputDirectory`;
the script intentionally preserves an existing bundle.

The launcher uses pinned Dockur 6.06 and Microsoft Windows Server 2019 evaluation
media, with media verification enabled. Server 2019 originally includes Framework
4.7.2. The guest verifies its registry release value before running tests; a newer
runtime is rejected. The host's installed Framework is unchanged.

If startup reports that Docker cannot access `/dev/kvm`, run:

```powershell
./artifacts/framework472-vm-setup/start.ps1 -WorkDirectory C:/wordnet-framework472-vm -SoftwareEmulation
```

This uses QEMU software emulation, which is slower. Do not install the Windows VM on
the ARM64 development PC through this launcher. Its Docker backend has no KVM and its
physical disk does not have enough space for this setup.

Open <http://localhost:8006> to watch Windows installation. The guest automatically
installs the signature-checked Microsoft x64 C++ runtime, provisions pinned English
OCR dependencies, and runs all tests with `-RequireFramework472`. Only the loopback
viewer port is exposed. VM credentials are generated locally in `secrets.env`; do not
commit or return that file or the VM disk.

## 3. Collect the Windows evidence

Results appear under:

```text
C:\wordnet-framework472-vm\shared\results\environment.json
C:\wordnet-framework472-vm\shared\results\net472.trx
C:\wordnet-framework472-vm\shared\results\status.json
C:\wordnet-framework472-vm\shared\results\setup.log
```

Required outcome: `status.json` has `passed: true` and `exactFramework472: true`, and
the TRX has at least the count in `eng/required-tests.txt` (currently 301), all passing,
with no skipped cases. Return these four files. A setup failure is useful evidence;
return its status/log instead of treating the VM as validated. If export fails, the
same reports remain inside the guest at `C:\wordnet-results`.

Stop the VM when finished:

```powershell
docker compose --project-name wordnet-framework472 --env-file C:/wordnet-framework472-vm/secrets.env -f C:/wordnet-framework472-vm/compose.json stop
```

Use the same generated Compose file to inspect logs or restart. Keep the disk until
results have been reviewed; use a new work directory for another fresh installation.

## 4. Android API 21 ARM64

Run the emulator directly on the host, separately from the Windows guest. It uses
Google's API 21 ARM64 r04 image and a pinned Windows x64 emulator 37.2.12 engine in an
isolated directory. The normal launcher rejects the legacy ARM64 configuration;
the provided headless aarch64 engine is invoked directly with software emulation.
This is a qualification probe, not a vendor-supported Windows ARM development setup.
On the ARM64 development host, the probe completed Android boot, reported API 21 /
`arm64-v8a`, and installed the test APK. The app then repeatedly exited with signal
11 during startup, before a test progress/report file existed. No conversion case
passed in that attempt. Its cause is not established; repeat on native x64 AK-PC to
separate this host's emulation behavior from a runtime or application issue. Passing
conversion/OCR results are still required before closing this gate.

Provide the full path to an installed SDK `platform-tools/adb.exe`. The example is the
Visual Studio Android SDK location; adjust it if AK-PC uses another location.

```powershell
$adb = 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe'
./eng/minimum-runtimes/start-android-api21.ps1 -AdbPath $adb
```

The script verifies download hashes, starts serial `emulator-5580`, waits for Android
boot, and verifies API `21` plus ABI `arm64-v8a`. First boot can be slow. It records
the image/emulator hashes, fingerprint and logs under `artifacts/android-api21/`.
An x86 or newer-API image cannot substitute for this gate.

Build the test app with the Android workload and SDK/JDK already configured for this
repository. Install the workload only on a machine intended for Android builds:

```powershell
dotnet workload list
# If android is absent:
dotnet workload install android --skip-manifest-update
./eng/prepare-android-tests.ps1
dotnet restore tests/wordnet.Android.Compatibility --locked-mode
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage
try {
    ./eng/run-android-tests.ps1 -AdbPath $adb -Serial emulator-5580 -RequiredApiLevel 21 -TimeoutSeconds 1800 -ResultsPath artifacts/android-api21/results.xml
} finally {
    & $adb -s emulator-5580 logcat -d | Set-Content artifacts/android-api21/logcat.txt
}
```

Return `artifacts/android-api21/environment.json`, `results.xml` if produced,
`logcat.txt`, and both emulator logs. The runner reads complete report XML directly
because API 21's shell lacks the executable file-check command available on newer
devices. Missing reports remain failures/timeouts; shell error text cannot pass.
Every conversion and English OCR case must pass without skips. Stop this emulator:

```powershell
& $adb -s emulator-5580 emu kill
```

## References and limits

- [Microsoft's Framework version table](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#net-framework-472).
- [Pinned Docker VM implementation](https://github.com/dockur/windows/tree/v6.06).
- [Google's API 21 image catalog](https://dl.google.com/android/repository/sys-img/android/sys-img2-1.xml).
- [Android emulator acceleration](https://developer.android.com/studio/run/emulator-acceleration).

Generated archives, system images, VM disks, credentials and raw machine logs stay in
ignored artifacts or the chosen external VM directory. No application dependency or
framework target changes are part of this setup. macOS qualification is deferred.
