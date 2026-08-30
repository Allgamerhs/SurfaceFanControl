using System;

namespace SurfaceProFanControl.Core
{
    public sealed class EcFanReading
    {
        public EcFanReading(
            int fan0Rpm,
            byte fan0DutyRaw,
            int fan1Rpm,
            byte fan1DutyRaw,
            EcThermalReading? thermal = null)
        {
            Fan0Rpm = fan0Rpm;
            Fan0DutyRaw = fan0DutyRaw;
            Fan1Rpm = fan1Rpm;
            Fan1DutyRaw = fan1DutyRaw;
            Thermal = thermal;
        }

        public int Fan0Rpm { get; }
        public byte Fan0DutyRaw { get; }
        public int Fan1Rpm { get; }
        public byte Fan1DutyRaw { get; }
        public EcThermalReading? Thermal { get; }

        public static EcFanReading Decode(
            byte fan0High,
            byte fan0Low,
            byte fan0DutyRaw,
            byte fan1High,
            byte fan1Low,
            byte fan1DutyRaw)
        {
            return new EcFanReading(
                DecodeBigEndianRpm(fan0High, fan0Low),
                fan0DutyRaw,
                DecodeBigEndianRpm(fan1High, fan1Low),
                fan1DutyRaw);
        }

        public static EcFanReading Decode(
            byte fan0High,
            byte fan0Low,
            byte fan0DutyRaw,
            byte fan1High,
            byte fan1Low,
            byte fan1DutyRaw,
            byte currentTemperatureRaw,
            byte criticalTemperatureRaw)
        {
            var thermal = EcThermalReading.Decode(currentTemperatureRaw, criticalTemperatureRaw);
            return new EcFanReading(
                DecodeBigEndianRpm(fan0High, fan0Low),
                fan0DutyRaw,
                DecodeBigEndianRpm(fan1High, fan1Low),
                fan1DutyRaw,
                thermal.IsPlausible() ? thermal : null);
        }

        public bool IsPlausible()
        {
            // Live correlation on the target established an operating range of
            // roughly 2,700-7,300 RPM. Leave headroom for the firmware maximum.
            return Fan0Rpm >= 0 && Fan0Rpm <= 15000 &&
                   Fan1Rpm >= 0 && Fan1Rpm <= 15000;
        }

        public static EcFanReading Median(EcFanReading first, EcFanReading second, EcFanReading third)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (third == null) throw new ArgumentNullException(nameof(third));

            var thermal = MergeThermal(first.Thermal, second.Thermal, third.Thermal);
            return new EcFanReading(
                Median(first.Fan0Rpm, second.Fan0Rpm, third.Fan0Rpm),
                (byte)Median(first.Fan0DutyRaw, second.Fan0DutyRaw, third.Fan0DutyRaw),
                Median(first.Fan1Rpm, second.Fan1Rpm, third.Fan1Rpm),
                (byte)Median(first.Fan1DutyRaw, second.Fan1DutyRaw, third.Fan1DutyRaw),
                thermal);
        }

        private static EcThermalReading? MergeThermal(
            EcThermalReading? first,
            EcThermalReading? second,
            EcThermalReading? third)
        {
            if (first != null && second != null && third != null)
            {
                return EcThermalReading.Median(first, second, third);
            }

            var available = new[] { first, second, third };
            EcThermalReading? merged = null;
            foreach (var sample in available)
            {
                if (sample == null)
                {
                    continue;
                }
                merged = merged == null
                    ? sample
                    : new EcThermalReading(
                        Math.Max(merged.TemperatureCelsius, sample.TemperatureCelsius),
                        Math.Min(merged.CriticalTemperatureCelsius, sample.CriticalTemperatureCelsius));
            }
            return merged;
        }

        private static int Median(int first, int second, int third)
        {
            return Math.Max(Math.Min(first, second), Math.Min(Math.Max(first, second), third));
        }

