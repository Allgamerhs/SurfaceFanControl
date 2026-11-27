using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Windows.Threading;

namespace SurfaceProFanControl.Infrastructure
{
    internal sealed class WindowActivation : IDisposable
    {
        private readonly EventWaitHandle _request;
        private RegisteredWaitHandle? _listener;

        public WindowActivation()
        {
            using var self = Process.GetCurrentProcess();
            _request = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(self.Id));
        }

        public void Listen(Dispatcher dispatcher, Action show)
        {
            _listener = ThreadPool.RegisterWaitForSingleObject(_request, (_, __) =>
            {
                if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(show);
            }, null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public static bool TryShowExisting()
        {
            using var self = Process.GetCurrentProcess();
            var peers = Process.GetProcessesByName(self.ProcessName);
            try
            {
                // The mutex winner publishes its signal before probing hardware or constructing WPF.
                var deadline = Stopwatch.StartNew();
                do
                {
                    foreach (var peer in peers)
                    {
                        if (peer.Id == self.Id) continue;
                        try
                        {
                            if (!EventWaitHandle.TryOpenExisting(EventName(peer.Id), EventWaitHandleRights.Modify, out var request))
                                continue;
                            using (request)
                            {
                                AllowSetForegroundWindow(peer.Id);
                                return request.Set();
                            }
                        }
                        catch (UnauthorizedAccessException) { /* A different logon can own a same-name app. */ }
                    }
                    Thread.Sleep(25);
                } while (deadline.ElapsedMilliseconds < 500);
                return false;
            }
            finally
            {
                foreach (var peer in peers) peer.Dispose();
            }
        }

        private static string EventName(int pid) =>
            @"Local\SurfaceProFanControl.ShowWindow." + pid.ToString(CultureInfo.InvariantCulture);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        public void Dispose()
        {
            _listener?.Unregister(null);
            _request.Dispose();
        }
    }
}
