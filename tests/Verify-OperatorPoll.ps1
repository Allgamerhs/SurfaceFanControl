# Synthetic regression for the compiled WPF operator/poll arbitration. Run in Windows PowerShell 5.1 -STA.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$CoreAssembly
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run with powershell.exe -NoProfile -STA -File tests/Verify-OperatorPoll.ps1 -Executable <built WPF exe>.'
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Windows.Forms
if ($CoreAssembly) { $null = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $CoreAssembly).Path) }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$type = $assembly.GetType('SurfaceProFanControl.MainWindow', $true)
$fields = [Reflection.BindingFlags]'Instance,NonPublic,Public,DeclaredOnly'
$run = $type.GetMethod('RunCommand', $fields)
$changeChoice = $type.GetMethod('MarkPolicySelectionChanged', $fields)
if (!$run -or $run.GetParameters().Count -ne 3 -or !$changeChoice) { throw 'Build the WPF operator/poll change first.' }

# The production command runs on a ThreadPool thread, where a PowerShell scriptblock has no runspace.
Add-Type @'
using System;
using System.Threading;
public sealed class SyntheticOperatorCommand<T> {
    readonly T snapshot;
    public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
    public readonly ManualResetEventSlim Release = new ManualResetEventSlim(true);
    public int Calls, Selections;
    public SyntheticOperatorCommand(T snapshot) { this.snapshot = snapshot; }
    public Func<T> Command { get { return Execute; } }
    public Action Selection { get { return () => Interlocked.Increment(ref Selections); } }
    T Execute() {
        Interlocked.Increment(ref Calls);
        Started.Set();
        if (!Release.Wait(5000)) throw new TimeoutException("Synthetic command was not released.");
        return snapshot;
    }
}
'@

# Do not construct the application/window. No Loaded handler, settings store, timer callback, tray
# icon, FanGuardClient, native window handle or hardware backend exists in this fixture.
$window = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
function Set-Field($Name, $Value) {
    $field = $type.GetField($Name, $fields)
    if (!$field) { throw "Missing compiled field $Name." }
    $field.SetValue($window, $Value.PSObject.BaseObject)
}
function Field($Name) { $type.GetField($Name, $fields).GetValue($window) }
foreach ($name in 'TimedBoostButton','ConstantMaximumButton','AutomaticButton','MeasureRpmButton','HealthTestButton',
    'ResetControlButton','ModeText','StatusText','TemperatureText','CriticalText','Fan0RpmText','Fan0DutyText',
    'Fan1RpmText','Fan1DutyText','CurveTurnOnValueText','CurveTurnOffValueText','CurvePreviewCanvas','CurvePreviewLine',
    'CurveReturnThreshold','CurveCurrentMarker','CurveSensorText','CurveHintText','LastUpdateText') {
    $field = $type.GetField($name, $fields)
    $field.SetValue($window, [Activator]::CreateInstance($field.FieldType))
}
Set-Field '_hardwareSupport' ([SurfaceProFanControl.Core.HardwareSupport]::new('FullControl', 'synthetic', 'synthetic'))
Set-Field '_settings' ([SurfaceProFanControl.Infrastructure.UserSettings]::new())
Set-Field '_lastTemperatureSignal' ([SurfaceProFanControl.Core.TemperatureSignal]::Empty)
$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromHours(1)
Set-Field '_timer' $timer
$parse = [SurfaceProFanControl.Infrastructure.FanGuardSnapshot].GetMethod('Parse', [Reflection.BindingFlags]'Static,NonPublic')
$sample = $parse.Invoke($null, @('OK|AUTO|0|0|0|0|--|--|00|00'))
function New-Command { [SyntheticOperatorCommand[SurfaceProFanControl.Infrastructure.FanGuardSnapshot]]::new($sample) }
function Start-Request($Command) {
    $run.Invoke($window, @($Command.Command, 'Synthetic command finished', $Command.Selection))
}
function New-Poll {
    $source = [Threading.Tasks.TaskCompletionSource[bool]]::new()
    Set-Field '_activePoll' $source.Task
    return $source
}
function Assert($Condition, [string]$Failure) { if (!$Condition) { throw $Failure } }
function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!(& $Condition) -and $watch.Elapsed.TotalSeconds -lt 8) {
        $dispatcher.Invoke([Windows.Threading.DispatcherPriority]::Background, [Action]{})
        [Threading.Thread]::Sleep(5)
    }
    Assert (& $Condition) $Failure
}
function Wait-Request($Task) {
    Wait-Until { $Task.IsCompleted } 'Timed out waiting for the compiled handler.'
    $Task.GetAwaiter().GetResult() | Out-Null
}
function Check-State($Command, [int]$Calls, [int]$Selections) {
    Assert ($Command.Calls -eq $Calls -and $Command.Selections -eq $Selections) "Expected $Calls commands and $Selections selections; saw $($Command.Calls) and $($Command.Selections)."
    Assert (!(Field '_requestInFlight')) 'The operator slot was not released.'
}

