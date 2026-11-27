using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SurfaceProFanControl.Shared
{
    internal static class FanGuardPipeIdentity
    {
        private const string GuardServiceName = "SurfaceProFanGuard";
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceQueryConfig = 0x0001;
        private const uint ServiceQueryStatus = 0x0004;
        private const int ScStatusProcessInfo = 0;
        private const uint ServiceKernelDriver = 0x00000001;
        private const uint ServiceWin32OwnProcess = 0x00000010;
        private const uint ServiceRunning = 0x00000004;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint Synchronize = 0x00100000;
        private const uint TokenQuery = 0x0008;
        private const uint WaitTimeout = 0x00000102;
        private const int ErrorInsufficientBuffer = 122;

        public static void VerifyConnectedServer(
            NamedPipeClientStream pipe,
            string expectedExecutablePath)
        {
            if (pipe == null)
            {
                throw new ArgumentNullException(nameof(pipe));
            }
            if (!pipe.IsConnected)
            {
                throw new IOException("The fan guard pipe is not connected.");
            }

            var expectedPath = Path.GetFullPath(expectedExecutablePath);
            var firstServerPid = ReadServerProcessId(pipe.SafePipeHandle);
            var processHandle = OpenProcess(
                ProcessQueryLimitedInformation | Synchronize,
                false,
                firstServerPid);
            if (processHandle == IntPtr.Zero)
            {
                throw NewWin32Exception("The fan guard server process could not be opened.");
            }

            try
            {
                RequireLiveProcess(processHandle);
                RequireExpectedProcessPath(processHandle, expectedPath);
                RequireLocalSystemToken(processHandle);
                RequireExpectedService(firstServerPid, expectedPath);

                var secondServerPid = ReadServerProcessId(pipe.SafePipeHandle);
                if (secondServerPid != firstServerPid)
                {
                    throw new InvalidDataException("The fan guard pipe server changed during authentication.");
                }
                RequireLiveProcess(processHandle);
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        private static uint ReadServerProcessId(SafePipeHandle pipeHandle)
        {
            if (!GetNamedPipeServerProcessId(pipeHandle, out var processId) || processId == 0)
            {
                throw NewWin32Exception("The fan guard pipe server identity could not be read.");
            }
            return processId;
        }

        private static void RequireLiveProcess(IntPtr processHandle)
        {
            var wait = WaitForSingleObject(processHandle, 0);
            if (wait != WaitTimeout)
            {
                throw new InvalidDataException("The fan guard server process is no longer alive.");
            }
        }

        private static void RequireExpectedProcessPath(IntPtr processHandle, string expectedPath)
        {
            var capacity = 32768;
            var builder = new StringBuilder(capacity);
            if (!QueryFullProcessImageName(processHandle, 0, builder, ref capacity))
            {
                throw NewWin32Exception("The fan guard server image path could not be read.");
            }

            if (!PathsEqual(builder.ToString(), expectedPath))
            {
                throw new InvalidDataException("The fan guard pipe is owned by an unexpected executable.");
            }
        }

        private static void RequireLocalSystemToken(IntPtr processHandle)
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
            {
                throw NewWin32Exception("The fan guard server token could not be opened.");
            }

            try
            {
                using (var identity = new WindowsIdentity(tokenHandle))
                {
                    if (identity.User == null ||
                        !identity.User.IsWellKnown(WellKnownSidType.LocalSystemSid))
                    {
                        throw new InvalidDataException("The fan guard pipe server is not LocalSystem.");
                    }
                }
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }

        private static void RequireExpectedService(uint expectedProcessId, string expectedPath)
        {
            var config = ReadServiceConfiguration(GuardServiceName, out var status);
            if (status.ServiceType != ServiceWin32OwnProcess ||
                status.CurrentState != ServiceRunning ||
                status.ProcessId == 0 ||
                status.ProcessId != expectedProcessId)
            {
                throw new InvalidDataException(
                    "The pipe server does not match the running fan guard service.");
            }

            var expectedCommand = "\"" + expectedPath + "\" --service";
            var actualCommand = Environment.ExpandEnvironmentVariables(config.BinaryPathName).Trim();
            if (config.ServiceType != ServiceWin32OwnProcess ||
                !string.Equals(actualCommand, expectedCommand, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(config.ServiceStartName, "LocalSystem", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The fan guard service configuration is not trusted.");
            }
        }

        public static string ReadRunningKernelDriverPath(string serviceName)
        {
            var config = ReadServiceConfiguration(serviceName, out var status);
            if (status.CurrentState != ServiceRunning ||
                status.ServiceType != ServiceKernelDriver ||
                config.ServiceType != ServiceKernelDriver)
            {
                throw new InvalidDataException("The configured driver is not a running kernel driver.");
            }
            return config.BinaryPathName;
        }

        private static ServiceConfiguration ReadServiceConfiguration(
            string serviceName, out ServiceStatusProcess status)
        {
            var manager = OpenSCManager(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
            {
                throw NewWin32Exception("The Service Control Manager could not be opened.");
            }

            try
            {
                var service = OpenService(
                    manager,
                    serviceName,
                    ServiceQueryConfig | ServiceQueryStatus);
                if (service == IntPtr.Zero)
                {
                    throw NewWin32Exception("The service could not be opened for status and configuration queries.");
                }

                try
                {
                    status = QueryStatus(service);
                    return QueryConfig(service);
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(manager);
            }
        }

        private static ServiceStatusProcess QueryStatus(IntPtr service)
        {
            var size = Marshal.SizeOf(typeof(ServiceStatusProcess));
            if (!QueryServiceStatusEx(
                    service,
                    ScStatusProcessInfo,
                    out var status,
                    size,
                    out _))
            {
                throw NewWin32Exception("The fan guard service status could not be read.");
            }
            return status;
        }

        private static ServiceConfiguration QueryConfig(IntPtr service)
        {
            QueryServiceConfigNative(service, IntPtr.Zero, 0, out var requiredBytes);
            if (requiredBytes == 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                throw NewWin32Exception("The fan guard service configuration size could not be read.");
            }

            var buffer = Marshal.AllocHGlobal(requiredBytes);
            try
            {
                if (!QueryServiceConfigNative(service, buffer, requiredBytes, out _))
                {
                    throw NewWin32Exception("The fan guard service configuration could not be read.");
                }
                var native = (QueryServiceConfig)Marshal.PtrToStructure(
                    buffer,
                    typeof(QueryServiceConfig));
                return new ServiceConfiguration(
                    native.ServiceType,
                    Marshal.PtrToStringUni(native.BinaryPathName) ?? string.Empty,
                    Marshal.PtrToStringUni(native.ServiceStartName) ?? string.Empty);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            var normalizedFirst = NormalizePath(first);
            var normalizedSecond = NormalizePath(second);
            return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            var value = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (value.StartsWith("\\??\\", StringComparison.Ordinal) ||
                value.StartsWith("\\\\?\\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        }

        private static Win32Exception NewWin32Exception(string message)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), message);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatusProcess
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
            public uint ProcessId;
            public uint ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QueryServiceConfig
        {
            public uint ServiceType;
            public uint StartType;
            public uint ErrorControl;
            public IntPtr BinaryPathName;
            public IntPtr LoadOrderGroup;
            public uint TagId;
            public IntPtr Dependencies;
            public IntPtr ServiceStartName;
            public IntPtr DisplayName;
        }

        private sealed class ServiceConfiguration
        {
            public ServiceConfiguration(uint serviceType, string binaryPathName, string serviceStartName)
            {
                ServiceType = serviceType;
                BinaryPathName = binaryPathName;
                ServiceStartName = serviceStartName;
            }

            public uint ServiceType { get; }
            public string BinaryPathName { get; }
            public string ServiceStartName { get; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(
            SafePipeHandle pipe,
            out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            IntPtr process,
            uint flags,
            StringBuilder executableName,
            ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string? machineName,
            string? databaseName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr serviceManager,
            string serviceName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatusEx(
            IntPtr service,
            int infoLevel,
            out ServiceStatusProcess serviceStatus,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceConfigNative(
            IntPtr service,
            IntPtr serviceConfig,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}
