param(
    [string]$Manifest = (Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json'),
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$InstalledFontDirectory,
    [string]$Renderer = 'pdftoppm',
    [ValidateSet('licensed', 'evaluation')][string]$ArtifactMode = 'evaluation'
)
# Test-only desktop reference capture, never called by the library or ordinary CI.
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Reference capture requires licensed desktop Word on Windows.' }
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$corpus = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($font in $corpus.fonts) {
    $installed = Join-Path $InstalledFontDirectory ([IO.Path]::GetFileName($font.path))
    if ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash.ToLowerInvariant() -ne $font.sha256) {
        throw 'Install the corpus Noto Sans faces matching the manifest before capturing Word references.'
    }
}
. (Join-Path $PSScriptRoot 'comparison-renderer.ps1')
$rendererVersion = Get-ComparisonRendererVersion -Renderer $Renderer
$word = $null
$references = [Collections.Generic.List[object]]::new()
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3 # ForceDisable macros; synthetic DOCX only.
    if (@($word.FontNames) -notcontains 'Noto Sans') { throw 'Word cannot see the pinned Noto Sans font family.' }
    foreach ($item in $corpus.documents) {
        $source = Join-Path (Split-Path -Parent $manifestPath) $item.path
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw 'Input hash mismatch.' }
        $document = $null
        try {
            # ConfirmConversions=false, ReadOnly=true, AddToRecentFiles=false.
            $document = $word.Documents.Open($source, $false, $true, $false)
            $pdf = Join-Path $output ($item.id + '-word.pdf')
            $document.ExportAsFixedFormat($pdf, 17)
            $references.Add([ordered]@{
                documentId = $item.id; kind = 'word'; version = "Word $($word.Version) build $($word.Build)"
                artifactMode = $ArtifactMode; inputSha256 = $item.sha256
                pdf = [IO.Path]::GetFileName($pdf); sha256 = (Get-FileHash -LiteralPath $pdf -Algorithm SHA256).Hash.ToLowerInvariant()
                fonts = $corpus.fonts; renderer = $rendererVersion
                executableSha256 = (Get-FileHash -LiteralPath (Join-Path $word.Path 'WINWORD.EXE') -Algorithm SHA256).Hash.ToLowerInvariant()
                architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            })
        } finally {
            if ($null -ne $document) { $document.Close(0); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) }
        }
    }
} finally {
    if ($null -ne $word) { $word.Quit(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word) }
    # Retain partial evidence if one reference failed; missing cases remain visible.
    [ordered]@{ schemaVersion = 1; references = @($references.ToArray()) } | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath (Join-Path $output 'references.json') -Encoding utf8
}
