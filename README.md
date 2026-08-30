# Surface Fan Control 1.8.0

Surface Fan Control is an open-source Windows desktop application for compact hardware monitoring
and carefully allowlisted fan control backends. The interface is intentionally small, uses a classic
Windows 2000-style theme, and is available in ten languages.

## Download editions

| Edition | Installation | Administrator | Hardware writes | Status |
|---|---|---:|---:|---|
| Portable Monitor | Extract the ZIP and run | No | No | Ready for public testing |
| Setup Monitor | Standard setup wizard; defaults to `C:\Program Files\SurfaceFanControl` | Yes | No | Ready for public testing |
| Setup / Full Control | Privileged setup with hardware backend | Yes | Surface Pro (2013) backend | Public release blocked until the WinRing0 dependency is replaced |

Download the current portable ZIP or standard Windows setup from
[GitHub Releases](https://github.com/Allgamerhs/SurfaceFanControl/releases/tag/v1.8.0).
The exact portable ZIP was scanned by VirusTotal with **0/67 detections**; see the
[VirusTotal report](https://www.virustotal.com/gui/file/727ca12b3e8da508a466e6d5f91cf875ed96cd5d8ac4dc48711009f7b6f2528b/detection).
The ZIP SHA-256 is `727CA12B3E8DA508A466E6D5F91CF875ED96CD5D8AC4DC48711009F7B6F2528B`.
The exact setup executable was scanned with **4/71 heuristic detections**; see its
[VirusTotal report](https://www.virustotal.com/gui/file/321a7ac69221bf0655c92b651a552beab8c466010e43d14e5970c14bd75f0913/detection).
Microsoft, Bitdefender, ESET, Kaspersky, Malwarebytes, Avast, and AVG reported it as undetected.
The setup SHA-256 is `321A7AC69221BF0655C92B651A552BEAB8C466010E43D14E5970C14BD75F0913`.

### Türkçe hızlı başlangıç

- Portable sürüm: ZIP dosyasını yeni bir klasöre çıkarıp `SurfaceProFanControl.exe` dosyasını
  çalıştırın. Kurulum veya yönetici izni istemez ve sisteme sürücü/hizmet eklemez.
- Kurulumlu izleme sürümü: `SurfaceFanControl-Setup-v1.8.0.exe` dosyasını çalıştırın. Kurulum
  sihirbazı varsayılan olarak `C:\Program Files\SurfaceFanControl` klasörünü kullanır; klasörü
  değiştirme, masaüstü kısayolu ve Windows ile başlama seçeneklerini sunar. Program Files'a
  kurulum için yönetici onayı ister; sürücü veya hizmet kurmaz ve donanıma yazma yapmaz.
- İndirdiğiniz dosyanın SHA-256 değerini sürümdeki `SHA256SUMS.txt` ile karşılaştırın.

## Features

- Firmware AUTO and verified maximum modes on explicitly supported hardware.
- Fixed maximum, timed maximum, temperature curve, hysteresis, minimum dwell time, rapid-rise boost,
  AC/battery profiles, startup delay, and critical-temperature protection.
- Live temperature, RPM measurement, session minimum/maximum, trend, a 60-sample chart, and bounded
  CSV telemetry.
- Profiles that can be imported/exported as bounded JSON.
- Tray controls, global shortcuts, start-with-Windows, start minimized, always-on-top, and close to tray.
- Turkish, English, German, Italian, Spanish, Arabic, Simplified Chinese, Japanese, Russian, and Polish.
- Classic fixed-size `480×410` interface with a dedicated fan icon.

## Hardware boundary

The current write backend targets the original Microsoft Surface Pro model reported as
`Microsoft Corporation / Surface with Windows 8 Pro`. Before any write, the guard independently
requires exactly one ITE IT8519 revision 08 controller at PNPCFG base `0x2E`. Only firmware AUTO
(`XDATA 0x0F10=00`) and verified maximum (`01`) are allowlisted. Other computers remain monitoring-only.

Hardware writes run in a LocalSystem guard with a short heartbeat lease, named-pipe identity checks,
a dirty-state recovery marker, a bounded watchdog, and mandatory firmware-AUTO restoration. Device
serial numbers, computer names, SMBIOS UUIDs, local IP addresses, firmware images, and remote deployment
scripts are intentionally excluded from this public source tree.

## Security warning

The private engineering build historically used `WinRing0x64.sys`. Microsoft classifies WinRing0 as a
known vulnerable driver under CVE-2020-14979. The binary is therefore ignored by Git and a public
full-control release is blocked while that dependency remains. Antivirus or VirusTotal results do not
remove this risk. See [SECURITY.md](SECURITY.md).

## Build and test

Requirements: Windows 10/11 x64, .NET Framework 4.8 targeting pack, and a current .NET SDK.

```powershell
dotnet restore .\src\SurfaceProFanControl\SurfaceProFanControl.csproj
dotnet build .\src\SurfaceProFanControl\SurfaceProFanControl.csproj -c Release -p:DistributionFlavor=Portable
dotnet build .\tools\I2EcFanTest\I2EcFanTest.csproj -c Release
& .\tools\I2EcFanTest\bin\Release\net48\SurfaceProI2EcFanTest.exe --self-test
dotnet run --project .\tests\SurfaceProFanControl.Core.Tests\SurfaceProFanControl.Core.Tests.csproj -c Release
& .\packaging\New-ReleasePackages.ps1 -OutputDirectory .\artifacts\release-candidate -IncludeSetup
```

The public monitoring setup definition is in `packaging/SurfaceFanControl.Monitor.iss`. The legacy
full-control definition in `packaging/SurfaceFanControl.iss` is deliberately guarded and cannot be
treated as a public release while the known-vulnerable driver is present.

## License

Application source is available under the [MIT License](LICENSE). Third-party components retain their
own license terms. Contributions must follow [CONTRIBUTING.md](CONTRIBUTING.md).