        private static int DecodeBigEndianRpm(byte high, byte low)
        {
            return (high << 8) | low;
        }
    }

    public enum FanHealthOutcome
    {
        Passed,
        Inconclusive,
        Failed
    }

    public sealed class FanHealthAssessment
    {
        public FanHealthAssessment(FanHealthOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public FanHealthOutcome Outcome { get; }
        public string Message { get; }

        public static bool IsStrongCooling(EcFanReading sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            return sample.Fan0Rpm >= 3500 && sample.Fan1Rpm >= 3500 &&
                   sample.Fan0DutyRaw >= 50 && sample.Fan1DutyRaw >= 50;
        }

        public static int NextStrongCoolingStreak(int currentStreak, EcFanReading? sample)
        {
            if (currentStreak < 0) throw new ArgumentOutOfRangeException(nameof(currentStreak));
            return sample != null && IsStrongCooling(sample) ? currentStreak + 1 : 0;
        }

        public static int NextResponsiveStreak(int currentStreak, EcFanReading baseline, EcFanReading? sample)
        {
            if (currentStreak < 0) throw new ArgumentOutOfRangeException(nameof(currentStreak));
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (sample == null)
            {
                return 0;
            }

            var responsive = FanResponded(
                                 baseline.Fan0Rpm,
                                 baseline.Fan0DutyRaw,
                                 sample.Fan0Rpm,
                                 sample.Fan0DutyRaw) &&
                             FanResponded(
                                 baseline.Fan1Rpm,
                                 baseline.Fan1DutyRaw,
                                 sample.Fan1Rpm,
                                 sample.Fan1DutyRaw);
            return responsive ? currentStreak + 1 : 0;
        }

        public static int NextZeroStreak(int currentStreak, EcFanReading? sample)
        {
            if (currentStreak < 0) throw new ArgumentOutOfRangeException(nameof(currentStreak));
            return sample != null && (sample.Fan0Rpm == 0 || sample.Fan1Rpm == 0)
                ? currentStreak + 1
                : 0;
        }

        public static FanHealthAssessment EvaluateStrongCooling(
            EcFanReading peak,
            int strongCoolingSamples,
            int zeroSamples,
            bool zeroSamplesIndicateFailure = true)
        {
            if (peak == null) throw new ArgumentNullException(nameof(peak));

            if (strongCoolingSamples >= 3 && IsStrongCooling(peak))
            {
                return new FanHealthAssessment(
                    FanHealthOutcome.Passed,
                    $"Cihaz zaten sıcaktı; iki fanın yüksek devirde çalıştığı doğrulandı ({peak.Fan0Rpm}/{peak.Fan1Rpm} RPM)." );
            }
            if (zeroSamplesIndicateFailure && zeroSamples >= 3)
            {
                return new FanHealthAssessment(
                    FanHealthOutcome.Failed,
                    "Fanlardan en az biri üç ardışık ölçümde 0 RPM bildirdi.");
            }

            return new FanHealthAssessment(
                FanHealthOutcome.Inconclusive,
                "Cihaz sıcakken ek yük verilmedi; iki fan için üç ardışık yüksek-devir örneği alınamadı.");
        }

        public static FanHealthAssessment Evaluate(
            EcFanReading baseline,
            EcFanReading peak,
            int responsiveSamples,
            int zeroSamples,
            bool zeroSamplesIndicateFailure = true)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (peak == null) throw new ArgumentNullException(nameof(peak));

            var fan0Increase = peak.Fan0Rpm - baseline.Fan0Rpm;
            var fan1Increase = peak.Fan1Rpm - baseline.Fan1Rpm;
            var fan0DutyIncrease = peak.Fan0DutyRaw - baseline.Fan0DutyRaw;
            var fan1DutyIncrease = peak.Fan1DutyRaw - baseline.Fan1DutyRaw;

            if (responsiveSamples >= 3 &&
                FanResponded(baseline.Fan0Rpm, baseline.Fan0DutyRaw, peak.Fan0Rpm, peak.Fan0DutyRaw) &&
                FanResponded(baseline.Fan1Rpm, baseline.Fan1DutyRaw, peak.Fan1Rpm, peak.Fan1DutyRaw))
            {
                var startedFromStop = IsStoppedBaseline(baseline.Fan0Rpm, baseline.Fan0DutyRaw) &&
                                      IsStoppedBaseline(baseline.Fan1Rpm, baseline.Fan1DutyRaw);
                return new FanHealthAssessment(
                    FanHealthOutcome.Passed,
                    startedFromStop
                        ? $"İki fan da duruştan çalışmaya başladı: Fan 1 {peak.Fan0Rpm} RPM, Fan 2 {peak.Fan1Rpm} RPM."
                        : $"İki fan da tepki verdi: Fan 1 +{fan0Increase} RPM, Fan 2 +{fan1Increase} RPM.");
            }

            if (zeroSamplesIndicateFailure && zeroSamples >= 3)
            {
                return new FanHealthAssessment(
                    FanHealthOutcome.Failed,
                    "Fanlardan en az biri test sırasında 0 RPM bildirdi.");
            }

            return new FanHealthAssessment(
                FanHealthOutcome.Inconclusive,
                "Test süresinde iki fan için yeterli RPM artışı ölçülemedi.");
        }

        private static bool FanResponded(int baselineRpm, byte baselineDuty, int sampleRpm, byte sampleDuty)
        {
            if (IsStoppedBaseline(baselineRpm, baselineDuty))
            {
                // Live target evidence: firmware may stop both fans near 60 °C,
                // then start them around 3,300 RPM / raw duty 48 under load.
                return sampleRpm >= 2500 && sampleDuty >= 30;
            }

            return sampleRpm >= Math.Max(3500, baselineRpm + 700) &&
                   sampleDuty >= baselineDuty + 10;
        }

        private static bool IsStoppedBaseline(int rpm, byte duty)
        {
            return rpm < 500 && duty < 5;
        }
    }
}
