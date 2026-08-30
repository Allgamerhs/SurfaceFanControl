using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SurfaceProI2EcFanTest;

internal sealed class FanGuardFailsafeEventNames
{
    public FanGuardFailsafeEventNames(string pulse, string ready, string acknowledge)
    {
        Pulse = pulse;
        Ready = ready;
        Acknowledge = acknowledge;
    }

    public string Pulse { get; }
    public string Ready { get; }
    public string Acknowledge { get; }
}

internal static class FanGuardFailsafeProtocol
{
    private const string EventPrefix = @"Global\SurfaceProFanGuard.Failsafe.";

    public static bool TryNormalizeNonce(string value, out string nonce)
    {
        if (Guid.TryParseExact(value, "N", out var parsed))
        {
            nonce = parsed.ToString("N");
            return true;
        }

        nonce = string.Empty;
        return false;
    }

    public static FanGuardFailsafeEventNames CreateEventNames(int processId, string nonce)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }
        if (!TryNormalizeNonce(nonce, out var normalizedNonce))
        {
            throw new ArgumentException("The failsafe nonce must be a 32-character GUID.", nameof(nonce));
        }

        var prefix = EventPrefix + processId.ToString(CultureInfo.InvariantCulture) + "." + normalizedNonce + ".";
        return new FanGuardFailsafeEventNames(
            prefix + "Pulse",
            prefix + "Ready",
            prefix + "Ack");
    }
}

internal sealed class FanGuardFailsafePulseChannel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new object();
    private readonly EventWaitHandle _pulse;
    private readonly EventWaitHandle _ready;
    private readonly EventWaitHandle _acknowledge;
    private readonly Func<bool> _isAlive;
    private bool _readyCompleted;
    private bool _faulted;

    public FanGuardFailsafePulseChannel(
        EventWaitHandle pulse,
        EventWaitHandle ready,
        EventWaitHandle acknowledge,
        Func<bool> isAlive)
    {
        _pulse = pulse ?? throw new ArgumentNullException(nameof(pulse));
        _ready = ready ?? throw new ArgumentNullException(nameof(ready));
        _acknowledge = acknowledge ?? throw new ArgumentNullException(nameof(acknowledge));
        _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
    }

    public void WaitUntilReady(TimeSpan timeout)
    {
        lock (_gate)
        {
            ThrowIfFaulted();
            try
            {
                WaitForSignal(_ready, _isAlive, timeout, "The independent fan failsafe did not become ready.");
                _readyCompleted = true;
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }
    }

    public long PulseAndRequireAcknowledge(TimeSpan timeout)
    {
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_readyCompleted)
            {
                throw new InvalidOperationException("The independent fan failsafe has not completed its READY handshake.");
            }
            try
            {
                while (_acknowledge.WaitOne(0))
                {
                    // A previous acknowledgement must never satisfy a new pulse.
                }
                if (!_isAlive())
                {
                    throw new InvalidOperationException("The independent fan failsafe is not running.");
                }

                _pulse.Set();
                WaitForSignal(
                    _acknowledge,
                    _isAlive,
                    timeout,
                    "The independent fan failsafe did not acknowledge its pulse.");
                return Stopwatch.GetTimestamp();
            }
            catch
            {
                // Never reuse this channel after a timeout. A late acknowledgement
                // could otherwise be mistaken for the response to a later pulse.
                _faulted = true;
                throw;
            }
        }
    }

    private void ThrowIfFaulted()
    {
        if (_faulted)
        {
            throw new InvalidOperationException("The independent fan failsafe channel is faulted.");
        }
    }

    private static void WaitForSignal(
        EventWaitHandle signal,
        Func<bool> isAlive,
        TimeSpan timeout,
        string timeoutMessage)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (!isAlive())
            {
                throw new InvalidOperationException("The independent fan failsafe terminated.");
            }

            var remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(timeoutMessage);
            }

            var slice = remaining < PollInterval ? remaining : PollInterval;
            var waitMilliseconds = Math.Max(1, (int)Math.Ceiling(slice.TotalMilliseconds));
            if (signal.WaitOne(waitMilliseconds))
            {
                if (!isAlive())
                {
                    throw new InvalidOperationException("The independent fan failsafe terminated during its handshake.");
                }
                return;
            }
        }
    }
}

