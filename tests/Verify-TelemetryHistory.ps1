[CmdletBinding()]
param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$storeType = $assembly.GetType('SurfaceProFanControl.Infrastructure.TelemetryHistoryStore', $true)
$sampleType = $assembly.GetType('SurfaceProFanControl.Infrastructure.TelemetrySnapshot', $true)
$logType = $assembly.GetType('SurfaceProFanControl.Infrastructure.AppLogger', $true)
$private = [Reflection.BindingFlags]'NonPublic,Static'
$pending = $storeType.GetField('_pendingWrite', $private)
$lastAttempt = $storeType.GetField('_lastAttempt', $private)
$temp = Join-Path ([IO.Path]::GetTempPath()) ('SurfaceHistory-' + [guid]::NewGuid().ToString('N'))
$csv = Join-Path $temp 'telemetry.csv'
$log = Join-Path $temp 'history-test.log'
$header = 'timestamp,temp_c,critical_c,cpu_percent,ac_power,battery_percent,mode'
$store = [Activator]::CreateInstance($storeType)
$sample = [Activator]::CreateInstance($sampleType, [object[]]@($null, $null, 35, $true, 87, [DateTime]::UtcNow))

function Assert($ok, [string]$message) { if (!$ok) { throw $message } }
function Wait-Write { Assert ($pending.GetValue($null).Wait(5000)) 'CSV writer did not finish.' }
function Enable-NextWrite { $lastAttempt.SetValue($null, $null) }
[void][IO.Directory]::CreateDirectory($temp)
try {
    $storeType.GetField('<CsvPath>k__BackingField', $private).SetValue($null, $csv)
    $logType.GetField('<LogPath>k__BackingField', $private).SetValue($null, $log)
    $busy = [Threading.Tasks.TaskCompletionSource[bool]]::new()
    $pending.SetValue($null, $busy.Task)
    1..20 | ForEach-Object { $store.Append($sample, 'BUSY') }
    Assert ([object]::ReferenceEquals($busy.Task, $pending.GetValue($null))) 'A pending writer was replaced.'
    Assert (!(Test-Path -LiteralPath $csv)) 'A second writer opened the CSV.'
    $busy.SetResult($true)

    $exclusive = [IO.File]::Open($csv, 'CreateNew', 'Write', 'None')
    try {
        $store.Append($sample, 'LOCKED')
        Wait-Write
        Assert (Test-Path -LiteralPath $log) 'Sharing failure was not reported.'
    } finally { $exclusive.Dispose() }
    $failedTask = $pending.GetValue($null)
    $store.Append($sample, 'EARLY')
    Assert ([object]::ReferenceEquals($failedTask, $pending.GetValue($null))) 'Failed writes retried inside ten seconds.'
    Enable-NextWrite
    $store.Append($sample, "MAX,`r`n")
    Wait-Write
    $lines = [IO.File]::ReadAllLines($csv)
    Assert ($lines.Length -eq 2 -and $lines[0] -ceq $header) 'Empty CSV recovery lost or duplicated the header.'
    Assert ($lines[1] -ceq ($sample.Timestamp.ToString('O') + ',,,35,1,87,MAX_')) 'CSV sample or timestamp changed.'
    $store.Append($sample, 'EARLY')
    Assert ([IO.File]::ReadAllLines($csv).Length -eq 2) 'Successful writes exceeded the sampling cadence.'

    $writer = [IO.StreamWriter]::new($csv, $false)
    try {
        $writer.WriteLine($header)
        $padding = 'x' * 220
        1..2500 | ForEach-Object { $writer.WriteLine(('{0:d4},{1}' -f $_, $padding)) }
    } finally { $writer.Dispose() }
    # With deletion denied, a failed replacement must preserve the complete original.
    $held = [IO.File]::Open($csv, 'Open', 'Read', 'ReadWrite')
    try {
        Enable-NextWrite
        $store.Append($sample, 'REPLACE_LOCKED')
        Wait-Write
        Assert ([IO.File]::ReadAllLines($csv).Length -eq 2502) 'Failed rotation truncated the existing file.'
        Assert ([IO.Directory]::GetFiles($temp, '*.tmp').Count -eq 0) 'Failed rotation left a temporary file.'
    } finally { $held.Dispose() }
    $held = [IO.File]::Open($csv, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        Enable-NextWrite
        $store.Append($sample, 'AFTER_TRIM')
        Wait-Write
        $lines = [IO.File]::ReadAllLines($csv)
        Assert ($lines.Length -eq 2001 -and $lines[0] -ceq $header) 'Rotation lost the header or retained the wrong number of rows.'
        Assert ($lines[1].StartsWith('0503,') -and $lines[-1].EndsWith(',AFTER_TRIM')) 'Rotation selected the wrong tail rows.'
        Assert ($held.Length -gt (Get-Item -LiteralPath $csv).Length) 'Rotation did not replace the existing file atomically.'
    } finally { $held.Dispose() }
    'PASS: bounded CSV writer, sharing failure, sampling cadence, header recovery and atomic rotation.'
} finally {
    foreach ($file in [IO.Directory]::GetFiles($temp)) { [IO.File]::Delete($file) }
    [IO.Directory]::Delete($temp)
}
