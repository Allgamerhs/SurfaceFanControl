using System;

namespace SurfaceProFanControl.Core
{
    /// <summary>
    /// Converts a user-facing cooling aggressiveness value into the two fan
    /// states that are proven safe on the target firmware: AUTO and MAX.
    /// It is deliberately not presented as PWM or an exact RPM setpoint.
    /// </summary>
    public static class CoolingAggressivenessPolicy
    {
        public static readonly TimeSpan MinimumDwell = TimeSpan.FromSeconds(60);

        public static CoolingThresholds GetThresholds(int aggressivenessPercent)
        {
            if (aggressivenessPercent < 0 || aggressivenessPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(aggressivenessPercent));
            }

            if (aggressivenessPercent == 0)
            {
                return new CoolingThresholds(double.PositiveInfinity, double.PositiveInfinity);
            }
            if (aggressivenessPercent == 100)
            {
                return new CoolingThresholds(double.NegativeInfinity, double.NegativeInfinity);
            }

            // 1% waits until about 80 C; 99% starts around 55 C.
            var turnOn = 80.0 - ((aggressivenessPercent - 1) * (25.0 / 98.0));
            return new CoolingThresholds(turnOn, turnOn - 5.0);
        }

        public static bool ShouldUseMaximum(
            int aggressivenessPercent,
            double? temperatureCelsius,
            bool maximumIsActive,
            TimeSpan timeInCurrentMode,
            bool timedBoostActive)
        {
            if (timedBoostActive || aggressivenessPercent == 100)
            {
                return true;
            }
            if (aggressivenessPercent == 0 || !temperatureCelsius.HasValue)
            {
                return false;
            }
            if (timeInCurrentMode < MinimumDwell)
            {
                return maximumIsActive;
            }

            var thresholds = GetThresholds(aggressivenessPercent);
            return maximumIsActive
                ? temperatureCelsius.Value > thresholds.TurnOffCelsius
                : temperatureCelsius.Value >= thresholds.TurnOnCelsius;
        }
    }

    public readonly struct CoolingThresholds
    {
        public CoolingThresholds(double turnOnCelsius, double turnOffCelsius)
        {
            TurnOnCelsius = turnOnCelsius;
            TurnOffCelsius = turnOffCelsius;
        }

        public double TurnOnCelsius { get; }
        public double TurnOffCelsius { get; }
    }
}
