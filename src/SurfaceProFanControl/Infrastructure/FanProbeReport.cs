using System.Globalization;
using System.Text;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public static class FanProbeReport
    {
        public static string ToJson(EcFanReading reading, EcThermalReading? thermal, string status)
        {
            return "{\r\n" +
                   "  \"success\": true,\r\n" +
                   $"  \"fan0Rpm\": {reading.Fan0Rpm},\r\n" +
                   $"  \"fan0DutyRaw\": {reading.Fan0DutyRaw},\r\n" +
                   $"  \"fan1Rpm\": {reading.Fan1Rpm},\r\n" +
                   $"  \"fan1DutyRaw\": {reading.Fan1DutyRaw},\r\n" +
                   $"  \"ecTemperatureCelsius\": {NumberOrNull(thermal?.TemperatureCelsius)},\r\n" +
                   $"  \"ecCriticalCelsius\": {NumberOrNull(thermal?.CriticalTemperatureCelsius)},\r\n" +
                   $"  \"status\": \"{Escape(status)}\"\r\n" +
                   "}\r\n";
        }

        private static string NumberOrNull(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.0", CultureInfo.InvariantCulture)
                : "null";
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }
    }
}
