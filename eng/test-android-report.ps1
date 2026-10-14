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
foreach ($api in @(21, 25, 26, 36)) {
    $expected = if ($api -ge 26) { 'shell ps -A' } else { 'shell ps' }
    if (((Get-AndroidProcessArguments $api) -join ' ') -ne $expected) { throw "Incorrect process visibility command for Android API $api." }
}
Write-Output 'Android process visibility checks passed for toolbox and toybox API levels.'
