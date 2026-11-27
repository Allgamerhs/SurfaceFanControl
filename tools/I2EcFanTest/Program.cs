using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SurfaceProFanControl.Core;
using SurfaceProFanControl.Shared;

namespace SurfaceProI2EcFanTest;

internal static class Program
{
    private const string ExpectedManufacturer = "Microsoft Corporation";
    private const string ExpectedModel = "Surface with Windows 8 Pro";

    private const int ExitUsage = 2;
    private const int ExitTargetMismatch = 10;
    private const int ExitNotElevated = 11;
    private const int ExitDecoderMismatch = 20;
    private const int ExitFingerprintMismatch = 30;
    private const int ExitControlResponseMissing = 40;
    private const int ExitPhysicalResponseMissing = 41;
    private const int ExitAutoRestoreFailed = 42;
    private const int ExitTestFailed = 43;
    private const int ExitAcPowerRequired = 44;
    private const int ExitUnexpected = 100;

    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
        {
            FanGuardService.RunService();
            return 0;
        }

        if (args.Length == 3 && string.Equals(args[0], "--guard-failsafe", StringComparison.OrdinalIgnoreCase))
        {
            return FanGuardFailsafe.Run(args[1], args[2]);
        }

        if (args.Length == 2 && string.Equals(args[0], "--verify-service-auto", StringComparison.OrdinalIgnoreCase))
        {
            return FanGuardDeploymentVerifier.Run(args[1]);
        }

        if (args.Length == 2 && string.Equals(args[0], "--authenticated-pipe-command", StringComparison.OrdinalIgnoreCase))
        {
            return FanGuardDeploymentVerifier.RunAuthenticatedCommand(args[1]);
        }

        if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            return SelfTests.Run();
        }

        if (args.Length == 1 && string.Equals(args[0], "--uninstall-services", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceRemoval.Run();
        }

        if (args.Length != 1 ||
            (!string.Equals(args[0], "--preflight", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(args[0], "--test-max", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(args[0], "--force-auto", StringComparison.OrdinalIgnoreCase)))
        {
            WriteError("USAGE", "Use --preflight, --test-max, --force-auto, --self-test, or an internal service command.", 0);
            return ExitUsage;
        }

        try
        {
            var target = TargetIdentity.Read();
            if (!target.IsExpected(ExpectedManufacturer, ExpectedModel))
            {
                WriteError("TARGET_MISMATCH", target.ToDisplayString(), 0);
                return ExitTargetMismatch;
            }

            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    WriteError("ADMIN_REQUIRED", "The fan test must run from an elevated process.", 0);
                    return ExitNotElevated;
                }
            }

            using (var mutex = new Mutex(false, @"Global\Access_ISABUS.HTP.Method"))
            {
                var acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(5), false);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (!acquired)
                    {
                        throw new TimeoutException("The global ISA/EC mutex could not be acquired.");
                    }

                    DriverTrust.VerifyRunningPinnedDriver();
                    using (var rawIo = WinRingPortIo.Open())
                    {
                        var identities = PnpScanner.FindIte8519(rawIo);
                        if (identities.Count != 1 || identities[0].BasePort != 0x2E || identities[0].Version != 0x08)
                        {
                            WriteError(
                                "DECODER_MISMATCH",
                                "Expected exactly one ITE 8519 revision 08 decoder at 0x002E.",
                                0);
                            return ExitDecoderMismatch;
                        }

                        using (var session = I2EcFanSession.Open(rawIo, identities[0].BasePort))
                        {
                            var controller = new FanController(session);
                            if (string.Equals(args[0], "--preflight", StringComparison.OrdinalIgnoreCase))
                            {
                                var snapshot = controller.Preflight(requireAutomaticMode: true);
                                WriteHeader(target, identities[0], "PREFLIGHT");
                                WriteSnapshot("BASELINE", snapshot);
                                Console.WriteLine("AUTO_MODE=VERIFIED");
                                Console.WriteLine("EC_MEMORY_WRITES=" + session.MemoryWriteCount);
                                return 0;
                            }

                            if (string.Equals(args[0], "--force-auto", StringComparison.OrdinalIgnoreCase))
                            {
                                controller.ValidateForRecovery();
                                try
                                {
                                    controller.ForceAutomaticMode();
                                }
                                catch (Exception ex)
                                {
                                    WriteError("AUTO_RESTORE_FAILED", ex.Message, session.MemoryWriteCount);
                                    return ExitAutoRestoreFailed;
                                }
                                WriteHeader(target, identities[0], "FORCE_AUTO");
                                Console.WriteLine("AUTO_MODE=RESTORED");
                                Console.WriteLine("FAN_MANUAL_0F10=0x00");
                                Console.WriteLine("EC_MEMORY_WRITES=" + session.MemoryWriteCount);
                                return 0;
                            }

                            FanTestResult result;
                            try
                            {
                                if (!PowerStatus.IsOnAcPower())
                                {
                                    WriteError("AC_POWER_REQUIRED", "The one-shot hardware test requires AC power.", 0);
                                    return ExitAcPowerRequired;
                                }
                                result = controller.RunMaximumTest(TimeSpan.FromMilliseconds(7500), 500);
                            }
                            catch (AutoRestoreException ex)
                            {
                                WriteError("AUTO_RESTORE_FAILED", ex.Message, session.MemoryWriteCount);
                                return ExitAutoRestoreFailed;
                            }
                            catch (ControlPlaneException ex)
                            {
                                WriteError("CONTROL_RESPONSE_MISSING", ex.Message, session.MemoryWriteCount);
                                return ExitControlResponseMissing;
                            }
                            catch (Exception ex)
                            {
                                WriteError("TEST_FAILED_AFTER_AUTO_RESTORE", ex.Message, session.MemoryWriteCount);
                                return ExitTestFailed;
                            }

                            WriteHeader(target, identities[0], "TEST_MAX");
                            WriteSnapshot("BASELINE", result.Baseline);
                            WriteSnapshot("PEAK", result.Peak);
                            Console.WriteLine("MAX_SINKS_B7=" + (result.ControlPlaneVerified ? "YES" : "NO"));
                            Console.WriteLine("PHYSICAL_FAN_RESPONSE=" + (result.PhysicalResponseVerified ? "YES" : "NO"));
                            Console.WriteLine("RESPONSIVE_STREAK=" + result.ResponsiveStreak);
                            Console.WriteLine("STRONG_COOLING_STREAK=" + result.StrongCoolingStreak);
                            Console.WriteLine("AUTO_MODE=RESTORED");
                            Console.WriteLine("FAN_MANUAL_0F10=0x00");
                            Console.WriteLine("EC_MEMORY_WRITES=" + session.MemoryWriteCount);

                            if (!result.ControlPlaneVerified)
                            {
                                Console.WriteLine("STATUS=CONTROL_RESPONSE_MISSING");
                                return ExitControlResponseMissing;
                            }

                            if (!result.PhysicalResponseVerified)
                            {
                                Console.WriteLine("STATUS=PHYSICAL_RESPONSE_MISSING");
                                return ExitPhysicalResponseMissing;
                            }

                            Console.WriteLine("STATUS=MAX_FAN_VERIFIED");
                            return 0;
                        }
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
        catch (FingerprintException ex)
        {
            WriteError("FINGERPRINT_MISMATCH", ex.Message, ex.MemoryWriteCount);
            return ExitFingerprintMismatch;
        }
        catch (Exception ex)
        {
            WriteError("UNEXPECTED", ex.GetType().Name + ": " + ex.Message, 0);
            return ExitUnexpected;
        }
    }

    private static void WriteHeader(TargetIdentity target, PnpIdentity identity, string mode)
    {
        Console.WriteLine("MODE=" + mode);
        Console.WriteLine("COMPUTER=" + target.ComputerName);
        Console.WriteLine("UUID=" + target.Uuid);
        Console.WriteLine("PNPCFG_BASE=0x{0:X4}", identity.BasePort);
        Console.WriteLine("CHIP_ID={0:X2}{1:X2}", identity.Id1, identity.Id2);
        Console.WriteLine("CHIP_VERSION=0x{0:X2}", identity.Version);
        Console.WriteLine("WRITE_ALLOWLIST=XDATA_0F10_VALUES_00_OR_01_ONLY");
    }

    private static void WriteSnapshot(string prefix, FanSnapshot snapshot)
    {
        Console.WriteLine(prefix + "_FAN0_RPM=" + snapshot.Reading.Fan0Rpm);
        Console.WriteLine(prefix + "_FAN0_DUTY=0x{0:X2}", snapshot.Reading.Fan0DutyRaw);
        Console.WriteLine(prefix + "_FAN1_RPM=" + snapshot.Reading.Fan1Rpm);
        Console.WriteLine(prefix + "_FAN1_DUTY=0x{0:X2}", snapshot.Reading.Fan1DutyRaw);
        Console.WriteLine(prefix + "_FAN0_COMMAND=0x{0:X2}", snapshot.Fan0Command);
        Console.WriteLine(prefix + "_FAN1_COMMAND=0x{0:X2}", snapshot.Fan1Command);
        if (snapshot.Reading.Thermal != null)
        {
            Console.WriteLine(prefix + "_TEMP_C=" + snapshot.Reading.Thermal.TemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture));
            Console.WriteLine(prefix + "_CRITICAL_C=" + snapshot.Reading.Thermal.CriticalTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture));
        }
    }

    private static void WriteError(string code, string message, int memoryWrites)
    {
        Console.Error.WriteLine("STATUS=ERROR");
        Console.Error.WriteLine("CODE=" + code);
        Console.Error.WriteLine("MESSAGE=" + message.Replace('\r', ' ').Replace('\n', ' '));
        Console.Error.WriteLine("EC_MEMORY_WRITES=" + memoryWrites);
    }
}

