$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$ExpectedHash) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { $actual = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
    if ($actual -ne $ExpectedHash) { throw "Checksum mismatch for $Path" }
}
Get-VerifiedFile 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata' (Join-Path $root 'ocr/tessdata/eng.traineddata') '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'
$archivePath = Join-Path $root 'android-native/tesseract4android-4.9.0.aar'
Get-VerifiedFile 'https://jitpack.io/cz/adaptech/tesseract4android/tesseract4android/4.9.0/tesseract4android-4.9.0.aar' $archivePath 'bce5d6413a1a5ae3d7240033fbbc851ba3217d0a08d9769400e17a077f42cb2a'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($name in @('libjpeg.so', 'libpngx.so', 'libleptonica.so', 'libtesseract.so')) {
        $relative = "jni/arm64-v8a/$name"
        $entry = $archive.GetEntry($relative)
        if ($null -eq $entry) { throw "Missing native asset $relative" }
        $path = Join-Path $root "android-native/$relative"
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        $source = $entry.Open(); $destination = [IO.File]::Create($path)
        try { $source.CopyTo($destination) } finally { $source.Dispose(); $destination.Dispose() }
    }
} finally { $archive.Dispose() }
Write-Output 'Android ARM64 test prerequisites ready. Native assets/model are test-only and excluded from the wordnet NuGet.'
