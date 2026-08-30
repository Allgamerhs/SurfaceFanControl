using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SurfaceProFanControl.Infrastructure
{
    [DataContract]
    public sealed class CoolingProfileFile
    {
        [DataMember(Order = 1)] public int FormatVersion { get; set; } = 2;
        [DataMember(Order = 2)] public int AggressivenessPercent { get; set; }
        [DataMember(Order = 3)] public bool UsePowerProfiles { get; set; }
        [DataMember(Order = 4)] public int AcAggressivenessPercent { get; set; }
        [DataMember(Order = 5)] public int BatteryAggressivenessPercent { get; set; }
        [DataMember(Order = 6)] public int BoostMinutes { get; set; }
        [DataMember(Order = 7)] public bool CustomCurveEnabled { get; set; }
        [DataMember(Order = 8)] public int CurveTurnOnCelsius { get; set; }
        [DataMember(Order = 9)] public int CurveTurnOffCelsius { get; set; }
        [DataMember(Order = 10)] public int CurveDwellSeconds { get; set; }
        [DataMember(Order = 11)] public bool PredictiveCoolingEnabled { get; set; }
    }

    public static class ProfileExchange
    {
        private const int MaximumProfileBytes = 16 * 1024;

        public static void Export(string path, UserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Normalize();
            var profile = new CoolingProfileFile
            {
                AggressivenessPercent = settings.CoolingAggressivenessPercent,
                UsePowerProfiles = settings.UsePowerProfiles,
                AcAggressivenessPercent = settings.AcAggressivenessPercent,
                BatteryAggressivenessPercent = settings.BatteryAggressivenessPercent,
                BoostMinutes = settings.BoostMinutes,
                CustomCurveEnabled = settings.CustomCurveEnabled,
                CurveTurnOnCelsius = settings.CurveTurnOnCelsius,
                CurveTurnOffCelsius = settings.CurveTurnOffCelsius,
                CurveDwellSeconds = settings.CurveDwellSeconds,
                PredictiveCoolingEnabled = settings.PredictiveCoolingEnabled
            };
            var serializer = new DataContractJsonSerializer(typeof(CoolingProfileFile));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, profile);
                stream.Flush(true);
            }
        }

        public static void Import(string path, UserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > MaximumProfileBytes)
            {
                throw new InvalidDataException("Profile file is empty or larger than 16 KB.");
            }
            var serializer = new DataContractJsonSerializer(typeof(CoolingProfileFile));
            CoolingProfileFile profile;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                profile = serializer.ReadObject(stream) as CoolingProfileFile
                    ?? throw new SerializationException("Profile data is missing.");
            }
            if (profile.FormatVersion != 1 && profile.FormatVersion != 2)
            {
                throw new InvalidDataException("Unsupported profile format version.");
            }
            ValidatePercent(profile.AggressivenessPercent);
            ValidatePercent(profile.AcAggressivenessPercent);
            ValidatePercent(profile.BatteryAggressivenessPercent);
            if (profile.BoostMinutes != 5 && profile.BoostMinutes != 15 &&
                profile.BoostMinutes != 30 && profile.BoostMinutes != 60)
            {
                throw new InvalidDataException("Unsupported boost duration.");
            }

            settings.CoolingAggressivenessPercent = profile.AggressivenessPercent;
            settings.UsePowerProfiles = profile.UsePowerProfiles;
            settings.AcAggressivenessPercent = profile.AcAggressivenessPercent;
            settings.BatteryAggressivenessPercent = profile.BatteryAggressivenessPercent;
            settings.BoostMinutes = profile.BoostMinutes;
            if (profile.FormatVersion >= 2)
            {
                settings.CustomCurveEnabled = profile.CustomCurveEnabled;
                settings.CurveTurnOnCelsius = profile.CurveTurnOnCelsius;
                settings.CurveTurnOffCelsius = profile.CurveTurnOffCelsius;
                settings.CurveDwellSeconds = profile.CurveDwellSeconds;
                settings.PredictiveCoolingEnabled = profile.PredictiveCoolingEnabled;
            }
            settings.Normalize();
        }

        private static void ValidatePercent(int value)
        {
            if (value < 0 || value > 100)
            {
                throw new InvalidDataException("Profile percentage must be between 0 and 100.");
            }
        }
    }
}
