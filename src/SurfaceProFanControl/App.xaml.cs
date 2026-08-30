using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using SurfaceProFanControl.Core;
using SurfaceProFanControl.Infrastructure;

namespace SurfaceProFanControl
{
    public partial class App : Application
    {
        private const string MutexName = @"Global\SurfaceProFanControl.Application";
        private Mutex? _instanceMutex;
        private bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var initialSettings = new UserSettingsStore().Load();
            LocalizationService.Use(string.IsNullOrWhiteSpace(initialSettings.LanguageCode)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : initialSettings.LanguageCode);

            var startupLaunch = e.Args.Length == 1 &&
                                string.Equals(e.Args[0], "--startup", StringComparison.OrdinalIgnoreCase);
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "--probe", StringComparison.OrdinalIgnoreCase))
            {
                RunHeadless(e.Args);
                return;
            }
            if (e.Args.Length == 1 &&
                (string.Equals(e.Args[0], "--enable-startup", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.Args[0], "--disable-startup", StringComparison.OrdinalIgnoreCase)))
            {
                RunStartupRegistrationCommand(e.Args[0]);
                return;
            }
            if (e.Args.Length > 0 && !startupLaunch)
            {
                MessageBox.Show(
                    "Bilinmeyen başlangıç seçeneği.",
                    "Surface Pro Fan Kontrolü",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(2);
                return;
            }

            if (!DistributionProfile.IsPortable &&
                !FanGuardServiceManager.IsRunningFromSecureInstall &&
                File.Exists(FanGuardServiceManager.InstalledApplicationPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(FanGuardServiceManager.InstalledApplicationPath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = FanGuardServiceManager.SecureInstallDirectory,
                        Arguments = startupLaunch ? "--startup" : string.Empty
                    });
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Güvenli kurulum açılamadı:\n\n" + ex.Message,
                        "Surface Pro Fan Kontrolü", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                }
                return;
            }

            try
            {
                if (!TryAcquireExclusiveInstance())
                {
                    MessageBox.Show("Surface Pro Soğutma Kontrolü zaten çalışıyor.",
                        "Uygulama açık", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown(2);
                    return;
                }

                var device = new HardwareProbe().ReadDevice();
                var hardwareSupport = DistributionProfile.IsPortable
                    ? new HardwareSupport(
                        FanControlAvailability.MonitoringOnly,
                        "portable-windows-monitor",
                        "DeviceMonitoringOnly")
                    : HardwareSupport.Detect(device.Identity);
                if (hardwareSupport.CanControl)
                {
                    FanGuardServiceManager.EnsureReady();
                }

                var window = new MainWindow(device, hardwareSupport, startupLaunch);
                MainWindow = window;
                window.Show();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Uygulama başlatılamadı.", ex);
                MessageBox.Show(
                    "Uygulama başlatılamadı:\n\n" + ex.Message + "\n\nHiçbir yeni profil uygulanmadı.",
                    "Surface Pro Soğutma Kontrolü",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void RunHeadless(string[] args)
        {
            var outputPath = args.Length >= 2 ? args[1] : null;
            try
            {
                if (args.Length != 2)
                {
                    WriteHeadlessError(outputPath, "Kullanım: --probe <dosya>");
                    Shutdown(2);
                    return;
                }

                var command = args[0];
                var device = new HardwareProbe().ReadDevice();

                if (string.Equals(command, "--probe", StringComparison.OrdinalIgnoreCase))
                {
                    var telemetry = new TelemetryService().Read();
                    File.WriteAllText(outputPath!, ProbeReport.ToJson(device, telemetry), new UTF8Encoding(false));
                    Shutdown(device.Identity.MatchesSurfacePro1Target(out _) ? 0 : 3);
                    return;
                }

                WriteHeadlessError(outputPath, "Bilinmeyen komut: " + command);
                Shutdown(2);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Başsız komut başarısız oldu.", ex);
                WriteHeadlessError(outputPath, ex.Message);
                Shutdown(1);
            }
        }

        private void RunStartupRegistrationCommand(string command)
        {
            try
            {
                var enabled = string.Equals(command, "--enable-startup", StringComparison.OrdinalIgnoreCase);
                StartupRegistration.SetEnabled(enabled);
                if (StartupRegistration.IsEnabled() != enabled)
                {
                    throw new InvalidOperationException("Windows başlangıç kaydı doğrulanamadı.");
                }
                Shutdown(0);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Windows başlangıç kaydı değiştirilemedi.", ex);
                Shutdown(1);
            }
        }

        private bool TryAcquireExclusiveInstance()
        {
            if (_ownsMutex)
            {
                return true;
            }

            _instanceMutex = new Mutex(false, MutexName);
            try
            {
                _ownsMutex = _instanceMutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }

            return _ownsMutex;
        }

        private static void WriteHeadlessError(string? outputPath, string message)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                AppLogger.Info("Başsız komut hatası: " + message);
                return;
            }

            try
            {
                var escaped = (message ?? string.Empty)
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
                File.WriteAllText(outputPath!, "{\r\n  \"success\": false,\r\n  \"error\": \"" + escaped + "\"\r\n}\r\n", new UTF8Encoding(false));
            }
            catch (Exception writeError)
            {
                AppLogger.Error("Başsız komut hata raporu yazılamadı.", writeError);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex && _instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The process is already exiting; ownership was lost unexpectedly.
                }
            }

            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
