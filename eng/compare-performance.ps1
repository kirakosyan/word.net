param(
    [Parameter(Mandatory = $true)][string[]]$Reference,
    [Parameter(Mandatory = $true)][string[]]$Candidate
)
$ErrorActionPreference = 'Stop'
function Read-Measurements([string[]]$Paths) {
    $reports = @($Paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })
    if ($reports.Count -eq 0) { throw 'Missing performance reports.' }
    $identity = $reports[0]
    $samples = [Collections.Generic.List[double]]::new()
    foreach ($report in $reports) {
        if ($report.Schema -ne 1 -or $report.MeasuredConversions -lt 31 -or $report.WarmupConversions -lt 20 -or
            $report.DocumentPages -ne 10 -or $report.DocumentImages -lt 2 -or $report.DocumentTables -lt 2 -or
            $report.FixtureSha256 -notmatch '^[0-9a-f]{64}$' -or $report.AssemblySha256 -notmatch '^[0-9a-f]{64}$' -or
            @($report.Milliseconds).Count -ne $report.MeasuredConversions) { throw 'Incomplete performance measurement.' }
        foreach ($key in @('Machine', 'Runtime', 'Architecture', 'OperatingSystem', 'Processor', 'Affinity', 'ServerGc', 'FixtureSha256', 'AssemblySha256')) {
            if ($null -eq $report.$key -or $report.$key -cne $identity.$key) { throw "Inconsistent measurement identity: $key" }
        }
        [uint64]$mask = [Convert]::ToUInt64($report.Affinity, 16)
        if ($mask -eq 0 -or ($mask -band ($mask - 1)) -ne 0) { throw 'Measurement was not restricted to one processor.' }
        if ($report.MaximumManagedUpperBoundBytes -le 0 -or $report.PeakSampledManagedBytes -le 0) { throw 'Missing managed-memory measurement.' }
        foreach ($sample in $report.Milliseconds) {
            if ([double]::IsNaN($sample) -or [double]::IsInfinity($sample) -or $sample -le 0) { throw 'Invalid timing sample.' }
            $samples.Add([double]$sample)
        }
    }
    $sorted = @($samples | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if (($sorted.Count % 2) -eq 0) { ($sorted[$middle - 1] + $sorted[$middle]) / 2 } else { $sorted[$middle] }
    return [pscustomobject]@{
        Identity = $identity; Samples = $sorted.Count; MedianMilliseconds = $median
        MaximumManagedUpperBoundBytes = ($reports | Measure-Object MaximumManagedUpperBoundBytes -Maximum).Maximum
        PeakSampledManagedBytes = ($reports | Measure-Object PeakSampledManagedBytes -Maximum).Maximum
    }
}
$baseline = Read-Measurements $Reference
$current = Read-Measurements $Candidate
foreach ($key in @('Machine', 'Runtime', 'Architecture', 'OperatingSystem', 'Processor', 'Affinity', 'ServerGc', 'FixtureSha256')) {
    if ($baseline.Identity.$key -cne $current.Identity.$key) { throw "Performance reports are not hardware/runtime/input comparable: $key" }
}
if ($current.MedianMilliseconds -ge 300) { throw 'Ten-page PDF export did not meet the 300 ms target.' }
if ($current.MaximumManagedUpperBoundBytes -ge 100000000 -or $current.PeakSampledManagedBytes -ge 100000000) {
    throw 'Ten-page PDF export did not meet the 100 MB managed-memory target.'
}
$ratio = $current.MedianMilliseconds / $baseline.MedianMilliseconds
if ($ratio -gt 1.25) { throw "PDF export regressed by more than 25 percent: ratio $ratio" }
[pscustomobject]@{
    Passed = $true; ReferenceMedianMilliseconds = $baseline.MedianMilliseconds; CandidateMedianMilliseconds = $current.MedianMilliseconds
    Ratio = $ratio; ReferenceSamples = $baseline.Samples; CandidateSamples = $current.Samples
    MaximumManagedUpperBoundBytes = $current.MaximumManagedUpperBoundBytes; PeakSampledManagedBytes = $current.PeakSampledManagedBytes
    FixtureSha256 = $current.Identity.FixtureSha256; Affinity = $current.Identity.Affinity
}
