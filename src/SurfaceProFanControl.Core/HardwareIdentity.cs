using System;

namespace SurfaceProFanControl.Core
{
    public sealed class HardwareIdentity
    {
        public HardwareIdentity(string manufacturer, string model, string biosVersion)
            : this(manufacturer, model, string.Empty, biosVersion, string.Empty) { }

        public HardwareIdentity(string manufacturer, string model, string uuid, string biosVersion, string serialNumber)
        {
            Manufacturer = manufacturer ?? string.Empty;
            Model = model ?? string.Empty;
            Uuid = uuid ?? string.Empty;
            BiosVersion = biosVersion ?? string.Empty;
            SerialNumber = serialNumber ?? string.Empty;
        }

        public string Manufacturer { get; }
        public string Model { get; }
        public string Uuid { get; }
        public string BiosVersion { get; }
        public string SerialNumber { get; }

        public bool MatchesSurfacePro1Target(out string reason)
        {
            if (!string.Equals(Manufacturer.Trim(), SurfacePro1Target.Manufacturer, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Üretici uyuşmuyor: {Manufacturer}";
                return false;
            }

            if (!string.Equals(Model.Trim(), SurfacePro1Target.Model, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Model uyuşmuyor: {Model}";
                return false;
            }

            reason = "Hedef Surface Pro 1 doğrulandı.";
            return true;
        }
    }

    public static class SurfacePro1Target
    {
        public const string Manufacturer = "Microsoft Corporation";
        public const string Model = "Surface with Windows 8 Pro";
        public const string Cpu = "Intel(R) Core(TM) i5-3317U CPU @ 1.70GHz";
        public const double FirmwareCriticalCelsius = 86.0;
    }
}