internal sealed class FanController
{
    private const byte ExpectedSpctrl1 = 0xC3;
    private const byte ExpectedBadrsel = 0x00;
    private const byte ExpectedDebugAuthorization = 0x00;
    private const byte MaximumTarget = 0xB7;
    private readonly I2EcFanSession _session;

    public FanController(I2EcFanSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public FanSnapshot Preflight(bool requireAutomaticMode)
    {
        ValidateControlFingerprint(requireAutomaticMode);

        var snapshot = ReadMedianSnapshot();
        if (!snapshot.Reading.IsPlausible() || snapshot.Reading.Thermal == null ||
            !snapshot.Reading.Thermal.IsPlausible())
        {
            throw new FingerprintException("Fan or thermal telemetry is not plausible.", _session.MemoryWriteCount);
        }

        var stopTemperature = Math.Min(82.0, snapshot.Reading.Thermal.CriticalTemperatureCelsius - 4.0);
        if (snapshot.Reading.Thermal.TemperatureCelsius >= stopTemperature)
        {
            throw new FingerprintException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Baseline temperature {0:0.0} C is at or above the {1:0.0} C one-shot test limit.",
                    snapshot.Reading.Thermal.TemperatureCelsius,
                    stopTemperature),
                _session.MemoryWriteCount);
        }

