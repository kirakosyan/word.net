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
$frameworks = @('net462', 'net472', 'netstandard2.0', 'net8.0', 'net10.0')
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
        if ($libraries.Count -ne $frameworks.Count) { throw "Unexpected framework assets in $path" }
        if ($extension -eq 'nupkg') {
            foreach ($group in $metadata.dependencies.group) {
                # .NET 10 prunes this package because its shared framework supplies assembly v10.
                if ($group.targetFramework -ne 'net10.0' -and @($group.dependency | Where-Object id -EQ 'System.Text.Encoding.CodePages').Count -ne 1) {
                    throw "Missing directly used code-page dependency for $($group.targetFramework) in $path"
                }
            }
            if (@($archive.Entries | Where-Object FullName -Match '/native/').Count -gt 0) { throw 'Core package contains native assets.' }
            if (@($metadata.dependencies.group.dependency | Where-Object id -Match 'Skia|HarfBuzz|wordnet.Ocr').Count -gt 0) { throw 'Core package pulls native providers.' }
        }
    } finally { $archive.Dispose() }
    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
    Write-Host "$hash  $path"
}

foreach ($id in @('wordnet.Fonts', 'wordnet.Ocr')) {
    $path = Join-Path $PackageDirectory "$id.$Version.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -cne $id -or $nuspec.package.metadata.version -cne $Version -or $nuspec.package.metadata.repository.commit -cne $Commit) {
            throw "Companion package identity mismatch: $id"
        }
        $targets = if ($id -eq 'wordnet.Fonts') { @('netstandard2.0') } else { $frameworks }
        foreach ($target in $targets) {
            if ($null -eq $archive.GetEntry("lib/$target/$id.dll")) { throw "Missing $target in $id" }
        }
        if ($id -eq 'wordnet.Fonts') {
            foreach ($entry in @('licenses/Liberation-OFL.txt', 'licenses/Carlito-OFL.txt', 'licenses/Caladea-OFL.txt', 'licenses/provenance.json')) {
                if ($null -eq $archive.GetEntry($entry)) { throw "Missing font notice: $entry" }
            }
        }
        if (@($archive.Entries | Where-Object FullName -Match '/native/').Count -gt 0) { throw 'Companion archive unexpectedly embeds native assets instead of explicit NuGet dependencies.' }
    } finally { $archive.Dispose() }
    Write-Host "Validated companion package: $path"
}

# Log only hashes and public package-entry names. These receipts allow independent
# comparison with repository-signed NuGet archives without exposing credentials or
# relying on a second authenticated artifact download.
foreach ($id in @('wordnet.Fonts', 'wordnet', 'wordnet.Ocr')) {
    $path = (Resolve-Path -LiteralPath (Join-Path $PackageDirectory "$id.$Version.nupkg")).Path
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $entries = [ordered]@{}
    try {
        foreach ($entry in ($archive.Entries | Sort-Object FullName)) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $stream = $entry.Open()
            try { $entries[$entry.FullName] = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose() }
        }
    } finally { $sha.Dispose(); $archive.Dispose() }
    $receipt = [ordered]@{
        package = "$id.$Version.nupkg"
        version = $Version
        commit = $Commit
        archiveSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        entries = $entries
    }
    Write-Host ('Release.Payload: ' + ($receipt | ConvertTo-Json -Depth 4 -Compress))
}
