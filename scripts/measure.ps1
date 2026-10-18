param([Parameter(Mandatory=$true)][int]$ProcessId, [ValidateRange(1,60)][int]$Seconds = 30, [string]$Label = 'idle')
$ErrorActionPreference = 'Stop'
$taskProcess = Get-Process -Id $ProcessId
$taskProcess.Refresh()
$taskStarted = [DateTimeOffset]::Now
$taskCpu = $taskProcess.TotalProcessorTime.TotalMilliseconds
$taskWatch = [System.Diagnostics.Stopwatch]::StartNew()
$taskSamples = @()
for ($taskIndex = 0; $taskIndex -lt $Seconds; $taskIndex++) {
    Start-Sleep -Seconds 1
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw 'The target process exited during measurement.' }
    $taskSamples += [pscustomobject]@{ WorkingSetMiB = $taskProcess.WorkingSet64 / 1MB; PrivateMiB = $taskProcess.PrivateMemorySize64 / 1MB }
}
$taskWatch.Stop()
[pscustomobject]@{
    Label = $Label
    StartedAt = $taskStarted.ToString('O')
    ProcessId = $ProcessId
    Executable = $taskProcess.Path
    Seconds = [Math]::Round($taskWatch.Elapsed.TotalSeconds, 2)
    LogicalProcessors = [Environment]::ProcessorCount
    CpuMilliseconds = [Math]::Round($taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpu, 3)
    CpuPercentOfMachine = [Math]::Round(($taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpu) / $taskWatch.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount * 100, 4)
    AverageWorkingSetMiB = [Math]::Round(($taskSamples | Measure-Object WorkingSetMiB -Average).Average, 2)
    PeakWorkingSetMiB = [Math]::Round(($taskSamples | Measure-Object WorkingSetMiB -Maximum).Maximum, 2)
    AveragePrivateCommitMiB = [Math]::Round(($taskSamples | Measure-Object PrivateMiB -Average).Average, 2)
} | ConvertTo-Json
