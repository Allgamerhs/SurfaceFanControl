using System;

namespace SurfaceProFanControl.Core
{
    public sealed class FanTestSafetyMonitor
    {
        private readonly double _stopTemperatureCelsius;
        private readonly int _maximumMissingSamples;
        private int _missingSamples;

        public FanTestSafetyMonitor(double stopTemperatureCelsius, int maximumMissingSamples = 3)
        {
            if (stopTemperatureCelsius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stopTemperatureCelsius));
            }
            if (maximumMissingSamples < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMissingSamples));
            }

            _stopTemperatureCelsius = stopTemperatureCelsius;
            _maximumMissingSamples = maximumMissingSamples;
        }

        public string? Evaluate(double? temperatureCelsius, bool onAcPower, int? batteryPercent)
        {
            if (!temperatureCelsius.HasValue)
            {
                _missingSamples++;
                return _missingSamples >= _maximumMissingSamples
                    ? "Sıcaklık telemetrisi üç kez kayboldu; işlemci yükü kesildi."
                    : null;
            }

            _missingSamples = 0;
            if (!onAcPower && batteryPercent.HasValue && batteryPercent.Value < 20)
            {
                return "Batarya %20'nin altına düştü; işlemci yükü kesildi.";
            }
            if (temperatureCelsius.Value >= _stopTemperatureCelsius)
            {
                return $"{_stopTemperatureCelsius:0} °C güvenlik sınırına ulaşıldı; işlemci yükü kesildi.";
            }

            return null;
        }
    }
}
