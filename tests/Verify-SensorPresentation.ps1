# Compiled WPF presentation regression with synthetic inputs, in a separate Windows PowerShell 5.1 process.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run with powershell.exe -NoProfile -STA.'
}
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$type = $assembly.GetType('SurfaceProFanControl.MainWindow', $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public,DeclaredOnly'
$static = [Reflection.BindingFlags]'Static,NonPublic'
$window = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
function Field([string]$name) { $type.GetField($name, $flags).GetValue($window) }
function Set-Field([string]$name, $value) { $type.GetField($name, $flags).SetValue($window, $value.PSObject.BaseObject) }
function Call([string]$name, [object[]]$parameters) { $type.GetMethod($name, $flags).Invoke($window, $parameters) }
function Assert($ok, [string]$message) { if (!$ok) { throw $message } }
function Caption([string]$key) { [SurfaceProFanControl.Infrastructure.LocalizationService]::Text($key) }
function Raw($temp, $critical, [bool]$ac, [datetime]$time) {
    [SurfaceProFanControl.Infrastructure.TelemetrySnapshot]::new($temp, $critical, 37, $ac, 72, $time)
}
function Paint($raw, [datetime]$now) {
    Set-Field '_lastRawTelemetry' $raw
    $merged = Call 'MergeTelemetry' @($raw, $now)
    Call 'UpdateCurvePresentation' @((Field '_lastTemperatureSignal'))
    return $merged
}

# Build only individual controls. The application, Window constructor, registry settings store,
# guard, notification icon and polling timer are never created.
foreach ($f in $type.GetFields($flags)) {
    if ([Windows.FrameworkElement].IsAssignableFrom($f.FieldType)) {
        $f.SetValue($window, [Activator]::CreateInstance($f.FieldType))
    }
}
$settings = [SurfaceProFanControl.Infrastructure.UserSettings]::new()
Set-Field '_settings' $settings
Set-Field '_hardwareSupport' ([SurfaceProFanControl.Core.HardwareSupport]::new('FullControl', 'synthetic', 'synthetic'))
Set-Field '_lastTemperatureSignal' ([SurfaceProFanControl.Core.TemperatureSignal]::Empty)
Set-Field '_temperatureSignalTracker' ([SurfaceProFanControl.Core.TemperatureSignalTracker]::new())
$history = $type.GetField('_temperatureHistory', $flags)
$history.SetValue($window, [Activator]::CreateInstance($history.FieldType))
$parse = [SurfaceProFanControl.Infrastructure.FanGuardSnapshot].GetMethod('Parse', $static)
$clock = [SurfaceProFanControl.Infrastructure.FanGuardSnapshot].GetField('<ReceivedAtUtc>k__BackingField', $flags)
$log = [SurfaceProFanControl.Infrastructure.AppLogger].GetField('<LogPath>k__BackingField', $static)
$logPath = [IO.Path]::GetTempFileName()
$log.SetValue($null, $logPath)
$source = $null
try {
    Call 'UpdateRpmDisplay' @()
    Assert ((Field 'VerifiedPeakRpmText').Text -ceq '-- / --') 'Peak RPM must start empty.'
    $now = [datetime]::UtcNow
    $a = $parse.Invoke($null, @('OK|AUTO|0|870|0|0|71|99|00|00'))
    $clock.SetValue($a, $now.AddSeconds(-25))
    $b = $parse.Invoke($null, @('OK|AUTO|813|430|0|0|--|--|00|00'))
    Call 'RecordEcMeasurement' @($a)
    Assert ((Field 'VerifiedPeakRpmText').Text -ceq '0 / 870') 'A measured zero was lost.'
    Call 'RecordEcMeasurement' @($b)
    Assert ((Field 'VerifiedPeakRpmText').Text -ceq '813 / 870') 'Peaks must be independent per fan.'
    Assert ((Field 'MeasuredFan0Text').Text -ceq '813 RPM' -and (Field 'MeasuredFan1Text').Text -ceq '430 RPM') 'Current readings were replaced by peak readings.'

    $raw = Raw 54 93 $true $now.AddSeconds(-2)
    $merged = Paint $raw $now
    Assert ($merged.TemperatureCelsius -eq 54 -and $merged.FirmwareCriticalCelsius -eq 93) 'Expired EC temperature did not fall back to ACPI.'
    Assert ((Field 'TemperatureText').ToolTip.StartsWith((Caption 'SensorAcpi')) -and (Field 'CurveSensorText').Text.StartsWith((Caption 'SensorAcpi'))) 'Curve or tooltip mislabeled the ACPI source.'
    Assert ((Field 'CurveCurrentMarker').Visibility -eq 'Visible') 'Valid ACPI temperature did not show the curve marker.'
    $missing = Paint (Raw $null $null $true $now) $now
    Assert ($null -eq $missing.TemperatureCelsius -and (Field 'CurveSensorText').Text -ceq (Caption 'SensorUnavailable')) 'Missing EC and ACPI produced a temperature.'
    $expired = Paint (Raw 88 98 $true $now.AddSeconds(-40)) $now
    Assert ($null -eq $expired.TemperatureCelsius -and $null -eq $expired.FirmwareCriticalCelsius) 'Expired ACPI data was reused.'
    Assert ((Field 'CurveCurrentMarker').Visibility -eq 'Collapsed' -and (Field 'TemperatureText').ToolTip -ceq (Caption 'SensorUnavailable')) 'Expired readings kept a live marker or tooltip.'
    $fresh = $parse.Invoke($null, @('OK|AUTO|702|451|0|0|72|101|00|00'))
    Call 'RecordEcMeasurement' @($fresh)
    $merged = Paint $raw ([datetime]::UtcNow)
    Assert ($merged.TemperatureCelsius -eq 72 -and $merged.FirmwareCriticalCelsius -eq 101 -and (Field 'TemperatureText').ToolTip.StartsWith((Caption 'SensorEc'))) 'Fresh EC data did not replace ACPI data or reveal its source.'

    # A hidden presentation source exercises the IsVisible guard and generated geometry without showing a window.
    $canvas = Field 'TemperatureGraphCanvas'
    $path = Field 'TemperatureGraphLine'
    [void]$canvas.Children.Add($path)
    $options = [Windows.Interop.HwndSourceParameters]::new('Synthetic sensor graph')
    $options.WindowStyle = [int]::MinValue # WS_POPUP, without WS_VISIBLE
    $options.Width = 240
    $options.Height = 80
    $source = [Windows.Interop.HwndSource]::new($options)
    $source.RootVisual = $canvas
    $canvas.Measure([Windows.Size]::new(240,80))
    $canvas.Arrange([Windows.Rect]::new(0,0,240,80))
    $now = [datetime]::UtcNow
    foreach ($point in @(@(-16,55), @(-15,56), @(-14,$null), @(-13,61), @(-12,62), @(-6,69), @(-5,70))) {
        $value = if ($null -ne $point[1]) { [double]$point[1] }
        Call 'AddTemperature' @($value, $now.AddSeconds($point[0]))
    }
    Assert ($null -ne $path.Data) 'The hidden presentation source did not render a graph.'
    $geometry = $path.Data.ToString([Globalization.CultureInfo]::InvariantCulture)
    $figures = @($geometry.Split('M') | Where-Object { $_ })
    Assert ($figures.Count -eq 3) "Expected three separate graph lines. Geometry: $geometry"
    foreach ($figure in $figures) { Assert ($figure.Contains('L')) 'A graph segment lost its valid measurements.' }

    $settings.UsePowerProfiles = $true
    $settings.CustomCurveEnabled = $false
    $settings.AcAggressivenessPercent = 95
    $settings.BatteryAggressivenessPercent = 15
    $ac = Raw 50 90 $true $now
    $battery = Raw 51 90 $false $now
    Assert ((Call 'EffectiveAggressiveness' @($ac)) -eq 95 -and (Call 'EffectiveAggressiveness' @($battery)) -eq 15) 'Fixture did not start in AC/battery mode.'
    # Invoke the actual user-selection path. Saving has no settings store and only logs to the temporary test file.
    Call 'ApplyHotKeyProfile' @([SurfaceProFanControl.Core.CoolingProfileId]::Balanced)
    $general = [SurfaceProFanControl.Core.CoolingProfileCatalog]::Get('Balanced').AggressivenessPercent
    Assert (!$settings.UsePowerProfiles -and !$settings.CustomCurveEnabled -and !(Field 'UsePowerProfilesCheckBox').IsChecked) 'Selecting a general preset left power overrides active.'
    Assert ((Call 'EffectiveAggressiveness' @($ac)) -eq $general -and (Call 'EffectiveAggressiveness' @($battery)) -eq $general) 'The new general profile was not effective on AC and battery.'
    'PASS: compiled WPF session RPM peaks, EC/ACPI temperature and expiry, graph gaps, general profile precedence.'
} finally {
    if ($source) { $source.Dispose() }
    [IO.File]::Delete($logPath)
}
