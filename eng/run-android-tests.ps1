param(
    [Parameter(Mandatory = $true)][string]$AdbPath,
    [string]$Serial,
    [string]$ResultsPath,
    [int]$TimeoutSeconds = 300,
    [ValidateRange(0, 100)][int]$RequiredApiLevel = 0
)
$ErrorActionPreference = 'Stop'
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
        if ($LASTEXITCODE -ne 0 -or $text -notmatch '^\s*<') { return $null }
    } finally { $ErrorActionPreference = $previousPreference }
    try { $document = [xml]$text } catch { return $null }
    if ($document.DocumentElement.Name -ne 'testsuite') { return $null }
    return $text
}
$apk = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/wordnet.Android.Compatibility/bin/Release/net10.0-android/android-arm64/org.wordnet.validation-Signed.apk'))
if (-not (Test-Path -LiteralPath $apk)) { throw 'Build the Android test app with -c Release -t:SignAndroidPackage first.' }
$api = ((Invoke-Adb @('shell', 'getprop', 'ro.build.version.sdk')) -join '').Trim()
$abi = ((Invoke-Adb @('shell', 'getprop', 'ro.product.cpu.abilist')) -join '').Trim()
if ($abi -notmatch '(^|,)arm64-v8a(,|$)') { throw "Android qualification requires ARM64; connected ABI list is $abi." }
if ($RequiredApiLevel -and $api -ne [string]$RequiredApiLevel) { throw "Expected Android API $RequiredApiLevel, but $Serial reports API $api." }
Invoke-Adb @('install', '--no-incremental', '-r', $apk)
Invoke-Adb @('shell', 'am', 'force-stop', 'org.wordnet.validation')
# Remove only this harness's previous report so a stale pass cannot satisfy a new run.
$clear = ((Invoke-Adb @('shell', 'run-as', 'org.wordnet.validation', 'rm', '-f', 'files/results.xml', 'files/progress.txt')) -join '').Trim()
if ($clear) { throw "Could not clear the test app's previous evidence: $clear" }
Invoke-Adb @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
Invoke-Adb @('shell', 'am', 'start', '-W', '-n', 'org.wordnet.validation/org.wordnet.validation.MainActivity')
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    $report = Read-AppReport
    if ($report) {
        $path = [IO.Path]::GetFullPath($ResultsPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, $report, [Text.UTF8Encoding]::new($false))
        $suite = ([xml]$report).testsuite
        Write-Output "Android: $($suite.tests) tests, $($suite.failures) failures, $($suite.skipped) skipped. Report: $path"
        $required = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-tests.txt') -Raw)
        if ([int]$suite.tests -lt $required -or [int]$suite.failures -ne 0 -or [int]$suite.skipped -ne 0) { throw 'Android qualification failed; inspect the report.' }
        exit 0
    }
    Start-Sleep -Seconds 2
}
$progress = (& $AdbPath -s $Serial shell run-as org.wordnet.validation cat files/progress.txt 2>&1) -join "`n"
throw "Android test app did not produce a report before the deadline. Last progress: $progress"
