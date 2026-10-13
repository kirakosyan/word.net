param(
    [Parameter(Mandatory = $true)][string]$AdbPath,
    [string]$WorkDirectory = (Join-Path $PSScriptRoot '../../artifacts/android-api21'),
    [ValidateRange(5554, 5682)][int]$Port = 5580,
    [ValidateRange(60, 3600)][int]$BootTimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This isolated emulator probe uses the pinned Windows x64 emulator build.' }
if ($Port % 2 -ne 0) { throw 'Use an even emulator console port.' }
$adb = [IO.Path]::GetFullPath($AdbPath)
if (-not (Test-Path -LiteralPath $adb)) { throw 'Supply an existing Android SDK platform-tools/adb.exe.' }
$root = [IO.Path]::GetFullPath($WorkDirectory)
$sdk = Join-Path $root 'sdk'
$avdRoot = Join-Path $root 'avd'
$avdName = 'wordnet-api21-arm64'
$avd = Join-Path $avdRoot "$avdName.avd"
$serial = "emulator-$Port"
$devices = (& $adb devices) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'ADB is unavailable.' }
if ($devices -match "(?m)^$serial\s") { throw "ADB already lists $serial. Use the existing emulator or choose a different work directory and port." }
[IO.Directory]::CreateDirectory($root) | Out-Null
function Get-Archive([string]$Url, [string]$Name, [string]$Sha256) {
    $path = Join-Path $root $Name
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $path }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { $actual = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
    if ($actual -ne $Sha256) { throw "Checksum mismatch: $path. Preserve/remove the failed download before retrying." }
    return $path
}
$emulatorHash = '3b524b6e0f1f5506d3d81af62ec9ce15b2a6f80f467e99574e167514ca181401'
$systemHash = '6f9f8ab64641ccbea92a02cc85c130a60c3464b87aa9d49952a8d8292f84d073'
$emulatorZip = Get-Archive 'https://dl.google.com/android/repository/emulator-windows_x64-16428233.zip' 'emulator.zip' $emulatorHash
$systemZip = Get-Archive 'https://dl.google.com/android/repository/sys-img/android/arm64-v8a-21_r04.zip' 'system-image.zip' $systemHash
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'emulator/emulator.exe'))) { Expand-Archive -LiteralPath $emulatorZip -DestinationPath $sdk }
$imageDirectory = Join-Path $sdk 'system-images/android-21/default'
if (-not (Test-Path -LiteralPath (Join-Path $imageDirectory 'arm64-v8a/system.img'))) { Expand-Archive -LiteralPath $systemZip -DestinationPath $imageDirectory }
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'platform-tools/adb.exe'))) {
    Copy-Item -LiteralPath ([IO.Path]::GetDirectoryName($adb)) -Destination (Join-Path $sdk 'platform-tools') -Recurse
}
[IO.Directory]::CreateDirectory($avd) | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $avd 'config.ini'))) {
    $config = @'
AvdId=wordnet-api21-arm64
abi.type=arm64-v8a
avd.ini.encoding=UTF-8
hw.cpu.arch=arm64
hw.cpu.ncore=2
hw.ramSize=2048
hw.lcd.width=480
hw.lcd.height=800
hw.lcd.density=160
hw.gpu.enabled=yes
hw.gpu.mode=swiftshader
hw.keyboard=yes
disk.dataPartition.size=2G
showDeviceFrame=no
tag.id=default
target=android-21
image.sysdir.1=system-images/android-21/default/arm64-v8a/
'@
    [IO.File]::WriteAllText((Join-Path $avd 'config.ini'), $config, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $avdRoot "$avdName.ini"), "avd.ini.encoding=UTF-8`npath=$avd`ntarget=android-21`n", [Text.UTF8Encoding]::new($false))
}
# The 37.2.12 launcher rejects this legacy ARM64 configuration. Its supplied
# headless aarch64 engine can boot the official API 21 image using software emulation.
$binary = Join-Path $sdk 'emulator/qemu/windows-x86_64/qemu-system-aarch64-headless.exe'
$savedEnvironment = @{}
foreach ($name in @('ANDROID_HOME', 'ANDROID_SDK_ROOT', 'ANDROID_AVD_HOME', 'ANDROID_EMULATOR_LAUNCHER_DIR', 'PATH')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:ANDROID_HOME = $sdk
    $env:ANDROID_SDK_ROOT = $sdk
    $env:ANDROID_AVD_HOME = $avdRoot
    $env:ANDROID_EMULATOR_LAUNCHER_DIR = Join-Path $sdk 'emulator'
    $env:PATH = "$sdk/emulator;$sdk/emulator/lib64;$env:PATH"
    $process = Start-Process -FilePath $binary -WorkingDirectory (Join-Path $sdk 'emulator') -ArgumentList "-avd $avdName -no-window -no-audio -no-snapshot -no-boot-anim -accel off -gpu swiftshader -port $Port" -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'emulator.stdout.log') -RedirectStandardError (Join-Path $root 'emulator.stderr.log')
} finally {
    # Only the emulator child uses the isolated SDK. Later Android builds still
    # need the caller's complete SDK with platforms, build tools and licenses.
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
$process.Id | Set-Content -LiteralPath (Join-Path $root 'emulator.pid')
Write-Output "Booting API 21 ARM64 through software emulation; first boot may take several minutes. Logs: $root"
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    do {
        if ($process.HasExited) { throw "Emulator exited with code $($process.ExitCode). Inspect emulator.stderr.log and emulator.stdout.log." }
        Start-Sleep -Seconds 5
        # An offline ADB target is expected during boot, including with Windows
        # PowerShell's native-stderr-as-ErrorRecord behavior.
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $boot = (& $adb -s $serial shell getprop sys.boot_completed 2>$null) -join ''
        } finally { $ErrorActionPreference = $previousPreference }
        if ($boot.Trim() -eq '1') { break }
        Write-Output "Waiting for Android boot: $([int]$timer.Elapsed.TotalSeconds)s"
    } while ($timer.Elapsed.TotalSeconds -lt $BootTimeoutSeconds)
    if ($boot.Trim() -ne '1') { throw 'Android did not finish booting before the deadline.' }
    $api = ((& $adb -s $serial shell getprop ro.build.version.sdk) -join '').Trim()
    $abi = ((& $adb -s $serial shell getprop ro.product.cpu.abilist) -join '').Trim()
    if ($api -ne '21' -or $abi -notmatch '(^|,)arm64-v8a(,|$)') { throw "Wrong runtime: API=$api ABI=$abi" }
    $fingerprint = ((& $adb -s $serial shell getprop ro.build.fingerprint) -join '').Trim()
    [ordered]@{ api = $api; abi = $abi; fingerprint = $fingerprint; serial = $serial; emulatorArchiveSha256 = $emulatorHash; systemArchiveSha256 = $systemHash; acceleration = 'software' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'environment.json') -Encoding UTF8
    Write-Output "Ready: $serial, API=$api, ABI=$abi. Run eng/run-android-tests.ps1 with this serial. Stop with: adb -s $serial emu kill"
} catch {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    throw
}
