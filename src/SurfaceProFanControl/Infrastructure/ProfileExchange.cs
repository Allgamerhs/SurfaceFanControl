using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    [DataContract]
    public sealed class CoolingProfileFile
    {
        [DataMember(Order = 1, IsRequired = true)] public int FormatVersion { get; set; } = 2;
        [DataMember(Order = 2, IsRequired = true)] public int AggressivenessPercent { get; set; }
        [DataMember(Order = 3)] public bool UsePowerProfiles { get; set; }
        [DataMember(Order = 4, IsRequired = true)] public int AcAggressivenessPercent { get; set; }
        [DataMember(Order = 5, IsRequired = true)] public int BatteryAggressivenessPercent { get; set; }
        [DataMember(Order = 6, IsRequired = true)] public int BoostMinutes { get; set; }
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
            Validate(profile);
            var destination = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetRandomFileName());
            var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (output)
                {
                    var serializer = new DataContractJsonSerializer(typeof(CoolingProfileFile));
                    serializer.WriteObject(output, profile);
                    output.Flush(true);
                }
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public static void Import(string path, UserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var serializer = new DataContractJsonSerializer(typeof(CoolingProfileFile));
            CoolingProfileFile profile;
            bool hasCurveSelection;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > MaximumProfileBytes)
                    throw new InvalidDataException("Profile file is empty or larger than 16 KB.");
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true))
                {
                    // The data contract reader can silently merge two concatenated JSON objects.
                    // Check the document boundary before it enforces the profile's members and types.
                    var members = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd())
                        as IDictionary<string, object>;
                    if (members == null)
                        throw new InvalidDataException("Profile must contain one JSON object.");
                    hasCurveSelection = members.ContainsKey(nameof(CoolingProfileFile.CustomCurveEnabled));
                }
                stream.Position = 0;
                profile = serializer.ReadObject(stream) as CoolingProfileFile
                    ?? throw new SerializationException("Profile data is missing.");
            }
            if (profile.FormatVersion == 2 && !hasCurveSelection)
                throw new InvalidDataException("Version 2 profile has no custom curve selection.");
            Validate(profile);
            settings.CoolingAggressivenessPercent = profile.AggressivenessPercent;
            settings.UsePowerProfiles = profile.UsePowerProfiles;
            settings.AcAggressivenessPercent = profile.AcAggressivenessPercent;
            settings.BatteryAggressivenessPercent = profile.BatteryAggressivenessPercent;
            settings.BoostMinutes = profile.BoostMinutes;
            settings.CustomCurveEnabled = profile.FormatVersion >= 2 && profile.CustomCurveEnabled;
            if (profile.FormatVersion >= 2)
            {
                settings.CurveTurnOnCelsius = profile.CurveTurnOnCelsius;
                settings.CurveTurnOffCelsius = profile.CurveTurnOffCelsius;
                settings.CurveDwellSeconds = profile.CurveDwellSeconds;
                settings.PredictiveCoolingEnabled = profile.PredictiveCoolingEnabled;
            }
        }

        private static void Validate(CoolingProfileFile profile)
        {
            if (profile.FormatVersion != 1 && profile.FormatVersion != 2)
                throw new InvalidDataException("Unsupported profile format version.");
            ValidatePercent(profile.AggressivenessPercent);
            ValidatePercent(profile.AcAggressivenessPercent);
            ValidatePercent(profile.BatteryAggressivenessPercent);
            if (!UserSettings.IsSupportedBoost(profile.BoostMinutes))
                throw new InvalidDataException("Unsupported boost duration.");
            if (profile.FormatVersion == 1) return;

            if (profile.CustomCurveEnabled && profile.UsePowerProfiles)
                throw new InvalidDataException("Select either the custom curve or power profiles.");
            if (!UserSettings.IsSupportedCurveDwell(profile.CurveDwellSeconds))
                throw new InvalidDataException("Unsupported fan curve dwell time.");
            try
            {
                BinaryFanCurvePolicy.Validate(profile.CurveTurnOnCelsius, profile.CurveTurnOffCelsius,
                    TimeSpan.FromSeconds(profile.CurveDwellSeconds));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidDataException("Invalid fan curve temperature thresholds.", ex);
            }
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