$dispatcher = [Windows.Threading.Dispatcher]::CurrentDispatcher
$oldContext = [Threading.SynchronizationContext]::Current
$logPath = [SurfaceProFanControl.Infrastructure.AppLogger].GetField('<LogPath>k__BackingField', [Reflection.BindingFlags]'Static,NonPublic')
$originalLog = $logPath.GetValue($null)
$tempLog = [IO.Path]::GetTempFileName()
try {
    $logPath.SetValue($null, $tempLog)
    [Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new($dispatcher))

    $poll = New-Poll
    $command = New-Command
    $command.Release.Reset()
    $first = Start-Request $command
    Assert (!$first.IsCompleted -and !(Field 'MeasureRpmButton').IsEnabled) 'The pending request did not reserve the operator slot.'
    $duplicate = Start-Request $command
    Assert ($duplicate.IsCompleted -and $command.Calls -eq 0) 'A second input was accepted while waiting for the poll.'
    $poll.SetResult($true)
    Wait-Until { $command.Started.IsSet } 'The successful poll did not release the synthetic command.'
    $duplicate = Start-Request $command
    Assert $duplicate.IsCompleted 'A second input was accepted during manual work.'
    $command.Release.Set()
    Wait-Request $first
    Check-State $command 1 1
    Assert ((Field 'StatusText').Text -eq 'Synthetic command finished' -and (Field 'MeasureRpmButton').IsEnabled) 'The successful command did not restore the UI.'

    $poll = New-Poll
    $command = New-Command
    $pending = Start-Request $command
    $poll.SetResult($false)
    Wait-Request $pending
    Check-State $command 0 0

    # Exercise the actual click handler on its cancellation branch. _guard is deliberately null.
    $poll = New-Poll
    $mode = Field 'ModeText'
    $mode.Text = 'Untouched'
    $click = $type.GetMethod('MeasureRpmButton_Click', $fields)
    $click.Invoke($window, @($null, $null))
    Assert ((Field '_requestInFlight') -and !(Field 'MeasureRpmButton').IsEnabled) 'An RPM click was lost during polling.'
    $poll.SetResult($false)
    Wait-Until { !(Field '_requestInFlight') } 'The failed poll did not release the RPM click.'
    Assert ($mode.Text -eq 'Untouched' -and (Field 'MeasureRpmButton').IsEnabled) 'The cancelled RPM click accessed the absent guard or left the button disabled.'

    $poll = New-Poll
    $poll.SetResult($false)
    $command = New-Command
    Wait-Request (Start-Request $command)
    Check-State $command 0 0

    $poll = New-Poll
    $command = New-Command
    $pending = Start-Request $command
    Wait-Request $pending
    Assert ((Field 'StatusText').Text -eq [TimeoutException]::new().Message -and (Field 'MeasureRpmButton').IsEnabled) 'The timeout was not shown or did not restore the UI.'
    $poll.SetResult($true)
    $dispatcher.Invoke([Windows.Threading.DispatcherPriority]::Background, [Action]{})
    Check-State $command 0 0

    $poll = New-Poll
    $command = New-Command
    $pending = Start-Request $command
    Set-Field '_closing' $true
    $poll.SetResult($true)
    Wait-Request $pending
    Check-State $command 0 0
    Assert (!(Field 'MeasureRpmButton').IsEnabled) 'Closing re-enabled operator input.'
    Set-Field '_closing' $false

    $poll = New-Poll
    $command = New-Command
    $pending = Start-Request $command
    $changeChoice.Invoke($window, @())
    $poll.SetResult($true)
    Wait-Request $pending
    Check-State $command 0 0

    $poll = New-Poll
    $poll.SetResult($true)
    $command = New-Command
    $command.Release.Reset()
    $pending = Start-Request $command
    Wait-Until { $command.Started.IsSet } 'The synthetic command did not start.'
    $changeChoice.Invoke($window, @())
    $command.Release.Set()
    Wait-Request $pending
    Check-State $command 1 0
    'Passed synthetic WPF operator/poll tests: single dispatch, concurrent input, failed and completed-failed polls, timeout, shutdown, and newer selections before and after dispatch.'
} finally {
    if ($command) { $command.Release.Set() }
    $timer.Stop()
    [Threading.SynchronizationContext]::SetSynchronizationContext($oldContext)
    $logPath.SetValue($null, $originalLog)
    [IO.File]::Delete($tempLog)
}
