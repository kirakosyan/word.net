param([string]$OutputDirectory = 'artifacts/pdfa-validation')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$pdfs = Join-Path $output 'pdfs'
if (Test-Path -LiteralPath $pdfs) { throw 'Choose a new PDF/A output directory; preserve previous evidence.' }
$fixtures = Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests/Fixtures/Enterprise'
& dotnet "$PSScriptRoot/../tools/wordnet.ManagedExport.Probe/bin/Release/net10.0/wordnet.ManagedExport.Probe.dll" pdfa $fixtures $pdfs
if ($LASTEXITCODE -ne 0) { throw 'PDF/A corpus export failed.' }
$expected = @(Import-Csv (Join-Path $fixtures 'manifest.csv') | Where-Object outcome -eq convert).Count
& docker run --rm --mount "type=bind,source=$pdfs,target=/data,readonly" `
    verapdf/cli:v1.30.2@sha256:d5ee329657cf9bc4b2400392dd54c7d0a0ce9980ff6fa2da5590eebeec007cdb `
    --format xml --flavour 2b /data 1> (Join-Path $output 'verapdf.xml') 2> (Join-Path $output 'verapdf.stderr')
$validatorExit = $LASTEXITCODE
[xml]$report = Get-Content (Join-Path $output 'verapdf.xml') -Raw
$items = @($report.SelectNodes("//*[local-name()='validationReport']"))
$failed = @($items | Where-Object isCompliant -ne true)
if ($validatorExit -ne 0 -or $items.Count -ne $expected -or $failed.Count -gt 0) {
    throw "PDF/A-2b validation failed: $($items.Count)/$expected reports, $($failed.Count) noncompliant, exit $validatorExit. Inspect verapdf.xml."
}
Write-Output "veraPDF 1.30.2: $expected/$expected PDF/A-2b files compliant."
