using System;

namespace SurfaceProFanControl.Core
{
    public static class TemperatureNormalizer
    {
        public static double? ToCelsius(double rawValue)
        {
            if (double.IsNaN(rawValue) || double.IsInfinity(rawValue) || rawValue <= 0)
            {
                return null;
            }

            // Standard ACPI reports tenths of kelvin. Surface Pro 1 firmware instead
            // exposes direct Celsius values (for example 75 with a critical point of 86).
            var celsius = rawValue >= 1500
                ? (rawValue / 10.0) - 273.15
                : rawValue;

            if (celsius < -20 || celsius > 150)
            {
                return null;
            }

            return Math.Round(celsius, 1, MidpointRounding.AwayFromZero);
        }
    }
}
