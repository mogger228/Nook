param(
    [string]$ProcessName = 'Nook',
    [ValidateRange(3, 300)][int]$Seconds = 15,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$targets = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
if ($targets.Count -ne 1) {
    throw "Expected one running '$ProcessName' process; found $($targets.Count)."
}
$targetId = $targets[0].Id
$target = Get-Process -Id $targetId
$cpuStart = $target.TotalProcessorTime.TotalSeconds
$timer = [Diagnostics.Stopwatch]::StartNew()
$workingSets = @()
$privateBytes = @()
for ($sample = 0; $sample -lt $Seconds; $sample++) {
    Start-Sleep -Seconds 1
    $target = Get-Process -Id $targetId -ErrorAction Stop
    $workingSets += $target.WorkingSet64
    $privateBytes += $target.PrivateMemorySize64
}
$timer.Stop()
$cpuSeconds = $target.TotalProcessorTime.TotalSeconds - $cpuStart
$result = [pscustomobject]@{
    ProcessName = $target.ProcessName
    ProcessId = $targetId
    Timestamp = (Get-Date).ToString('o')
    DurationSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 2)
    LogicalProcessors = [Environment]::ProcessorCount
    CpuSeconds = [Math]::Round($cpuSeconds, 4)
    CpuPercentMachine = [Math]::Round(100 * $cpuSeconds / $timer.Elapsed.TotalSeconds / [Environment]::ProcessorCount, 3)
    AverageWorkingSetMiB = [Math]::Round(($workingSets | Measure-Object -Average).Average / 1MB, 2)
    PeakWorkingSetMiB = [Math]::Round(($workingSets | Measure-Object -Maximum).Maximum / 1MB, 2)
    AveragePrivateMiB = [Math]::Round(($privateBytes | Measure-Object -Average).Average / 1MB, 2)
    HandlesAtEnd = $target.HandleCount
    Note = 'Keep pointer away from dock and avoid interaction during idle sampling. This is one short sample, not a leak or stress test.'
}
if ($OutputPath) {
    $result | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}
$result
