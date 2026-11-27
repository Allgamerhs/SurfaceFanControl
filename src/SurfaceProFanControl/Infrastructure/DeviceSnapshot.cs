using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class DeviceSnapshot
    {
        public DeviceSnapshot(HardwareIdentity identity, string processor, string operatingSystem)
            : this(identity, string.Empty, processor, operatingSystem) { }

        public DeviceSnapshot(HardwareIdentity identity, string computerName, string processor, string operatingSystem)
        {
            Identity = identity;
            ComputerName = computerName;
            Processor = processor;
            OperatingSystem = operatingSystem;
        }

        public HardwareIdentity Identity { get; }
        public string ComputerName { get; }
        public string Processor { get; }
        public string OperatingSystem { get; }
    }
}
