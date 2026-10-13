param(
    [string]$TestDirectory = (Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests/bin/Release/net472/win-x64'),
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/production-tests/framework'),
    [switch]$RequireFramework472,
    [switch]$UseProvisionedOcr
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Framework qualification requires Windows.' }
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $key = $registry.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full')
    if ($null -eq $key) { throw 'No .NET Framework 4.x Full runtime is installed.' }
    try { $release = [int]$key.GetValue('Release') } finally { $key.Dispose() }
} finally { $registry.Dispose() }
$isMinimum = $release -eq 461808 -or $release -eq 461814
if ($release -lt 461808) { throw 'The installed Framework is older than 4.7.2.' }
if ($RequireFramework472 -and -not $isMinimum) { throw "Framework release $release is not 4.7.2 itself. Do not downgrade a shared PC; use an isolated VM for this lane." }
$testRoot = [IO.Path]::GetFullPath($TestDirectory)
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$runner = Join-Path $testRoot 'wordnet.Conversion.Tests.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Build the net472 tests on the development PC, then copy the complete win-x64 output directory here.' }
[IO.Directory]::CreateDirectory($results) | Out-Null
if (-not $UseProvisionedOcr) { & (Join-Path $PSScriptRoot 'prepare-ocr-tests.ps1') -Architecture x64 }
if (-not $env:WORDNET_TESSDATA -or -not $env:WORDNET_TESSERACT_LIBRARY) { throw 'Provision local x64 English OCR before running the suite.' }
function Hash([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$evidence = [ordered]@{
    utc = [DateTime]::UtcNow.ToString('o')
    os = [Environment]::OSVersion.VersionString
    reportedArchitecture = "$env:PROCESSOR_ARCHITECTURE / $env:PROCESSOR_ARCHITEW6432"
    testProcessArchitecture = 'x64'
    frameworkRelease = $release
    exactFramework472 = $isMinimum
    librarySha256 = (Hash (Join-Path $testRoot 'wordnet.dll'))
    testSha256 = (Hash $runner)
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'environment.json') -Encoding UTF8
# The in-process xUnit executable needs only Framework, not a modern .NET SDK on the old VM.
& $runner -noColor -failSkips -failWarns -result-trx (Join-Path $results 'net472.trx')
if ($LASTEXITCODE -ne 0) { throw "Framework test runner failed with exit code $LASTEXITCODE." }
[xml]$report = Get-Content -LiteralPath (Join-Path $results 'net472.trx') -Raw
$counters = $report.TestRun.ResultSummary.Counters
$required = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-tests.txt') -Raw)
if ([int]$counters.total -lt $required -or [int]$counters.passed -ne [int]$counters.total) { throw 'Framework report is incomplete or contains failing/skipped cases.' }
Write-Output "Framework suite passed. Release=$release; exact 4.7.2=$isMinimum. Evidence: $results"
