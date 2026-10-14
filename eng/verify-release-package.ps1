param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$PackageDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')
Assert-ReleaseVersion -Version $Version
if ($Commit -notmatch '^[0-9a-f]{40}$') { throw 'Expected a full Git commit SHA.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$frameworks = @('net472', 'netstandard2.0', 'net8.0', 'net10.0')
foreach ($extension in @('nupkg', 'snupkg')) {
    $path = Join-Path $PackageDirectory "wordnet.$Version.$extension"
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        $manifest = $archive.GetEntry('wordnet.nuspec')
        if ($null -eq $manifest) { throw "Missing nuspec in $path" }
        $reader = [IO.StreamReader]::new($manifest.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        if ($metadata.id -cne 'wordnet' -or $metadata.version -cne $Version) {
            throw "Package identity mismatch in $path"
        }
        if ($metadata.repository.commit -cne $Commit -or
            $metadata.repository.url -ne 'https://github.com/kirakosyan/word.net') {
            throw "Package repository/commit mismatch in $path"
        }
        if ($extension -eq 'nupkg' -and $metadata.license.InnerText -ne 'MIT') {
            throw "Missing MIT license in $path"
        }
        $required = if ($extension -eq 'nupkg') {
            @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
              'licenses/NotoSans-OFL.txt', 'licenses/NotoSans-Provenance.md') +
            @($frameworks | ForEach-Object { "lib/$_/wordnet.dll"; "lib/$_/wordnet.xml" })
        } else {
            @($frameworks | ForEach-Object { "lib/$_/wordnet.pdb" })
        }
        foreach ($entry in $required) {
            if ($null -eq $archive.GetEntry($entry) -or $archive.GetEntry($entry).Length -eq 0) {
                throw "Missing or empty $entry in $path"
            }
        }
        $libraries = @($archive.Entries | Where-Object { $_.FullName -match '^lib/[^/]+/wordnet\.(dll|pdb)$' })
        if ($libraries.Count -ne 4) { throw "Unexpected framework assets in $path" }
    } finally { $archive.Dispose() }
    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
    Write-Host "$hash  $path"
}
