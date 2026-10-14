param(
    [string]$TestDirectory = (Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests/bin/Release/net472/win-x64'),
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$tests = [IO.Path]::GetFullPath($TestDirectory)
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path (Join-Path $tests 'wordnet.Conversion.Tests.exe'))) { throw 'Build the complete net472 test output first.' }
if (Test-Path -LiteralPath $output) { throw 'Choose a new bundle path; existing evidence is not overwritten.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($tests, $output, [IO.Compression.CompressionLevel]::Optimal, $false)
$zip = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($script in @('run-framework-tests.ps1', 'framework-process.ps1', 'prepare-ocr-tests.ps1', 'required-tests.txt')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot $script), $script) | Out-Null
    }
    foreach ($notice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot "../$notice"), $notice) | Out-Null
    }
    $runner = @'
param([switch]$RequireFramework472, [ValidateSet('default', 'legacy')][string]$JitMode = 'default')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/prepare-ocr-tests.ps1" -Architecture x64 -OutputDirectory "$PSScriptRoot/prerequisites"
& "$PSScriptRoot/run-framework-tests.ps1" -TestDirectory $PSScriptRoot -ResultsDirectory "$PSScriptRoot/results" -RequireFramework472:$RequireFramework472 -UseProvisionedOcr -JitMode $JitMode
'@
    $writer = [IO.StreamWriter]::new($zip.CreateEntry('run.ps1').Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write($runner) } finally { $writer.Dispose() }
    $instructions = @'
wordnet Windows x64 qualification bundle

Extract this ZIP to a dedicated directory. No .NET SDK is needed.
In PowerShell from that directory run:
  ./run.ps1
For the actual .NET Framework 4.7.2 minimum-runtime gate run:
  ./run.ps1 -RequireFramework472 -JitMode legacy

Actual 4.7.2 x64 needs the legacy JIT compatibility configuration for this candidate.
Default-JIT comparison runs use -JitMode default and remain a separate failing lane.
The mode is scoped to the test child and recorded with the configuration hash.

The runner checks the installed runtime without changing it. It downloads and verifies
pinned English OCR test prerequisites, executes every case and writes results/net472.trx
and results/environment.json. Return both files. A newer Framework runtime is recorded
as such and cannot satisfy -RequireFramework472. Do not downgrade a shared machine.
The test app has no production service or credentials. Fixtures and dependencies retain
their licenses in Fixtures/ and THIRD_PARTY_NOTICES.md. This bundle is test-only.
'@
    $writer = [IO.StreamWriter]::new($zip.CreateEntry('RUNNING.txt').Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write($instructions) } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
Write-Output $output
