$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'android-report.ps1')
# Parser checks only; these strings are not conversion qualification evidence.
$xml = '<testsuite tests="0" failures="0" skipped="0" />'
foreach ($text in @($xml, ([char]0xFEFF + $xml))) {
    if ((Get-CompleteAndroidReport $text) -ne $xml) { throw 'A complete Android report was rejected or changed.' }
}
foreach ($text in @('', 'cat: files/results.xml: No such file or directory', '<testsuite', '<other />')) {
    if ($null -ne (Get-CompleteAndroidReport $text)) { throw 'Incomplete or non-report output was accepted.' }
}
Write-Output 'Android report decoding checks passed: UTF-8 BOM, plain XML and invalid/incomplete output.'
