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
[VirusTotal report](https://www.virustotal.com/gui/file/649e48d440314364d2116727f0f6c7ebf9834414b5181bcc5ae3833c06a93282/detection).
The ZIP SHA-256 is `649E48D440314364D2116727F0F6C7EBF9834414B5181BCC5AE3833C06A93282`.
The exact setup executable was scanned with **4/71 heuristic detections**; see its
[VirusTotal report](https://www.virustotal.com/gui/file/d7660930ef64d0f28c738962aa4aa73ab0a85273f0793a4be0967e132c091713/detection).
Microsoft, Bitdefender, ESET, Kaspersky, Malwarebytes, Avast, and AVG reported it as undetected.
The setup SHA-256 is `D7660930EF64D0F28C738962AA4AA73AB0A85273F0793A4BE0967E132C091713`.

### Quick start

- Portable edition: extract the ZIP into a new folder and run `SurfaceProFanControl.exe`. It
  requires no installation or administrator rights and installs no driver or service.
- Setup edition: run `SurfaceFanControl-Setup-v1.8.0.exe`. The wizard defaults to
  `C:\Program Files\SurfaceFanControl` and lets the user select another folder, create a desktop
  shortcut, and enable start with Windows. Installing under Program Files requires administrator
  approval. The public setup installs no driver or service and performs no hardware writes.
- Compare the downloaded file's SHA-256 value with the release information before running it.

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

## Compatibility and support

Surface Fan Control is designed primarily for Microsoft Surface devices. The public monitoring
editions may start on other Windows computers, but this does not mean that their embedded controller,
temperature sensors, fan tachometer, or fan-control interface is supported. Hardware writes are enabled
only for a device that passes the project's exact model and controller allowlist. At present, the
verified write backend targets the original Microsoft Surface Pro reported as
`Microsoft Corporation / Surface with Windows 8 Pro`. All other devices remain monitoring-only.

If temperature or RPM values are unavailable, the fan does not react, or the application reports that
the hardware is unsupported, first confirm that you are using the latest release and compare your exact
Surface model with the compatibility information above. Do not install random kernel drivers, copy
drivers from another fan-control project, or force hardware writes intended for a different computer.

For help, open a [GitHub issue](https://github.com/Allgamerhs/SurfaceFanControl/issues). Diagnostic
logs are important: they show which operation failed and often make the difference between a guess and
a reproducible fix. Reproduce the problem once, note the approximate time, and attach the relevant files:

- application log: `%LOCALAPPDATA%\SurfaceProFanControl\surface-pro-fan-control.log`;
- fan-guard service log, when present: `%ProgramData%\SurfaceProFanControl\fan-guard.log`;
- telemetry history: `%LOCALAPPDATA%\SurfaceProFanControl\telemetry.csv`. Enable **Telemetry logging**
  in the application and let it run long enough to capture the problem before attaching this file.

Also include the following system and problem details:

- device manufacturer, exact Surface model, and generation;
- Windows edition, version, OS build, and architecture (`x64` or `ARM64`);
- BIOS/UEFI firmware version and processor model;
- Surface Fan Control version and whether you used the Setup or Portable edition;
- a clear description of the expected behavior and what actually happened;
- relevant screenshots, error messages, and reproducible steps;
- whether temperature and RPM readings appear in the application, and whether the device was on AC
  power or battery when the problem occurred.

Logs and system specifications are welcome, but before posting remove device serial numbers, computer
names, user names, IP addresses, SMBIOS UUIDs, access tokens, and any other private information. Do not
delete ordinary error messages, timestamps, model names, Windows build information, firmware versions,
temperature readings, or fan readings: those details are useful for diagnosis. Security vulnerabilities
should be reported through GitHub's private vulnerability reporting feature instead of a public issue.
Support for additional Surface generations depends on verified hardware documentation and safe testing
evidence; a request alone cannot make an unknown embedded-controller interface safe to write.

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
