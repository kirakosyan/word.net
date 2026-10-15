param([string]$Output = 'artifacts/packages')
$ErrorActionPreference = 'Stop'
foreach ($id in @('wordnet.Fonts', 'wordnet', 'wordnet.Ocr')) {
    dotnet pack "src/$id/$id.csproj" -c Release --no-build --no-restore --output $Output
    if ($LASTEXITCODE -ne 0) { throw "Packaging failed: $id" }
}
