function Wait-FrameworkJitModule {
    param(
        [Diagnostics.Process]$Process,
        [Diagnostics.Stopwatch]$Timer,
        [int]$TimeoutMilliseconds,
        [string]$ExpectedModule,
        [scriptblock]$ReadModules = {
            param($Child)
            $Child.Modules | Where-Object ModuleName -in @('clrjit.dll', 'compatjit.dll') | ForEach-Object {
                [ordered]@{ name = $_.ModuleName; version = $_.FileVersionInfo.FileVersion }
            }
        }
    )
    $observed = @()
    while (-not $Process.HasExited -and $Timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        try {
            $Process.Refresh()
            $observed = @(& $ReadModules $Process)
        } catch [InvalidOperationException] { if (-not $Process.HasExited) { throw } }
        catch [ComponentModel.Win32Exception] {
            # ERROR_PARTIAL_COPY can occur before a live child's loader is ready.
            # Keep polling within the overall deadline, including under emulation.
        }
        if (@($observed | Where-Object { $_.name -eq $ExpectedModule }).Count) { break }
        Start-Sleep -Milliseconds 50
    }
    return $observed
}