        return snapshot;
    }

    public void ValidateForRecovery()
    {
        ValidateControlFingerprint(requireAutomaticMode: false);
    }

    public FanGuardStatus ReadGuardStatus()
    {
        ValidateControlFingerprint(requireAutomaticMode: false);
        var mode = _session.ReadXData(0x0F10);
        var shadow = _session.ReadXData(0x0439);
        var snapshot = ReadMedianSnapshot();
        return new FanGuardStatus(mode, shadow, snapshot);
    }

    public FanGuardStatus EnterMaximumMode()
    {
        ValidateControlFingerprint(requireAutomaticMode: true);
        var baseline = ReadMedianSnapshot();
        if (!baseline.Reading.IsPlausible() || baseline.Reading.Thermal == null ||
            !baseline.Reading.Thermal.IsPlausible())
        {
            throw new FingerprintException("Fan or thermal telemetry is not plausible.", _session.MemoryWriteCount);
        }

        try
        {
            _session.WriteFanMode(0x01);
            VerifyFanMode(0x01);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var controlAcknowledged = 0;
            var physicalAcknowledged = 0;
            var baselineAlreadyStrong = FanHealthAssessment.IsStrongCooling(baseline.Reading);
            FanSnapshot? latest = null;
            do
            {
                Thread.Sleep(250);
                var mode = _session.ReadXData(0x0F10);
                var shadow = _session.ReadXData(0x0439);
                latest = ReadMedianSnapshot();
                controlAcknowledged = mode == 0x01 && shadow == 0x01 && latest.AllTargetsEqual(MaximumTarget)
                    ? controlAcknowledged + 1
                    : 0;
                var physicalResponse = baselineAlreadyStrong
                    ? FanHealthAssessment.IsStrongCooling(latest.Reading)
                    : FanHealthAssessment.NextResponsiveStreak(0, baseline.Reading, latest.Reading) == 1 ||
                      FanHealthAssessment.IsStrongCooling(latest.Reading);
                physicalAcknowledged = physicalResponse ? physicalAcknowledged + 1 : 0;
                if (controlAcknowledged >= 2 && physicalAcknowledged >= 2)
                {
                    return new FanGuardStatus(mode, shadow, latest);
                }
            }
            while (DateTime.UtcNow < deadline);

            throw new ControlPlaneException(
                "Firmware maximum mode or the physical response of both fans was not verified within five seconds.");
        }
        catch
        {
            ForceAutomaticMode();
            throw;
        }
    }

    public FanModeStatus EnterMaximumOneShot()
    {
        ValidateControlFingerprint(requireAutomaticMode: true);
        try
        {
            _session.WriteFanMode(0x01);
            VerifyFanMode(0x01);
            VerifyModeShadow(0x01);
            _session.CommitVerifiedMaximum();
            return new FanModeStatus(0x01, 0x01);
        }
        catch
        {
            ForceAutomaticMode();
            throw;
        }
    }

    private void ValidateControlFingerprint(bool requireAutomaticMode)
    {
        var spctrl1 = ReadStableControl(0x200D, "SPCTRL1");
        var badrsel = ReadStableControl(0x200A, "BADRSEL");
        var authorization = ReadStableControl(0x0383, "DEBUG_AUTH");
        var mode = ReadStableControl(0x0F10, "FAN_MANUAL");
        var modeShadow = ReadStableControl(0x0439, "FAN_MODE_SHADOW");

        if (spctrl1 != ExpectedSpctrl1 || badrsel != ExpectedBadrsel || authorization != ExpectedDebugAuthorization)
        {
            throw new FingerprintException(
                string.Format(
                    "Expected SPCTRL1/BADRSEL/AUTH C3/00/00, got {0:X2}/{1:X2}/{2:X2}.",
                    spctrl1,
                    badrsel,
                    authorization),
                _session.MemoryWriteCount);
        }

        if (mode != 0x00 && mode != 0x01)
        {
            throw new FingerprintException(
                string.Format("Unexpected fan manual-mode value 0x{0:X2}.", mode),
                _session.MemoryWriteCount);
        }

        if (modeShadow != 0x00 && modeShadow != 0x01)
        {
            throw new FingerprintException(
                string.Format("Unexpected fan-mode shadow value 0x{0:X2}.", modeShadow),
                _session.MemoryWriteCount);
        }

        if (requireAutomaticMode && (mode != 0x00 || modeShadow != 0x00))
        {
            throw new FingerprintException(
                "The first test requires acknowledged automatic mode (0x0F10=00 and shadow 0x0439=00). Run --force-auto first.",
                _session.MemoryWriteCount);
        }
    }

    public FanTestResult RunMaximumTest(TimeSpan maximumDuration, int sampleIntervalMilliseconds)
    {
        if (maximumDuration <= TimeSpan.Zero || maximumDuration > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }
        if (sampleIntervalMilliseconds < 1 || sampleIntervalMilliseconds > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleIntervalMilliseconds));
        }

        var baseline = Preflight(requireAutomaticMode: true);
        var peak = baseline;
        var maxSinkStreak = 0;
        var responsiveStreak = 0;
        var strongCoolingStreak = 0;
        var zeroFanStreak = 0;
        Exception? testFailure = null;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _session.WriteFanMode(0x01);
            VerifyFanMode(0x01);
            while (stopwatch.Elapsed < maximumDuration)
            {
                var mode = _session.ReadXData(0x0F10);
                if (mode != 0x01)
                {
                    throw new IOException(string.Format("Manual-mode readback changed unexpectedly to 0x{0:X2}.", mode));
                }
                var modeShadow = _session.ReadXData(0x0439);
                if (modeShadow != 0x00 && modeShadow != 0x01)
                {
                    throw new InvalidDataException(
                        string.Format("Fan-mode shadow became invalid: 0x{0:X2}.", modeShadow));
                }

                var sample = ReadMedianSnapshot();
                if (!sample.Reading.IsPlausible() || sample.Reading.Thermal == null ||
                    !sample.Reading.Thermal.IsPlausible())
                {
                    throw new InvalidDataException("Fan or thermal telemetry became implausible during the test.");
                }

                var stopTemperature = Math.Min(82.0, sample.Reading.Thermal.CriticalTemperatureCelsius - 4.0);
                if (sample.Reading.Thermal.TemperatureCelsius >= stopTemperature ||
                    sample.Reading.Thermal.TemperatureCelsius >=
                    baseline.Reading.Thermal!.TemperatureCelsius + 2.0)
                {
                    throw new InvalidDataException("The thermal stop condition was reached during the maximum-fan test.");
                }
                if (sample.Reading.Fan0Rpm > 10500 || sample.Reading.Fan1Rpm > 10500)
                {
                    throw new InvalidDataException("An RPM sample exceeded the 10500 RPM validation ceiling.");
                }

                maxSinkStreak = modeShadow == 0x01 && sample.AllTargetsEqual(MaximumTarget)
                    ? maxSinkStreak + 1
                    : 0;
                responsiveStreak = FanHealthAssessment.NextResponsiveStreak(responsiveStreak, baseline.Reading, sample.Reading);
                strongCoolingStreak = FanHealthAssessment.NextStrongCoolingStreak(strongCoolingStreak, sample.Reading);
                zeroFanStreak = FanHealthAssessment.NextZeroStreak(zeroFanStreak, sample.Reading);
                peak = FanSnapshot.Peak(peak, sample);

                Console.WriteLine(
                    "SAMPLE_MS={0};MODE=01;SHADOW={1:X2};TARGETS={2:X2}/{3:X2}/{4:X2}/{5:X2};RPM={6}/{7};TEMP={8}",
                    stopwatch.ElapsedMilliseconds,
                    modeShadow,
                    sample.Fan0Command,
                    sample.Reading.Fan0DutyRaw,
                    sample.Fan1Command,
                    sample.Reading.Fan1DutyRaw,
                    sample.Reading.Fan0Rpm,
                    sample.Reading.Fan1Rpm,
                    sample.Reading.Thermal.TemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture));

                if (stopwatch.Elapsed >= TimeSpan.FromSeconds(2) && maxSinkStreak < 2)
                {
                    throw new ControlPlaneException(
                        "Firmware did not acknowledge manual mode and hold all four maximum targets at B7 within two seconds.");
                }
                if (stopwatch.Elapsed >= TimeSpan.FromSeconds(2) && zeroFanStreak >= 2)
                {
                    throw new InvalidDataException("At least one fan remained at 0 RPM after the two-second spin-up window.");
                }

                var remaining = maximumDuration - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    Thread.Sleep((int)Math.Min(sampleIntervalMilliseconds, Math.Max(1, remaining.TotalMilliseconds)));
                }
            }
        }
        catch (Exception ex)
        {
            testFailure = ex;
        }

        try
        {
            ForceAutomaticMode();
        }
        catch (Exception rollbackFailure)
        {
            if (testFailure != null)
            {
                throw new AutoRestoreException(
                    "The fan test failed and automatic-mode restoration also failed.",
                    new AggregateException(testFailure, rollbackFailure));
            }

            throw new AutoRestoreException("Automatic-mode restoration failed.", rollbackFailure);
        }

        if (testFailure != null)
        {
            ExceptionDispatchInfo.Capture(testFailure).Throw();
        }

        var controlVerified = maxSinkStreak >= 2;
        var baselineAlreadyStrong = FanHealthAssessment.IsStrongCooling(baseline.Reading);
        var physicalVerified = baselineAlreadyStrong
            ? strongCoolingStreak >= 3
            : responsiveStreak >= 3 || strongCoolingStreak >= 3;

        return new FanTestResult(
            baseline,
            peak,
            controlVerified,
            physicalVerified,
            responsiveStreak,
            strongCoolingStreak);
    }

    public void ForceAutomaticMode()
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _session.WriteFanMode(0x00);
                VerifyFanMode(0x00);
                VerifyModeShadow(0x00);
                _session.MarkAutomaticModeVerified();
                return;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                Thread.Sleep(50);
            }
        }

        throw new IOException("Automatic mode could not be written and verified in three attempts.", lastFailure);
    }

    public FanModeStatus ForceAutomaticModeAndReport()
    {
        ForceAutomaticMode();
        return new FanModeStatus(0x00, 0x00);
    }

    private byte ReadStableControl(ushort address, string name)
    {
        const int maximumReads = 8;
        var previous = _session.ReadXData(address);
        for (var attempt = 1; attempt < maximumReads; attempt++)
        {
            var current = _session.ReadXData(address);
            if (previous == current)
            {
                return current;
            }
            previous = current;
            Thread.Sleep(1);
        }

        throw new FingerprintException(
            string.Format("{0} did not produce two consecutive equal reads within {1} samples.",
                name,
                maximumReads),
            _session.MemoryWriteCount);
    }

    private void VerifyFanMode(byte expected)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var first = _session.ReadXData(0x0F10);
            var second = _session.ReadXData(0x0F10);
            if (first == expected && second == expected)
            {
                return;
            }
            Thread.Sleep(20);
        }

        throw new IOException(string.Format("Fan mode 0x{0:X2} was not verified twice.", expected));
    }

    private void VerifyModeShadow(byte expected)
    {
        var consecutive = 0;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var value = _session.ReadXData(0x0439);
            consecutive = value == expected ? consecutive + 1 : 0;
            if (consecutive >= 2)
            {
                return;
            }
            Thread.Sleep(50);
        }

        throw new IOException(string.Format("Firmware fan-mode shadow did not acknowledge 0x{0:X2}.", expected));
    }

    private FanSnapshot ReadCoherentSnapshot()
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var fan0Command = _session.ReadXData(0x1808);
                var fan0Rpm = ReadStableRpm(0x010D, 0x010E);
                var fan0Duty = _session.ReadXData(0x010F);
                var fan1Command = _session.ReadXData(0x1809);
                var fan1Rpm = ReadStableRpm(0x0110, 0x0111);
                var fan1Duty = _session.ReadXData(0x0112);
                var temperature = _session.ReadXData(0x0114);
                var critical = _session.ReadXData(0x0115);

                if (fan0Command != fan0Duty || fan1Command != fan1Duty)
                {
                    throw new InvalidDataException("Fan command/mirror pairs changed during the snapshot.");
                }

                return new FanSnapshot(
                    new EcFanReading(
                        fan0Rpm,
                        fan0Duty,
                        fan1Rpm,
                        fan1Duty,
                        EcThermalReading.Decode(temperature, critical)),
                    fan0Command,
                    fan1Command);
            }
            catch (Exception ex) when (!(ex is FingerprintException))
            {
                lastFailure = ex;
                Thread.Sleep(10);
            }
        }

        throw new InvalidDataException("A coherent fan snapshot could not be read in eight attempts.", lastFailure);
    }

    private FanSnapshot ReadMedianSnapshot()
    {
        return FanSnapshot.Median(
            ReadCoherentSnapshot(),
            ReadCoherentSnapshot(),
            ReadCoherentSnapshot());
    }

    private int ReadStableRpm(ushort highAddress, ushort lowAddress)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var firstHigh = _session.ReadXData(highAddress);
            var low = _session.ReadXData(lowAddress);
            var secondHigh = _session.ReadXData(highAddress);
            if (firstHigh == secondHigh)
            {
                return (firstHigh << 8) | low;
            }
        }

        throw new InvalidDataException("An RPM high byte changed in five consecutive reads.");
    }
}

