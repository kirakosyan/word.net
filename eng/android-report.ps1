function Get-CompleteAndroidReport([string]$Text) {
    # ADB transports XDocument.Save's UTF-8 BOM as a character on some hosts.
    # Strip that encoding marker before validating XML, never shell error text.
    $text = $Text.TrimStart([char]0xFEFF)
    if ($text -notmatch '^\s*<') { return $null }
    try { $document = [xml]$text } catch { return $null }
    if ($document.DocumentElement.Name -ne 'testsuite') { return $null }
    return $text
}
