using System;

namespace SurfaceProFanControl.Core
{
    public static class BinaryFanCurvePolicy
    {
        public const double PredictiveLeadCelsius = 3.0;
        public const double PredictiveTrendCelsiusPerMinute = 2.0;

        public static bool ShouldUseMaximum(
            double turnOnCelsius,
            double turnOffCelsius,
            TimeSpan minimumDwell,
            double? temperatureCelsius,
            double trendCelsiusPerMinute,
            bool predictiveCoolingEnabled,
            bool maximumIsActive,
            TimeSpan timeInCurrentMode,
            bool timedBoostActive,
            bool emergencyActive)
        {
            Validate(turnOnCelsius, turnOffCelsius, minimumDwell);

            if (emergencyActive || timedBoostActive)
            {
                return true;
            }
            if (!temperatureCelsius.HasValue)
            {
                return false;
            }
            if (timeInCurrentMode < minimumDwell)
            {
                return maximumIsActive;
            }
            if (maximumIsActive)
            {
                return temperatureCelsius.Value > turnOffCelsius;
            }

            var activation = turnOnCelsius;
            if (predictiveCoolingEnabled && trendCelsiusPerMinute >= PredictiveTrendCelsiusPerMinute)
            {
                activation -= PredictiveLeadCelsius;
            }
            return temperatureCelsius.Value >= activation;
        }

        public static void Validate(double turnOnCelsius, double turnOffCelsius, TimeSpan minimumDwell)
        {
            if (turnOnCelsius < 50.0 || turnOnCelsius > 90.0)
            {
                throw new ArgumentOutOfRangeException(nameof(turnOnCelsius));
            }
            if (turnOffCelsius < 40.0 || turnOffCelsius > turnOnCelsius - 3.0)
            {
                throw new ArgumentOutOfRangeException(nameof(turnOffCelsius));
            }
            if (minimumDwell < TimeSpan.FromSeconds(15) || minimumDwell > TimeSpan.FromMinutes(5))
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDwell));
            }
        }
    }
}