internal sealed class I2EcFanSession : IDisposable
{
    private const byte Depth2AddressIndex = 0x2E;
    private const byte Depth2DataIndex = 0x2F;
    private const byte I2EcAddressLow = 0x10;
    private const byte I2EcAddressHigh = 0x11;
    private const byte I2EcData = 0x12;
    private const int SelectorAttempts = 8;

    private readonly GuardedFanPortIo _io;
    private readonly ushort _basePort;
    private readonly ushort _dataPort;
    private bool _manualModeMayBeActive;
    private bool _disposed;

    private I2EcFanSession(IPortIo rawIo, ushort basePort)
    {
        if (basePort != 0x2E)
        {
            throw new ArgumentOutOfRangeException(nameof(basePort), "The write tool is locked to base port 0x002E.");
        }
        _basePort = basePort;
        _dataPort = checked((ushort)(basePort + 1));
        _io = new GuardedFanPortIo(rawIo, basePort);
    }

    public int MemoryWriteCount => _io.MemoryWriteCount;

    public static I2EcFanSession Open(IPortIo rawIo, ushort basePort)
    {
        return new I2EcFanSession(rawIo, basePort);
    }

    public byte ReadXData(ushort address)
    {
        ThrowIfDisposed();
        WriteDepth2(I2EcAddressHigh, (byte)(address >> 8));
        WriteDepth2(I2EcAddressLow, (byte)address);
        SelectDepth2Address(I2EcData);
        return ReadConfig(Depth2DataIndex);
    }

    public void WriteFanMode(byte value)
    {
        ThrowIfDisposed();
        if (value != 0x00 && value != 0x01)
        {
            throw new InvalidOperationException("Only fan mode values 00 (auto) and 01 (firmware maximum) are allowed.");
        }

        if (value == 0x01)
        {
            // Arm cleanup before the IOCTL: a driver may perform the port write and still return an error.
            _manualModeMayBeActive = true;
        }

        WriteDepth2(I2EcAddressHigh, 0x0F);
        WriteDepth2(I2EcAddressLow, 0x10);
        SelectDepth2Address(I2EcData);
        _io.AuthorizeOneFanModeWrite(value);
        try
        {
            WriteConfig(Depth2DataIndex, value);
        }
        finally
        {
            // A selector/driver failure can occur after authorization but before
            // the guarded data-port write consumes it. Never let that one-shot
            // token block the mandatory AUTO fallback.
            _io.CancelPendingFanModeWriteAuthorization();
        }
    }

    public void MarkAutomaticModeVerified()
    {
        ThrowIfDisposed();
        _manualModeMayBeActive = false;
    }

    public void CommitVerifiedMaximum()
    {
        ThrowIfDisposed();
        _manualModeMayBeActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var failures = new List<Exception>();
        if (_manualModeMayBeActive)
        {
            TryRestore(
                true,
                () =>
                {
                    WriteFanMode(0x00);
                    var first = ReadXData(0x0F10);
                    var second = ReadXData(0x0F10);
                    if (first != 0 || second != 0)
                    {
                        throw new IOException("Dispose fallback could not verify automatic mode.");
                    }
                    var shadowStable = false;
                    for (var attempt = 0; attempt < 30; attempt++)
                    {
                        var shadowFirst = ReadXData(0x0439);
                        var shadowSecond = ReadXData(0x0439);
                        if (shadowFirst == 0 && shadowSecond == 0)
                        {
                            shadowStable = true;
                            break;
                        }
                        Thread.Sleep(50);
                    }
                    if (!shadowStable)
                    {
                        throw new IOException("Dispose fallback could not verify automatic-mode ownership.");
                    }
                    MarkAutomaticModeVerified();
                },
                "automatic fan mode",
                failures);
        }

        _disposed = true;
        if (failures.Count > 0)
        {
            throw new AggregateException("Fan-session cleanup failed.", failures);
        }
    }

    private static void TryRestore(bool required, Action action, string name, ICollection<Exception> failures)
    {
        if (!required)
        {
            return;
        }
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException("Failed to restore " + name + ".", ex));
        }
    }

    private byte ReadConfig(byte index)
    {
        SelectOuterIndex(index);
        return _io.ReadByte(_dataPort);
    }

    private void WriteConfig(byte index, byte value)
    {
        SelectOuterIndex(index);
        _io.WriteByte(_dataPort, value);
    }

    private byte ReadDepth2(byte address)
    {
        SelectDepth2Address(address);
        return ReadConfig(Depth2DataIndex);
    }

    private void WriteDepth2(byte address, byte value)
    {
        if (address != I2EcAddressLow && address != I2EcAddressHigh)
        {
            throw new InvalidOperationException("Only the fixed I2EC high/low latches may be written.");
        }

        byte observed = 0;
        for (var attempt = 0; attempt < SelectorAttempts; attempt++)
        {
            SelectDepth2Address(address);
            WriteConfig(Depth2DataIndex, value);
            SelectDepth2Address(address);
            observed = ReadConfig(Depth2DataIndex);
            if (observed == value)
            {
                return;
            }
            Thread.Sleep(1);
        }

        throw new IOException(string.Format(
            "I2EC latch 0x{0:X2} did not retain 0x{1:X2}; last read 0x{2:X2}.",
            address,
            value,
            observed));
    }

    private void SelectDepth2Address(byte address)
    {
        byte observed = 0;
        for (var attempt = 0; attempt < SelectorAttempts; attempt++)
        {
            WriteConfig(Depth2AddressIndex, address);
            observed = ReadConfig(Depth2AddressIndex);
            if (observed == address)
            {
                return;
            }
            Thread.Sleep(1);
        }

        throw new IOException(string.Format(
            "Depth-2 selector did not retain 0x{0:X2}; last read 0x{1:X2}.",
            address,
            observed));
    }

    private void SelectOuterIndex(byte index)
    {
        byte observed = 0;
        for (var attempt = 0; attempt < SelectorAttempts; attempt++)
        {
            _io.WriteByte(_basePort, index);
            observed = _io.ReadByte(_basePort);
            if (observed == index)
            {
                return;
            }
            Thread.Sleep(1);
        }

        throw new IOException(string.Format(
            "Outer PNPCFG index did not retain 0x{0:X2}; last echo 0x{1:X2}.",
            index,
            observed));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(I2EcFanSession));
        }
    }
}

