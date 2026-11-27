# Surface Fan Control 1.9.0

Surface Fan Control is an open-source Windows desktop app for monitoring temperature, CPU load,
battery level and AC power. Its public downloads display the readings available through Windows.
The source also includes a fan-control backend for the first-generation Surface Pro.

## Download editions

Both downloads run the same monitoring app on x64 Windows. They do not install a fan service or
kernel driver. Fan-mode controls and fan RPM measurement are disabled in these editions.

| Download | How to start |
|---|---|
| [Portable Monitor ZIP](https://github.com/Allgamerhs/SurfaceFanControl/releases/download/v1.9.0/SurfaceFanControl-Portable-v1.9.0.zip) | Extract the ZIP and run `SurfaceProFanControl.exe`. No installation or administrator rights required. |
| [Monitor Setup for Windows](https://github.com/Allgamerhs/SurfaceFanControl/releases/download/v1.9.0/SurfaceFanControl-Setup-v1.9.0.exe) | Run the installer. It defaults to `C:\Program Files\SurfaceFanControl` and offers installation folder, shortcut and startup options. |

Before running the download, compare its SHA-256 with the entry for that filename in
[SHA256SUMS.txt](https://github.com/Allgamerhs/SurfaceFanControl/releases/download/v1.9.0/SHA256SUMS.txt).

## Features

- View temperature history for the last minute and optionally log telemetry to a local CSV file.
  An expired temperature clears from the live display after 20 seconds without a new reading.
- Import and export settings profiles as JSON.
- Use the system tray, start with Windows, start minimized, keep the window on top, or close to tray.
- The interface is available in ten languages, including Turkish, English, German, Italian, Spanish,
  Arabic, Simplified Chinese, Japanese, Russian, and Polish.
- Classic fixed-size `480×410` interface with a dedicated fan icon.

## Hardware boundary

The full-control source supports firmware AUTO, fixed and timed maximum cooling, temperature curves,
AC/battery profiles, and critical-temperature protection. Its Status tab shows both measured fan
speeds and the time of the last successful reading. RPM values expire after 20 seconds. Distribution
of the full-control installer is blocked pending a replacement for its driver dependency.

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
