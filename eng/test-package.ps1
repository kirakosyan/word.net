param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$ResultsDirectory = 'artifacts/package-tests',
    [switch]$IncludeFramework,
    [switch]$UseProvisionedOcr
)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/verify-release-package.ps1" -Version $Version -Commit $Commit
if (-not $UseProvisionedOcr) { & "$PSScriptRoot/prepare-ocr-tests.ps1" }
$project = Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests'
# Re-evaluate the generated obj/packaged lock after validating the supplied archives.
# Local preview rebuilds can reuse a version; committed source lock files stay intact.
dotnet restore $project "-p:WordnetPackageVersion=$Version" --force-evaluate --configfile "$PSScriptRoot/NuGet.Packages.Config"
if ($LASTEXITCODE -ne 0) { throw 'Packaged restore failed.' }
dotnet build $project -c Release --no-restore "-p:WordnetPackageVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Packaged build failed.' }
$required = [int](Get-Content "$PSScriptRoot/required-tests.txt" -Raw)
function Test-Asset([string]$Framework, [string]$Label) {
    dotnet test $project -c Release -f $Framework --no-build --no-restore "-p:WordnetPackageVersion=$Version" --logger "trx;LogFileName=$Label.trx" --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw "Packaged $Label tests failed." }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $ResultsDirectory "$Label.trx")
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt $required -or $counters.passed -ne $counters.total) { throw "Missing, failed or skipped cases: $Label" }
}
$oldExpected = $env:WORDNET_EXPECTED_TFM
try {
    $env:WORDNET_EXPECTED_TFM = $null
    Test-Asset net8.0 net8
    Test-Asset net10.0 net10
    if ($IncludeFramework) {
        if ($env:OS -ne 'Windows_NT') { throw 'Framework execution requires Windows.' }
        if (-not $UseProvisionedOcr) { & "$PSScriptRoot/prepare-ocr-tests.ps1" -Architecture x64 }
        Test-Asset net472 net472
    }
    if (-not $UseProvisionedOcr) { & "$PSScriptRoot/prepare-ocr-tests.ps1" }
    # Force the portable asset from this exact archive, then verify its TFM in the suite.
    # Modern runtime dependency closure is also checked by the complete conversion/OCR run.
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "artifacts/packages/wordnet.$Version.nupkg").Path)
    try {
        foreach ($framework in @('net8.0', 'net10.0')) {
            $library = Join-Path $project "bin/Release/$framework/wordnet.dll"
            $original = [IO.File]::ReadAllBytes($library)
            try {
                $source = $archive.GetEntry('lib/netstandard2.0/wordnet.dll').Open()
                $destination = [IO.File]::Create($library)
                try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
                $env:WORDNET_EXPECTED_TFM = '.NETStandard,Version=v2.0'
                Test-Asset $framework "netstandard-on-$framework"
            } finally { [IO.File]::WriteAllBytes($library, $original) }
        }
    } finally { $archive.Dispose() }
} finally { $env:WORDNET_EXPECTED_TFM = $oldExpected }
