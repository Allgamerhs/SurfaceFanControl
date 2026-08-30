using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SurfaceProFanControl.Infrastructure
{
    public static class StartupRegistration
    {
        private const string TaskName = "SurfaceProFanControl";
        private static readonly XNamespace TaskNamespace =
            "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static bool IsEnabled()
        {
            var query = RunTaskScheduler(
                "/Query /TN " + Quote(TaskName) + " /XML",
                allowMissingTask: true);
            if (query.ExitCode != 0 || string.IsNullOrWhiteSpace(query.Output))
            {
                return false;
            }

            try
            {
                var task = XDocument.Parse(query.Output).Root;
                if (task == null || task.Name != TaskNamespace + "Task")
                {
                    return false;
                }

                var principal = task.Element(TaskNamespace + "Principals")?
                    .Elements(TaskNamespace + "Principal").SingleOrDefault();
                var trigger = task.Element(TaskNamespace + "Triggers")?
                    .Elements(TaskNamespace + "LogonTrigger").SingleOrDefault();
                var action = task.Element(TaskNamespace + "Actions")?
                    .Elements(TaskNamespace + "Exec").SingleOrDefault();

                return principal != null && trigger != null && action != null &&
                       UserIdentityMatchesCurrent(
                           (string?)principal.Element(TaskNamespace + "UserId")) &&
                       string.Equals((string?)principal.Element(TaskNamespace + "LogonType"),
                           "InteractiveToken", StringComparison.Ordinal) &&
                       string.Equals((string?)principal.Element(TaskNamespace + "RunLevel"),
                           ExpectedRunLevel, StringComparison.Ordinal) &&
                       UserIdentityMatchesCurrent(
                           (string?)trigger.Element(TaskNamespace + "UserId")) &&
                       !string.Equals((string?)trigger.Element(TaskNamespace + "Enabled"),
                           "false", StringComparison.OrdinalIgnoreCase) &&
                       PathsEqual((string?)action.Element(TaskNamespace + "Command"),
                           ExpectedApplicationPath) &&
                       string.Equals(((string?)action.Element(TaskNamespace + "Arguments"))?.Trim(),
                           "--startup", StringComparison.Ordinal) &&
                       PathsEqual((string?)action.Element(TaskNamespace + "WorkingDirectory"),
                           ExpectedWorkingDirectory);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException ||
                                       ex is InvalidOperationException ||
                                       ex is ArgumentException)
            {
                AppLogger.Error("Windows başlangıç görevi doğrulanamadı.", ex);
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                DeleteTaskAndRequireMissing();
                return;
            }

            var temporary = Path.Combine(
                Path.GetTempPath(),
                "SurfaceProFanControl-startup-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                SaveTaskDocument(temporary);
                RunTaskScheduler(
                    "/Create /TN " + Quote(TaskName) + " /XML " + Quote(temporary) + " /F",
                    allowMissingTask: false);
                if (!IsEnabled())
                {
                    throw new InvalidOperationException(
                        "Windows başlangıç görevi oluşturulduktan sonra doğrulanamadı.");
                }
            }
            catch (Exception operationError)
            {
                try
                {
                    DeleteTaskAndRequireMissing();
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Windows başlangıç görevi doğrulanamadı ve geri alma da başarısız oldu.",
                        operationError,
                        rollbackError);
                }
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception cleanupError)
                {
                    // The elevated process created this high-integrity file. A stale,
                    // already-consumed XML file is harmless; do not invert a verified
                    // task registration merely because best-effort cleanup failed.
                    AppLogger.Error("Geçici başlangıç görevi XML dosyası silinemedi.", cleanupError);
                }
            }
        }

        internal static string ExpectedCommand =>
            "\"" + ExpectedApplicationPath + "\" --startup";

        private static string ExpectedApplicationPath => DistributionProfile.IsPortable
            ? DistributionProfile.ApplicationPath
            : FanGuardServiceManager.InstalledApplicationPath;

        private static string ExpectedWorkingDirectory => DistributionProfile.IsPortable
            ? DistributionProfile.WorkingDirectory
            : FanGuardServiceManager.ApplicationInstallDirectory;

        private static string ExpectedRunLevel => DistributionProfile.IsPortable
            ? "LeastPrivilege"
            : "HighestAvailable";

        private static XDocument CreateTaskDocument()
        {
            var userSid = CurrentUserSid;
            return new XDocument(
                new XElement(TaskNamespace + "Task",
                    new XAttribute("version", "1.4"),
                    new XElement(TaskNamespace + "Triggers",
                        new XElement(TaskNamespace + "LogonTrigger",
                            new XElement(TaskNamespace + "Enabled", "true"),
                            new XElement(TaskNamespace + "UserId", userSid))),
                    new XElement(TaskNamespace + "Principals",
                        new XElement(TaskNamespace + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(TaskNamespace + "UserId", userSid),
                            new XElement(TaskNamespace + "LogonType", "InteractiveToken"),
                            new XElement(TaskNamespace + "RunLevel", ExpectedRunLevel))),
                    new XElement(TaskNamespace + "Settings",
                        new XElement(TaskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(TaskNamespace + "DisallowStartIfOnBatteries", "false"),
                        new XElement(TaskNamespace + "StopIfGoingOnBatteries", "false"),
                        new XElement(TaskNamespace + "AllowHardTerminate", "true"),
                        new XElement(TaskNamespace + "StartWhenAvailable", "true"),
                        new XElement(TaskNamespace + "AllowStartOnDemand", "true"),
                        new XElement(TaskNamespace + "Enabled", "true"),
                        new XElement(TaskNamespace + "Hidden", "false"),
                        new XElement(TaskNamespace + "ExecutionTimeLimit", "PT0S"),
                        new XElement(TaskNamespace + "Priority", "7")),
                    new XElement(TaskNamespace + "Actions",
                        new XAttribute("Context", "Author"),
                        new XElement(TaskNamespace + "Exec",
                            new XElement(TaskNamespace + "Command",
                                ExpectedApplicationPath),
                            new XElement(TaskNamespace + "Arguments", "--startup"),
                            new XElement(TaskNamespace + "WorkingDirectory",
                                ExpectedWorkingDirectory)))));
        }

        private static void SaveTaskDocument(string path)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.Unicode,
                Indent = false,
                OmitXmlDeclaration = false,
                CloseOutput = true
            };
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = XmlWriter.Create(stream, settings))
            {
                CreateTaskDocument().Save(writer);
            }
        }

        private static string CurrentUserSid
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return identity.User?.Value ??
                           throw new InvalidOperationException("Geçerli Windows kullanıcı SID'i okunamadı.");
                }
            }
        }

        private static bool UserIdentityMatchesCurrent(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            try
            {
                IdentityReference reference = candidate!.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
                    ? (IdentityReference)new SecurityIdentifier(candidate)
                    : new NTAccount(candidate);
                var translated = (SecurityIdentifier)reference.Translate(typeof(SecurityIdentifier));
                return string.Equals(translated.Value, CurrentUserSid, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException ||
                                       ex is IdentityNotMappedException ||
                                       ex is SystemException)
            {
                return false;
            }
        }

        private static TaskSchedulerResult RunTaskScheduler(string arguments, bool allowMissingTask)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(startInfo) ??
                   throw new InvalidOperationException("Windows Görev Zamanlayıcı başlatılamadı."))
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 && !allowMissingTask)
                {
                    throw new InvalidOperationException(
                        "Windows başlangıç görevi değiştirilemedi: " +
                        (error + " " + output).Trim());
                }
                return new TaskSchedulerResult(process.ExitCode, output);
            }
        }

        private static void DeleteTaskAndRequireMissing()
        {
            RunTaskScheduler(
                "/Delete /TN " + Quote(TaskName) + " /F",
                allowMissingTask: true);
            var query = RunTaskScheduler(
                "/Query /TN " + Quote(TaskName) + " /XML",
                allowMissingTask: true);
            if (query.ExitCode == 0)
            {
                throw new InvalidOperationException("Windows başlangıç görevi silinemedi.");
            }
        }

        private static bool PathsEqual(string? first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return false;
            }
            return string.Equals(
                Path.GetFullPath(first!.Trim()),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private readonly struct TaskSchedulerResult
        {
            public TaskSchedulerResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }

            public int ExitCode { get; }
            public string Output { get; }
        }
    }
}
