[CmdletBinding()]
param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$type = $assembly.GetType('SurfaceProFanControl.Infrastructure.TelemetryService', $true)
$private = [Reflection.BindingFlags]'Instance,NonPublic'
$times = $type.GetNestedType('CpuTimes', [Reflection.BindingFlags]::NonPublic)
$usage = $type.GetMethod('CpuUsageSince', [Reflection.BindingFlags]'Static,NonPublic')
$hz = [Diagnostics.Stopwatch]::Frequency

function Sample([uint64]$Idle, [uint64]$Kernel, [uint64]$User, [long]$Stamp) {
    [Activator]::CreateInstance($times, [object[]]@($Idle,$Kernel,$User,$Stamp))
}
function Assert($ok, [string]$message) { if (!$ok) { throw $message } }

$baseline = Sample 50 80 10 $hz
$next = 2 * $hz
foreach ($row in @(
    @{Sample=(Sample 80 120 20 $next); Want=40}, # 30 idle out of 50 total
    @{Sample=(Sample 80 110 10 $next); Want=0},
    @{Sample=(Sample 50 90 20 $next); Want=100},
    @{Sample=(Sample 50 80 10 $next); Want=$null}, # No elapsed CPU time
    @{Sample=(Sample 70 81 11 $next); Want=$null}, # Impossible idle delta
    @{Sample=(Sample 49 90 20 $next); Want=$null}, # Reset counter
    @{Sample=(Sample 80 120 20 (12 * $hz)); Want=$null}
)) {
    $actual = $usage.Invoke($null, @($baseline,$row.Sample))
    Assert ($row.Want -eq $actual) "Unexpected CPU percentage; expected '$($row.Want)', saw '$actual'."
}

# Exercise a slow multi-group provider without starting WMI or touching user settings.
$service = [Activator]::CreateInstance($type)
$multi = $type.GetMethod('ReadMultiGroupCpuLoad', $private)
$slot = $type.GetField('_systemWideQuery', $private)
$started = $type.GetField('_systemWideQueryStarted', $private)
$pending = [Threading.Tasks.TaskCompletionSource[Nullable[int]]]::new()
$started.SetValue($service, [long]$hz)
$slot.SetValue($service, $pending.Task)
Assert ($null -eq $multi.Invoke($service, @([long]$next))) 'A pending first query produced a value.'
$pending.SetResult(31)
Assert (31 -eq $multi.Invoke($service, @([long]$next))) 'A completed system-wide query was lost.'
$slow = [Threading.Tasks.TaskCompletionSource[Nullable[int]]]::new()
$started.SetValue($service, [long](6 * $hz))
$slot.SetValue($service, $slow.Task)
Assert (31 -eq $multi.Invoke($service, @([long](7 * $hz)))) 'A recent sample vanished during a new query.'
Assert ($null -eq $multi.Invoke($service, @([long](12 * $hz)))) 'An expired sample was shown as current.'
Assert ([object]::ReferenceEquals($slow.Task,$slot.GetValue($service))) 'A second query replaced the pending query.'
'PASS: CPU deltas, missing and stale measurements, and nonblocking multi-group sampling.'
