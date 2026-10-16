$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'comparison-renderer.ps1')
$windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($windowsHost) {
    $native = $env:ComSpec
    $bannerArguments = '/d /c "(echo pdftoppm version control&echo Copyright control) 1>&2"'
    $failureArguments = '/d /c exit 7'
} else {
    $native = '/bin/sh'
    $bannerArguments = '-c "printf ''pdftoppm version control\r\nCopyright control\r\n'' >&2"'
    $failureArguments = '-c "exit 7"'
}
$actual = Get-ComparisonRendererVersion -Renderer $native -Arguments $bannerArguments
if ($actual -cne "pdftoppm version control`nCopyright control") { throw "Unexpected banner capture: $actual" }
if ($actual -match 'CategoryInfo|At line:') { throw 'PowerShell error formatting leaked into the renderer identity.' }
$rejected = $false
try { Get-ComparisonRendererVersion -Renderer $native -Arguments $failureArguments | Out-Null }
catch { if ($_.Exception.Message -notmatch 'exit 7') { throw }; $rejected = $true }
if (-not $rejected) { throw 'Nonzero renderer exit was accepted.' }
Write-Output "Renderer capture checks passed on PowerShell $($PSVersionTable.PSVersion)."
