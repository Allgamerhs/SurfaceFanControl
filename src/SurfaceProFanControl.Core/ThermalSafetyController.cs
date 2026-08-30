using System;

namespace SurfaceProFanControl.Core
{
    public enum SafetyDecision
    {
        None,
        EnterEmergency,
        StayEmergency,
        ExitEmergency
    }

    public sealed class ThermalSafetyController
    {
        private readonly double _triggerCelsius;
        private readonly double _releaseCelsius;
        private readonly int _releaseSamples;
        private int _coolSampleCount;

        public ThermalSafetyController(double triggerCelsius = 82.0, double releaseCelsius = 74.0, int releaseSamples = 5)
        {
            if (triggerCelsius <= releaseCelsius)
            {
                throw new ArgumentException("Acil eşik, bırakma eşiğinden yüksek olmalıdır.");
            }

            if (releaseSamples < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseSamples));
            }

            _triggerCelsius = triggerCelsius;
            _releaseCelsius = releaseCelsius;
            _releaseSamples = releaseSamples;
        }

        public bool IsEmergency { get; private set; }

        public SafetyDecision Evaluate(double? temperatureCelsius)
        {
            if (!temperatureCelsius.HasValue)
            {
                _coolSampleCount = 0;
                return IsEmergency ? SafetyDecision.StayEmergency : SafetyDecision.None;
            }

            if (!IsEmergency && temperatureCelsius.Value >= _triggerCelsius)
            {
                IsEmergency = true;
                _coolSampleCount = 0;
                return SafetyDecision.EnterEmergency;
            }

            if (!IsEmergency)
            {
                return SafetyDecision.None;
            }

            if (temperatureCelsius.Value <= _releaseCelsius)
            {
                _coolSampleCount++;
                if (_coolSampleCount >= _releaseSamples)
                {
                    IsEmergency = false;
                    _coolSampleCount = 0;
                    return SafetyDecision.ExitEmergency;
                }
            }
            else
            {
                _coolSampleCount = 0;
            }

            return SafetyDecision.StayEmergency;
        }
    }
}
