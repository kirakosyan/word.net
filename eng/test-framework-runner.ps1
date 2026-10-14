param([string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'These checks require the Windows x64 Framework runner.' }
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $PSScriptRoot '../artifacts/framework-runner-self-test' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$fixture = Join-Path $work 'fixture'
$results = Join-Path $work 'results'
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.Directory]::CreateDirectory($results) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /warnaserror+ /target:exe /platform:x64 ("/out:{0}" -f (Join-Path $fixture 'wordnet.Conversion.Tests.exe')) (Join-Path $PSScriptRoot 'test-assets/FrameworkRunnerProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the negative-test worker.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../src/wordnet/bin/Release/net472/wordnet.dll') -Destination $fixture -Force
$savedData = $env:WORDNET_TESSDATA
$savedLibrary = $env:WORDNET_TESSERACT_LIBRARY
$savedExit = $env:WORDNET_RUNNER_PROBE_EXIT
try {
    # The fixture never uses OCR. Avoid downloading prerequisites for runner checks.
    $env:WORDNET_TESSDATA = $fixture
    $env:WORDNET_TESSERACT_LIBRARY = $fixture
    $env:WORDNET_RUNNER_PROBE_EXIT = $null
    $configuration = Join-Path $fixture 'wordnet.Conversion.Tests.exe.config'
    '<configuration><runtime><useLegacyJit enabled="1" /></runtime></configuration>' | Set-Content -LiteralPath $configuration
    $modeMismatchRejected = $false
    try { & (Join-Path $PSScriptRoot 'run-framework-tests.ps1') -TestDirectory $fixture -ResultsDirectory $results -UseProvisionedOcr -JitMode default }
    catch { $modeMismatchRejected = $_.Exception.Message -like '*does not match -JitMode*' }
    Remove-Item -LiteralPath $configuration
    if (-not $modeMismatchRejected) { throw 'A legacy-JIT application configuration was accepted as default-JIT evidence.' }
    $deadlineRejected = $false
    try { & (Join-Path $PSScriptRoot 'run-framework-tests.ps1') -TestDirectory $fixture -ResultsDirectory $results -UseProvisionedOcr -TimeoutSeconds 3 }
    catch { $deadlineRejected = $_.Exception.Message -like '*exceeded the 3-second deadline*' }
    if (-not $deadlineRejected) { throw 'An over-deadline worker was accepted.' }
    $workerId = [int](Get-Content -LiteralPath (Join-Path $fixture 'worker.pid'))
    if (Get-Process -Id $workerId -ErrorAction SilentlyContinue) { throw 'The timed-out worker was not terminated.' }
    $env:WORDNET_RUNNER_PROBE_EXIT = 'fail'
    $exitCodePreserved = $false
    try { & (Join-Path $PSScriptRoot 'run-framework-tests.ps1') -TestDirectory $fixture -ResultsDirectory $results -UseProvisionedOcr -TimeoutSeconds 10 }
    catch { $exitCodePreserved = $_.Exception.Message -like '*exit code 17.*' }
    if (-not $exitCodePreserved) { throw 'The fast-failing worker exit code was lost.' }
    # Deliberately tempting stale evidence: a zero-exit worker writes no new report.
    # This fixture must never be counted as a conversion qualification result.
    $required = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-tests.txt') -Raw)
    $report = Join-Path $results 'net472.trx'
    "<TestRun><ResultSummary><Counters total='$required' passed='$required' /></ResultSummary></TestRun>" | Set-Content -LiteralPath $report
    $env:WORDNET_RUNNER_PROBE_EXIT = '1'
    $staleRejected = $false
    try { & (Join-Path $PSScriptRoot 'run-framework-tests.ps1') -TestDirectory $fixture -ResultsDirectory $results -UseProvisionedOcr -TimeoutSeconds 10 }
    catch { $staleRejected = $_.Exception.Message -like '*net472.trx*' }
    if (-not $staleRejected -or (Test-Path -LiteralPath $report)) { throw 'Stale evidence satisfied a run that produced no report.' }
    if (@(Get-ChildItem -LiteralPath $results -Filter 'net472.previous-*.trx').Count -lt 1) { throw 'The prior report was not preserved.' }
    $evidence = [ordered]@{ jitModeMismatchRejected=$true; deadlineRejected=$true; workerTerminated=$true; exitCodePreserved=$true; staleReportRejected=$true; previousReportPreserved=$true }
    $evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'self-test.json') -Encoding UTF8
    Write-Output 'Framework runner negative checks passed: deadline, worker termination, exit code, stale-report rejection and report preservation.'
} finally {
    $env:WORDNET_TESSDATA = $savedData
    $env:WORDNET_TESSERACT_LIBRARY = $savedLibrary
    $env:WORDNET_RUNNER_PROBE_EXIT = $savedExit
}
