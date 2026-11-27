using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class TelemetryHistoryStore
    {
        private const long MaximumBytes = 512 * 1024;
        private const int RetainedLines = 2000;
        private static readonly object Gate = new object();
        private const string Header = "timestamp,temp_c,critical_c,cpu_percent,ac_power,battery_percent,mode";
        private static readonly Encoding CsvEncoding = new UTF8Encoding(false);
        private static Task _pendingWrite = Task.CompletedTask;
        private static long? _lastAttempt;
        private static bool _reportedFailure;

        public static string CsvPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfaceProFanControl",
            "telemetry.csv");

        public void Append(TelemetrySnapshot telemetry, string mode)
        {
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));
            lock (Gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (!_pendingWrite.IsCompleted || now - _lastAttempt < 10 * Stopwatch.Frequency) return;
                _lastAttempt = now;
                _pendingWrite = Task.Run(() => Write(telemetry, mode));
            }
        }

        private static void Write(TelemetrySnapshot telemetry, string mode)
        {
            try
            {
                var directory = Path.GetDirectoryName(CsvPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var line = string.Join(",",
                    telemetry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    Number(telemetry.TemperatureCelsius),
                    Number(telemetry.FirmwareCriticalCelsius),
                    Integer(telemetry.CpuLoadPercent),
                    telemetry.OnAcPower.HasValue ? (telemetry.OnAcPower.Value ? "1" : "0") : string.Empty,
                    Integer(telemetry.BatteryPercent),
                    SanitizeMode(mode));
                using (var stream = new FileStream(CsvPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, CsvEncoding))
                {
                    if (stream.Length == 0) writer.WriteLine(Header);
                    writer.WriteLine(line);
                }
                TrimIfNeeded();
                _reportedFailure = false;
            }
            catch (Exception ex)
            {
                if (!_reportedFailure) AppLogger.Error("Telemetri geçmişi yazılamadı.", ex);
                _reportedFailure = true;
            }
        }

        private static void TrimIfNeeded()
        {
            var info = new FileInfo(CsvPath);
            if (info.Length <= MaximumBytes) return;
            var retained = new Queue<string>(RetainedLines);
            string header;
            using (var reader = new StreamReader(CsvPath, CsvEncoding))
            {
                header = reader.ReadLine() ?? Header;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (retained.Count == RetainedLines) retained.Dequeue();
                    retained.Enqueue(line);
                }
            }

            var temporaryPath = CsvPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporaryPath, false, CsvEncoding))
                {
                    writer.WriteLine(header);
                    foreach (var line in retained) writer.WriteLine(line);
                }
                File.Replace(temporaryPath, CsvPath, null);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        private static string Number(double? value) =>
            value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

        private static string Integer(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private static string SanitizeMode(string mode) =>
            (mode ?? string.Empty).Replace(",", "_").Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
