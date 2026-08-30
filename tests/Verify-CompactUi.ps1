[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $root 'src\SurfaceProFanControl\MainWindow.xaml'
$codePath = Join-Path $root 'src\SurfaceProFanControl\MainWindow.xaml.cs'
$localizationPath = Join-Path $root 'src\SurfaceProFanControl.Core\LocalizationCatalog.cs'
$guardClientPath = Join-Path $root 'src\SurfaceProFanControl\Infrastructure\FanGuardClient.cs'
$guardPath = Join-Path $root 'tools\I2EcFanTest\FanGuardService.cs'
$propsPath = Join-Path $root 'Directory.Build.props'
$manifestPath = Join-Path $root 'src\SurfaceProFanControl\app.manifest'
$readmePath = Join-Path $root 'README.md'
$projectPath = Join-Path $root 'src\SurfaceProFanControl\SurfaceProFanControl.csproj'
$iconPath = Join-Path $root 'src\SurfaceProFanControl\Assets\fan.ico'
$temperatureTrackerPath = Join-Path $root 'src\SurfaceProFanControl.Core\TemperatureSignalTracker.cs'
$curvePolicyPath = Join-Path $root 'src\SurfaceProFanControl.Core\BinaryFanCurvePolicy.cs'

[xml]$xaml = Get-Content -LiteralPath $xamlPath -Raw
$window = $xaml.DocumentElement
if ($window.GetAttribute('Width') -ne '480' -or $window.GetAttribute('Height') -ne '410' -or
    $window.GetAttribute('MinWidth') -ne '480' -or $window.GetAttribute('MinHeight') -ne '410' -or
    $window.GetAttribute('MaxWidth') -ne '480' -or $window.GetAttribute('MaxHeight') -ne '410') {
    throw 'v1.8.0 penceresi sabit 480x410 değil.'
}
if ($window.GetAttribute('Background') -ne '#C0C0C0' -or $window.GetAttribute('ResizeMode') -ne 'CanMinimize') {
    throw 'Windows 2000 klasik pencere sözleşmesi bozuldu.'
}

$ns = New-Object Xml.XmlNamespaceManager($xaml.NameTable)
$ns.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$tabs = @($xaml.SelectNodes('//w:TabItem', $ns))
if ($tabs.Count -ne 5) { throw "Beş sekme bekleniyordu; bulunan=$($tabs.Count)" }
$tabNames = @($tabs | ForEach-Object { $_.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml') })
foreach ($name in @('FanControlTab','CurveTab','ProfilesTab','StatusTab','SettingsTab')) {
    if ($name -notin $tabNames) { throw "v1.8.0 sekmesi eksik: $name" }
}

$named = @($xaml.SelectNodes('//*[@x:Name]', $ns))
$names = @($named | ForEach-Object { $_.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml') })
$required = @(
    'ModeText','TemperatureText','ProfileComboBox','AggressivenessSlider','BoostMinutesComboBox',
    'TimedBoostButton','ConstantMaximumButton','AutomaticButton','UsePowerProfilesCheckBox',
    'AcAggressivenessSlider','BatteryAggressivenessSlider','ImportProfileButton','ExportProfileButton',
    'HardwareSupportText','MeasuredFan0Text','MeasuredFan1Text','TemperatureGraphLine',
    'MeasureRpmButton','HealthTestButton','OpenTelemetryButton','LanguageComboBox',
    'StartWithWindowsCheckBox','StartMinimizedCheckBox','AlwaysOnTopCheckBox','CloseToTrayCheckBox',
    'NotificationsCheckBox','StartupDelayComboBox','ResetControlButton','OpenLogButton','StatusText','LastUpdateText',
    'CustomCurveEnabledCheckBox','CurveTurnOnSlider','CurveTurnOffSlider','CurveDwellComboBox',
    'PredictiveCoolingCheckBox','CurvePreviewLine','CurveCurrentMarker','CurveSensorText','CurveHintText'
)
foreach ($name in $required) {
    if ($name -notin $names) { throw "v1.8.0 arayüz öğesi eksik: $name" }
}

$code = Get-Content -LiteralPath $codePath -Raw
foreach ($fragment in @(
    'ConstantMaximumButton_Click','TimedBoostButton_Click','CoolingProfileCatalog.EffectiveAggressiveness',
    'ThermalSafetyController','ProfileExchange.Import','ProfileExchange.Export','TelemetryHistoryStore',
    'Forms.NotifyIcon','RegisterHotKey','LocalizationService.Use','HardwareSupport','_guard.Measure()',
    'FanHealthAssessment.IsStrongCooling','_guard.SetAutomatic()','_guard.Measure()',
    'TemperatureSignalTracker','BinaryFanCurvePolicy.ShouldUseMaximum','EcMeasurementIntervalTicks',
    '_loadingSettings = true;','InitializeComponent();')) {
    if (-not $code.Contains($fragment)) { throw "v1.8.0 davranışı kaynakta eksik: $fragment" }
}
if ($code -match '(?i)Set(Fan)?(Rpm|Pwm|Duty|Percent)\s*\(') {
    throw 'Kanıtlanmamış RPM/PWM/duty/yüzde setpoint yolu bulundu.'
}

$localization = Get-Content -LiteralPath $localizationPath -Raw
foreach ($codeName in @('"tr"','"en"','"de"','"it"','"es"','"ar"','"zh-Hans"','"ja"','"ru"','"pl"')) {
    if (-not $localization.Contains($codeName)) { throw "Dil kataloğu eksik: $codeName" }
}
foreach ($fragment in @('RightToLeft','Microsoft YaHei UI','Meiryo UI','ProfileConstantMaximum')) {
    if (-not $localization.Contains($fragment)) { throw "Yerelleştirme sözleşmesi eksik: $fragment" }
}

$client = Get-Content -LiteralPath $guardClientPath -Raw
$guard = Get-Content -LiteralPath $guardPath -Raw
foreach ($fragment in @('SetMaximum() => Send("MAX|','SetAutomatic() => Send("AUTO")','Measure() => Send("MEASURE")')) {
    if (-not $client.Contains($fragment)) { throw "UI/guard komutu eksik: $fragment" }
}
foreach ($fragment in @('case "MAX":','case "AUTO":','case "MEASURE":','FormatMeasuredStatus')) {
    if (-not $guard.Contains($fragment)) { throw "Guard v1.8 komutu eksik: $fragment" }
}

if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf) -or (Get-Item -LiteralPath $iconPath).Length -lt 256) {
    throw 'Fan simgesi eksik veya geçersiz.'
}
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$applicationIcon = [string]$project.SelectSingleNode('/Project/PropertyGroup/ApplicationIcon').'#text'
if ($applicationIcon -ne 'Assets\fan.ico') {
    throw 'Fan simgesi EXE ApplicationIcon olarak ayarlı değil.'
}
foreach ($path in @($temperatureTrackerPath, $curvePolicyPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Eğri çekirdeği eksik: $path" }
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -ne '1.8.0' -or [string]$props.Project.PropertyGroup.AssemblyVersion -ne '1.8.0.0' -or
    [string]$props.Project.PropertyGroup.FileVersion -ne '1.8.0.0') {
    throw 'Merkezi sürüm 1.8.0 değil.'
}
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$manifestNs = New-Object Xml.XmlNamespaceManager($manifest.NameTable)
$manifestNs.AddNamespace('a', 'urn:schemas-microsoft-com:asm.v1')
if ($manifest.SelectSingleNode('/a:assembly/a:assemblyIdentity', $manifestNs).version -ne '1.8.0.0') {
    throw 'Manifest sürümü 1.8.0.0 değil.'
}

$documentation = Get-Content -LiteralPath $readmePath -Raw
foreach ($fragment in @('480×410','Portable Monitor','ITE IT8519','VirusTotal','CVE-2020-14979','ten languages')) {
    if (-not $documentation.Contains($fragment)) { throw "v1.8.0 belge sözleşmesi eksik: $fragment" }
}

[pscustomobject]@{
    Result = 'PASS'
    Version = $version
    Size = '480x410'
    Theme = 'Windows 2000 classic (#C0C0C0)'
    Tabs = $tabNames -join ', '
    Languages = 10
    HardwareModes = 'AUTO, MAX; MEASURE read-only'
    NamedElements = $names.Count
}
