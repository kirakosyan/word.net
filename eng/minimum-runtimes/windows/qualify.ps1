$ErrorActionPreference = 'Stop'
$results = 'C:\wordnet-results'
$tests = 'C:\wordnet-tests'
[IO.Directory]::CreateDirectory($results) | Out-Null
$status = [ordered]@{ passed = $false; stage = 'setup'; error = $null; frameworkRelease = $null; exactFramework472 = $false }
$exitCode = 1
Start-Transcript -Path (Join-Path $results 'setup.log') -Force | Out-Null
try {
    $status.frameworkRelease = [int](Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release)
    $status.exactFramework472 = $status.frameworkRelease -in @(461808, 461814)
    if (-not $status.exactFramework472) { throw "Guest Framework release $($status.frameworkRelease) is not actual 4.7.2. Preserve the results; do not count this run as the minimum-runtime gate." }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundle.json') -Raw | ConvertFrom-Json
    $zip = Join-Path $PSScriptRoot 'tests.zip'
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.testsSha256) { throw 'Guest test bundle hash mismatch.' }
    if (Test-Path -LiteralPath $tests) { throw 'Guest test directory already exists. Use a fresh VM for this qualification run.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $tests
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    # The native OCR/graphics assets need the Microsoft x64 C++ runtime on a clean OS.
    $vc = Join-Path $results 'vc_redist.x64.exe'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $vc
    $signature = Get-AuthenticodeSignature -LiteralPath $vc
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Microsoft Corporation(,|$)') { throw 'Microsoft C++ runtime signature validation failed.' }
    $status.vcRuntimeSha256 = (Get-FileHash -LiteralPath $vc -Algorithm SHA256).Hash.ToLowerInvariant()
    $installer = Start-Process -FilePath $vc -ArgumentList '/install /quiet /norestart' -WindowStyle Hidden -PassThru -Wait
    if ($installer.ExitCode -notin @(0, 1638)) { throw "C++ runtime installation returned $($installer.ExitCode). Resolve any required reboot before running the suite." }
    & (Join-Path $tests 'prepare-ocr-tests.ps1') -Architecture x64 -OutputDirectory 'C:\wordnet-prerequisites'
    $status.stage = 'tests'
    & (Join-Path $tests 'run-framework-tests.ps1') -TestDirectory $tests -ResultsDirectory $results -RequireFramework472 -UseProvisionedOcr
    $status.passed = $true
    $status.stage = 'complete'
    $exitCode = 0
} catch {
    $status.error = $_.Exception.Message
    Write-Output $_
} finally {
    $status | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'status.json') -Encoding UTF8
    Stop-Transcript | Out-Null
    # Dockur's private container share; no host credential or public SMB port is used.
    $share = '\\host.lan\Data'
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $share) -and $deadline.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Seconds 2 }
    if (Test-Path -LiteralPath $share) {
        $published = Join-Path $share 'results'
        [IO.Directory]::CreateDirectory($published) | Out-Null
        Get-ChildItem -LiteralPath $results -File | Where-Object Extension -ne '.exe' | Copy-Item -Destination $published -Force
    } else { Write-Output "Could not export reports. Retrieve $results from the guest viewer."; $exitCode = 1 }
}
exit $exitCode
