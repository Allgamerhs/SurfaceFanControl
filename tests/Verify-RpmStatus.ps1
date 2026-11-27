# Run in a separate Windows PowerShell 5.1 STA process on the Surface.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ScreenshotDirectory,
    [switch]$LiveMeasurement,
    [string]$InstalledExecutable
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Use powershell.exe -STA.' }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
[Windows.Application]::ResourceAssembly = $assembly
if (-not [SurfaceProFanControl.Infrastructure.DistributionProfile]::IsPortable) { throw 'Portable build required.' }
$device = [SurfaceProFanControl.Infrastructure.HardwareProbe]::new().ReadDevice()
if (-not [SurfaceProFanControl.Core.HardwareSupport]::Detect($device.Identity).CanControl) { throw 'Target Surface Pro required.' }
if ([SurfaceProFanControl.Infrastructure.StartupRegistration]::IsEnabled()) { throw 'This test host is registered for startup.' }

# Application queues its startup callback during construction. Cancel it before pumping
# the dispatcher so the test can use the compiled App.xaml without starting another window.
$hooks = [Windows.Threading.Dispatcher]::CurrentDispatcher.Hooks
$posted = [Collections.Generic.List[Windows.Threading.DispatcherOperation]]::new()
$capture = [Windows.Threading.DispatcherHookEventHandler]{ param($sender, $eventArgs) $posted.Add($eventArgs.Operation) }
$hooks.add_OperationPosted($capture)
try { $app = [SurfaceProFanControl.App]::new() } finally { $hooks.remove_OperationPosted($capture) }
if (-not $posted.Count) { throw 'Could not isolate the application startup callback.' }
foreach ($operation in $posted) {
    if (-not $operation.Abort()) { throw 'Could not suppress the application startup callback.' }
}
$app.InitializeComponent()
$support = [SurfaceProFanControl.Core.HardwareSupport]::new('MonitoringOnly', 'portable-windows-monitor', 'DeviceMonitoringOnly')
$window = [SurfaceProFanControl.MainWindow]::new($device, $support, $false)
$private = [Reflection.BindingFlags]'Instance,NonPublic'
$type = $window.GetType()
$settings = $type.GetField('_settings', $private).GetValue($window)
$settings.TelemetryLoggingEnabled = $false
$settings.NotificationsEnabled = $false
$settings.CloseToTray = $false
$settings.LanguageCode = 'tr'
$type.GetMethod('ApplyLocalization', $private).Invoke($window, @())
$type.GetField('_trayIcon', $private).GetValue($window).Visible = $false
$onSource = $type.GetMethod('Window_SourceInitialized', $private)
$window.remove_SourceInitialized([Delegate]::CreateDelegate([EventHandler], $window, $onSource))
$window.ShowInTaskbar = $false
$window.ShowActivated = $false
$tab = $window.FindName('StatusTab')
$tab.IsSelected = $true
$line = $window.FindName('LastMeasurementText')
$fan0 = $window.FindName('MeasuredFan0Text')
$fan1 = $window.FindName('MeasuredFan1Text')
$record = $type.GetMethod('RecordEcMeasurement', $private)
$timer = $type.GetField('_timer', $private).GetValue($window)
$missing = "G$([char]0xfc)ncel devir verisi yok" # ASCII script for Windows PowerShell's default file encoding.

function Wait-Ui([int]$Milliseconds) {
    $frame = [Windows.Threading.DispatcherFrame]::new()
    $delay = [Windows.Threading.DispatcherTimer]::new()
    $delay.Interval = [TimeSpan]::FromMilliseconds($Milliseconds)
    $delay.add_Tick({ $delay.Stop(); $frame.Continue = $false })
    $delay.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
}

function Read-Screen([string]$Stage) {
    $window.UpdateLayout()
    if (-not ($tab.IsSelected -and $line.IsVisible -and $line.ActualWidth -gt 0 -and $fan0.IsVisible -and $fan1.IsVisible)) {
        throw "The RPM fields are not visible in the selected Status tab ($Stage)."
    }
    if ($ScreenshotDirectory) {
        $null = [IO.Directory]::CreateDirectory($ScreenshotDirectory)
        $view = $window.Content
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]$view.ActualWidth, [int]$view.ActualHeight, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($view)
        $png = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream = [IO.File]::Create((Join-Path $ScreenshotDirectory "$Stage.png"))
        try { $png.Save($stream) } finally { $stream.Dispose() }
    }
    [pscustomobject]@{ Line = $line.Text; Fan1 = $fan0.Text; Fan2 = $fan1.Text }
}

function Assert-Fresh($Screen, $Sample, [datetime]$Before) {
    $match = [regex]::Match($Screen.Line, '\b[0-9]{2}:[0-9]{2}:[0-9]{2}\b')
    $after = Get-Date
    $expectedTimes = for ($t = $Before; $t -lt $after.AddSeconds(1); $t = $t.AddSeconds(1)) { $t.ToString('HH:mm:ss') }
    if (-not $match.Success -or $expectedTimes -notcontains $match.Value -or $Screen.Line.Contains($missing) -or
        $Screen.Fan1 -ne ('{0:N0} RPM' -f $Sample.Fan0Rpm) -or $Screen.Fan2 -ne ('{0:N0} RPM' -f $Sample.Fan1Rpm)) {
        throw "Unexpected fresh RPM display: $Screen"
    }
    $match.Value
}

