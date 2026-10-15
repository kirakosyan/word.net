param(
    [string]$CorpusPath = 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise',
    [string]$OutputCsv = 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise/word-pages.csv',
    [string]$PdfDirectory = 'artifacts/enterprise-audit/word-references'
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Reference generation requires an installed, licensed Microsoft Word on Windows; conversion does not.' }
$corpus = (Resolve-Path -LiteralPath $CorpusPath).Path
$pdfRoot = [IO.Path]::GetFullPath($PdfDirectory)
New-Item -ItemType Directory -Force $pdfRoot | Out-Null
$application = $null
$records = [Collections.Generic.List[object]]::new()
try {
    $application = New-Object -ComObject Word.Application
    $application.Visible = $false
    $application.DisplayAlerts = 0
    $application.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
    if ($application.Documents.Count -ne 0) { throw 'Word reference instance is not isolated.' }
    foreach ($entry in Import-Csv (Join-Path $corpus 'manifest.csv')) {
        # Malformed/expansion-limit inputs are not opened by an external desktop app.
        if ($entry.outcome -ne 'convert') { continue }
        $document = $null
        $file = Join-Path $corpus $entry.file
        try {
            $document = $application.Documents.Open($file, $false, $true, $false)
            $document.Repaginate()
            $pages = $document.ComputeStatistics(2)
            $document.ExportAsFixedFormat((Join-Path $pdfRoot ([IO.Path]::GetFileNameWithoutExtension($entry.file) + '.pdf')), 17)
            $records.Add([pscustomobject]@{
                file = $entry.file
                category = if ($entry.file -in @('tiny-runs.docx', 'oversized-row.docx')) { 'stress' } else { 'business' }
                word_pages = $pages
                word_version = $application.Version
                word_build = $application.Build
                default_font = $document.Content.Font.Name
                sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            })
            Write-Output "$($entry.file): Word pages=$pages"
        } finally {
            if ($null -ne $document) { $document.Close(0); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) | Out-Null }
        }
    }
    $records | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding utf8
} finally {
    if ($null -ne $application -and $application.Documents.Count -eq 0) {
        $application.Quit(0)
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($application) | Out-Null
    }
}
Write-Output "Recorded $($records.Count) independently paginated Word references. PDF files remain outside Git."
