using System;
using System.IO;
using System.Reflection;

namespace SurfaceProFanControl.Infrastructure
{
    public static class DistributionProfile
    {
#if PORTABLE_MONITOR
        public const bool IsPortable = true;
        public const bool AllowsHardwareControl = false;
#else
        public const bool IsPortable = false;
        public const bool AllowsHardwareControl = true;
#endif

        public static string ApplicationPath
        {
            get
            {
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null && !string.IsNullOrWhiteSpace(entryAssembly.Location))
                {
                    return Path.GetFullPath(entryAssembly.Location);
                }

                return Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "SurfaceProFanControl.exe"));
            }
        }

        public static string WorkingDirectory =>
            Path.GetDirectoryName(ApplicationPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        internal static bool IsVersionAlongsideInstall =>
            !IsPortable && string.Equals(ApplicationPath, Path.Combine(
                FanGuardServiceManager.ApplicationInstallDirectory, "Versions",
                typeof(DistributionProfile).Assembly.GetName().Version.ToString(3), "SurfaceProFanControl.exe"),
                StringComparison.OrdinalIgnoreCase);
    }
}
