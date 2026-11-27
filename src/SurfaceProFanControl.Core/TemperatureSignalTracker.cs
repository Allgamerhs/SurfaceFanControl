using System;
using System.Collections.Generic;
using System.Linq;

namespace SurfaceProFanControl.Core
{
    public sealed class TemperatureSignalTracker
    {
        private const int WindowSize = 3;
        private readonly Queue<TemperatureSample> _samples = new Queue<TemperatureSample>();
        private double? _minimum;
        private double? _maximum;

        public void Add(double? temperatureCelsius, DateTime timestampUtc)
        {
            if (!temperatureCelsius.HasValue || double.IsNaN(temperatureCelsius.Value) ||
                double.IsInfinity(temperatureCelsius.Value) || temperatureCelsius.Value < -20.0 ||
                temperatureCelsius.Value > 120.0)
            {
                return;
            }

            var timestamp = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : timestampUtc.ToUniversalTime();
            if (_samples.Count > 0 && timestamp < _samples.Last().TimestampUtc)
                _samples.Clear();
            _samples.Enqueue(new TemperatureSample(temperatureCelsius.Value, timestamp));
            while (_samples.Count > WindowSize)
            {
                _samples.Dequeue();
            }

            _minimum = !_minimum.HasValue ? temperatureCelsius : Math.Min(_minimum.Value, temperatureCelsius.Value);
            _maximum = !_maximum.HasValue ? temperatureCelsius : Math.Max(_maximum.Value, temperatureCelsius.Value);
        }

        public TemperatureSignal Read(DateTime nowUtc, TimeSpan staleAfter)
        {
            if (staleAfter <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(staleAfter));
            }
            if (_samples.Count == 0)
            {
                return TemperatureSignal.Empty;
            }

            var now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
            var last = _samples.Last();
            var age = now < last.TimestampUtc ? TimeSpan.MaxValue : now - last.TimestampUtc;
            if (age >= staleAfter)
            {
                return new TemperatureSignal(last.Celsius, _minimum, _maximum, 0.0, age, true, 0);
            }

            var fresh = _samples.Where(sample => now - sample.TimestampUtc < staleAfter).ToArray();
            var ordered = fresh.Select(sample => sample.Celsius).OrderBy(value => value).ToArray();
            var median = ordered[ordered.Length / 2];
            var first = fresh[0];
            var elapsedMinutes = (last.TimestampUtc - first.TimestampUtc).TotalMinutes;
            var trend = elapsedMinutes >= (1.0 / 60.0)
                ? (last.Celsius - first.Celsius) / elapsedMinutes
                : 0.0;
            return new TemperatureSignal(
                median,
                _minimum,
                _maximum,
                trend,
                age,
                false,
                fresh.Length);
        }

        private readonly struct TemperatureSample
        {
            public TemperatureSample(double celsius, DateTime timestampUtc)
            {
                Celsius = celsius;
                TimestampUtc = timestampUtc;
            }

            public double Celsius { get; }
            public DateTime TimestampUtc { get; }
        }
    }

    public readonly struct TemperatureSignal
    {
        internal TemperatureSignal(
            double? temperatureCelsius,
            double? minimumCelsius,
            double? maximumCelsius,
            double trendCelsiusPerMinute,
            TimeSpan age,
            bool isStale,
            int sampleCount)
        {
            TemperatureCelsius = temperatureCelsius;
            MinimumCelsius = minimumCelsius;
            MaximumCelsius = maximumCelsius;
            TrendCelsiusPerMinute = trendCelsiusPerMinute;
            Age = age;
            IsStale = isStale;
            SampleCount = sampleCount;
        }

        public static TemperatureSignal Empty { get; } = new TemperatureSignal(
            null, null, null, 0.0, TimeSpan.MaxValue, true, 0);

        public double? TemperatureCelsius { get; }
        public double? MinimumCelsius { get; }
        public double? MaximumCelsius { get; }
        public double TrendCelsiusPerMinute { get; }
        public TimeSpan Age { get; }
        public bool IsStale { get; }
        public int SampleCount { get; }
        public double? ControlTemperatureCelsius => IsStale ? null : TemperatureCelsius;
    }
}
