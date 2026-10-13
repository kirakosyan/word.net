param(
    [ValidateSet('x64', 'arm64')][string]$Architecture,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/ocr'),
    [string]$NativeLibraryPath
)
$ErrorActionPreference = 'Stop'
if (-not $Architecture) {
    # An emulated PowerShell can launch native ARM64 dotnet; use the test host architecture.
    $info = (& dotnet --info) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $info -notmatch '(?m)^\s*Architecture:\s+(x64|arm64)\s*$') {
        throw 'Could not determine dotnet host architecture; pass -Architecture explicitly.'
    }
    $Architecture = $Matches[1]
}
$root = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($root) | Out-Null
$data = Join-Path $root 'tessdata'
[IO.Directory]::CreateDirectory($data) | Out-Null
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$ExpectedHash) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { $actual = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
    if ($actual -ne $ExpectedHash) { throw "Checksum mismatch for $Path" }
}
Get-VerifiedFile 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata' (Join-Path $data 'eng.traineddata') '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'
$windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if (-not $NativeLibraryPath -and $windowsHost) {
    # Native binaries only, used as test prerequisites. The package's managed application is not referenced or shipped.
    $package = Join-Path $root 'fieldcure.documentparsers.ocr.1.2.0.nupkg'
    Get-VerifiedFile 'https://api.nuget.org/v3-flatcontainer/fieldcure.documentparsers.ocr/1.2.0/fieldcure.documentparsers.ocr.1.2.0.nupkg' $package 'b7be5c12cd693714347ddb8eb746ab9fd5607a50dc396f0bd8315d4941ce2dff'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $native = Join-Path $root "native/$Architecture"
    [IO.Directory]::CreateDirectory($native) | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        foreach ($name in @('tesseract50.dll', 'leptonica-1.82.0.dll')) {
            $entry = $archive.GetEntry("$Architecture/$name")
            if ($null -eq $entry) { throw "Missing native asset $name" }
            $inputStream = $entry.Open(); $buffer = [IO.MemoryStream]::new()
            try { $inputStream.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $inputStream.Dispose(); $buffer.Dispose() }
            $path = Join-Path $native $name
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try {
                $expected = [Convert]::ToBase64String($algorithm.ComputeHash($bytes))
                $existing = if (Test-Path -LiteralPath $path) { [Convert]::ToBase64String($algorithm.ComputeHash([IO.File]::ReadAllBytes($path))) } else { '' }
            } finally { $algorithm.Dispose() }
            if ($existing -ne $expected) { [IO.File]::WriteAllBytes($path, $bytes) }
        }
    } finally { $archive.Dispose() }
    $NativeLibraryPath = Join-Path $native 'tesseract50.dll'
}
if (-not $NativeLibraryPath -and [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) {
    $prefix = & brew --prefix tesseract
    if ($LASTEXITCODE -ne 0) { throw 'Install Tesseract 5 with Homebrew first.' }
    $NativeLibraryPath = Join-Path $prefix.Trim() 'lib/libtesseract.5.dylib'
}
if (-not $NativeLibraryPath) {
    $triplet = if ($Architecture -eq 'arm64') { 'aarch64-linux-gnu' } else { 'x86_64-linux-gnu' }
    $NativeLibraryPath = "/usr/lib/$triplet/libtesseract.so.5"
}
if (-not (Test-Path -LiteralPath $NativeLibraryPath)) { throw 'Install a native Tesseract 5 library or pass -NativeLibraryPath.' }
$env:WORDNET_TESSDATA = $data
$env:WORDNET_TESSERACT_LIBRARY = [IO.Path]::GetFullPath($NativeLibraryPath)
if ($env:GITHUB_ENV) {
    [IO.File]::AppendAllText($env:GITHUB_ENV, "WORDNET_TESSDATA=$data`nWORDNET_TESSERACT_LIBRARY=$env:WORDNET_TESSERACT_LIBRARY`n", [Text.UTF8Encoding]::new($false))
}
Write-Output "English OCR prerequisites ready for $Architecture. Environment variables are set in this PowerShell process."
