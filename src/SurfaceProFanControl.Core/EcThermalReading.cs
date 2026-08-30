using System;

namespace SurfaceProFanControl.Core
{
    public sealed class EcThermalReading
    {
        public EcThermalReading(double temperatureCelsius, double criticalTemperatureCelsius)
        {
            TemperatureCelsius = temperatureCelsius;
            CriticalTemperatureCelsius = criticalTemperatureCelsius;
        }

        public double TemperatureCelsius { get; }
        public double CriticalTemperatureCelsius { get; }

        public static EcThermalReading Decode(byte currentRaw, byte criticalRaw)
        {
            return new EcThermalReading(
                TemperatureNormalizer.ToCelsius(currentRaw) ?? double.NaN,
                TemperatureNormalizer.ToCelsius(criticalRaw) ?? double.NaN);
        }

        public bool IsPlausible()
        {
            return !double.IsNaN(TemperatureCelsius) &&
                   !double.IsNaN(CriticalTemperatureCelsius) &&
                   TemperatureCelsius >= -20.0 && TemperatureCelsius <= 120.0 &&
                   CriticalTemperatureCelsius >= 70.0 && CriticalTemperatureCelsius <= 110.0;
        }

        public static EcThermalReading Median(
            EcThermalReading first,
            EcThermalReading second,
            EcThermalReading third)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (third == null) throw new ArgumentNullException(nameof(third));

            return new EcThermalReading(
                Median(first.TemperatureCelsius, second.TemperatureCelsius, third.TemperatureCelsius),
                Median(first.CriticalTemperatureCelsius, second.CriticalTemperatureCelsius, third.CriticalTemperatureCelsius));
        }

        private static double Median(double first, double second, double third)
        {
            return Math.Max(Math.Min(first, second), Math.Min(Math.Max(first, second), third));
        }
    }

    public static class ThermalTelemetryResolver
    {
        public static double? ConservativeTemperature(double? acpiCelsius, double? ecCelsius)
        {
            var acpi = IsValidTemperature(acpiCelsius) ? acpiCelsius : null;
            var ec = IsValidTemperature(ecCelsius) ? ecCelsius : null;
            if (acpi.HasValue && ec.HasValue)
            {
                return Math.Max(acpi.Value, ec.Value);
            }
            return acpi ?? ec;
        }

        public static double ResolveCriticalTemperature(
            double? acpiCelsius,
            double? ecCelsius,
            double fallbackCelsius = 86.0)
        {
            var acpi = IsValidCritical(acpiCelsius) ? acpiCelsius : null;
            var ec = IsValidCritical(ecCelsius) ? ecCelsius : null;
            if (acpi.HasValue && ec.HasValue)
            {
                return Math.Min(acpi.Value, ec.Value);
            }
            if (acpi.HasValue)
            {
                return acpi.Value;
            }
            if (ec.HasValue)
            {
                return ec.Value;
            }
            if (!IsValidCritical(fallbackCelsius))
            {
                throw new ArgumentOutOfRangeException(nameof(fallbackCelsius));
            }
            return fallbackCelsius;
        }

        private static bool IsValidTemperature(double? value)
        {
            return value.HasValue &&
                   !double.IsNaN(value.Value) &&
                   !double.IsInfinity(value.Value) &&
                   value.Value >= -20.0 && value.Value <= 150.0;
        }

        private static bool IsValidCritical(double? value)
        {
            return value.HasValue &&
                   !double.IsNaN(value.Value) &&
                   !double.IsInfinity(value.Value) &&
                   value.Value >= 70.0 && value.Value <= 110.0;
        }
    }
}
