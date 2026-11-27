using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class TelemetrySnapshot
    {
        public TelemetrySnapshot(double? temperatureCelsius, double? firmwareCriticalCelsius, int? cpuLoadPercent, bool onAcPower, int? batteryPercent, DateTime timestamp)
            : this(temperatureCelsius, firmwareCriticalCelsius, cpuLoadPercent, onAcPower, batteryPercent, timestamp, timestamp) { }

        internal TelemetrySnapshot(double? temperatureCelsius, double? firmwareCriticalCelsius, int? cpuLoadPercent, bool? onAcPower, int? batteryPercent, DateTime timestamp, DateTime temperatureTimestamp)
        {
            TemperatureTimestamp = temperatureTimestamp;
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
        public bool? OnAcPower { get; }
        public DateTime TemperatureTimestamp { get; }
        public int? BatteryPercent { get; }
        public DateTime Timestamp { get; }
    }

    public sealed class TelemetryService
    {
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
        private static readonly bool SingleProcessorGroup = GetActiveProcessorGroupCount() == 1;
        private const int CpuReadingMaxAgeSeconds = 10;
        private readonly object _thermalGate = new object();
        private Task<(double? Celsius, double? Critical, DateTime Timestamp)>? _thermalQuery;
        private (double? Celsius, double? Critical, DateTime Timestamp) _thermalSample;
        private readonly object _cpuGate = new object();
        private CpuTimes? _previousCpu = SingleProcessorGroup ? ReadCpuTimes() : null;
        private Task<int?>? _systemWideQuery;
        private long _systemWideQueryStarted;
        private long _systemWideSampleStarted;
        private int? _systemWideCpu;

        // A one-shot probe has no previous sampling interval. The Windows formatted
        // counter supplies its first system-wide percentage without a warm-up poll.
        public TelemetrySnapshot Read() => ReadCore(oneShot: true);

        public TelemetrySnapshot ReadForDisplay() => ReadCore(oneShot: false);

        public TelemetrySnapshot ReadSafety() => ReadForDisplay();

        private TelemetrySnapshot ReadCore(bool oneShot)
        {
            var thermal = oneShot ? SampleThermalZone() : ReadLatestThermalZone();
            var cpuLoad = oneShot ? ReadFormattedCpuLoad() : ReadDisplayCpuLoad();
            ReadPowerStatus(out var onAcPower, out var batteryPercent);
            return new TelemetrySnapshot(thermal.Celsius, thermal.Critical, cpuLoad, onAcPower, batteryPercent, DateTime.Now, thermal.Timestamp);
        }

        // A slow WMI provider must not hold the heartbeat or an operator's RPM request.
        private (double? Celsius, double? Critical, DateTime Timestamp) ReadLatestThermalZone()
        {
            lock (_thermalGate)
            {
                if (_thermalQuery?.IsCompleted == true)
                {
                    _thermalSample = _thermalQuery.GetAwaiter().GetResult();
                    _thermalQuery = null;
                }
                if (_thermalQuery == null) _thermalQuery = Task.Run(SampleThermalZone);
                var age = DateTime.UtcNow - _thermalSample.Timestamp;
                return age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(20)
                    ? _thermalSample : (null, null, _thermalSample.Timestamp);
            }
        }

        private static (double? Celsius, double? Critical, DateTime Timestamp) SampleThermalZone()
        {
            var started = DateTime.UtcNow;
            try
            {
                var temperature = ReadThermalZone(out var critical);
                return (temperature, critical, started);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ACPI sıcaklık sensörü okunamadı.", ex);
                return (null, null, started);
            }
        }

        private int? ReadDisplayCpuLoad()
        {
            lock (_cpuGate)
            {
                if (!SingleProcessorGroup) return ReadMultiGroupCpuLoad(Stopwatch.GetTimestamp());
                var previous = _previousCpu;
                _previousCpu = ReadCpuTimes();
                return previous.HasValue && _previousCpu.HasValue
                    ? CpuUsageSince(previous.Value, _previousCpu.Value)
                    : null;
            }
        }

        // GetSystemTimes covers only the calling processor group on these PCs.
        // Keep one system-wide query outstanding; fan control never waits for it.
        private int? ReadMultiGroupCpuLoad(long now)
        {
            if (_systemWideQuery != null && _systemWideQuery.IsCompleted)
            {
                _systemWideCpu = _systemWideQuery.GetAwaiter().GetResult();
                _systemWideSampleStarted = _systemWideQueryStarted;
                _systemWideQuery = null;
            }
            if (_systemWideQuery == null && (_systemWideQueryStarted == 0 ||
                now - _systemWideQueryStarted >= 5L * Stopwatch.Frequency))
            {
                _systemWideQueryStarted = now;
                _systemWideQuery = Task.Run(ReadFormattedCpuLoad);
            }
            return _systemWideSampleStarted != 0 && now - _systemWideSampleStarted <= CpuReadingMaxAgeSeconds * Stopwatch.Frequency
                ? _systemWideCpu : null;
        }

        private readonly struct CpuTimes
        {
            public CpuTimes(ulong idle, ulong kernel, ulong user, long timestamp)
            {
                Idle = idle;
                Kernel = kernel;
                User = user;
                Timestamp = timestamp;
            }
            public ulong Idle { get; }
            public ulong Kernel { get; }
            public ulong User { get; }
            public long Timestamp { get; }
        }

        private static CpuTimes? ReadCpuTimes()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            return new CpuTimes(idle, kernel, user, Stopwatch.GetTimestamp());
        }

        private static int? CpuUsageSince(CpuTimes previous, CpuTimes current)
        {
            var age = current.Timestamp - previous.Timestamp;
            if (age <= 0 || age > CpuReadingMaxAgeSeconds * Stopwatch.Frequency ||
                current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
                return null;

            // Kernel time already includes idle time.
            var total = (double)(current.Kernel - previous.Kernel) + (current.User - previous.User);
            var idle = current.Idle - previous.Idle;
            if (total <= 0 || idle > total) return null;
            return (int)Math.Round(100.0 * (total - idle) / total);
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
                        if (current.HasValue && name.IndexOf("TZ0", StringComparison.OrdinalIgnoreCase) >= 0)
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

        private static int? ReadFormattedCpuLoad()
        {
            try
            {
                return QueryCpuLoad();
            }
            catch (Exception ex)
            {
                AppLogger.Error("CPU yük sayacı okunamadı; sıcaklık koruması çalışmaya devam edecek.", ex);
                return null;
            }
        }

        private static int? QueryCpuLoad()
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
                        var rawValue = item["PercentProcessorTime"];
                        if (rawValue == null) return null;
                        var value = Convert.ToDouble(rawValue);
                        return value >= 0 && value <= 100 ? (int?)Math.Round(value) : null;
                    }
                }
                }
            }

            return null;
        }

        private static void ReadPowerStatus(out bool? onAcPower, out int? batteryPercent)
        {
            if (!GetSystemPowerStatus(out var status))
            {
                onAcPower = null;
                batteryPercent = null;
                return;
            }

            onAcPower = status.ACLineStatus <= 1 ? status.ACLineStatus == 1 : (bool?)null;
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

        [DllImport("kernel32.dll")]
        private static extern ushort GetActiveProcessorGroupCount();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    }
}
