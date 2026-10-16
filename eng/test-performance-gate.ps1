$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('wordnet-performance-gate-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $reference = Join-Path $directory 'reference.json'; $candidate = Join-Path $directory 'candidate.json'
    $report = [ordered]@{
        Schema = 1; SourceRevision = 'fixture'; Machine = 'test'; Runtime = '.NET 10'; Architecture = 'X64'; OperatingSystem = 'test'; Processor = 'test'; Affinity = '1'; ServerGc = $false
        FixtureSha256 = ('a' * 64); AssemblySha256 = ('b' * 64); DocumentPages = 10; DocumentImages = 2; DocumentTables = 2
        WarmupConversions = 20; MeasuredConversions = 31; Milliseconds = @(1..31 | ForEach-Object { 100.0 }); MaximumManagedUpperBoundBytes = 40000000; PeakSampledManagedBytes = 20000000
    }
    $report | ConvertTo-Json | Set-Content $reference
    $report | ConvertTo-Json | Set-Content $candidate
    $result = & "$PSScriptRoot/compare-performance.ps1" -Reference $reference -Candidate $candidate
    if (-not $result.Passed) { throw 'Comparable passing reports were rejected.' }
    foreach ($case in @('regression', 'duration', 'memory', 'machine', 'input', 'affinity', 'missing')) {
        $bad = [ordered]@{}; foreach ($key in $report.Keys) { $bad[$key] = $report[$key] }
        switch ($case) {
            regression { $bad.Milliseconds = @(1..31 | ForEach-Object { 126.0 }) }
            duration { $bad.Milliseconds = @(1..31 | ForEach-Object { 300.0 }) }
            memory { $bad.MaximumManagedUpperBoundBytes = 100000000 }
            machine { $bad.Machine = 'different' }
            input { $bad.FixtureSha256 = ('c' * 64) }
            affinity { $bad.Affinity = '3' }
            missing { $bad.MeasuredConversions = 0; $bad.Milliseconds = @() }
        }
        $bad | ConvertTo-Json | Set-Content $candidate
        $rejected = $false
        try { & "$PSScriptRoot/compare-performance.ps1" -Reference $reference -Candidate $candidate | Out-Null }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Invalid performance evidence was accepted: $case" }
    }
    Write-Output 'Performance gate: passing evidence accepted; seven invalid/regression cases rejected.'
} finally {
    # Exact directory created above, never a user-supplied or enumerated path.
    if ([IO.Path]::GetDirectoryName($directory) -ne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar)) { throw 'Temporary directory containment failed.' }
    Remove-Item -LiteralPath $directory -Recurse -Force
}
