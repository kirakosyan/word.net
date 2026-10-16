# Shared by desktop Word capture and the shell-independent regression checks.
function Get-ComparisonRendererVersion {
    param([Parameter(Mandatory = $true)][string]$Renderer, [string]$Arguments = '-v')
    $command = Get-Command -Name $Renderer -CommandType Application -ErrorAction Stop
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $command.Source
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Could not start the renderer version check.' }
        # Read both pipes concurrently without PowerShell's native stderr handling.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Renderer version check timed out.' }
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Renderer version check failed (exit $($process.ExitCode))." }
        $banner = if ($errorOutput) { $errorOutput } else { $output }
        # Match Python's text-mode capture on Windows and Unix, including old references.
        return (($banner.Trim() -split '\r\n|\r|\n') -join "`n")
    } finally { $process.Dispose() }
}
