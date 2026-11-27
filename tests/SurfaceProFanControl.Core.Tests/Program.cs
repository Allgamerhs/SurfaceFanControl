using System;
using System.Collections.Generic;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Core.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int Checks;

        private static int Main()
        {
            Run("ACPI tenths-kelvin conversion", () => Equal(26.9, TemperatureNormalizer.ToCelsius(3000), 0.05));
            Run("Surface direct-celsius conversion", () => Equal(75.0, TemperatureNormalizer.ToCelsius(75), 0.01));
            Run("Invalid temperature rejected", () => True(TemperatureNormalizer.ToCelsius(999) == null));
            Run("Exact Surface target accepted", TargetAccepted);
            Run("Same model with a different UUID is accepted", DifferentUuidAccepted);
            Run("Wrong model rejected", WrongTargetRejected);
            Run("Emergency hysteresis", EmergencyHysteresis);
            Run("Missing telemetry cannot clear emergency", MissingTelemetryDoesNotClearEmergency);
            Run("EC fan RPM is big-endian", EcFanRpmIsBigEndian);
            Run("Implausible torn EC read rejected", TornEcReadRejected);
            Run("EC fan median rejects one transient", EcMedianRejectsTransient);
            Run("EC thermal registers decode as direct Celsius", EcThermalRegistersDecode);
            Run("Invalid EC thermal zero is rejected", InvalidEcThermalZeroRejected);
            Run("Over-critical EC thermal remains a safety signal", OverCriticalEcThermalIsPreserved);
            Run("Unified EC fan sample carries thermal data", UnifiedEcSampleCarriesThermal);
            Run("Unified EC median carries thermal median", UnifiedEcMedianCarriesThermalMedian);
            Run("Unified EC median preserves two valid thermal samples", UnifiedEcMedianPreservesValidThermals);
            Run("Thermal resolver chooses conservative values", ThermalResolverIsConservative);
            Run("Thermal resolver uses firmware fallback", ThermalResolverUsesFallback);
            Run("Fan health response passes", FanHealthResponsePasses);
            Run("Fans starting from zero pass health test", FansStartingFromZeroPass);
            Run("Low noise from zero does not pass health test", LowNoiseFromZeroDoesNotPass);
            Run("Stopped-fan response boundaries are exact", StoppedFanResponseBoundariesAreExact);
            Run("Stopped fan fails health test", StoppedFanFailsHealthTest);
            Run("Cool stopped fans are not an early failure", CoolStoppedFansAreInconclusive);
            Run("Single response is inconclusive", SingleResponseIsInconclusive);
            Run("Single zero sample is inconclusive", SingleZeroIsInconclusive);
            Run("Thermal watchdog cuts at limit", ThermalWatchdogCutsAtLimit);
            Run("Thermal watchdog cuts on low battery", ThermalWatchdogCutsOnLowBattery);
            Run("Thermal watchdog tolerates transient loss", ThermalWatchdogToleratesTransientLoss);
            Run("Thermal watchdog cuts after sustained loss", ThermalWatchdogCutsAfterSustainedLoss);
            Run("Already-hot strong cooling passes", AlreadyHotStrongCoolingPasses);
            Run("Already-hot weak cooling is inconclusive", AlreadyHotWeakCoolingIsInconclusive);
            Run("Strong cooling threshold rejects one weak fan", StrongCoolingRejectsOneWeakFan);
            Run("Strong cooling exact threshold passes", StrongCoolingExactThresholdPasses);
            Run("Missing sample resets strong streak", MissingSampleResetsStrongStreak);
            Run("Missing sample resets response and zero streaks", MissingSampleResetsOtherStreaks);
            Run("Idle zeros without temperature are inconclusive", IdleZerosWithoutTemperatureAreInconclusive);
            Run("Cooling aggressiveness threshold endpoints", CoolingAggressivenessThresholdEndpoints);
            Run("Cooling aggressiveness rejects invalid percentages", CoolingAggressivenessRejectsInvalidPercentages);
            Run("Cooling aggressiveness turn-on edge is exact", CoolingAggressivenessTurnOnEdgeIsExact);
            Run("Cooling aggressiveness uses five-degree hysteresis", CoolingAggressivenessUsesFiveDegreeHysteresis);
            Run("Cooling aggressiveness enforces sixty-second dwell", CoolingAggressivenessEnforcesSixtySecondDwell);
            Run("Missing temperature falls back to automatic", MissingTemperatureFallsBackToAutomatic);
            Run("Temperature signal median rejects one spike", TemperatureSignalMedianRejectsSpike);
            Run("Temperature signal reports trend and range", TemperatureSignalReportsTrendAndRange);
            Run("Temperature signal becomes stale", TemperatureSignalBecomesStale);
            Run("First temperature after a gap controls cooling immediately", TemperatureRecoversAfterGap);
            Run("Future temperature is unavailable after a clock correction", FutureTemperatureIsStale);
            Run("Binary curve uses separate on and off thresholds", BinaryCurveUsesHysteresis);
            Run("Binary curve enforces configured dwell", BinaryCurveEnforcesDwell);
            Run("Binary curve predictive cooling enters early", BinaryCurvePredictsFastRise);
            Run("Binary curve ignores slow rise below threshold", BinaryCurveIgnoresSlowRise);
            Run("Binary curve stale sensor returns firmware automatic", BinaryCurveStaleSensorReturnsAutomatic);
            Run("Binary curve emergency and boost bypass dwell", BinaryCurveOverridesBypassDwell);
            Run("Binary curve rejects unsafe threshold spacing", BinaryCurveRejectsUnsafeSpacing);
            Run("Timed boost overrides automatic preference", TimedBoostOverridesAutomaticPreference);
            Run("Built-in cooling profiles expose safe AUTO/MAX endpoints", BuiltInProfilesExposeSafeEndpoints);
            Run("Custom profile matching does not fake a built-in profile", CustomProfileMatchingIsExplicit);
            Run("Power profiles choose the active power source", PowerProfilesChooseActiveSource);
            Run("Power profiles reject invalid percentages", PowerProfilesRejectInvalidPercentages);
            Run("Exact Surface receives full-control backend", SurfaceReceivesFullControlBackend);
            Run("Unknown computer receives monitoring-only backend", UnknownComputerReceivesMonitoringBackend);
            Run("Ten localization catalogs are complete", LocalizationCatalogsAreComplete);

            if (Failures.Count == 0)
            {
                Console.WriteLine($"PASS: {Checks}/{Checks} SurfaceProFanControl core checks");
                return 0;
            }

            Console.Error.WriteLine($"FAIL: {Failures.Count} check(s)");
            foreach (var failure in Failures)
            {
                Console.Error.WriteLine(" - " + failure);
            }

            return 1;
        }

        private static void TargetAccepted()
        {
            var identity = new HardwareIdentity(
                "Microsoft Corporation",
                "Surface with Windows 8 Pro",
                "{11111111-2222-4333-8444-555555555555}",
                "1.07.0050",
                "test-device");
            True(identity.MatchesSurfacePro1Target(out _));
        }

        private static void DifferentUuidAccepted()
        {
            var identity = new HardwareIdentity(
                "Microsoft Corporation",
                "Surface with Windows 8 Pro",
                "00000000-0000-0000-0000-000000000000",
                "1.07.0050",
                "test");
            True(identity.MatchesSurfacePro1Target(out _));
        }

        private static void WrongTargetRejected()
        {
            var identity = new HardwareIdentity(
                "Microsoft Corporation",
                "Different Surface Model",
                Guid.NewGuid().ToString(),
                "1.07.0050",
                "test");
            True(!identity.MatchesSurfacePro1Target(out var reason));
            True(reason.Contains("Model", StringComparison.Ordinal));
        }

        private static void EmergencyHysteresis()
        {
            var controller = new ThermalSafetyController(82, 74, 3);
            True(controller.Evaluate(81.9) == SafetyDecision.None);
            True(controller.Evaluate(82.0) == SafetyDecision.EnterEmergency);
            True(controller.Evaluate(74.0) == SafetyDecision.StayEmergency);
            True(controller.Evaluate(74.0) == SafetyDecision.StayEmergency);
            True(controller.Evaluate(74.0) == SafetyDecision.ExitEmergency);
            True(!controller.IsEmergency);
        }

        private static void MissingTelemetryDoesNotClearEmergency()
        {
            var controller = new ThermalSafetyController(82, 74, 1);
            controller.Evaluate(90);
            True(controller.Evaluate(null) == SafetyDecision.StayEmergency);
            True(controller.IsEmergency);
        }

        private static void EcFanRpmIsBigEndian()
        {
            var reading = EcFanReading.Decode(0x0A, 0xA9, 39, 0x1C, 0x8D, 110);
            True(reading.Fan0Rpm == 2729);
            True(reading.Fan1Rpm == 7309);
            True(reading.Fan0DutyRaw == 39);
            True(reading.Fan1DutyRaw == 110);
        }

        private static void TornEcReadRejected()
        {
            var reading = EcFanReading.Decode(0x90, 0x4B, 45, 0x0C, 0x15, 44);
            True(!reading.IsPlausible());
        }

        private static void EcMedianRejectsTransient()
        {
            var first = new EcFanReading(2725, 39, 2740, 39);
            var transient = new EcFanReading(14900, 200, 2719, 38);
            var third = new EcFanReading(2732, 40, 2730, 40);
            var median = EcFanReading.Median(first, transient, third);
            True(median.Fan0Rpm == 2732);
            True(median.Fan0DutyRaw == 40);
            True(median.Fan1Rpm == 2730);
        }

        private static void EcThermalRegistersDecode()
        {
            var reading = EcThermalReading.Decode(55, 86);
            True(reading.IsPlausible());
            Equal(55.0, reading.TemperatureCelsius, 0.01);
            Equal(86.0, reading.CriticalTemperatureCelsius, 0.01);
        }

        private static void InvalidEcThermalZeroRejected()
        {
            True(!EcThermalReading.Decode(0, 86).IsPlausible());
            True(!EcThermalReading.Decode(55, 0).IsPlausible());
        }

        private static void OverCriticalEcThermalIsPreserved()
        {
            var reading = EcThermalReading.Decode(92, 86);
            True(reading.IsPlausible());
            Equal(92.0, reading.TemperatureCelsius, 0.01);
        }

        private static void UnifiedEcSampleCarriesThermal()
        {
            var reading = EcFanReading.Decode(0x0A, 0xA9, 39, 0x0A, 0xCC, 39, 55, 86);
            True(reading.Thermal != null);
            Equal(55.0, reading.Thermal?.TemperatureCelsius, 0.01);
            Equal(86.0, reading.Thermal?.CriticalTemperatureCelsius, 0.01);
        }

        private static void UnifiedEcMedianCarriesThermalMedian()
        {
            var first = new EcFanReading(2700, 39, 2700, 39, EcThermalReading.Decode(55, 86));
            var transient = new EcFanReading(9000, 110, 9000, 110, EcThermalReading.Decode(70, 86));
            var third = new EcFanReading(2750, 40, 2750, 40, EcThermalReading.Decode(56, 86));
            var median = EcFanReading.Median(first, transient, third);
            True(median.Thermal != null);
            Equal(56.0, median.Thermal?.TemperatureCelsius, 0.01);
        }

        private static void UnifiedEcMedianPreservesValidThermals()
        {
            var first = new EcFanReading(2700, 39, 2700, 39, EcThermalReading.Decode(91, 86));
            var invalid = new EcFanReading(2710, 39, 2710, 39);
            var third = new EcFanReading(2720, 40, 2720, 40, EcThermalReading.Decode(92, 86));
            var median = EcFanReading.Median(first, invalid, third);
            True(median.Thermal != null);
            Equal(92.0, median.Thermal?.TemperatureCelsius, 0.01);
            Equal(86.0, median.Thermal?.CriticalTemperatureCelsius, 0.01);
        }

        private static void ThermalResolverIsConservative()
        {
            Equal(56.0, ThermalTelemetryResolver.ConservativeTemperature(56, 55), 0.01);
            Equal(86.0, ThermalTelemetryResolver.ResolveCriticalTemperature(90, 86), 0.01);
        }

        private static void ThermalResolverUsesFallback()
        {
            Equal(55.0, ThermalTelemetryResolver.ConservativeTemperature(null, 55), 0.01);
            Equal(86.0, ThermalTelemetryResolver.ResolveCriticalTemperature(null, null), 0.01);
        }

        private static void FanHealthResponsePasses()
        {
            var baseline = new EcFanReading(2725, 39, 2764, 39);
            var peak = new EcFanReading(7309, 110, 7309, 108);
            var result = FanHealthAssessment.Evaluate(baseline, peak, responsiveSamples: 3, zeroSamples: 0);
            True(result.Outcome == FanHealthOutcome.Passed);
        }

        private static void FansStartingFromZeroPass()
        {
            var baseline = new EcFanReading(0, 0, 0, 0);
            var running = new EcFanReading(3385, 48, 3374, 48);
            var streak = FanHealthAssessment.NextResponsiveStreak(0, baseline, running);
            streak = FanHealthAssessment.NextResponsiveStreak(streak, baseline, running);
            streak = FanHealthAssessment.NextResponsiveStreak(streak, baseline, running);
            var result = FanHealthAssessment.Evaluate(baseline, running, streak, zeroSamples: 0);
            True(streak == 3);
            True(result.Outcome == FanHealthOutcome.Passed);
        }

        private static void LowNoiseFromZeroDoesNotPass()
        {
            var baseline = new EcFanReading(0, 0, 0, 0);
            var noise = new EcFanReading(400, 1, 350, 1);
            True(FanHealthAssessment.NextResponsiveStreak(0, baseline, noise) == 0);
        }

        private static void StoppedFanResponseBoundariesAreExact()
        {
            var stoppedBoundary = new EcFanReading(499, 4, 499, 4);
            var exactResponse = new EcFanReading(2500, 30, 2500, 30);
            True(FanHealthAssessment.NextResponsiveStreak(0, stoppedBoundary, exactResponse) == 1);

            True(FanHealthAssessment.NextResponsiveStreak(
                0,
                new EcFanReading(500, 4, 499, 4),
                exactResponse) == 0);
            True(FanHealthAssessment.NextResponsiveStreak(
                0,
                new EcFanReading(499, 5, 499, 4),
                exactResponse) == 0);
            True(FanHealthAssessment.NextResponsiveStreak(
                0,
                stoppedBoundary,
                new EcFanReading(2499, 30, 2500, 30)) == 0);
            True(FanHealthAssessment.NextResponsiveStreak(
                0,
                stoppedBoundary,
                new EcFanReading(2500, 29, 2500, 30)) == 0);
            True(FanHealthAssessment.NextResponsiveStreak(
                0,
                stoppedBoundary,
                new EcFanReading(2500, 30, 400, 1)) == 0);
        }

        private static void StoppedFanFailsHealthTest()
        {
            var baseline = new EcFanReading(2700, 39, 2700, 39);
            var peak = new EcFanReading(4200, 70, 0, 0);
            var result = FanHealthAssessment.Evaluate(baseline, peak, responsiveSamples: 0, zeroSamples: 3);
            True(result.Outcome == FanHealthOutcome.Failed);
        }

        private static void CoolStoppedFansAreInconclusive()
        {
            var stopped = new EcFanReading(0, 0, 0, 0);
            var result = FanHealthAssessment.Evaluate(
                stopped,
                stopped,
                responsiveSamples: 0,
                zeroSamples: 20,
                zeroSamplesIndicateFailure: false);
            True(result.Outcome == FanHealthOutcome.Inconclusive);
        }

        private static void SingleResponseIsInconclusive()
        {
            var baseline = new EcFanReading(2700, 39, 2700, 39);
            var peak = new EcFanReading(7000, 105, 7000, 105);
            var result = FanHealthAssessment.Evaluate(baseline, peak, responsiveSamples: 1, zeroSamples: 0);
            True(result.Outcome == FanHealthOutcome.Inconclusive);
        }

        private static void SingleZeroIsInconclusive()
        {
            var baseline = new EcFanReading(2700, 39, 2700, 39);
            var peak = new EcFanReading(4200, 70, 0, 0);
            var result = FanHealthAssessment.Evaluate(baseline, peak, responsiveSamples: 0, zeroSamples: 1);
            True(result.Outcome == FanHealthOutcome.Inconclusive);
        }

        private static void ThermalWatchdogCutsAtLimit()
        {
            var monitor = new FanTestSafetyMonitor(78);
            True(monitor.Evaluate(77.9, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(78.0, onAcPower: true, batteryPercent: 80)?.Contains("78", StringComparison.Ordinal) == true);
        }

        private static void ThermalWatchdogCutsOnLowBattery()
        {
            var monitor = new FanTestSafetyMonitor(78);
            True(monitor.Evaluate(60, onAcPower: false, batteryPercent: 19)?.Contains("Batarya", StringComparison.Ordinal) == true);
        }

        private static void ThermalWatchdogToleratesTransientLoss()
        {
            var monitor = new FanTestSafetyMonitor(78);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(60, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80) == null);
        }

        private static void ThermalWatchdogCutsAfterSustainedLoss()
        {
            var monitor = new FanTestSafetyMonitor(78);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80) == null);
            True(monitor.Evaluate(null, onAcPower: true, batteryPercent: 80)?.Contains("kayboldu", StringComparison.Ordinal) == true);
        }

        private static void AlreadyHotStrongCoolingPasses()
        {
            var peak = new EcFanReading(7100, 105, 7050, 104);
            var result = FanHealthAssessment.EvaluateStrongCooling(peak, strongCoolingSamples: 3, zeroSamples: 0);
            True(result.Outcome == FanHealthOutcome.Passed);
        }

        private static void AlreadyHotWeakCoolingIsInconclusive()
        {
            var peak = new EcFanReading(2800, 40, 2820, 40);
            var result = FanHealthAssessment.EvaluateStrongCooling(peak, strongCoolingSamples: 0, zeroSamples: 0);
            True(result.Outcome == FanHealthOutcome.Inconclusive);
        }

        private static void StrongCoolingRejectsOneWeakFan()
        {
            var sample = new EcFanReading(7100, 105, 2800, 40);
            True(!FanHealthAssessment.IsStrongCooling(sample));
        }

        private static void StrongCoolingExactThresholdPasses()
        {
            True(FanHealthAssessment.IsStrongCooling(new EcFanReading(3500, 50, 3500, 50)));
            True(!FanHealthAssessment.IsStrongCooling(new EcFanReading(3499, 50, 3500, 50)));
            True(!FanHealthAssessment.IsStrongCooling(new EcFanReading(3500, 49, 3500, 50)));
        }

        private static void MissingSampleResetsStrongStreak()
        {
            var strong = new EcFanReading(5000, 80, 5000, 80);
            var streak = FanHealthAssessment.NextStrongCoolingStreak(0, strong);
            streak = FanHealthAssessment.NextStrongCoolingStreak(streak, strong);
            True(streak == 2);
            True(FanHealthAssessment.NextStrongCoolingStreak(streak, null) == 0);
        }

        private static void MissingSampleResetsOtherStreaks()
        {
            var baseline = new EcFanReading(2700, 39, 2700, 39);
            var responsive = new EcFanReading(5000, 80, 5000, 80);
            var stopped = new EcFanReading(0, 0, 5000, 80);
            True(FanHealthAssessment.NextResponsiveStreak(2, baseline, null) == 0);
            True(FanHealthAssessment.NextResponsiveStreak(0, baseline, responsive) == 1);
            True(FanHealthAssessment.NextZeroStreak(2, null) == 0);
            True(FanHealthAssessment.NextZeroStreak(0, stopped) == 1);
        }

        private static void IdleZerosWithoutTemperatureAreInconclusive()
        {
            var stopped = new EcFanReading(0, 0, 0, 0);
            var result = FanHealthAssessment.EvaluateStrongCooling(
                stopped,
                strongCoolingSamples: 0,
                zeroSamples: 3,
                zeroSamplesIndicateFailure: false);
            True(result.Outcome == FanHealthOutcome.Inconclusive);
        }

        private static void CoolingAggressivenessThresholdEndpoints()
        {
            var automatic = CoolingAggressivenessPolicy.GetThresholds(0);
            True(double.IsPositiveInfinity(automatic.TurnOnCelsius));
            True(double.IsPositiveInfinity(automatic.TurnOffCelsius));

            var leastAggressive = CoolingAggressivenessPolicy.GetThresholds(1);
            Equal(80.0, leastAggressive.TurnOnCelsius, 0.001);
            Equal(75.0, leastAggressive.TurnOffCelsius, 0.001);

            var midpoint = CoolingAggressivenessPolicy.GetThresholds(50);
            Equal(67.5, midpoint.TurnOnCelsius, 0.001);
            Equal(62.5, midpoint.TurnOffCelsius, 0.001);

            var mostAggressiveCurve = CoolingAggressivenessPolicy.GetThresholds(99);
            Equal(55.0, mostAggressiveCurve.TurnOnCelsius, 0.001);
            Equal(50.0, mostAggressiveCurve.TurnOffCelsius, 0.001);

            var maximum = CoolingAggressivenessPolicy.GetThresholds(100);
            True(double.IsNegativeInfinity(maximum.TurnOnCelsius));
            True(double.IsNegativeInfinity(maximum.TurnOffCelsius));
        }

        private static void CoolingAggressivenessRejectsInvalidPercentages()
        {
            Throws<ArgumentOutOfRangeException>(() => CoolingAggressivenessPolicy.GetThresholds(-1));
            Throws<ArgumentOutOfRangeException>(() => CoolingAggressivenessPolicy.GetThresholds(101));
        }

        private static void CoolingAggressivenessTurnOnEdgeIsExact()
        {
            var dwell = CoolingAggressivenessPolicy.MinimumDwell;
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(50, 67.499, false, dwell, false));
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(50, 67.5, false, dwell, false));
        }

        private static void CoolingAggressivenessUsesFiveDegreeHysteresis()
        {
            var dwell = CoolingAggressivenessPolicy.MinimumDwell;
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(50, 62.501, true, dwell, false));
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(50, 62.5, true, dwell, false));
        }

        private static void CoolingAggressivenessEnforcesSixtySecondDwell()
        {
            var beforeDwell = CoolingAggressivenessPolicy.MinimumDwell - TimeSpan.FromMilliseconds(1);
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(50, 90, false, beforeDwell, false));
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(50, 40, true, beforeDwell, false));

            var atDwell = CoolingAggressivenessPolicy.MinimumDwell;
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(50, 90, false, atDwell, false));
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(50, 40, true, atDwell, false));
        }

        private static void MissingTemperatureFallsBackToAutomatic()
        {
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(
                50,
                temperatureCelsius: null,
                maximumIsActive: true,
                timeInCurrentMode: CoolingAggressivenessPolicy.MinimumDwell,
                timedBoostActive: false));
        }

        private static void TemperatureSignalMedianRejectsSpike()
        {
            var tracker = new TemperatureSignalTracker();
            var start = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            tracker.Add(60, start);
            tracker.Add(99, start.AddSeconds(5));
            tracker.Add(61, start.AddSeconds(10));
            var signal = tracker.Read(start.AddSeconds(10), TimeSpan.FromSeconds(30));
            Equal(61, signal.TemperatureCelsius, 0.01);
            True(!signal.IsStale && signal.SampleCount == 3);
        }

        private static void TemperatureSignalReportsTrendAndRange()
        {
            var tracker = new TemperatureSignalTracker();
            var start = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            tracker.Add(60, start);
            tracker.Add(61, start.AddSeconds(30));
            tracker.Add(62, start.AddMinutes(1));
            var signal = tracker.Read(start.AddMinutes(1), TimeSpan.FromSeconds(90));
            Equal(2, signal.TrendCelsiusPerMinute, 0.01);
            Equal(60, signal.MinimumCelsius, 0.01);
            Equal(62, signal.MaximumCelsius, 0.01);
        }

        private static void TemperatureSignalBecomesStale()
        {
            var tracker = new TemperatureSignalTracker();
            var start = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            tracker.Add(65, start);
            var signal = tracker.Read(start.AddSeconds(30), TimeSpan.FromSeconds(30));
            True(signal.IsStale && !signal.ControlTemperatureCelsius.HasValue);
            Equal(65, signal.TemperatureCelsius, 0.01);
        }

        private static void TemperatureRecoversAfterGap()
        {
            var tracker = new TemperatureSignalTracker();
            var start = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            tracker.Add(40, start);
            tracker.Add(41, start.AddSeconds(5));
            tracker.Add(85, start.AddMinutes(1));
            var recovered = tracker.Read(start.AddMinutes(1), TimeSpan.FromSeconds(20));
            Equal(85, recovered.ControlTemperatureCelsius, 0.01);
            Equal(0, recovered.TrendCelsiusPerMinute, 0.01);
            True(recovered.SampleCount == 1);
            Equal(40, recovered.MinimumCelsius, 0.01);
            Equal(85, recovered.MaximumCelsius, 0.01);
        }

        private static void FutureTemperatureIsStale()
        {
            var tracker = new TemperatureSignalTracker();
            var now = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
            tracker.Add(40, now.AddMinutes(1));
            True(!tracker.Read(now, TimeSpan.FromSeconds(20)).ControlTemperatureCelsius.HasValue);
            tracker.Add(75, now);
            var current = tracker.Read(now, TimeSpan.FromSeconds(20));
            Equal(75, current.ControlTemperatureCelsius, 0.01);
            True(current.SampleCount == 1);
        }

        private static void BinaryCurveUsesHysteresis()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 70, 0, false, false, dwell, false, false));
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 62.1, 0, false, true, dwell, false, false));
            True(!BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 62, 0, false, true, dwell, false, false));
        }

        private static void BinaryCurveEnforcesDwell()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(!BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 80, 0, false, false, dwell - TimeSpan.FromMilliseconds(1), false, false));
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 50, 0, false, true, dwell - TimeSpan.FromMilliseconds(1), false, false));
        }

        private static void BinaryCurvePredictsFastRise()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 67, 2, true, false, dwell, false, false));
        }

        private static void BinaryCurveIgnoresSlowRise()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(!BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, 69.9, 1.9, true, false, dwell, false, false));
        }

        private static void BinaryCurveStaleSensorReturnsAutomatic()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(!BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, null, 50, true, true, TimeSpan.Zero, false, false));
        }

        private static void BinaryCurveOverridesBypassDwell()
        {
            var dwell = TimeSpan.FromSeconds(60);
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, null, 0, false, false, TimeSpan.Zero, true, false));
            True(BinaryFanCurvePolicy.ShouldUseMaximum(70, 62, dwell, null, 0, false, false, TimeSpan.Zero, false, true));
        }

        private static void BinaryCurveRejectsUnsafeSpacing()
        {
            Throws<ArgumentOutOfRangeException>(() => BinaryFanCurvePolicy.Validate(70, 68, TimeSpan.FromSeconds(60)));
        }

        private static void TimedBoostOverridesAutomaticPreference()
        {
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(
                aggressivenessPercent: 0,
                temperatureCelsius: null,
                maximumIsActive: false,
                timeInCurrentMode: TimeSpan.Zero,
                timedBoostActive: true));
            True(CoolingAggressivenessPolicy.ShouldUseMaximum(
                aggressivenessPercent: 100,
                temperatureCelsius: null,
                maximumIsActive: false,
                timeInCurrentMode: TimeSpan.Zero,
                timedBoostActive: false));
            True(!CoolingAggressivenessPolicy.ShouldUseMaximum(
                aggressivenessPercent: 0,
                temperatureCelsius: 100,
                maximumIsActive: true,
                timeInCurrentMode: CoolingAggressivenessPolicy.MinimumDwell,
                timedBoostActive: false));
        }

        private static void BuiltInProfilesExposeSafeEndpoints()
        {
            True(CoolingProfileCatalog.BuiltIn.Count == 5);
            True(CoolingProfileCatalog.Get(CoolingProfileId.Automatic).AggressivenessPercent == 0);
            True(CoolingProfileCatalog.Get(CoolingProfileId.ConstantMaximum).AggressivenessPercent == 100);
        }

        private static void CustomProfileMatchingIsExplicit()
        {
            True(CoolingProfileCatalog.Match(20) == CoolingProfileId.Quiet);
            True(CoolingProfileCatalog.Match(37) == CoolingProfileId.Custom);
            Throws<ArgumentOutOfRangeException>(() => CoolingProfileCatalog.Get(CoolingProfileId.Custom));
        }

        private static void PowerProfilesChooseActiveSource()
        {
            True(CoolingProfileCatalog.EffectiveAggressiveness(50, false, 80, 20, true) == 50);
            True(CoolingProfileCatalog.EffectiveAggressiveness(50, true, 80, 20, true) == 80);
            True(CoolingProfileCatalog.EffectiveAggressiveness(50, true, 80, 20, false) == 20);
        }

        private static void PowerProfilesRejectInvalidPercentages()
        {
            Throws<ArgumentOutOfRangeException>(() =>
                CoolingProfileCatalog.EffectiveAggressiveness(50, true, 101, 20, true));
        }

        private static void SurfaceReceivesFullControlBackend()
        {
            var identity = new HardwareIdentity(
                "Microsoft Corporation",
                "Surface with Windows 8 Pro",
                Guid.NewGuid().ToString(),
                "1.07.0050",
                "035494332753");
            var support = HardwareSupport.Detect(identity);
            True(support.CanControl);
            True(support.BackendId == "surface-pro-1-it8519");
        }

        private static void UnknownComputerReceivesMonitoringBackend()
        {
            var identity = new HardwareIdentity("Example", "Desktop", Guid.NewGuid().ToString(), "1", "2");
            var support = HardwareSupport.Detect(identity);
            True(!support.CanControl);
            True(support.Availability == FanControlAvailability.MonitoringOnly);
        }

        private static void LocalizationCatalogsAreComplete()
        {
            True(LocalizationCatalog.SupportedLanguages.Count == 10);
            var expectedKeys = LocalizationCatalog.Keys.Count;
            foreach (var language in LocalizationCatalog.SupportedLanguages)
            {
                foreach (var key in LocalizationCatalog.Keys)
                {
                    True(!string.IsNullOrWhiteSpace(LocalizationCatalog.Get(language.Code, key)));
                }
                True(LocalizationCatalog.Keys.Count == expectedKeys);
            }
            True(LocalizationCatalog.Language("ar").RightToLeft);
            True(LocalizationCatalog.NormalizeLanguageCode("zh-CN") == "zh-Hans");
            True(LocalizationCatalog.NormalizeLanguageCode("pl-PL") == "pl");
        }

        private static void Run(string name, Action test)
        {
            Checks++;
            try
            {
                test();
            }
            catch (Exception exception)
            {
                Failures.Add($"{name}: {exception.Message}");
            }
        }

        private static void True(bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        private static void Equal(double expected, double? actual, double tolerance)
        {
            if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual?.ToString() ?? "null"}.");
            }
        }

        private static void Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
    }
}
