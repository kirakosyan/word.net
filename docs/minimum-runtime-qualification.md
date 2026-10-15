# Finish minimum-runtime qualification on AK-PC

The 1.1.0-alpha.1 preview adds a net462 asset. It builds using reference assemblies
and its managed export probe runs on installed Framework 4.8.1. This is not actual
Framework 4.6.2 qualification. Keep the minimum-4.7.2 legacy-JIT requirement below
until a fresh runtime qualification removes it. See the enterprise audit for the
isolated span-indexing reproduction and the new package boundary.

This runbook preserves `net472;netstandard2.0;net8.0;net10.0` and the `WordNet` API.
macOS remains deferred. Minimum-runtime qualification covers **actual Framework 4.7.2**
and **Android API 21 ARM64** execution of every shared test. The earlier 301-case
Framework baseline passes with the explicit legacy x64 JIT configuration below;
the current review fixes require fresh minimum-runtime execution. Default JIT remains unqualified. Android
remains unresolved and further investigation is deferred at the user's request.
A VM/emulator starting is not a passing qualification report.

AK-PC's installed Framework is **4.8.1** (release `533509`). All 348 cases pass
directly on this PC, including English OCR. Framework 4.x releases replace earlier
versions in place, so targeting `net472` here does not execute 4.7.2 itself. See
[Microsoft's runtime rules](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#remarks-for-version-45-and-later).
Use the direct run below for this PC; the isolated VM instructions are only for
investigating the declared older minimum without replacing the host runtime.

## 1. Get the branch

On AK-PC, open PowerShell in the repository and run:

```powershell
git fetch origin
git switch codex/production-readiness
git pull --ff-only
```

Keep any local work; do not reset or clean the checkout to run these instructions.
Commands below assume the repository root. RTK is optional for a human running them.

## 2. Run directly on AK-PC

Build the Release tests using the pinned SDK, then provision OCR and run in the same
PowerShell process:

```powershell
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
./eng/prepare-ocr-tests.ps1 -Architecture x64
./eng/run-framework-tests.ps1 -UseProvisionedOcr
```

This records the installed Framework, CLR/JIT versions, processor and assembly hashes.
The runner enforces an external deadline (default 1,800 seconds), preserves any old
report and requires a new complete passing report. `-RequireFramework472` deliberately
rejects AK-PC's 4.8.1 runtime rather than mislabelling its results.

## 3. Prepare an isolated Windows VM

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
OCR dependencies, and runs all tests with `-RequireFramework472 -JitMode legacy`. Only the loopback
viewer port is exposed. VM credentials are generated locally in `secrets.env`; do not
commit or return that file or the VM disk.

## 4. Collect the Windows evidence

Results appear under:

```text
C:\wordnet-framework472-vm\shared\results\environment.json
C:\wordnet-framework472-vm\shared\results\net472.trx
C:\wordnet-framework472-vm\shared\results\status.json
C:\wordnet-framework472-vm\shared\results\setup.log
```

Required outcome: `status.json` has `passed: true` and `exactFramework472: true`, and
the TRX has at least the count in `eng/required-tests.txt` (currently 348), all passing,
with no skipped cases. Return these four files. A setup failure is useful evidence;
return its status/log instead of treating the VM as validated. If export fails, the
same reports remain inside the guest at `C:\wordnet-results`.

Stop the VM when finished:

```powershell
docker compose --project-name wordnet-framework472 --env-file C:/wordnet-framework472-vm/secrets.env -f C:/wordnet-framework472-vm/compose.json stop
```

Use the same generated Compose file to inspect logs or restart. Keep the disk until
results have been reviewed; use a new work directory for another fresh installation.

The initial 2026-10-14 AK-PC investigation executed all 301 cases on actual 4.7.2, with 268
passing and 33 failing. Hardware-accelerated Hyper-V reproduced the software-emulated
failures with the same library/test hashes and default JIT settings. Servicing the
4.7.2 CLR/JIT to `4.7.4150.0` did not resolve them. PDF content extraction and PNG
validation are optimization-sensitive in a small provider probe; the exact cause
was narrowed further by inspecting generated native code: repeated span accesses
incorrectly reuse the byte at index 1 for later indices. An independent minimal
constructor reproduces it. The documented legacy x64 JIT application setting resolves
all 33 failures: all 301 cases, including OCR, pass with no skips on release 461814.
Default-JIT operation remains unqualified; this is an explicit compatibility
configuration, not a repair to Microsoft's JIT.

For an application that must execute on actual 4.7.2 x64, merge this setting into its
existing application configuration; preserve its assembly binding redirects:

```xml
<configuration>
  <runtime>
    <useLegacyJit enabled="1" />
  </runtime>
</configuration>
```

For a prebuilt test bundle, run `./run.ps1 -RequireFramework472 -JitMode legacy`, or
pass `-JitMode legacy` directly to `eng/run-framework-tests.ps1`. The runner scopes
the setting to its child process, records the requested mode and application config
hash, and rejects conflicting application settings. It does not change machine
settings or the caller's configuration file. See Microsoft's
[legacy JIT application setting](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/uselegacyjit-element).
The VM's automated qualification script explicitly uses this compatibility mode.
Reports are retained before removing disposable test disks.

## Current 1.0 desktop qualification — 2026-10-14

The revised 348-case source and locally packed 1.0.0 suites pass on actual Framework 4.7.2 x64, release
`461814`, Windows Server 2019 build 17763, with no failures or skips. The environment
report confirms `compatjit.dll` version `4.7.3460.0` loaded in the test child with the
explicit legacy setting; the application configuration was not modified. Source
library SHA-256 is `81a0fc88eeae225511f9e1bda1e63bd2d0c3fa8f63f4c525e07f323ad552b43b`;
the package's Framework asset matches it. The local candidate archive SHA-256 is
`b620978157e2c44504e801b30033ae3820d727e231ed56ba12d8e915c411d931`.
This local candidate is not the final main-branch CI archive used for publication.
Reports are retained under `artifacts/stable-release/results/`. Release publication also
requires an exact-package rerun from the successful main CI artifact, as described in
[releasing](releasing.md). Default-JIT 4.7.2 is unsupported in the 1.0 profile.

The temporary Hyper-V guest has no network adapter. On this Windows 11 host, a Server
2019 Windows Docker container would also require Hyper-V isolation; Linux Docker/Mono
cannot substitute for Microsoft's actual Framework runtime. All owned guest disks and
installation media are removed after preserving release evidence.

## 5. Android API 21 ARM64 — deferred from 1.0

Run the emulator directly on the host, separately from the Windows guest. It uses
Google's API 21 ARM64 r04 image and a pinned Windows x64 emulator 37.2.12 engine in an
isolated directory. The normal launcher rejects the legacy ARM64 configuration;
the provided headless aarch64 engine is invoked directly with software emulation.
This is a qualification probe, not a vendor-supported Windows ARM development setup.
The default .NET 10 JIT app exited with signal 11 before tests on both the ARM64
development host and x64 AK-PC. A minimal .NET 10 Android app without WordNet or its
providers reproduced the startup crash on AK-PC. An interpreter build started and
completed all 301 conversion/OCR cases with no failures/skips. That software-emulated
interpreter baseline does not qualify the default JIT or a physical API 21 device.
Subsequent interpreter runs with Android workloads 36.1.69 and 36.1.2 crashed in
native graphics after 141 and 106 completed cases, respectively. The earlier complete
report does not establish a repeatable baseline. Further Android work stopped at the
user's request, leaving this gate unresolved. Experimental runtime pins and default-mode
changes were removed. The commands below remain available for future investigation;
they do not establish qualification.

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
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage -p:UseInterpreter=false
try {
    ./eng/run-android-tests.ps1 -AdbPath $adb -Serial emulator-5580 -RequiredApiLevel 21 -ExecutionMode jit -TimeoutSeconds 1800 -ResultsPath artifacts/android-api21/results.xml
} finally {
    & $adb -s emulator-5580 logcat -d | Set-Content artifacts/android-api21/logcat.txt
}
```

Return `artifacts/android-api21/environment.json`, `results.xml` if produced,
`results.environment.json`, `logcat.txt`, and both emulator logs. The runner records
the APK hash before startup, rejects an execution-mode/API mismatch, preserves old
host reports and fails early if the process exits without a report. It reads complete
report XML directly because API 21's shell lacks the executable file-check command available on newer
devices. Missing reports remain failures/timeouts; shell error text cannot pass.
Every conversion and English OCR case must pass without skips. Stop this emulator:

```powershell
& $adb -s emulator-5580 emu kill
```

For an explicitly labelled interpreter comparison, rebuild with `-p:UseInterpreter=true`
and run with `-ExecutionMode interpreter -TimeoutSeconds 7200`. Interpreter results
never count as JIT evidence.
The original AK-PC interpreter run exceeded its 1,800-second
host deadline; a separate read-only collector verified the installed APK hash and
retrieved its eventual 301/301 report. The original timeout remains recorded.

## References and limits

- [Microsoft's Framework version table](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#net-framework-472).
- [Pinned Docker VM implementation](https://github.com/dockur/windows/tree/v6.06).
- [Google's API 21 image catalog](https://dl.google.com/android/repository/sys-img/android/sys-img2-1.xml).
- [Android emulator acceleration](https://developer.android.com/studio/run/emulator-acceleration).

Generated archives, system images, VM disks, credentials and raw machine logs stay in
ignored artifacts or the chosen external VM directory. No application dependency or
framework target changes are part of this setup. macOS qualification is deferred.
