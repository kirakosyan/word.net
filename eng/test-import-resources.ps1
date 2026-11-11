param(
    [ValidateSet('net8.0', 'net10.0', 'net472')][string]$Framework = 'net10.0',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/import-resources'),
    [int]$MaximumSeconds = 60,
    [int]$MaximumMemoryMiB = 512
)
$ErrorActionPreference = 'Stop'
if ($MaximumSeconds -lt 1 -or $MaximumMemoryMiB -lt 1) { throw 'Guard limits must be positive.' }
if ($Framework -eq 'net472' -and -not $IsWindows) { throw 'Framework tests require Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$report = Join-Path $output "$Framework.xml"
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$binary = Join-Path $root "tests/wordnet.Conversion.Tests/bin/Release/$Framework/wordnet.Conversion.Tests.dll"
$start = [Diagnostics.ProcessStartInfo]::new()
if ($Framework -eq 'net472') {
    $binary = Join-Path $root 'tests/wordnet.Conversion.Tests/bin/Release/net472/win-x64/wordnet.Conversion.Tests.exe'
    $start.FileName = $binary
} else {
    $start.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $start.ArgumentList.Add($binary)
}
if (-not (Test-Path -LiteralPath $binary)) { throw "Build the $Framework tests first: $binary" }
foreach ($argument in @('-class', 'WordNet.Tests.ImportResourceTests', '-parallelMode', 'none', '-noLogo', '-noColor', '-failSkips', '-result-xml', $report)) {
    $start.ArgumentList.Add($argument)
}
$start.WorkingDirectory = $root
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$timer = [Diagnostics.Stopwatch]::StartNew()
$peak = 0L
$status = 'completed'
$started = $false
try {
    if (-not $process.Start()) { throw 'Could not start resource regression worker.' }
    $started = $true
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    while (-not $process.WaitForExit(50)) {
        try {
            $process.Refresh()
            $peak = [Math]::Max($peak, $process.PeakWorkingSet64)
        } catch [ComponentModel.Win32Exception] {
            if (-not $process.HasExited) { throw }
        } catch [InvalidOperationException] {
            if (-not $process.HasExited) { throw }
        }
        if ($peak -gt $MaximumMemoryMiB * 1MB) { $status = 'memory-limit'; break }
        if ($timer.Elapsed.TotalSeconds -gt $MaximumSeconds) { $status = 'timeout'; break }
    }
    if ($status -ne 'completed' -and -not $process.HasExited) { $process.Kill($true) }
    $process.WaitForExit()
    $timer.Stop()
    [IO.File]::WriteAllText((Join-Path $output "$Framework.stdout.txt"), $stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $output "$Framework.stderr.txt"), $stderr.GetAwaiter().GetResult())
    if ($status -eq 'completed' -and $process.ExitCode -ne 0) { $status = 'worker-failed' }
    if ($status -eq 'completed') {
        if (-not (Test-Path -LiteralPath $report)) { $status = 'missing-report' }
        else {
            [xml]$tests = Get-Content -LiteralPath $report -Raw
            $assembly = $tests.assemblies.assembly
            if ([int]$assembly.total -lt 31 -or $assembly.passed -ne $assembly.total -or [int]$assembly.failed -ne 0 -or [int]$assembly.skipped -ne 0) {
                $status = 'incomplete-tests'
            }
        }
    }
    $receipt = [ordered]@{
        framework = $Framework; status = $status; exitCode = $process.ExitCode
        elapsedSeconds = $timer.Elapsed.TotalSeconds; sampledPeakBytes = $peak
        maximumSeconds = $MaximumSeconds; maximumMemoryMiB = $MaximumMemoryMiB
        testAssemblySha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
        librarySha256 = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($binary)) 'wordnet.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        providerSha256 = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($binary)) 'wordnet.Ocr.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $json = $receipt | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $output "$Framework.json"), $json)
    Write-Output $json
    if ($status -ne 'completed') { throw "Resource regressions failed: $status. See $output." }
} finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