internal sealed class GuardedFanPortIo
{
    private readonly IPortIo _inner;
    private readonly ushort _basePort;
    private readonly ushort _dataPort;
    private byte? _outerIndex;
    private byte? _depth2Address;
    private byte? _highAddress;
    private byte? _lowAddress;
    private byte? _authorizedFanModeValue;

    public GuardedFanPortIo(IPortIo inner, ushort basePort)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _basePort = basePort;
        _dataPort = checked((ushort)(basePort + 1));
    }

    public int MemoryWriteCount { get; private set; }

    public void AuthorizeOneFanModeWrite(byte value)
    {
        if (value != 0x00 && value != 0x01)
        {
            throw new InvalidOperationException("The write authorization accepts only values 00 or 01.");
        }
        if (_highAddress != 0x0F || _lowAddress != 0x10 || _depth2Address != 0x12)
        {
            throw new InvalidOperationException("The write authorization is locked to XDATA 0x0F10.");
        }
        if (_authorizedFanModeValue.HasValue)
        {
            throw new InvalidOperationException("A fan-mode write authorization is already pending.");
        }
        _authorizedFanModeValue = value;
    }

    public void CancelPendingFanModeWriteAuthorization()
    {
        _authorizedFanModeValue = null;
    }

    public byte ReadByte(ushort port)
    {
        EnsureAllowedPort(port);
        var value = _inner.ReadByte(port);
        if (port == _basePort)
        {
            _outerIndex = value;
        }
        else if (_outerIndex == 0x2E)
        {
            _depth2Address = value;
        }
        else if (_outerIndex == 0x2F && _depth2Address == 0x10)
        {
            _lowAddress = value;
        }
        else if (_outerIndex == 0x2F && _depth2Address == 0x11)
        {
            _highAddress = value;
        }
        return value;
    }

    public void WriteByte(ushort port, byte value)
    {
        EnsureAllowedPort(port);
        if (port == _basePort)
        {
            _inner.WriteByte(port, value);
            _outerIndex = value;
            return;
        }

        if (_outerIndex == 0x2E)
        {
            _inner.WriteByte(port, value);
            _depth2Address = value;
            return;
        }

        if (_outerIndex == 0x2F && _depth2Address == 0x10)
        {
            _inner.WriteByte(port, value);
            _lowAddress = value;
            return;
        }

        if (_outerIndex == 0x2F && _depth2Address == 0x11)
        {
            _inner.WriteByte(port, value);
            _highAddress = value;
            return;
        }

        if (_outerIndex == 0x2F && _depth2Address == 0x12 &&
            _highAddress == 0x0F && _lowAddress == 0x10 &&
            _authorizedFanModeValue == value && (value == 0x00 || value == 0x01))
        {
            _authorizedFanModeValue = null;
            _inner.WriteByte(port, value);
            MemoryWriteCount++;
            return;
        }

        _authorizedFanModeValue = null;
        throw new InvalidOperationException(
            string.Format(
                "Blocked data-port write: address=0x{0}{1}, value=0x{2:X2}.",
                _highAddress.HasValue ? _highAddress.Value.ToString("X2") : "--",
                _lowAddress.HasValue ? _lowAddress.Value.ToString("X2") : "--",
                value));
    }

    private void EnsureAllowedPort(ushort port)
    {
        if (port != _basePort && port != _dataPort)
        {
            throw new InvalidOperationException(string.Format("Port 0x{0:X4} is outside the 0x2E/0x2F allow-list.", port));
        }
    }
}

internal static class PnpScanner
{
    private static readonly ushort[] CandidateBases = { 0x2E, 0x4E };

    public static PnpIdentity ReadExpectedAt2E(IPortIo io)
    {
        const ushort basePort = 0x2E;
        var id1 = ReadWithEcho(io, basePort, 0x20, out var echo1);
        var id2 = ReadWithEcho(io, basePort, 0x21, out var echo2);
        var version = ReadWithEcho(io, basePort, 0x22, out var echo3);
        return new PnpIdentity(
            basePort,
            id1,
            id2,
            version,
            echo1 == 0x20 && echo2 == 0x21 && echo3 == 0x22);
    }

    public static IReadOnlyList<PnpIdentity> FindIte8519(IPortIo io)
    {
        var result = new List<PnpIdentity>();
        foreach (var basePort in CandidateBases)
        {
            var savedIndex = io.ReadByte(basePort);
            try
            {
                var id1 = ReadWithEcho(io, basePort, 0x20, out var echo1);
                var id2 = ReadWithEcho(io, basePort, 0x21, out var echo2);
                var version = ReadWithEcho(io, basePort, 0x22, out var echo3);
                var identity = new PnpIdentity(
                    basePort,
                    id1,
                    id2,
                    version,
                    echo1 == 0x20 && echo2 == 0x21 && echo3 == 0x22);
                if (identity.IsIte8519)
                {
                    result.Add(identity);
                }
            }
            finally
            {
                io.WriteByte(basePort, savedIndex);
            }
        }
        return result;
    }

    private static byte ReadWithEcho(IPortIo io, ushort basePort, byte index, out byte echo)
    {
        const int maximumReads = 8;
        var dataPort = checked((ushort)(basePort + 1));
        byte previous = 0;
        var previousValid = false;
        for (var attempt = 0; attempt < maximumReads; attempt++)
        {
            io.WriteByte(basePort, index);
            var currentEcho = io.ReadByte(basePort);
            var current = io.ReadByte(dataPort);
            if (currentEcho == index)
            {
                if (previousValid && current == previous)
                {
                    echo = currentEcho;
                    return current;
                }
                previous = current;
                previousValid = true;
            }
            else
            {
                previousValid = false;
            }
            Thread.Sleep(1);
        }

        echo = 0xFF;
        return previous;
    }
}

internal static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    public static bool IsOnAcPower()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemPowerStatus failed.");
        }
        return status.AcLineStatus == 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}

internal static class DriverTrust
{
    private const string ExpectedSha256 = "11BD2C9F9E2397C9A16E0990E4ED2CF0679498FE0FD418A3DFDAC60B5C160EE5";
    private static readonly string InstalledDriver = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SurfaceFanControl", "Privileged", "WinRing0x64.sys");
    private static readonly string LegacyDriver = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "SurfaceProFanControl", "WinRing0x64.sys");

    public static void VerifyRunningPinnedDriver()
    {
        var path = NormalizeDriverPath(FanGuardPipeIdentity.ReadRunningKernelDriverPath("WinRing0_1_2_0"));
        if (!string.Equals(path, InstalledDriver, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, LegacyDriver, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The running driver is outside the supported installation directories.");
        }

        using (var stream = File.OpenRead(path))
        using (var sha256 = SHA256.Create())
        {
            var hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            if (!string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The running WinRing0 driver SHA-256 value is not pinned.");
            }
        }
    }

    private static string NormalizeDriverPath(string? rawPath)
    {
        var path = Environment.ExpandEnvironmentVariables((rawPath ?? string.Empty).Trim().Trim('"'));
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = path.Substring(4);
        }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path.Substring(4);
        }
        return path;
    }
}

internal interface IPortIo : IDisposable
{
    byte ReadByte(ushort port);
    void WriteByte(ushort port, byte value);
}

