using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace SurfaceProFanControl.Infrastructure
{
    public static class FanGuardServiceManager
    {
        private const string DriverServiceName = "WinRing0_1_2_0";
        private const string GuardServiceName = "SurfaceProFanGuard";
        private const string InstallRegistryKey = @"SOFTWARE\SurfaceFanControl";
        private const string InstallLocationValue = "InstallLocation";
        private const string ExpectedDriverSha256 = "11BD2C9F9E2397C9A16E0990E4ED2CF0679498FE0FD418A3DFDAC60B5C160EE5";
        private const string Version17GuardSha256 = "D46E395FE488DDF87E4DD380D9C724296F44733C3E672E2225A3E9BBC9DAE8EC";

        public static string ApplicationInstallDirectory => ReadApplicationInstallDirectory();

        // Privileged binaries deliberately remain in a machine-wide protected location even when
        // the user selects another drive for the UI. A LocalSystem service must never execute from
        // a user-writable portable or Downloads directory.
        public static string SecureInstallDirectory => Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SurfaceFanControl",
            "Privileged"));

        public static bool IsRunningFromSecureInstall => string.Equals(
            Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
            ApplicationInstallDirectory,
            StringComparison.OrdinalIgnoreCase);

        public static string InstalledApplicationPath => Path.Combine(ApplicationInstallDirectory, "SurfaceProFanControl.exe");

        internal static string PackagedGuardPath => Path.Combine(SecureInstallDirectory, "SurfaceProFanGuard.exe");

        private static string Version17GuardPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SurfaceProFanControl", "SurfaceProFanGuard.exe");

        internal static string ResolveExistingGuardPath()
        {
            using (var controller = new ServiceController(GuardServiceName))
            {
                if (controller.Status != ServiceControllerStatus.Running)
                    throw new InvalidOperationException("Fan koruma hizmeti çalışmıyor.");
            }

            var service = ReadService(GuardServiceName, systemDriver: false);
            if (!service.Exists || !string.Equals(service.StartName, "LocalSystem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kurulu fan koruma hizmetinin kimliği doğrulanamadı.");

            foreach (var path in new[] { PackagedGuardPath, Version17GuardPath })
            {
                if (string.Equals(service.RawPath.Trim(), Quote(path) + " --service", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            throw new InvalidDataException("Kurulu fan koruma hizmetinin dosya yolu doğrulanamadı.");
        }

        internal static void VerifyVersion17Guard(string path)
        {
            if (PathsEqual(path, Version17GuardPath) &&
                !string.Equals(ComputeSha256(path), Version17GuardSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kurulu 1.7.0 fan koruma hizmetinin SHA-256 doğrulaması başarısız.");
        }

        public static void EnsureReady()
        {
            var expectedApplicationDirectory = ApplicationInstallDirectory;
            var actualDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(actualDirectory, expectedApplicationDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Donanım hizmeti yalnız güvenli kurulum konumundan çalışır. Masaüstündeki Surface Pro Soğutma Kontrolü kısayolunu açın.");
            }

            var privilegedDirectory = SecureInstallDirectory;
            var driverPath = Path.Combine(privilegedDirectory, "WinRing0x64.sys");
            var guardPath = Path.Combine(privilegedDirectory, "SurfaceProFanGuard.exe");
            RequireFile(driverPath);
            RequireFile(guardPath);
            if (!string.Equals(ComputeSha256(driverPath), ExpectedDriverSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Fan sürücüsünün SHA-256 doğrulaması başarısız.");
            }

            EnsureDriverService(driverPath);
            EnsureGuardService(guardPath);
            StartAndWait(GuardServiceName, TimeSpan.FromSeconds(12));

            Exception? lastFailure = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            do
            {
                try
                {
                    var status = new FanGuardClient().ReadStatus();
                    if (!status.IsAutomatic)
                    {
                        throw new InvalidDataException("Fan hizmeti başlangıçta otomatik modu doğrulamadı.");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    Thread.Sleep(250);
                }
            }
            while (DateTime.UtcNow < deadline);

            throw new InvalidOperationException("Fan koruma hizmeti hazır olmadı.", lastFailure);
        }

        public static string GuardLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SurfaceProFanControl",
            "fan-guard.log");

        private static void EnsureDriverService(string expectedPath)
        {
            var existing = ReadService(DriverServiceName, systemDriver: true);
            if (existing.Exists && !PathsEqual(existing.ExecutablePath, expectedPath))
            {
                throw new InvalidOperationException("WinRing0 hizmeti beklenmeyen bir dosyaya bağlı; güvenli kurulum durduruldu.");
            }
            if (!existing.Exists)
            {
                RunSc("create " + DriverServiceName + " type= kernel start= demand error= normal binPath= " + Quote(expectedPath));
            }
            else
            {
                RunSc("config " + DriverServiceName + " start= demand binPath= " + Quote(expectedPath));
            }

            var verified = ReadService(DriverServiceName, systemDriver: true);
            if (!verified.Exists || !PathsEqual(verified.ExecutablePath, expectedPath))
            {
                throw new InvalidDataException("WinRing0 hizmetinin son ImagePath doğrulaması başarısız.");
            }
        }

        private static void EnsureGuardService(string expectedPath)
        {
            var expectedCommand = Quote(expectedPath) + " --service";
            var existing = ReadService(GuardServiceName, systemDriver: false);
            if (existing.Exists && !string.Equals(existing.RawPath.Trim(), expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fan koruma hizmeti beklenmeyen bir dosyaya bağlı; güvenli kurulum durduruldu.");
            }
            if (!existing.Exists)
            {
                RunSc("create " + GuardServiceName + " start= auto obj= LocalSystem error= normal depend= " + DriverServiceName +
                      " binPath= " + Quote(expectedCommand) + " DisplayName= " + Quote("Surface Pro Fan Guard"));
            }
            else
            {
                RunSc("config " + GuardServiceName + " start= auto obj= LocalSystem depend= " + DriverServiceName +
                      " binPath= " + Quote(expectedCommand));
            }

            RunSc("failure " + GuardServiceName + " reset= 60 actions= restart/1000/restart/3000/restart/5000");
            RunSc("failureflag " + GuardServiceName + " 1");

            var verified = ReadService(GuardServiceName, systemDriver: false);
            if (!verified.Exists || !PathsEqual(verified.ExecutablePath, expectedPath) ||
                !string.Equals(verified.StartName, "LocalSystem", StringComparison.OrdinalIgnoreCase) ||
                !verified.RawPath.Trim().EndsWith(" --service", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Fan koruma hizmetinin son ImagePath doğrulaması başarısız.");
            }
        }

        private static void StartAndWait(string serviceName, TimeSpan timeout)
        {
            using (var controller = new ServiceController(serviceName))
            {
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Stopped)
                {
                    controller.Start();
                }
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
                }
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    throw new InvalidOperationException("Fan koruma hizmeti çalışmıyor: " + controller.Status);
                }
            }
        }

        private static ServiceRecord ReadService(string name, bool systemDriver)
        {
            var className = systemDriver ? "Win32_SystemDriver" : "Win32_Service";
            using (var searcher = new ManagementObjectSearcher(
                       "root\\cimv2",
                       "SELECT PathName" + (systemDriver ? string.Empty : ", StartName") +
                       " FROM " + className + " WHERE Name='" + name.Replace("'", "''") + "'"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var raw = Convert.ToString(item["PathName"], CultureInfo.InvariantCulture) ?? string.Empty;
                        return new ServiceRecord(
                            true,
                            raw,
                            systemDriver ? NormalizeDriverPath(raw) : ExtractExecutablePath(raw),
                            systemDriver ? string.Empty : Convert.ToString(item["StartName"], CultureInfo.InvariantCulture) ?? string.Empty);
                    }
                }
            }
            return new ServiceRecord(false, string.Empty, string.Empty, string.Empty);
        }

        private static string ExtractExecutablePath(string command)
        {
            var value = Environment.ExpandEnvironmentVariables(command.Trim());
            if (value.StartsWith("\\??\\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = value.IndexOf('\"', 1);
                return end > 1 ? value.Substring(1, end - 1) : value.Trim('\"');
            }
            var separator = value.IndexOf(' ');
            return separator > 0 ? value.Substring(0, separator) : value;
        }

        private static string NormalizeDriverPath(string rawPath)
        {
            var value = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
            if (value.StartsWith("\\??\\", StringComparison.Ordinal) ||
                value.StartsWith("\\\\?\\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }
            return value;
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return false;
            }
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static void RunSc(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(startInfo) ?? throw new InvalidOperationException("sc.exe başlatılamadı."))
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Win32Exception(process.ExitCode, "Hizmet yapılandırması başarısız: " + (error + " " + output).Trim());
                }
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Kurulum dosyası eksik.", path);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string ReadApplicationInstallDirectory()
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(InstallRegistryKey, writable: false))
                {
                    var configured = Convert.ToString(key?.GetValue(InstallLocationValue), CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        return Path.GetFullPath(configured!.Trim().Trim('"'))
                            .TrimEnd(Path.DirectorySeparatorChar);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException ||
                                       ex is System.Security.SecurityException ||
                                       ex is IOException ||
                                       ex is ArgumentException ||
                                       ex is NotSupportedException)
            {
                throw new InvalidOperationException("Güvenli kurulum konumu kayıt defterinden okunamadı.", ex);
            }

            // Backward-compatible fallback for existing manual/private deployments.
            return Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "SurfaceProFanControl"));
        }

        private sealed class ServiceRecord
        {
            public ServiceRecord(bool exists, string rawPath, string executablePath, string startName)
            {
                Exists = exists;
                RawPath = rawPath;
                ExecutablePath = executablePath;
                StartName = startName;
            }

            public bool Exists { get; }
            public string RawPath { get; }
            public string ExecutablePath { get; }
            public string StartName { get; }
        }
    }
}
