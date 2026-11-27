# Uses two isolated Windows PowerShell hosts. Neither launches the WPF application or connects to the guard.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [switch]$Request
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run with powershell.exe -NoProfile -STA.'
}
Add-Type -AssemblyName WindowsBase
$exe = (Resolve-Path -LiteralPath $Executable).Path
$type = [Reflection.Assembly]::LoadFrom($exe).GetType('SurfaceProFanControl.Infrastructure.WindowActivation', $true)
if ($Request) {
    if (!$type.GetMethod('TryShowExisting').Invoke($null, @())) { throw 'The second process could not reach the first.' }
    return
}

function Request-Activation {
    $arguments = '-NoProfile -NonInteractive -STA -File "{0}" -Executable "{1}" -Request' -f $PSCommandPath, $exe
    $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (!$child.WaitForExit(10000) -or $child.ExitCode -ne 0) { throw 'Duplicate launch failed.' }
    } finally { $child.Dispose() }
}

$owner = [Activator]::CreateInstance($type)
try {
    # An immediate second launch can arrive while the first process is still probing hardware.
    Request-Activation
    $dispatcher = [Windows.Threading.Dispatcher]::CurrentDispatcher
    $received = [Collections.Generic.List[int]]::new()
    $callback = [Action]{ $received.Add([Threading.Thread]::CurrentThread.ManagedThreadId) }
    $type.GetMethod('Listen').Invoke($owner, @($dispatcher, $callback))
    foreach ($expectedCount in 1, 2) {
        if ($expectedCount -eq 2) { Request-Activation }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($received.Count -lt $expectedCount -and $timer.ElapsedMilliseconds -lt 2000) {
            $frame = [Windows.Threading.DispatcherFrame]::new()
            $null = $dispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false })
            [Windows.Threading.Dispatcher]::PushFrame($frame)
            Start-Sleep -Milliseconds 10
        }
        if ($received.Count -ne $expectedCount -or $received[$expectedCount - 1] -ne [Threading.Thread]::CurrentThread.ManagedThreadId) {
            throw 'The window activation callback did not run once on the UI dispatcher.'
        }
    }
    'Passed cross-process activation before and after window initialization.'
} finally { $owner.Dispose() }
