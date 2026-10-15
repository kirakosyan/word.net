param(
    [Parameter(Mandatory = $true)][string]$ReferenceRevision,
    [string]$OutputDirectory = 'artifacts/performance-gate'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) { throw 'Choose a new performance output directory; previous evidence is preserved.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($name in @('Directory.Build.props', 'Directory.Packages.props', 'NuGet.Config', 'global.json')) {
    Copy-Item -LiteralPath (Join-Path $repository $name) -Destination (Join-Path $output $name)
}
$baseline = (& git -C $repository rev-parse --verify "$ReferenceRevision^{commit}") -join ''
$candidate = (& git -C $repository rev-parse --verify HEAD) -join ''
if ($baseline -notmatch '^[0-9a-f]{40}$' -or $candidate -notmatch '^[0-9a-f]{40}$') { throw 'Could not resolve exact source commits.' }
$archive = Join-Path $output 'baseline.zip'
& git -C $repository archive --format=zip "--output=$archive" $baseline
if ($LASTEXITCODE -ne 0) { throw 'Could not export reference source.' }
$baselineSource = Join-Path $output 'baseline-source'
Expand-Archive -LiteralPath $archive -DestinationPath $baselineSource
$measurements = Join-Path $output 'measurements'
[IO.Directory]::CreateDirectory($measurements) | Out-Null
$projects = @{}
foreach ($variant in @('reference', 'candidate')) {
    $harness = Join-Path $output "harness-$variant"
    [IO.Directory]::CreateDirectory($harness) | Out-Null
    # The same current harness source builds against both public APIs. Generated
    # comparison locks live only here; committed production locks are preserved.
    Get-ChildItem (Join-Path $repository 'tools/wordnet.Benchmarks') -File | Where-Object Extension -in @('.cs', '.csproj') |
        Copy-Item -Destination $harness
    $core = if ($variant -eq 'reference') { Join-Path $baselineSource 'src/wordnet/wordnet.csproj' } else { Join-Path $repository 'src/wordnet/wordnet.csproj' }
    $project = Join-Path $harness 'wordnet.Benchmarks.csproj'
    & dotnet restore $project "-p:WordnetSourceProject=$core"
    if ($LASTEXITCODE -ne 0) { throw "Performance harness restore failed: $variant" }
    & dotnet build $project -c Release --no-restore "-p:WordnetSourceProject=$core"
    if ($LASTEXITCODE -ne 0) { throw "Performance harness build failed: $variant" }
    $projects[$variant] = Join-Path $harness 'bin/Release/net10.0/wordnet.Benchmarks.dll'
}
$fixture = Join-Path $repository 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise/benchmark-10-pages-images.docx'
$reports = @{ reference = @(); candidate = @() }
# Alternate independent warm workers to reduce time/order bias; both retain the
# same affinity bit and machine identity, which the comparison verifies.
foreach ($round in 1..2) {
    foreach ($variant in @('reference', 'candidate')) {
        $report = Join-Path $measurements "$variant-$round.json"
        $revision = if ($variant -eq 'reference') { $baseline } else { $candidate }
        & dotnet $projects[$variant] --measure $fixture $report $revision
        if ($LASTEXITCODE -ne 0) { throw "Performance measurement failed: $variant" }
        $reports[$variant] += $report
    }
}
$comparison = & "$PSScriptRoot/compare-performance.ps1" -Reference $reports.reference -Candidate $reports.candidate
$comparison | ConvertTo-Json | Set-Content (Join-Path $output 'comparison.json') -Encoding utf8
$comparison
