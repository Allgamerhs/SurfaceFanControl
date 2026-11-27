using System;
using System.Globalization;
using System.Text;

namespace SurfaceProFanControl.Infrastructure
{
    public static class ProbeReport
    {
        public static string ToJson(DeviceSnapshot device, TelemetrySnapshot telemetry)
        {
            var supported = device.Identity.MatchesSurfacePro1Target(out var reason);
            var builder = new StringBuilder();
            builder.AppendLine("{");
            Append(builder, "manufacturer", device.Identity.Manufacturer, true);
            Append(builder, "model", device.Identity.Model, true);
            Append(builder, "biosVersion", device.Identity.BiosVersion, true);
            Append(builder, "processor", device.Processor, true);
            Append(builder, "operatingSystem", device.OperatingSystem, true);
            builder.AppendLine($"  \"targetMatched\": {supported.ToString().ToLowerInvariant()},");
            Append(builder, "targetReason", reason, true);
            AppendNumber(builder, "temperatureCelsius", telemetry.TemperatureCelsius, true);
            AppendNumber(builder, "firmwareCriticalCelsius", telemetry.FirmwareCriticalCelsius, true);
            builder.AppendLine($"  \"cpuLoadPercent\": {(telemetry.CpuLoadPercent.HasValue ? telemetry.CpuLoadPercent.Value.ToString(CultureInfo.InvariantCulture) : "null")},");
            builder.AppendLine($"  \"onAcPower\": {telemetry.OnAcPower?.ToString().ToLowerInvariant() ?? "null"},");
            builder.AppendLine($"  \"batteryPercent\": {(telemetry.BatteryPercent.HasValue ? telemetry.BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string key, string value, bool comma)
        {
            builder.Append("  \"").Append(Escape(key)).Append("\": \"").Append(Escape(value)).Append('"');
            builder.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendNumber(StringBuilder builder, string key, double? value, bool comma)
        {
            builder.Append("  \"").Append(Escape(key)).Append("\": ");
            builder.Append(value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "null");
            builder.AppendLine(comma ? "," : string.Empty);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
