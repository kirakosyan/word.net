param(
    [Parameter(Mandatory = $true)][string]$AdbPath,
    [string]$Serial,
    [string]$ResultsPath,
    [int]$TimeoutSeconds = 300
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
$apk = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/wordnet.Android.Compatibility/bin/Release/net10.0-android/android-arm64/org.wordnet.validation-Signed.apk'))
if (-not (Test-Path -LiteralPath $apk)) { throw 'Build the Android test app with -c Release -t:SignAndroidPackage first.' }
Invoke-Adb @('install', '--no-incremental', '-r', $apk)
Invoke-Adb @('shell', 'am', 'force-stop', 'org.wordnet.validation')
# Remove only this harness's previous report so a stale pass cannot satisfy a new run.
Invoke-Adb @('shell', 'run-as', 'org.wordnet.validation', 'rm', '-f', 'files/results.xml')
Invoke-Adb @('shell', 'run-as', 'org.wordnet.validation', 'rm', '-f', 'files/progress.txt')
Invoke-Adb @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
Invoke-Adb @('shell', 'am', 'start', '-W', '-n', 'org.wordnet.validation/org.wordnet.validation.MainActivity')
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    & $AdbPath -s $Serial shell run-as org.wordnet.validation test -f files/results.xml
    if ($LASTEXITCODE -eq 0) {
        $report = (Invoke-Adb @('exec-out', 'run-as', 'org.wordnet.validation', 'cat', 'files/results.xml')) -join "`n"
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
