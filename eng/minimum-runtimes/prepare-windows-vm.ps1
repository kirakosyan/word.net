param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/framework472-vm-setup'),
    [string]$TestDirectory,
    [ValidateSet('source', 'package')][string]$TestAsset = 'source',
    [string]$PackageVersion
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; an existing setup bundle will not be overwritten.' }
if ($TestAsset -eq 'package' -and (-not $TestDirectory -or -not $PackageVersion)) {
    throw 'Package bundles require the already-tested package-mode TestDirectory and PackageVersion.'
}
Push-Location $repository
try {
    if (-not $TestDirectory) {
        & dotnet restore tests/wordnet.Conversion.Tests --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Locked test restore failed.' }
        & dotnet build tests/wordnet.Conversion.Tests -c Release -f net472 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Framework test build failed.' }
        $TestDirectory = Join-Path $repository 'tests/wordnet.Conversion.Tests/bin/Release/net472/win-x64'
    }
    $tests = [IO.Path]::GetFullPath($TestDirectory)
    $commit = (& git rev-parse HEAD) -join ''
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
    $binaries = [ordered]@{}
    foreach ($file in @('wordnet.dll', 'wordnet.Fonts.dll', 'wordnet.Ocr.dll', 'wordnet.Conversion.Tests.exe')) {
        $path = Join-Path $tests $file
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion
        if (-not $version.EndsWith("+$commit", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Stale test output: $file was not built from $commit."
        }
        $binaries[$file] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $packages = [ordered]@{}
    if ($TestAsset -eq 'package') {
        & (Join-Path $repository 'eng/verify-release-package.ps1') -Version $PackageVersion -Commit $commit
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        foreach ($id in @('wordnet', 'wordnet.Fonts', 'wordnet.Ocr')) {
            $path = Join-Path $repository "artifacts/packages/$id.$PackageVersion.nupkg"
            $packages[$id] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $archive = [IO.Compression.ZipFile]::OpenRead($path)
            try {
                $tfm = if ($id -eq 'wordnet.Fonts') { 'netstandard2.0' } else { 'net472' }
                $entry = $archive.GetEntry("lib/$tfm/$id.dll")
                if (-not $entry) { throw "Missing packaged Framework dependency: $id" }
                $stream = $entry.Open()
                $algorithm = [Security.Cryptography.SHA256]::Create()
                try { $hash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
                finally { $stream.Dispose(); $algorithm.Dispose() }
                if ($hash -ne $binaries["$id.dll"]) { throw "Test output does not contain the exact packaged $id asset." }
            } finally { $archive.Dispose() }
        }
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    & (Join-Path $repository 'eng/create-framework-bundle.ps1') -TestDirectory $tests -OutputPath (Join-Path $output 'tests.zip')
    foreach ($file in @('start.ps1', 'install.bat', 'qualify.ps1', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "windows/$file") -Destination (Join-Path $output $file)
    }
    $manifest = [ordered]@{
        testsSha256 = (Get-FileHash -LiteralPath (Join-Path $output 'tests.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        sourceCommit = $commit
        workflowRun = $env:GITHUB_RUN_ID
        testAsset = $TestAsset
        requiredCases = [int](Get-Content eng/required-tests.txt -Raw)
        jitMode = 'legacy'
        binaries = $binaries
        packageVersion = $PackageVersion
        packages = $packages
    }
    [IO.File]::WriteAllText((Join-Path $output 'bundle.json'), ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    Write-Output "Prepared $output . Run its start.ps1 on the Windows x64 test PC."
} finally { Pop-Location }
