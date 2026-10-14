function Get-CompleteAndroidReport([string]$Text) {
    # ADB transports XDocument.Save's UTF-8 BOM as a character on some hosts.
    # Strip that encoding marker before validating XML, never shell error text.
    $text = $Text.TrimStart([char]0xFEFF)
    if ($text -notmatch '^\s*<') { return $null }
    try { $document = [xml]$text } catch { return $null }
    if ($document.DocumentElement.Name -ne 'testsuite') { return $null }
    return $text
}

function Get-AndroidProcessArguments([int]$ApiLevel) {
    # Toolbox ps on API 21 interprets -A as a name filter; modern toybox ps
    # needs -A to include processes belonging to application UIDs.
    if ($ApiLevel -ge 26) { return @('shell', 'ps', '-A') }
    return @('shell', 'ps')
}
