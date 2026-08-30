using System;

namespace SurfaceProFanControl.Core
{
    public enum FanControlAvailability
    {
        MonitoringOnly,
        FullControl
    }

    public readonly struct HardwareSupport
    {
        public HardwareSupport(
            FanControlAvailability availability,
            string backendId,
            string localizationKey)
        {
            Availability = availability;
            BackendId = backendId ?? throw new ArgumentNullException(nameof(backendId));
            LocalizationKey = localizationKey ?? throw new ArgumentNullException(nameof(localizationKey));
        }

        public FanControlAvailability Availability { get; }
        public string BackendId { get; }
        public string LocalizationKey { get; }
        public bool CanControl => Availability == FanControlAvailability.FullControl;

        public static HardwareSupport Detect(HardwareIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            return identity.MatchesSurfacePro1Target(out _)
                ? new HardwareSupport(FanControlAvailability.FullControl, "surface-pro-1-it8519", "DeviceSupportedSurface")
                : new HardwareSupport(FanControlAvailability.MonitoringOnly, "generic-windows-monitor", "DeviceMonitoringOnly");
        }
    }
}
