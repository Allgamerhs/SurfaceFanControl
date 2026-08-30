using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class TelemetryHistoryStore
    {
        private const long MaximumBytes = 512 * 1024;
        private const int RetainedLines = 2000;
        private static readonly object Gate = new object();
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public static string CsvPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfaceProFanControl",
            "telemetry.csv");

        public void Append(TelemetrySnapshot telemetry, string mode)
        {
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));
            if (DateTime.UtcNow - _lastWriteUtc < TimeSpan.FromSeconds(10)) return;

            lock (Gate)
            {
                if (DateTime.UtcNow - _lastWriteUtc < TimeSpan.FromSeconds(10)) return;
                var directory = Path.GetDirectoryName(CsvPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(CsvPath))
                {
                    File.WriteAllText(CsvPath,
                        "timestamp,temp_c,critical_c,cpu_percent,ac_power,battery_percent,mode\r\n",
                        new UTF8Encoding(false));
                }
                var line = string.Join(",",
                    telemetry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    Number(telemetry.TemperatureCelsius),
                    Number(telemetry.FirmwareCriticalCelsius),
                    Integer(telemetry.CpuLoadPercent),
                    telemetry.OnAcPower ? "1" : "0",
                    Integer(telemetry.BatteryPercent),
                    SanitizeMode(mode));
                File.AppendAllText(CsvPath, line + "\r\n", new UTF8Encoding(false));
                _lastWriteUtc = DateTime.UtcNow;
                TrimIfNeeded();
            }
        }

        private static void TrimIfNeeded()
        {
            var info = new FileInfo(CsvPath);
            if (info.Length <= MaximumBytes) return;
            var lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
            var retained = lines.Skip(Math.Max(1, lines.Length - RetainedLines)).ToArray();
            var output = new[] { lines[0] }.Concat(retained);
            File.WriteAllLines(CsvPath, output, new UTF8Encoding(false));
        }

        private static string Number(double? value) =>
            value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

        private static string Integer(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private static string SanitizeMode(string mode) =>
            (mode ?? string.Empty).Replace(",", "_").Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
