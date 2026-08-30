using System;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace SurfaceProI2EcFanTest;

internal static class ServiceRemoval
{
    private const string GuardServiceName = "SurfaceProFanGuard";
    private const string DriverServiceName = "WinRing0_1_2_0";

    public static int Run()
    {
        try
        {
            if (ServiceExists(GuardServiceName))
            {
                using (var guard = new ServiceController(GuardServiceName))
                {
                    guard.Refresh();
                    if (guard.Status == ServiceControllerStatus.Running)
                    {
                        var autoResult = FanGuardDeploymentVerifier.RunAuthenticatedCommand("AUTO");
                        if (autoResult != 0)
                        {
                            throw new InvalidOperationException(
                                "Firmware automatic mode could not be verified before service removal.");
                        }

                        guard.Stop();
                        guard.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                }

                DeleteService(GuardServiceName);
            }

            if (ServiceExists(DriverServiceName))
            {
                using (var driver = new ServiceController(DriverServiceName))
                {
                    driver.Refresh();
                    if (driver.Status != ServiceControllerStatus.Stopped)
                    {
                        driver.Stop();
                        driver.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                }

                DeleteService(DriverServiceName);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SERVICE_REMOVAL_FAILED: " + ex.Message);
            return 1;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using (var service = new ServiceController(serviceName))
            {
                _ = service.Status;
                return true;
            }
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception native && native.NativeErrorCode == 1060)
        {
            return false;
        }
    }

    private static void DeleteService(string serviceName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.SystemDirectory, "sc.exe"),
            Arguments = "delete \"" + serviceName + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(startInfo) ??
               throw new InvalidOperationException("sc.exe could not be started."))
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Win32Exception(
                    process.ExitCode,
                    "Service deletion failed: " + (error + " " + output).Trim());
            }
        }
    }
}