internal sealed class FanGuardFailsafeSupervisor : IDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcknowledgeTimeout = TimeSpan.FromSeconds(1);

    private readonly EventWaitHandle _pulse;
    private readonly EventWaitHandle _ready;
    private readonly EventWaitHandle _acknowledge;
    private readonly Process _process;
    private readonly FanGuardFailsafePulseChannel _channel;
    private bool _disposed;

    private FanGuardFailsafeSupervisor(
        EventWaitHandle pulse,
        EventWaitHandle ready,
        EventWaitHandle acknowledge,
        Process process)
    {
        _pulse = pulse;
        _ready = ready;
        _acknowledge = acknowledge;
        _process = process;
        _channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, IsAlive);
    }

    public static FanGuardFailsafeSupervisor Start(int serviceProcessId)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var names = FanGuardFailsafeProtocol.CreateEventNames(serviceProcessId, nonce);
        EventWaitHandle? pulse = null;
        EventWaitHandle? ready = null;
        EventWaitHandle? acknowledge = null;
        Process? process = null;
        try
        {
            pulse = CreateUniqueEvent(names.Pulse);
            ready = CreateUniqueEvent(names.Ready);
            acknowledge = CreateUniqueEvent(names.Acknowledge);
            process = FanGuardFailsafe.StartCompanion(serviceProcessId, nonce);
            var supervisor = new FanGuardFailsafeSupervisor(pulse, ready, acknowledge, process);
            supervisor._channel.WaitUntilReady(ReadyTimeout);
            return supervisor;
        }
        catch
        {
            TryTerminate(process);
            process?.Dispose();
            acknowledge?.Dispose();
            ready?.Dispose();
            pulse?.Dispose();
            throw;
        }
    }

    public bool IsAlive()
    {
        if (_disposed)
        {
            return false;
        }
        try
        {
            return !_process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public long PulseAndRequireAcknowledge()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FanGuardFailsafeSupervisor));
        }
        return _channel.PulseAndRequireAcknowledge(AcknowledgeTimeout);
    }

    public void SignalShutdown()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            _pulse.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _acknowledge.Dispose();
        _ready.Dispose();
        _pulse.Dispose();
        _process.Dispose();
    }

    private static EventWaitHandle CreateUniqueEvent(string name)
    {
        var result = new EventWaitHandle(false, EventResetMode.AutoReset, name, out var createdNew);
        if (!createdNew)
        {
            result.Dispose();
            throw new InvalidOperationException("A failsafe handshake event already exists: " + name);
        }
        return result;
    }

    private static void TryTerminate(Process? process)
    {
        if (process == null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch
        {
        }
    }
}

internal static class FanGuardFailsafe
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(8);

    public static Process StartCompanion(int serviceProcessId, string nonce)
    {
        if (!FanGuardFailsafeProtocol.TryNormalizeNonce(nonce, out var normalizedNonce))
        {
            throw new ArgumentException("The failsafe nonce must be a 32-character GUID.", nameof(nonce));
        }

        var executable = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new FileNotFoundException("The guard executable path could not be resolved.", executable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Format(
                CultureInfo.InvariantCulture,
                "--guard-failsafe {0} {1}",
                serviceProcessId,
                normalizedNonce),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The out-of-process fan failsafe could not start.");
    }

    public static int Run(string processIdText, string nonceText)
    {
        try
        {
            if (!int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
                processId <= 0 ||
                !FanGuardFailsafeProtocol.TryNormalizeNonce(nonceText, out var nonce))
            {
                return 2;
            }

            var names = FanGuardFailsafeProtocol.CreateEventNames(processId, nonce);
            using (var parent = Process.GetProcessById(processId))
            using (var pulse = EventWaitHandle.OpenExisting(names.Pulse))
            using (var ready = EventWaitHandle.OpenExisting(names.Ready))
            using (var acknowledge = EventWaitHandle.OpenExisting(names.Acknowledge))
            {
                VerifySameExecutable(parent);
                ready.Set();

                var freshness = Stopwatch.StartNew();
                var dirtyWasPresent = false;
                while (true)
                {
                    parent.Refresh();
                    if (parent.HasExited)
                    {
                        return RecoverAfterParentExit();
                    }

                    var dirty = IsDirty();
                    if (!dirty)
                    {
                        dirtyWasPresent = false;
                        freshness.Restart();
                        if (pulse.WaitOne(1000))
                        {
                            acknowledge.Set();
                        }
                        continue;
                    }

                    if (!dirtyWasPresent)
                    {
                        dirtyWasPresent = true;
                        freshness.Restart();
                    }

                    if (pulse.WaitOne(1000))
                    {
                        acknowledge.Set();
                        freshness.Restart();
                        continue;
                    }

                    if (freshness.Elapsed <= ValidationTimeout)
                    {
                        continue;
                    }

                    GuardLog.Info("Out-of-process failsafe detected a stale dirty maximum; terminating the blocked guard.");
                    parent.Kill();
                    parent.WaitForExit(5000);
                    return RecoverAfterParentExit();
                }
            }
        }
        catch (Exception ex)
        {
            GuardLog.Error("Out-of-process failsafe failed.", ex);
            return 100;
        }
    }

    private static int RecoverAfterParentExit()
    {
        if (!IsDirty())
        {
            return 0;
        }

        // SCM recovery normally restarts the guard in one second. Give that fresh
        // process first chance to take the mutex and perform startup AUTO recovery.
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (!IsDirty())
            {
                return 0;
            }
            Thread.Sleep(250);
        }

        var executable = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return 101;
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--force-auto",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var recovery = Process.Start(startInfo))
        {
            if (recovery == null)
            {
                return 102;
            }
            if (!recovery.WaitForExit(20000))
            {
                recovery.Kill();
                return 103;
            }
            if (recovery.ExitCode != 0)
            {
                GuardLog.Error(
                    "Out-of-process AUTO recovery returned a failure.",
                    new InvalidOperationException(recovery.StandardError.ReadToEnd()));
                return recovery.ExitCode;
            }
        }

        new FanGuardJournal().Clear();
        return IsDirty() ? 104 : 0;
    }

    private static void VerifySameExecutable(Process parent)
    {
        var parentPath = parent.MainModule?.FileName;
        var ownPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(ownPath) ||
            !Path.GetFullPath(parentPath).Equals(Path.GetFullPath(ownPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Failsafe parent executable mismatch.");
        }
    }

    private static bool IsDirty()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SurfaceProFanControl",
            "fan-guard.dirty");
        return File.Exists(path) || File.Exists(path + ".tmp");
    }
}
