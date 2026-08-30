using System;
using System.Management;
using System.Runtime.InteropServices;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class TelemetrySnapshot
    {
        public TelemetrySnapshot(double? temperatureCelsius, double? firmwareCriticalCelsius, int? cpuLoadPercent, bool onAcPower, int? batteryPercent, DateTime timestamp)
        {
            TemperatureCelsius = temperatureCelsius;
            FirmwareCriticalCelsius = firmwareCriticalCelsius;
            CpuLoadPercent = cpuLoadPercent;
            OnAcPower = onAcPower;
            BatteryPercent = batteryPercent;
            Timestamp = timestamp;
        }

        public double? TemperatureCelsius { get; }
        public double? FirmwareCriticalCelsius { get; }
        public int? CpuLoadPercent { get; }
        public bool OnAcPower { get; }
        public int? BatteryPercent { get; }
        public DateTime Timestamp { get; }
    }

    public sealed class TelemetryService
    {
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

        public TelemetrySnapshot Read()
        {
            return ReadCore(includeCpuLoad: true);
        }

        public TelemetrySnapshot ReadSafety()
        {
            return ReadCore(includeCpuLoad: false);
        }

        private static TelemetrySnapshot ReadCore(bool includeCpuLoad)
        {
            double? temperature = null;
            double? critical = null;
            int? cpuLoad = null;

            try
            {
                temperature = ReadThermalZone(out critical);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ACPI sıcaklık sensörü okunamadı.", ex);
            }

            try
            {
                if (includeCpuLoad)
                {
                    cpuLoad = ReadCpuLoad();
                }
            }
            catch (Exception ex)
            {
                // A missing performance counter must not suppress thermal safety.
                AppLogger.Error("CPU yük sayacı okunamadı; sıcaklık koruması çalışmaya devam edecek.", ex);
            }

            ReadPowerStatus(out var onAcPower, out var batteryPercent);
            return new TelemetrySnapshot(temperature, critical, cpuLoad, onAcPower, batteryPercent, DateTime.Now);
        }

        private static double? ReadThermalZone(out double? critical)
        {
            critical = null;
            double? firstTemperature = null;
            double? firstCritical = null;

            using (var searcher = new ManagementObjectSearcher(
                new ManagementScope("root\\wmi"),
                new ObjectQuery("SELECT InstanceName, CurrentTemperature, CriticalTripPoint FROM MSAcpi_ThermalZoneTemperature")))
            {
                searcher.Options.Timeout = QueryTimeout;
                using (var results = searcher.Get())
                {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var current = TemperatureNormalizer.ToCelsius(Convert.ToDouble(item["CurrentTemperature"]));
                        var currentCritical = TemperatureNormalizer.ToCelsius(Convert.ToDouble(item["CriticalTripPoint"]));
                        firstTemperature = firstTemperature ?? current;
                        firstCritical = firstCritical ?? currentCritical;

                        var name = Convert.ToString(item["InstanceName"]) ?? string.Empty;
                        if (name.IndexOf("TZ0", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            critical = currentCritical;
                            return current;
                        }
                    }
                }
                }
            }

            critical = firstCritical;
            return firstTemperature;
        }

        private static int? ReadCpuLoad()
        {
            using (var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'"))
            {
                searcher.Options.Timeout = QueryTimeout;
                using (var results = searcher.Get())
                {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var value = Convert.ToInt32(item["PercentProcessorTime"]);
                        return Math.Max(0, Math.Min(100, value));
                    }
                }
                }
            }

            return null;
        }

        private static void ReadPowerStatus(out bool onAcPower, out int? batteryPercent)
        {
            if (!GetSystemPowerStatus(out var status))
            {
                onAcPower = false;
                batteryPercent = null;
                return;
            }

            onAcPower = status.ACLineStatus == 1;
            batteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : (int?)null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
    }
}
