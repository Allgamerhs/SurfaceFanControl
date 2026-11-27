using System;
using Microsoft.Win32;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class UserSettings
    {
        public int CoolingAggressivenessPercent { get; set; }
        public int BoostMinutes { get; set; } = 15;
        public bool UsePowerProfiles { get; set; }
        public int AcAggressivenessPercent { get; set; } = 80;
        public int BatteryAggressivenessPercent { get; set; } = 20;
        public bool StartMinimized { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool CloseToTray { get; set; } = true;
        public bool NotificationsEnabled { get; set; } = true;
        public bool TelemetryLoggingEnabled { get; set; } = true;
        public int StartupDelaySeconds { get; set; } = 15;
        public string LanguageCode { get; set; } = string.Empty;
        public bool CustomCurveEnabled { get; set; } = true;
        public int CurveTurnOnCelsius { get; set; } = 70;
        public int CurveTurnOffCelsius { get; set; } = 62;
        public int CurveDwellSeconds { get; set; } = 60;
        public bool PredictiveCoolingEnabled { get; set; } = true;

        internal UserSettings Copy() => (UserSettings)MemberwiseClone();

        internal void Normalize()
        {
            CoolingAggressivenessPercent = Math.Max(0, Math.Min(100, CoolingAggressivenessPercent));
            AcAggressivenessPercent = Math.Max(0, Math.Min(100, AcAggressivenessPercent));
            BatteryAggressivenessPercent = Math.Max(0, Math.Min(100, BatteryAggressivenessPercent));
            StartupDelaySeconds = Math.Max(0, Math.Min(60, StartupDelaySeconds));
            if (StartupDelaySeconds != 0 && StartupDelaySeconds != 15 &&
                StartupDelaySeconds != 30 && StartupDelaySeconds != 60)
                StartupDelaySeconds = 15;
            if (CustomCurveEnabled) UsePowerProfiles = false;
            CurveTurnOnCelsius = Math.Max(50, Math.Min(90, CurveTurnOnCelsius));
            CurveTurnOffCelsius = Math.Max(40, Math.Min(CurveTurnOnCelsius - 3, CurveTurnOffCelsius));
            if (!IsSupportedCurveDwell(CurveDwellSeconds))
            {
                CurveDwellSeconds = 60;
            }
            LanguageCode = string.IsNullOrWhiteSpace(LanguageCode)
                ? string.Empty
                : LocalizationCatalog.NormalizeLanguageCode(LanguageCode);
            if (!IsSupportedBoost(BoostMinutes))
            {
                BoostMinutes = 15;
            }
        }

        internal static bool IsSupportedCurveDwell(int seconds) =>
            seconds == 30 || seconds == 60 || seconds == 120 || seconds == 300;

        internal static bool IsSupportedBoost(int minutes) =>
            minutes == 5 || minutes == 15 || minutes == 30 || minutes == 60;
    }

    public sealed class UserSettingsStore
    {
        private const string SettingsKeyPath = @"Software\SurfaceProFanControl\Ui";
        private readonly string _keyPath;

        public UserSettingsStore() : this(SettingsKeyPath) { }

        internal UserSettingsStore(string keyPath)
        {
            _keyPath = keyPath;
        }

        public UserSettings Load()
        {
            var settings = new UserSettings();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false))
                {
                    if (key != null)
                    {
                        settings.CoolingAggressivenessPercent = ReadInteger(
                            key, "CoolingAggressivenessPercent", 0);
                        settings.BoostMinutes = ReadInteger(key, "BoostMinutes", 15);
                        settings.UsePowerProfiles = ReadBoolean(key, "UsePowerProfiles", false);
                        settings.AcAggressivenessPercent = ReadInteger(key, "AcAggressivenessPercent", 80);
                        settings.BatteryAggressivenessPercent = ReadInteger(key, "BatteryAggressivenessPercent", 20);
                        settings.StartMinimized = ReadBoolean(key, "StartMinimized", false);
                        settings.AlwaysOnTop = ReadBoolean(key, "AlwaysOnTop", false);
                        settings.CloseToTray = ReadBoolean(key, "CloseToTray", true);
                        settings.NotificationsEnabled = ReadBoolean(key, "NotificationsEnabled", true);
                        settings.TelemetryLoggingEnabled = ReadBoolean(key, "TelemetryLoggingEnabled", true);
                        settings.StartupDelaySeconds = ReadInteger(key, "StartupDelaySeconds", 15);
                        settings.LanguageCode = Convert.ToString(key.GetValue(
                            "LanguageCode", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? string.Empty;
                        // Before curves existed, a stored aggressiveness or power profile was the active policy.
                        var hasLegacyPolicy = key.GetValue("CoolingAggressivenessPercent") != null ||
                            key.GetValue("UsePowerProfiles") != null;
                        settings.CustomCurveEnabled = ReadBoolean(key, "CustomCurveEnabled", !hasLegacyPolicy);
                        settings.CurveTurnOnCelsius = ReadInteger(key, "CurveTurnOnCelsius", 70);
                        settings.CurveTurnOffCelsius = ReadInteger(key, "CurveTurnOffCelsius", 62);
                        settings.CurveDwellSeconds = ReadInteger(key, "CurveDwellSeconds", 60);
                        settings.PredictiveCoolingEnabled = ReadBoolean(key, "PredictiveCoolingEnabled", true);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Kullanıcı fan ayarları okunamadı; güvenli varsayılanlar kullanılıyor.", ex);
                settings = new UserSettings();
            }

            settings.Normalize();
            return settings;
        }

        public void Save(UserSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Normalize();
            using (var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Kullanıcı fan ayarları kayıt anahtarı açılamadı.");
                }

                key.SetValue("CoolingAggressivenessPercent", settings.CoolingAggressivenessPercent,
                    RegistryValueKind.DWord);
                key.SetValue("BoostMinutes", settings.BoostMinutes, RegistryValueKind.DWord);
                key.SetValue("UsePowerProfiles", settings.UsePowerProfiles ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AcAggressivenessPercent", settings.AcAggressivenessPercent, RegistryValueKind.DWord);
                key.SetValue("BatteryAggressivenessPercent", settings.BatteryAggressivenessPercent, RegistryValueKind.DWord);
                key.SetValue("StartMinimized", settings.StartMinimized ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AlwaysOnTop", settings.AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CloseToTray", settings.CloseToTray ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("NotificationsEnabled", settings.NotificationsEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TelemetryLoggingEnabled", settings.TelemetryLoggingEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("StartupDelaySeconds", settings.StartupDelaySeconds, RegistryValueKind.DWord);
                key.SetValue("LanguageCode", settings.LanguageCode ?? string.Empty, RegistryValueKind.String);
                key.SetValue("CustomCurveEnabled", settings.CustomCurveEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CurveTurnOnCelsius", settings.CurveTurnOnCelsius, RegistryValueKind.DWord);
                key.SetValue("CurveTurnOffCelsius", settings.CurveTurnOffCelsius, RegistryValueKind.DWord);
                key.SetValue("CurveDwellSeconds", settings.CurveDwellSeconds, RegistryValueKind.DWord);
                key.SetValue("PredictiveCoolingEnabled", settings.PredictiveCoolingEnabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static int ReadInteger(RegistryKey key, string name, int fallback)
        {
            var value = key.GetValue(name, fallback, RegistryValueOptions.DoNotExpandEnvironmentNames);
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception) when (value != null)
            {
                return fallback;
            }
        }

        private static bool ReadBoolean(RegistryKey key, string name, bool fallback)
        {
            var value = ReadInteger(key, name, fallback ? 1 : 0);
            if (value == 0) return false;
            if (value == 1) return true;
            return fallback;
        }
    }
}
