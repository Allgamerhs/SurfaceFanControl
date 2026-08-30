[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$IncludeSetup,
    [switch]$AllowKnownVulnerableDriver
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Directory.Build.props içinde sürüm bulunamadı.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root ("artifacts\release-v" + $version)
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
        throw "Çıktı klasörü boş değil; mevcut sürümün üzerine yazılmadı: $output"
    }
}
else {
    New-Item -ItemType Directory -Path $output | Out-Null
}

if ($IncludeSetup -and -not $AllowKnownVulnerableDriver) {
    throw 'Setup paketi WinRing0 içeriyor. Yerel deneme için -AllowKnownVulnerableDriver açıkça verilmelidir; genel yayın yasaktır.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SurfaceFanControl-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

function Invoke-DotnetBuild {
    param([string]$Flavor, [string]$Destination)

    New-Item -ItemType Directory -Path $Destination | Out-Null
    & dotnet build (Join-Path $root 'src\SurfaceProFanControl\SurfaceProFanControl.csproj') `
        -c Release -p:DistributionFlavor=$Flavor -o $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "$Flavor derlemesi başarısız oldu: $LASTEXITCODE"
    }
}

function Require-File {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Gerekli dosya eksik: $Path"
    }
}

function Copy-ReleaseFile {
    param([string]$Source, [string]$DestinationDirectory, [string]$DestinationName)
    Require-File $Source
    $name = if ([string]::IsNullOrWhiteSpace($DestinationName)) {
        Split-Path -Leaf $Source
    }
    else {
        $DestinationName
    }
    Copy-Item -LiteralPath $Source -Destination (Join-Path $DestinationDirectory $name)
}

function Get-InnoCompiler {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @(
        (Join-Path $root 'artifacts\build-tools\InnoSetup7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'))) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }
    throw 'Inno Setup derleyicisi ISCC.exe bulunamadı.'
}

$releaseAssets = [Collections.Generic.List[string]]::new()
try {
    $portableBuild = Join-Path $temporaryRoot 'portable-build'
    $portableStage = Join-Path $temporaryRoot 'portable-package'
    Invoke-DotnetBuild 'Portable' $portableBuild
    New-Item -ItemType Directory -Path $portableStage | Out-Null

    foreach ($forbidden in @('WinRing0x64.sys','SurfaceProFanGuard.exe','SurfaceProFanGuard.exe.config')) {
        if (Test-Path -LiteralPath (Join-Path $portableBuild $forbidden)) {
            throw "Portable derlemede kalıcı sistem bileşeni bulundu: $forbidden"
        }
    }
    foreach ($name in @(
        'SurfaceProFanControl.exe',
        'SurfaceProFanControl.exe.config',
        'SurfaceProFanControl.Core.dll')) {
        Copy-ReleaseFile (Join-Path $portableBuild $name) $portableStage
    }
    Copy-ReleaseFile (Join-Path $root 'LICENSE') $portableStage 'LICENSE.txt'
    Copy-ReleaseFile (Join-Path $PSScriptRoot 'PORTABLE-README.txt') $portableStage 'README.txt'

    $portableZip = Join-Path $output ("SurfaceFanControl-Portable-v$version.zip")
    Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableZip -CompressionLevel Optimal
    $releaseAssets.Add($portableZip)

    if ($IncludeSetup) {
        $installedBuild = Join-Path $temporaryRoot 'installed-build'
        $installerOutput = Join-Path $temporaryRoot 'installer-output'
        Invoke-DotnetBuild 'Installed' $installedBuild
        New-Item -ItemType Directory -Path $installerOutput | Out-Null

        $driverPath = Join-Path $installedBuild 'WinRing0x64.sys'
        Require-File $driverPath
        $driverHash = (Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash
        if ($driverHash -ne '11BD2C9F9E2397C9A16E0990E4ED2CF0679498FE0FD418A3DFDAC60B5C160EE5') {
            throw "WinRing0 sabit SHA-256 değeri uyuşmuyor: $driverHash"
        }
        if ((Get-AuthenticodeSignature -LiteralPath $driverPath).Status -ne 'Valid') {
            throw 'WinRing0 Authenticode imzası geçerli değil.'
        }

        $iscc = Get-InnoCompiler
        & $iscc `
            ("/DAppVersion=" + $version) `
            ("/DBuildDir=" + $installedBuild) `
            ("/DSourceRoot=" + $root) `
            ("/DOutputDir=" + $installerOutput) `
            (Join-Path $PSScriptRoot 'SurfaceFanControl.iss')
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup derlemesi başarısız oldu: $LASTEXITCODE"
        }

        $setupName = "SurfaceFanControl-Setup-v$version.exe"
        $setupCompiled = Join-Path $installerOutput $setupName
        Require-File $setupCompiled
        $setupAsset = Join-Path $output $setupName
        Copy-Item -LiteralPath $setupCompiled -Destination $setupAsset
        $releaseAssets.Add($setupAsset)

        $setupStage = Join-Path $temporaryRoot 'setup-package'
        New-Item -ItemType Directory -Path $setupStage | Out-Null
        Copy-ReleaseFile $setupCompiled $setupStage
        Copy-ReleaseFile (Join-Path $root 'LICENSE') $setupStage 'LICENSE.txt'
        Copy-ReleaseFile (Join-Path $PSScriptRoot 'SETUP-README.txt') $setupStage 'README.txt'
        $setupZip = Join-Path $output ("SurfaceFanControl-Setup-v$version.zip")
        Compress-Archive -Path (Join-Path $setupStage '*') -DestinationPath $setupZip -CompressionLevel Optimal
        $releaseAssets.Add($setupZip)
    }

    $hashLines = foreach ($asset in $releaseAssets | Sort-Object) {
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $asset)"
    }
    $hashPath = Join-Path $output 'SHA256SUMS.txt'
    [IO.File]::WriteAllLines($hashPath, $hashLines, [Text.UTF8Encoding]::new($false))

    $virusTotalRows = foreach ($asset in $releaseAssets | Sort-Object) {
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        '| `{0}` | `{1}` | [VirusTotal raporu](https://www.virustotal.com/gui/file/{1}) |' -f `
            (Split-Path -Leaf $asset), $hash
    }
    $virusTotal = @(
        '# VirusTotal reports',
        '',
        'Bu bağlantılar yalnızca nihai dosyalar VirusTotal''a gönderildikten sonra sonuç gösterir.',
        'Bir rapor, dosyanın güvenli olduğunu garanti etmez.',
        '',
        '| Dosya | SHA-256 | Rapor |',
        '|---|---|---|',
        $virusTotalRows
    )
    [IO.File]::WriteAllLines(
        (Join-Path $output 'VIRUSTOTAL.md'),
        [string[]]$virusTotal,
        [Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        Result = 'PASS'
        Version = $version
        OutputDirectory = $output
        Portable = Split-Path -Leaf $portableZip
        SetupIncluded = [bool]$IncludeSetup
        Assets = $releaseAssets.Count
    }
}
finally {
    $temporaryFull = [IO.Path]::GetFullPath($temporaryRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($temporaryFull.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        $temporaryFull -like '*SurfaceFanControl-release-*' -and
        (Test-Path -LiteralPath $temporaryFull -PathType Container)) {
        Remove-Item -LiteralPath $temporaryFull -Recurse -Force
    }
}