internal sealed class WinRingPortIo : IPortIo
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint IoctlReadIoPortByte = 0x9C4060CC;
    private const uint IoctlWriteIoPortByte = 0x9C40A0D8;
    private readonly SafeFileHandle _device;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WritePortInput
    {
        public uint Port;
        public byte Value;
    }

    private WinRingPortIo(SafeFileHandle device)
    {
        _device = device;
    }

    public static WinRingPortIo Open()
    {
        var device = CreateFile(
            @"\\.\WinRing0_1_2_0",
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (device.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            device.Dispose();
            throw new Win32Exception(error, "The temporary WinRing0 device could not be opened.");
        }
        return new WinRingPortIo(device);
    }

    public byte ReadByte(ushort port)
    {
        uint input = port;
        if (!DeviceIoControl(
                _device,
                IoctlReadIoPortByte,
                ref input,
                sizeof(uint),
                out var output,
                sizeof(uint),
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), string.Format("Port 0x{0:X4} read failed.", port));
        }
        return (byte)output;
    }

    public void WriteByte(ushort port, byte value)
    {
        var input = new WritePortInput { Port = port, Value = value };
        if (!DeviceIoControl(
                _device,
                IoctlWriteIoPortByte,
                ref input,
                Marshal.SizeOf<WritePortInput>(),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), string.Format("Port 0x{0:X4} write failed.", port));
        }
    }

    public void Dispose()
    {
        _device.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        ref uint input,
        int inputSize,
        out uint output,
        int outputSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        ref WritePortInput input,
        int inputSize,
        IntPtr output,
        int outputSize,
        out int bytesReturned,
        IntPtr overlapped);
}

internal sealed class TargetIdentity
{
    private TargetIdentity(string computerName, string manufacturer, string model, string uuid)
    {
        ComputerName = computerName;
        Manufacturer = manufacturer;
        Model = model;
        Uuid = uuid;
    }

