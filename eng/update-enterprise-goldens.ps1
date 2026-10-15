$ErrorActionPreference = 'Stop'
$previous = $env:WORDNET_REGENERATE_GOLDENS
try {
    $env:WORDNET_REGENERATE_GOLDENS = Join-Path (Split-Path $PSScriptRoot) 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise/Goldens'
    dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --filter FullyQualifiedName~EnterpriseVisualTests
    if ($LASTEXITCODE -ne 0) { throw 'Golden generation failed.' }
    Write-Host 'Inspect every changed PNG before accepting the new baselines. These are regression baselines, not Microsoft Word references.'
} finally { $env:WORDNET_REGENERATE_GOLDENS = $previous }
