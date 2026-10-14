$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')

foreach ($version in @('1.0.0', '0.1.0', '10.20.30', '1.0.0-rc.1', '0.1.0-alpha.10', '1.0.0-0', '1.0.0-01a')) {
    Assert-ReleaseVersion -Version $version
}
foreach ($version in @('01.0.0', '1.00.0', '1.0.00', '1.0', '1.0.0.0', '1.0.0-', '1.0.0-alpha..1',
        '1.0.0-01', '1.0.0+build.1', '../1.0.0', '1.0.0/other', "1.0.0`n", ' 1.0.0', '1.0.0 ')) {
    $rejected = $false
    try { Assert-ReleaseVersion -Version $version } catch { $rejected = $true }
    if (!$rejected) { throw "Accepted an ambiguous or unsafe release version: $version" }
}
Write-Output 'Stable/prerelease identities accepted; malformed and normalized identities rejected.'