    public string ComputerName { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public string Uuid { get; }

    public static TargetIdentity Read()
    {
        return new TargetIdentity(
            Environment.MachineName,
            ReadFirst("Win32_ComputerSystem", "Manufacturer"),
            ReadFirst("Win32_ComputerSystem", "Model"),
            ReadFirst("Win32_ComputerSystemProduct", "UUID"));
    }

    public bool IsExpected(string manufacturer, string model)
    {
        return Equal(Manufacturer, manufacturer) && Equal(Model, model);
    }

    public string ToDisplayString()
    {
        return string.Format("Computer={0}; Manufacturer={1}; Model={2}; UUID={3}", ComputerName, Manufacturer, Model, Uuid);
    }

    private static string ReadFirst(string className, string property)
    {
        using (var searcher = new ManagementObjectSearcher(
                   "root\\cimv2",
                   string.Format("SELECT {0} FROM {1}", property, className)))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    return Convert.ToString(item[property])?.Trim() ?? string.Empty;
                }
            }
        }
        return string.Empty;
    }

    private static bool Equal(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class PnpIdentity
{
    public PnpIdentity(ushort basePort, byte id1, byte id2, byte version, bool echoesValid)
    {
        BasePort = basePort;
        Id1 = id1;
        Id2 = id2;
        Version = version;
        EchoesValid = echoesValid;
    }

    public ushort BasePort { get; }
    public byte Id1 { get; }
    public byte Id2 { get; }
    public byte Version { get; }
    public bool EchoesValid { get; }
    public bool IsIte8519 => EchoesValid && Id1 == 0x85 && Id2 == 0x19;
}

internal sealed class FanSnapshot
{
    public FanSnapshot(EcFanReading reading, byte fan0Command, byte fan1Command)
    {
        Reading = reading ?? throw new ArgumentNullException(nameof(reading));
        Fan0Command = fan0Command;
        Fan1Command = fan1Command;
    }

    public EcFanReading Reading { get; }
    public byte Fan0Command { get; }
    public byte Fan1Command { get; }

    public bool AllTargetsEqual(byte expected)
    {
        return Fan0Command == expected && Reading.Fan0DutyRaw == expected &&
               Fan1Command == expected && Reading.Fan1DutyRaw == expected;
    }

    public static FanSnapshot Peak(FanSnapshot first, FanSnapshot second)
    {
        var firstThermal = first.Reading.Thermal;
        var secondThermal = second.Reading.Thermal;
        EcThermalReading? thermal = null;
        if (firstThermal != null && secondThermal != null)
        {
            thermal = new EcThermalReading(
                Math.Max(firstThermal.TemperatureCelsius, secondThermal.TemperatureCelsius),
                Math.Min(firstThermal.CriticalTemperatureCelsius, secondThermal.CriticalTemperatureCelsius));
        }

        return new FanSnapshot(
            new EcFanReading(
                Math.Max(first.Reading.Fan0Rpm, second.Reading.Fan0Rpm),
                Math.Max(first.Reading.Fan0DutyRaw, second.Reading.Fan0DutyRaw),
                Math.Max(first.Reading.Fan1Rpm, second.Reading.Fan1Rpm),
                Math.Max(first.Reading.Fan1DutyRaw, second.Reading.Fan1DutyRaw),
                thermal),
            Math.Max(first.Fan0Command, second.Fan0Command),
            Math.Max(first.Fan1Command, second.Fan1Command));
    }

    public static FanSnapshot Median(FanSnapshot first, FanSnapshot second, FanSnapshot third)
    {
        if (first == null) throw new ArgumentNullException(nameof(first));
        if (second == null) throw new ArgumentNullException(nameof(second));
        if (third == null) throw new ArgumentNullException(nameof(third));
        return new FanSnapshot(
            EcFanReading.Median(first.Reading, second.Reading, third.Reading),
            (byte)MedianValue(first.Fan0Command, second.Fan0Command, third.Fan0Command),
            (byte)MedianValue(first.Fan1Command, second.Fan1Command, third.Fan1Command));
    }

    private static int MedianValue(int first, int second, int third)
    {
        return Math.Max(Math.Min(first, second), Math.Min(Math.Max(first, second), third));
    }
}

internal sealed class FanTestResult
{
    public FanTestResult(
        FanSnapshot baseline,
        FanSnapshot peak,
        bool controlPlaneVerified,
        bool physicalResponseVerified,
        int responsiveStreak,
        int strongCoolingStreak)
    {
        Baseline = baseline;
        Peak = peak;
        ControlPlaneVerified = controlPlaneVerified;
        PhysicalResponseVerified = physicalResponseVerified;
        ResponsiveStreak = responsiveStreak;
        StrongCoolingStreak = strongCoolingStreak;
    }

    public FanSnapshot Baseline { get; }
    public FanSnapshot Peak { get; }
    public bool ControlPlaneVerified { get; }
    public bool PhysicalResponseVerified { get; }
    public int ResponsiveStreak { get; }
    public int StrongCoolingStreak { get; }
}

internal sealed class FanGuardStatus
{
    public FanGuardStatus(byte mode, byte modeShadow, FanSnapshot snapshot)
    {
        Mode = mode;
        ModeShadow = modeShadow;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public byte Mode { get; }
    public byte ModeShadow { get; }
    public FanSnapshot Snapshot { get; }
    public bool IsMaximum => Mode == 0x01 && ModeShadow == 0x01 && Snapshot.AllTargetsEqual(0xB7);
    public bool IsAutomatic => Mode == 0x00 && ModeShadow == 0x00;
}

internal sealed class FanModeStatus
{
    public FanModeStatus(byte mode, byte modeShadow)
    {
        Mode = mode;
        ModeShadow = modeShadow;
    }

    public byte Mode { get; }
    public byte ModeShadow { get; }
    public bool IsMaximum => Mode == 0x01 && ModeShadow == 0x01;
    public bool IsAutomatic => Mode == 0x00 && ModeShadow == 0x00;
}

internal sealed class FingerprintException : Exception
{
    public FingerprintException(string message, int memoryWriteCount)
        : base(message)
    {
        MemoryWriteCount = memoryWriteCount;
    }

    public int MemoryWriteCount { get; }
}

internal sealed class AutoRestoreException : Exception
{
    public AutoRestoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class ControlPlaneException : Exception
{
    public ControlPlaneException(string message)
        : base(message)
    {
    }
}

internal static class SelfTests
{
    public static int Run()
    {
        var completed = 0;

        using (var fake = FakeItePortIo.CreateTarget())
        {
            var identities = PnpScanner.FindIte8519(fake);
            Assert(identities.Count == 1 && identities[0].BasePort == 0x2E && identities[0].Version == 0x08,
                "Target PNPCFG identity was not recognized.");
            Assert(fake.MemoryWriteCount == 0, "PNPCFG identity scan wrote EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            fake.IgnoredOuterIndexValue = 0x20;
            fake.IgnoredOuterIndexCount = 1;
            var identities = PnpScanner.FindIte8519(fake);
            Assert(identities.Count == 1 && identities[0].BasePort == 0x2E,
                "A transient PNPCFG index-selection miss was not retried.");
            Assert(fake.MemoryWriteCount == 0,
                "PNPCFG index retry wrote EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            var rejected = false;
            try
            {
                session.WriteFanMode(0x02);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Assert(rejected, "Fan-mode value 02 was not rejected.");
            Assert(fake.MemoryWriteCount == 0, "Rejected value reached EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            var guard = new GuardedFanPortIo(fake, 0x2E);
            guard.WriteByte(0x2E, 0x2E);
            guard.WriteByte(0x2F, 0x11);
            guard.WriteByte(0x2E, 0x2F);
            guard.WriteByte(0x2F, 0x18);
            guard.WriteByte(0x2E, 0x2E);
            guard.WriteByte(0x2F, 0x10);
            guard.WriteByte(0x2E, 0x2F);
            guard.WriteByte(0x2F, 0x08);
            guard.WriteByte(0x2E, 0x2E);
            guard.WriteByte(0x2F, 0x12);
            var rejected = false;
            try
            {
                guard.AuthorizeOneFanModeWrite(0x01);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Assert(rejected, "An address outside XDATA 0F10 was authorized.");
            Assert(fake.MemoryWriteCount == 0, "Off-list address reached EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            fake.IgnoredDepth2SelectionValue = 0x12;
            fake.IgnoredDepth2SelectionCount = 1;
            var snapshot = new FanController(session).Preflight(requireAutomaticMode: true);
            Assert(snapshot.Reading.IsPlausible(),
                "A transient depth-2 selector miss was not recovered by read-back.");
            Assert(fake.MemoryWriteCount == 0,
                "Depth-2 selector recovery wrote EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            fake.IgnoredDepth2SelectionValue = 0x12;
            fake.IgnoredDepth2SelectionCount = 1000;
            var rejected = false;
            try
            {
                new FanController(session).Preflight(requireAutomaticMode: true);
            }
            catch (IOException)
            {
                rejected = true;
            }
            Assert(rejected,
                "A persistently rejected depth-2 selector was accepted.");
            Assert(fake.MemoryWriteCount == 0,
                "Rejected depth-2 selector reached EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            fake.IgnoredFanDataOuterIndexCount = 8;
            var failed = false;
            try
            {
                session.WriteFanMode(0x01);
            }
            catch (IOException)
            {
                failed = true;
            }
            Assert(failed,
                "Injected post-authorization selector failure was not surfaced.");
            session.WriteFanMode(0x00);
            session.MarkAutomaticModeVerified();
            Assert(fake.GetXData(0x0F10) == 0,
                "A stale write authorization blocked mandatory AUTO recovery.");
            Assert(fake.MemoryWriteCount == 1,
                "Authorization cleanup test made an unexpected EC-memory write.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            fake.TransientReadAddress = 0x200A;
            fake.TransientReadValue = 0xC3;
            fake.TransientReadCount = 1;
            var snapshot = new FanController(session).Preflight(requireAutomaticMode: true);
            Assert(snapshot.Reading.IsPlausible(),
                "A one-sample stale BADRSEL read was not retried safely.");
            Assert(fake.MemoryWriteCount == 0,
                "Transient fingerprint retry wrote EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            fake.AlternatingReadAddress = 0x200A;
            var rejected = false;
            try
            {
                new FanController(session).Preflight(requireAutomaticMode: true);
            }
            catch (FingerprintException)
            {
                rejected = true;
            }
            Assert(rejected,
                "A persistently alternating BADRSEL fingerprint was accepted.");
            Assert(fake.MemoryWriteCount == 0,
                "Rejected alternating fingerprint wrote EC memory.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        using (var session = I2EcFanSession.Open(fake, 0x2E))
        {
            var result = new FanController(session).RunMaximumTest(TimeSpan.FromMilliseconds(100), 1);
            Assert(result.ControlPlaneVerified, "Firmware maximum sinks were not observed.");
            Assert(result.PhysicalResponseVerified, "Simulated physical response was not observed.");
            Assert(fake.GetXData(0x0F10) == 0, "Successful test did not restore automatic mode.");
            Assert(fake.MemoryWriteCount == 2, "Successful test did not make exactly one mode and one rollback write.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            fake.ThrowOnFirstManualSnapshot = true;
            using (var session = I2EcFanSession.Open(fake, 0x2E))
            {
                var failed = false;
                try
                {
                    new FanController(session).RunMaximumTest(TimeSpan.FromMilliseconds(100), 1);
                }
                catch (InvalidDataException)
                {
                    failed = true;
                }
                Assert(failed, "Injected observation failure was not surfaced.");
                Assert(fake.GetXData(0x0F10) == 0, "Failure path did not restore automatic mode.");
                Assert(fake.MemoryWriteCount == 2, "Failure path did not make exactly one mode and one rollback write.");
            }
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            fake.SetXData(0x0F10, 0x01);
            fake.SetXData(0x0114, 0x00);
            fake.SetXData(0x0115, 0x00);
            using (var session = I2EcFanSession.Open(fake, 0x2E))
            {
                var controller = new FanController(session);
                controller.ValidateForRecovery();
                controller.ForceAutomaticMode();
                Assert(fake.GetXData(0x0F10) == 0, "Recovery was blocked by invalid fan/thermal telemetry.");
                Assert(fake.MemoryWriteCount == 1, "Recovery did not make exactly one automatic-mode write.");
            }
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            using (var maximumSession = I2EcFanSession.Open(fake, 0x2E))
            {
                var entered = new FanController(maximumSession).EnterMaximumOneShot();
                Assert(entered.IsMaximum, "One-shot maximum command was not acknowledged.");
            }
            Assert(fake.GetXData(0x0F10) == 1,
                "Verified one-shot maximum did not persist after its command-scoped session closed.");
            Assert(fake.MemoryWriteCount == 1,
                "One-shot maximum made more than its single allow-listed write.");

            using (var automaticSession = I2EcFanSession.Open(fake, 0x2E))
            {
                var automatic = new FanController(automaticSession).ForceAutomaticModeAndReport();
                Assert(automatic.IsAutomatic, "One-shot automatic command was not acknowledged.");
            }
            Assert(fake.GetXData(0x0F10) == 0,
                "One-shot automatic command did not restore firmware ownership.");
            Assert(fake.MemoryWriteCount == 2,
                "One-shot MAX/AUTO pair made an unexpected EC-memory write.");
            completed++;
        }

        using (var fake = FakeItePortIo.CreateTarget())
        {
            using (var session = I2EcFanSession.Open(fake, 0x2E))
            {
                var controller = new FanController(session);
                var entered = controller.EnterMaximumMode();
                Assert(entered.IsMaximum, "Persistent maximum mode was not fully acknowledged.");
                Assert(controller.ReadGuardStatus().IsMaximum,
                    "Maximum mode was not retained while its guarded I2EC session was alive.");
                Assert(fake.GetXData(0x0F10) == 1,
                    "Live maximum ownership did not retain firmware manual mode.");
            }
            Assert(fake.GetXData(0x0F10) == 0,
                "Maximum-session disposal did not restore automatic mode.");
            Assert(fake.MemoryWriteCount == 2,
                "Maximum-session ownership did not make exactly one mode and one automatic write.");
            completed++;
        }

        completed += FanGuardFailsafeSelfTests.Run();

        Console.WriteLine("SELF_TESTS=PASS");
        Console.WriteLine("TEST_COUNT=" + completed);
        Console.WriteLine("WRITE_ALLOWLIST=XDATA_0F10_VALUES_00_OR_01_ONLY");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed: " + message);
        }
    }
}

internal sealed class FakeItePortIo : IPortIo
{
    private readonly Dictionary<ushort, byte> _xdata = new Dictionary<ushort, byte>();
    private byte _outerIndex = 0x07;
    private byte _depth2Address = 0x44;
    private byte _i2EcHigh = 0x12;
    private byte _i2EcLow = 0x34;
    private int _alternatingReadCount;
    private bool _dataMuxStalePending;
    private byte _previousDataPortValue;

    public int MemoryWriteCount { get; private set; }
    public bool ThrowOnFirstManualSnapshot { get; set; }
    public ushort? TransientReadAddress { get; set; }
    public byte TransientReadValue { get; set; }
    public int TransientReadCount { get; set; }
    public ushort? AlternatingReadAddress { get; set; }
    public byte? IgnoredOuterIndexValue { get; set; }
    public int IgnoredOuterIndexCount { get; set; }
    public byte? IgnoredDepth2SelectionValue { get; set; }
    public int IgnoredDepth2SelectionCount { get; set; }
    public int IgnoredFanDataOuterIndexCount { get; set; }
    public bool DelayDataMuxOneRead { get; set; }
    public byte OuterIndex => _outerIndex;
    public byte Depth2Address => _depth2Address;
    public byte I2EcHigh => _i2EcHigh;
    public byte I2EcLow => _i2EcLow;

    public static FakeItePortIo CreateTarget()
    {
        var fake = new FakeItePortIo();
        fake.SetXData(0x200D, 0xC3);
        fake.SetXData(0x200A, 0x00);
        fake.SetXData(0x0383, 0x00);
        fake.SetXData(0x0F10, 0x00);
        fake.SetXData(0x0439, 0x00);
        fake.SetXData(0x1808, 0x00);
        fake.SetXData(0x010D, 0x00);
        fake.SetXData(0x010E, 0x00);
        fake.SetXData(0x010F, 0x00);
        fake.SetXData(0x1809, 0x00);
        fake.SetXData(0x0110, 0x00);
        fake.SetXData(0x0111, 0x00);
        fake.SetXData(0x0112, 0x00);
        fake.SetXData(0x0114, 55);
        fake.SetXData(0x0115, 86);
        return fake;
    }

    public byte ReadByte(ushort port)
    {
        if (port == 0x2E || port == 0x4E)
        {
            return port == 0x2E ? _outerIndex : (byte)0xFF;
        }
        if (port == 0x4F)
        {
            return 0xFF;
        }
        if (port != 0x2F)
        {
            throw new InvalidOperationException("Unexpected fake port read.");
        }

        byte current = _outerIndex switch
        {
            0x20 => (byte)0x85,
            0x21 => (byte)0x19,
            0x22 => (byte)0x08,
            0x2E => _depth2Address,
            0x2F when _depth2Address == 0x10 => _i2EcLow,
            0x2F when _depth2Address == 0x11 => _i2EcHigh,
            0x2F when _depth2Address == 0x12 => ReadXData(),
            _ => (byte)0xFF
        };
        if (DelayDataMuxOneRead && _dataMuxStalePending)
        {
            _dataMuxStalePending = false;
            var stale = _previousDataPortValue;
            _previousDataPortValue = current;
            return stale;
        }
        _previousDataPortValue = current;
        return current;
    }

    public void WriteByte(ushort port, byte value)
    {
        if (port == 0x4E)
        {
            return;
        }
        if (port == 0x2E)
        {
            if (value == 0x2F && IgnoredFanDataOuterIndexCount > 0 &&
                _depth2Address == 0x12 && _i2EcHigh == 0x0F && _i2EcLow == 0x10)
            {
                IgnoredFanDataOuterIndexCount--;
                return;
            }
            if (IgnoredOuterIndexValue == value && IgnoredOuterIndexCount > 0)
            {
                IgnoredOuterIndexCount--;
                return;
            }
            if (DelayDataMuxOneRead && _outerIndex != value)
            {
                _dataMuxStalePending = true;
            }
            _outerIndex = value;
            return;
        }
        if (port != 0x2F)
        {
            throw new InvalidOperationException("Unexpected fake port write.");
        }

        if (_outerIndex == 0x2E)
        {
            if (IgnoredDepth2SelectionValue == value && IgnoredDepth2SelectionCount > 0)
            {
                IgnoredDepth2SelectionCount--;
                return;
            }
            _depth2Address = value;
            return;
        }
        if (_outerIndex == 0x2F && _depth2Address == 0x10)
        {
            _i2EcLow = value;
            return;
        }
        if (_outerIndex == 0x2F && _depth2Address == 0x11)
        {
            _i2EcHigh = value;
            return;
        }
        if (_outerIndex == 0x2F && _depth2Address == 0x12)
        {
            var address = (ushort)((_i2EcHigh << 8) | _i2EcLow);
            MemoryWriteCount++;
            SetXData(address, value);
            if (address == 0x0F10 && value == 0x01)
            {
                SetXData(0x0439, 0x01);
                SetXData(0x1808, 0xB7);
                SetXData(0x010F, 0xB7);
                SetXData(0x1809, 0xB7);
                SetXData(0x0112, 0xB7);
                SetXData(0x010D, 0x13);
                SetXData(0x010E, 0x88);
                SetXData(0x0110, 0x13);
                SetXData(0x0111, 0x88);
            }
            else if (address == 0x0F10 && value == 0x00)
            {
                SetXData(0x0439, 0x00);
            }
            return;
        }
        throw new InvalidOperationException("Unexpected fake data-port write.");
    }

    public void SetXData(ushort address, byte value)
    {
        _xdata[address] = value;
    }

    public byte GetXData(ushort address)
    {
        return _xdata.TryGetValue(address, out var value) ? value : (byte)0;
    }

    public void Dispose()
    {
    }

    private byte ReadXData()
    {
        var address = (ushort)((_i2EcHigh << 8) | _i2EcLow);
        if (TransientReadAddress == address && TransientReadCount > 0)
        {
            TransientReadCount--;
            return TransientReadValue;
        }
        if (AlternatingReadAddress == address)
        {
            var value = ((_alternatingReadCount / 2) & 1) == 0
                ? (byte)0xC3
                : GetXData(address);
            _alternatingReadCount++;
            return value;
        }
        if (ThrowOnFirstManualSnapshot && GetXData(0x0F10) == 0x01 && address == 0x1808)
        {
            throw new IOException("Injected manual snapshot failure.");
        }
        return GetXData(address);
    }
}