try {
    $window.Show()
    $startup = [Diagnostics.Stopwatch]::StartNew()
    while (-not $timer.IsEnabled -and $startup.Elapsed.TotalSeconds -lt 25) { Wait-Ui 200 }
    if (-not $timer.IsEnabled) { throw 'The window did not start its polling timer.' }
    $initial = Read-Screen 'initial'
    if (-not $initial.Line.Contains($missing) -or $initial.Line -match '\d\d:\d\d:\d\d' -or ($initial.Fan1 + $initial.Fan2) -match '[0-9]') {
        throw "Unexpected state before any measurement: $initial"
    }

    # Hold the poll open to exercise aging while the normal request remains outstanding.
    $type.GetField('_requestInFlight', $private).SetValue($window, $true)
    $parse = [SurfaceProFanControl.Infrastructure.FanGuardSnapshot].GetMethod('Parse', [Reflection.BindingFlags]'Static,NonPublic')
    $controlledResponse = 'OK|AUTO|0|731|0|0|--|--|00|00'
    if ($LiveMeasurement) {
        if (-not $InstalledExecutable) { throw 'Supply the verified installed executable for a live read.' }
        $installedPath = (Resolve-Path -LiteralPath $InstalledExecutable).Path
        if ((Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -ne '3E7E9D40E2688F3CAF01486A6DE21F3BE3FFAC68868FAB64E31F5DC39315F608') {
            throw 'Live read requires the verified installed 1.7.0 client.'
        }
        $guardPath = Join-Path ([IO.Path]::GetDirectoryName($installedPath)) 'SurfaceProFanGuard.exe'
        if ((Get-FileHash -LiteralPath $guardPath -Algorithm SHA256).Hash -ne 'D46E395FE488DDF87E4DD380D9C724296F44733C3E672E2225A3E9BBC9DAE8EC') {
            throw 'Live read requires the verified installed 1.7.0 guard.'
        }
        $installed = [Reflection.Assembly]::LoadFrom($installedPath)
        if (-not [string]::Equals($installed.Location, $installedPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The live measurement client did not load from the verified installation.'
        }
        $client = $installed.CreateInstance('SurfaceProFanControl.Infrastructure.FanGuardClient')
        $before = Get-Date
        $reading = $client.Measure()
        $invariant = [Globalization.CultureInfo]::InvariantCulture
        $thermal = foreach ($value in $reading.TemperatureCelsius, $reading.CriticalCelsius) {
            if ($null -eq $value) { '--' } else { $value.ToString($invariant) }
        }
        $response = ('OK', $reading.State, $reading.Fan0Rpm, $reading.Fan1Rpm, $reading.Fan0Duty, $reading.Fan1Duty,
            $thermal[0], $thermal[1], $reading.Mode.ToString('X2'), $reading.Shadow.ToString('X2')) -join '|'
        $source = 'live'
    } else {
        $response = $controlledResponse
        $source = 'controlled'
        $before = Get-Date
    }
    $sample = $parse.Invoke($null, @($response))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $record.Invoke($window, @($sample))
    $fresh = Read-Screen "$source-fresh"
    $stamp = Assert-Fresh $fresh $sample $before
    Wait-Ui 17500
    if ($watch.Elapsed.TotalSeconds -lt 20 -and $line.Text -ne $fresh.Line) { throw 'The reading expired early.' }
    while (-not $line.Text.Contains($missing) -and $watch.Elapsed.TotalSeconds -lt 25) { Wait-Ui 150 }
    $elapsed = $watch.Elapsed.TotalSeconds
    $stale = Read-Screen "$source-stale"
    if ($elapsed -lt 20 -or -not $stale.Line.Contains($missing) -or -not $stale.Line.Contains($stamp) -or ($stale.Fan1 + $stale.Fan2) -match '[0-9]') {
        throw "Stale readings were not cleared while retaining the measurement time: $stale"
    }
    $before = Get-Date
    $sample = $parse.Invoke($null, @($controlledResponse))
    $record.Invoke($window, @($sample))
    $recovered = Read-Screen 'controlled-recovery'
    $newStamp = Assert-Fresh $recovered $sample $before
    if ($newStamp -eq $stamp) { throw 'The timestamp did not advance after recovery.' }
    [pscustomobject]@{ Model = $device.Identity.Model; Version = $assembly.GetName().Version.ToString(); Initial = $initial;
        SampleSource = $source; Fresh = $fresh; Stale = $stale; ObservedAfterSeconds = [math]::Round($elapsed, 2); ControlledRecovery = $recovered } | ConvertTo-Json -Depth 3
} finally {
    $window.Close()
}
