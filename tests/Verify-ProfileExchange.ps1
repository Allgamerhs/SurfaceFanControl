param([Parameter(Mandatory)][string]$Executable)

$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$settings = [SurfaceProFanControl.Infrastructure.UserSettings]::new()
$exchange = [SurfaceProFanControl.Infrastructure.ProfileExchange]
$testId = [guid]::NewGuid().ToString('N')
$keyPath = "Software\SurfaceFanProfileTest-$testId"
$testDirectory = [IO.Directory]::CreateDirectory((Join-Path $env:TEMP "SurfaceFanProfileTest-$testId"))
$path = Join-Path $testDirectory.FullName 'profile.json'
$invalidPath = Join-Path $testDirectory.FullName 'invalid.json'
$checks = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}

try {
    $settings.CoolingAggressivenessPercent = 100
    $settings.AcAggressivenessPercent = 0
    $settings.BatteryAggressivenessPercent = 71
    $settings.PredictiveCoolingEnabled = $false
    $settings.CurveTurnOnCelsius = 90
    $settings.CurveTurnOffCelsius = 87
    $settings.CurveDwellSeconds = 300
    $settings.BoostMinutes = 60
    $exchange::Export($path, $settings)
    $settings.CurveDwellSeconds = 30
    $exchange::Export($path, $settings)
    $saved = [IO.File]::ReadAllText($path)
    $settings.CurveDwellSeconds = 60
    $settings.AlwaysOnTop = $true
    $exchange::Import($path, $settings)
    Assert-True ($settings.CustomCurveEnabled -and $settings.CurveTurnOnCelsius -eq 90 -and
        $settings.CurveTurnOffCelsius -eq 87 -and $settings.CurveDwellSeconds -eq 30 -and
        $settings.BoostMinutes -eq 60 -and !$settings.PredictiveCoolingEnabled -and
        $settings.CoolingAggressivenessPercent -eq 100 -and $settings.AcAggressivenessPercent -eq 0 -and
        $settings.BatteryAggressivenessPercent -eq 71 -and $settings.AlwaysOnTop) 'Version 2 round trip failed.'

    $previous = $settings | ConvertTo-Json -Compress
    $invalid = @(
        $saved.Replace('"AggressivenessPercent":100,',''),
        $saved.Replace('"AggressivenessPercent":100','"AggressivenessPercent":101'),
        $saved.Replace('"CurveTurnOffCelsius":87','"CurveTurnOffCelsius":89'),
        $saved.Replace('"CurveDwellSeconds":30','"CurveDwellSeconds":45'),
        $saved.Replace('"CustomCurveEnabled":true,',''),
        $saved.Replace('"UsePowerProfiles":false','"UsePowerProfiles":true'),
        $saved.Replace('"FormatVersion":2','"FormatVersion":3'),
        ($saved + '{}'), $saved.Substring(0, $saved.Length - 1), '', (' ' * 16385)
    )
    foreach ($case in $invalid) {
        [IO.File]::WriteAllText($invalidPath, $case)
        $rejected = $false
        try { $exchange::Import($invalidPath, $settings) } catch { $rejected = $true }
        Assert-True ($rejected -and ($settings | ConvertTo-Json -Compress) -eq $previous) 'Invalid import modified settings or succeeded.'
    }
    $lock = [IO.File]::Open($path, 'Open', 'Read', 'Read')
    try {
        $rejected = $false
        try { $exchange::Export($path, $settings) } catch { $rejected = $true }
        Assert-True ($rejected -and [IO.File]::ReadAllText($path) -ceq $saved) 'Failed export damaged the previous file.'
    } finally { $lock.Dispose() }

    $legacy = '{"FormatVersion":1,"AggressivenessPercent":20,"UsePowerProfiles":true,"AcAggressivenessPercent":80,"BatteryAggressivenessPercent":0,"BoostMinutes":5}'
    [IO.File]::WriteAllText($path, $legacy)
    $exchange::Import($path, $settings)
    Assert-True (!$settings.CustomCurveEnabled -and $settings.UsePowerProfiles -and
        $settings.AcAggressivenessPercent -eq 80 -and $settings.BatteryAggressivenessPercent -eq 0 -and
        $settings.CurveTurnOnCelsius -eq 90 -and $settings.AlwaysOnTop) 'Version 1 did not restore its active policy.'

    $storeType = [SurfaceProFanControl.Infrastructure.UserSettingsStore]
    $store = [Activator]::CreateInstance($storeType, [Reflection.BindingFlags]'Instance,NonPublic',
        $null, [object[]]@($keyPath), $null)
    Assert-True ($store.Load().CustomCurveEnabled) 'Fresh installation lost its curve default.'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($keyPath)
    try {
        $key.SetValue('CoolingAggressivenessPercent',100)
        $key.SetValue('CloseToTray',2)
        $key.SetValue('StartupDelaySeconds',12)
    } finally { $key.Dispose() }
    $migrated = $store.Load()
    Assert-True (!$migrated.CustomCurveEnabled -and $migrated.CoolingAggressivenessPercent -eq 100 -and
        $migrated.CloseToTray -and $migrated.StartupDelaySeconds -eq 15) 'Legacy settings or fallback defaults were lost.'
    $store.Save($settings)
    Assert-True (($store.Load() | ConvertTo-Json -Compress) -eq ($settings | ConvertTo-Json -Compress)) 'Registry round trip failed.'
    Write-Output "PASS: $checks profile and settings checks"
} finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($keyPath, $false)
    [IO.File]::Delete($path)
    [IO.File]::Delete($invalidPath)
    $testDirectory.Delete()
}
