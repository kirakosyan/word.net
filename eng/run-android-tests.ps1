param(
    [Parameter(Mandatory = $true)][string]$AdbPath,
    [string]$Serial,
    [string]$ResultsPath,
    [ValidateRange(5, 7200)][int]$TimeoutSeconds = 300,
    [ValidateRange(0, 100)][int]$RequiredApiLevel = 0,
    [ValidateSet('jit', 'interpreter')][string]$ExecutionMode = 'jit',
    [ValidateRange(5, 300)][int]$StartupGraceSeconds = 60
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'android-report.ps1')
if (-not $ResultsPath) { $ResultsPath = Join-Path $PSScriptRoot '../artifacts/test-results/android-arm64/results.xml' }
if (-not $Serial) {
    $devices = @(& $AdbPath devices | Where-Object { $_ -match '^\S+\s+device$' })
    if ($devices.Count -ne 1) { throw 'Connect one authorized Android ARM64 device or pass -Serial.' }
    $Serial = ($devices[0] -split '\s+')[0]
}
function Invoke-Adb([string[]]$Arguments) {
    & $AdbPath -s $Serial @Arguments
    if ($LASTEXITCODE -ne 0) { throw "ADB command failed: $($Arguments -join ' ')" }
}
function Read-AppReport {
    # API 21 has no executable `test` for run-as, and legacy ADB shell transport
    # can report exit zero even when the remote command failed. Read the report
    # directly and require complete XML; a missing/partial file is not a pass.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $text = (& $AdbPath -s $Serial exec-out run-as org.wordnet.validation cat files/results.xml 2>$null) -join "`n"
        if ($LASTEXITCODE -ne 0) { return $null }
    } finally { $ErrorActionPreference = $previousPreference }
    return Get-CompleteAndroidReport $text
}
$apk = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/wordnet.Android.Compatibility/bin/Release/net10.0-android/android-arm64/org.wordnet.validation-Signed.apk'))
if (-not (Test-Path -LiteralPath $apk)) { throw 'Build the Android test app with -c Release -t:SignAndroidPackage first.' }
$api = ((Invoke-Adb @('shell', 'getprop', 'ro.build.version.sdk')) -join '').Trim()
$abi = ((Invoke-Adb @('shell', 'getprop', 'ro.product.cpu.abilist')) -join '').Trim()
if ($abi -notmatch '(^|,)arm64-v8a(,|$)') { throw "Android qualification requires ARM64; connected ABI list is $abi." }
if ($RequiredApiLevel -and $api -ne [string]$RequiredApiLevel) { throw "Expected Android API $RequiredApiLevel, but $Serial reports API $api." }
$algorithm = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead($apk)
try { $apkHash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
finally { $stream.Dispose(); $algorithm.Dispose() }
$path = [IO.Path]::GetFullPath($ResultsPath)
$directory = [IO.Path]::GetDirectoryName($path)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$environmentPath = [IO.Path]::ChangeExtension($path, 'environment.json')
$previous = Join-Path $directory ('{0}.previous-{1}' -f [IO.Path]::GetFileNameWithoutExtension($path), [DateTime]::UtcNow.Ticks)
if (Test-Path -LiteralPath $path) { Move-Item -LiteralPath $path -Destination ($previous + '.xml') }
if (Test-Path -LiteralPath $environmentPath) { Move-Item -LiteralPath $environmentPath -Destination ($previous + '.environment.json') }
$evidence = [ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); api = $api; abi = $abi; requestedExecutionMode = $ExecutionMode; apkSha256 = $apkHash; reportCollected = $false }
$evidence | ConvertTo-Json | Set-Content -LiteralPath $environmentPath -Encoding UTF8
Invoke-Adb @('install', '--no-incremental', '-r', $apk)
Invoke-Adb @('shell', 'am', 'force-stop', 'org.wordnet.validation')
# Remove only this harness's previous report so a stale pass cannot satisfy a new run.
$clear = ((Invoke-Adb @('shell', 'run-as', 'org.wordnet.validation', 'rm', '-f', 'files/results.xml', 'files/progress.txt')) -join '').Trim()
if ($clear) { throw "Could not clear the test app's previous evidence: $clear" }
Invoke-Adb @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
# API 21's `am start -W` can remain blocked after a native startup crash. Let the
# host's bounded report/process polling own the wait instead.
Invoke-Adb @('shell', 'am', 'start', '-n', 'org.wordnet.validation/org.wordnet.validation.MainActivity')
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    $report = Read-AppReport
    if ($report) {
        [IO.File]::WriteAllText($path, $report, [Text.UTF8Encoding]::new($false))
        $suite = ([xml]$report).testsuite
        if ($suite.executionMode -ne $ExecutionMode) { throw "Expected $ExecutionMode execution evidence, but the report records '$($suite.executionMode)'. Rebuild with the intended UseInterpreter setting." }
        if ($suite.api -ne $api -or $suite.architecture -ne 'Arm64') { throw 'The report does not match the selected Android API/architecture.' }
        $evidence.reportCollected = $true
        $evidence.executionMode = [string]$suite.executionMode
        $evidence.runtime = [string]$suite.runtime
        $evidence | ConvertTo-Json | Set-Content -LiteralPath $environmentPath -Encoding UTF8
        Write-Output "Android: $($suite.tests) tests, $($suite.failures) failures, $($suite.skipped) skipped. Report: $path"
        $required = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-tests.txt') -Raw)
        if ([int]$suite.tests -lt $required -or [int]$suite.failures -ne 0 -or [int]$suite.skipped -ne 0) { throw 'Android qualification failed; inspect the report.' }
        exit 0
    }
    if ($timer.Elapsed.TotalSeconds -ge $StartupGraceSeconds) {
        $processArguments = Get-AndroidProcessArguments ([int]$api)
        $processes = (& $AdbPath -s $Serial @processArguments) -join "`n"
        if ($LASTEXITCODE -ne 0 -or $processes -notmatch '(?m)^USER\s+PID\s') { throw 'Could not inspect the Android test process after startup.' }
        if ($processes -notmatch '(?m)\sorg\.wordnet\.validation\s*$') { throw 'The Android test process exited without producing a complete report. Collect logcat; no qualification pass is established.' }
    }
    Start-Sleep -Seconds 2
}
$progress = (& $AdbPath -s $Serial shell run-as org.wordnet.validation cat files/progress.txt 2>&1) -join "`n"
throw "Android test app did not produce a report before the deadline. Last progress: $progress"
