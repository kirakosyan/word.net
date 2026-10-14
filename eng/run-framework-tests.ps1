param(
    [string]$TestDirectory = (Join-Path $PSScriptRoot '../tests/wordnet.Conversion.Tests/bin/Release/net472/win-x64'),
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/production-tests/framework'),
    [switch]$RequireFramework472,
    [switch]$UseProvisionedOcr,
    [ValidateSet('default', 'legacy')][string]$JitMode = 'default',
    [ValidateRange(1, 7200)][int]$TimeoutSeconds = 1800
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'framework-process.ps1')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Framework qualification requires Windows.' }
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $key = $registry.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full')
    if ($null -eq $key) { throw 'No .NET Framework 4.x Full runtime is installed.' }
    try { $release = [int]$key.GetValue('Release') } finally { $key.Dispose() }
} finally { $registry.Dispose() }
$isMinimum = $release -eq 461808 -or $release -eq 461814
if ($release -lt 461808) { throw 'The installed Framework is older than 4.7.2.' }
if ($RequireFramework472 -and -not $isMinimum) { throw "Framework release $release is not 4.7.2 itself. Do not downgrade a shared PC; use an isolated VM for this lane." }
$runtimeOverrides = [ordered]@{}
foreach ($name in @('COMPlus_JITMinOpts', 'COMPlus_JitNoInline', 'COMPlus_JitDisableOptimizations', 'COMPlus_UseLegacyJit', 'COMPlus_AltJit', 'COMPlus_ZapDisable')) {
    $value = [Environment]::GetEnvironmentVariable($name, 'Process')
    if ($null -ne $value) { $runtimeOverrides[$name] = $value }
}
if ($RequireFramework472 -and $runtimeOverrides.Count -ne 0) {
    throw 'Minimum-runtime qualification rejects inherited diagnostic COMPlus overrides. Select the documented JIT mode with -JitMode instead.'
}
$testRoot = [IO.Path]::GetFullPath($TestDirectory)
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$runner = Join-Path $testRoot 'wordnet.Conversion.Tests.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Build the net472 tests on the development PC, then copy the complete win-x64 output directory here.' }
$configPath = $runner + '.config'
if (Test-Path -LiteralPath $configPath) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($configPath, $settings)
    try {
        $config = [Xml.XmlDocument]::new()
        $config.XmlResolver = $null
        $config.Load($reader)
    } finally { $reader.Dispose() }
    $jitSettings = @($config.SelectNodes('/configuration/runtime/useLegacyJit'))
    if ($jitSettings.Count -gt 1) { throw 'The test application has conflicting useLegacyJit settings.' }
    if ($jitSettings.Count -eq 1) {
        $configured = [string]$jitSettings[0].enabled
        if ($configured -notin @('0', '1')) { throw 'The test application useLegacyJit setting must be 0 or 1.' }
        if (($configured -eq '1') -ne ($JitMode -eq 'legacy')) { throw 'The test application configuration does not match -JitMode. Use a matching bundle so JIT evidence cannot be mislabeled.' }
    }
}
[IO.Directory]::CreateDirectory($results) | Out-Null
if (-not $UseProvisionedOcr) { & (Join-Path $PSScriptRoot 'prepare-ocr-tests.ps1') -Architecture x64 }
if (-not $env:WORDNET_TESSDATA -or -not $env:WORDNET_TESSERACT_LIBRARY) { throw 'Provision local x64 English OCR before running the suite.' }
function Hash([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$evidence = [ordered]@{
    utc = [DateTime]::UtcNow.ToString('o')
    os = [Environment]::OSVersion.VersionString
    reportedArchitecture = "$env:PROCESSOR_ARCHITECTURE / $env:PROCESSOR_ARCHITEW6432"
    processorIdentifier = $env:PROCESSOR_IDENTIFIER
    testProcessArchitecture = 'x64'
    frameworkRelease = $release
    exactFramework472 = $isMinimum
    clrFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/clr.dll')).FileVersion
    jitFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/clrjit.dll')).FileVersion
    runtimeOverrides = $runtimeOverrides
    requestedJitMode = $JitMode
    loadedJitModules = @()
    childRuntimeOverrides = $(if ($JitMode -eq 'legacy') { @{ COMPlus_UseLegacyJit = '1' } } else { @{} })
    testConfigurationSha256 = $(if (Test-Path -LiteralPath $configPath) { Hash $configPath } else { $null })
    librarySha256 = (Hash (Join-Path $testRoot 'wordnet.dll'))
    testSha256 = (Hash $runner)
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'environment.json') -Encoding UTF8
# The in-process xUnit executable needs only Framework, not a modern .NET SDK on the old VM.
$trxPath = Join-Path $results 'net472.trx'
if (Test-Path -LiteralPath $trxPath) {
    Move-Item -LiteralPath $trxPath -Destination (Join-Path $results ("net472.previous-{0}.trx" -f [DateTime]::UtcNow.Ticks))
}
$stdout = Join-Path $results 'tests.stdout.log'
$stderr = Join-Path $results 'tests.stderr.log'
function Stop-TestRunner([Diagnostics.Process]$TestProcess) {
    try {
        if (-not $TestProcess.HasExited) { $TestProcess.Kill(); $TestProcess.WaitForExit() }
    } catch [InvalidOperationException] {
        # Exiting between HasExited and Kill is harmless. Keep other failures visible.
        if (-not $TestProcess.HasExited) { throw }
    }
}
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $runner
$startInfo.Arguments = '-noColor -failSkips -failWarns -result-trx "{0}"' -f $trxPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
if ($JitMode -eq 'legacy') {
    # This changes only our child process. The host environment/registry and the
    # caller's app.config remain untouched. Consumers use the documented app setting.
    $startInfo.EnvironmentVariables['COMPlus_UseLegacyJit'] = '1'
}
# Own the process handle from creation; Windows PowerShell's Start-Process can
# lose ExitCode after its redirection event handler observes a fast process exit.
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
try { if (-not $process.Start()) { throw 'Could not start the Framework test process.' } }
catch { $process.Dispose(); throw }
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    Write-Output "Framework tests running with a $TimeoutSeconds-second external deadline. Logs: $results"
    $expectedJit = $(if ($JitMode -eq 'legacy') { 'compatjit.dll' } else { 'clrjit.dll' })
    $evidence.loadedJitModules = @(Wait-FrameworkJitModule -Process $process -Timer $timer -TimeoutMilliseconds ($TimeoutSeconds * 1000) -ExpectedModule $expectedJit)
    $remaining = [Math]::Max(0, $TimeoutSeconds * 1000 - [int]$timer.ElapsedMilliseconds)
    if (-not $process.WaitForExit($remaining)) {
        Stop-TestRunner $process
        throw "Framework test runner exceeded the $TimeoutSeconds-second deadline. No qualification pass is established."
    }
    $exitCode = $process.ExitCode
} finally {
    Stop-TestRunner $process
    [IO.File]::WriteAllText($stdout, $outputTask.GetAwaiter().GetResult())
    [IO.File]::WriteAllText($stderr, $errorTask.GetAwaiter().GetResult())
    $process.Dispose()
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results 'environment.json') -Encoding UTF8
    Get-Content -LiteralPath $stdout
    Get-Content -LiteralPath $stderr
}
if ($exitCode -ne 0) { throw "Framework test runner failed with exit code $exitCode." }
if ($JitMode -eq 'legacy' -and -not @($evidence.loadedJitModules | Where-Object { $_.name -eq 'compatjit.dll' }).Count) {
    throw 'The legacy JIT module was not observed in the test process. No legacy-JIT qualification pass is established.'
}
[xml]$report = Get-Content -LiteralPath (Join-Path $results 'net472.trx') -Raw
$counters = $report.TestRun.ResultSummary.Counters
$required = [int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-tests.txt') -Raw)
if ([int]$counters.total -lt $required -or [int]$counters.passed -ne [int]$counters.total) { throw 'Framework report is incomplete or contains failing/skipped cases.' }
Write-Output "Framework suite passed. Release=$release; exact 4.7.2=$isMinimum; requested JIT=$JitMode. Evidence: $results"
