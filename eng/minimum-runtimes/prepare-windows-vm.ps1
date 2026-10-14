param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/framework472-vm-setup'))
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; an existing setup bundle will not be overwritten.' }
Push-Location $repository
try {
    & dotnet restore tests/wordnet.Conversion.Tests --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked test restore failed.' }
    & dotnet build tests/wordnet.Conversion.Tests -c Release -f net472 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Framework test build failed.' }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    & (Join-Path $repository 'eng/create-framework-bundle.ps1') -OutputPath (Join-Path $output 'tests.zip')
    foreach ($file in @('start.ps1', 'install.bat', 'qualify.ps1', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "windows/$file") -Destination (Join-Path $output $file)
    }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead((Join-Path $output 'tests.zip'))
    try { $hash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
    $commit = (& git rev-parse HEAD) -join ''
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
    $manifest = [ordered]@{ testsSha256 = $hash; sourceCommit = $commit; requiredCases = [int](Get-Content eng/required-tests.txt -Raw); jitMode = 'legacy' }
    [IO.File]::WriteAllText((Join-Path $output 'bundle.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Write-Output "Prepared $output . Run its start.ps1 on the Windows x64 test PC."
} finally { Pop-Location }
