using System;
using System.Management;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public sealed class HardwareProbe
    {
        public DeviceSnapshot ReadDevice()
        {
            var manufacturer = ReadFirst("Win32_ComputerSystem", "Manufacturer");
            var model = ReadFirst("Win32_ComputerSystem", "Model");
            var uuid = ReadFirst("Win32_ComputerSystemProduct", "UUID");
            var bios = ReadFirst("Win32_BIOS", "SMBIOSBIOSVersion");
            var serial = ReadFirst("Win32_BIOS", "SerialNumber");
            var processor = ReadFirst("Win32_Processor", "Name");
            var os = ReadFirst("Win32_OperatingSystem", "Caption") + " " + ReadFirst("Win32_OperatingSystem", "Version");

            var identity = new HardwareIdentity(manufacturer, model, uuid, bios, serial);
            return new DeviceSnapshot(identity, Environment.MachineName, processor, os.Trim());
        }

        private static string ReadFirst(string className, string property)
        {
            using (var searcher = new ManagementObjectSearcher("root\\cimv2", $"SELECT {property} FROM {className}"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        return Convert.ToString(item[property])?.Trim() ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }
    }
}
