$ErrorActionPreference = 'Stop'
$app = $null; $document = $null
try {
    $app = New-Object -ComObject Word.Application
    $app.Visible = $false; $app.DisplayAlerts = 0; $app.AutomationSecurity = 3
    if ($app.Documents.Count -ne 0) { throw 'Word fixture instance is not isolated.' }
    $source = (Resolve-Path 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise/letter-stories.docx').Path
    $target = [IO.Path]::GetFullPath('tests/wordnet.Conversion.Tests/Fixtures/Enterprise/word-generated-stories.doc')
    $document = $app.Documents.Open($source, $false, $true, $false)
    $document.EmbedTrueTypeFonts = $false
    $document.RemovePersonalInformation = $true
    $document.RemoveDocumentInformation(99)
    $document.SaveAs2([ref]$target, [ref]0)
    $provenance = [ordered]@{
        source = 'letter-stories.docx'
        source_sha256 = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
        producer = "Microsoft Word $($app.Version), build $($app.Build), Word 97 format"
        provenance = 'Independent wordnet-authored text, headers and page fields; Office-generated format reference only. No font embedding or macros. Personal information removed.'
    }
} finally {
    if ($null -ne $document) { $document.Close(0); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) | Out-Null }
    if ($null -ne $app -and $app.Documents.Count -eq 0) { $app.Quit(0); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($app) | Out-Null }
}
$provenance['sha256'] = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
$provenance | ConvertTo-Json | Set-Content 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise/word-generated-stories.json' -Encoding utf8
