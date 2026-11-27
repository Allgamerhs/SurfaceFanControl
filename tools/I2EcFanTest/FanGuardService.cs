using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SurfaceProI2EcFanTest;

internal sealed class FanGuardService : ServiceBase
{
    internal const string WindowsServiceName = "SurfaceProFanGuard";
    internal const string PipeName = "SurfaceProFanControl.Guard.v1";

    private readonly object _lifecycleGate = new object();
    private CancellationTokenSource? _cancellation;
    private Thread? _worker;
    private FanGuardFailsafeSupervisor? _failsafe;
    private volatile bool _stopping;

    public FanGuardService()
    {
        ServiceName = WindowsServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    public static void RunService()
    {
        ServiceBase.Run(new FanGuardService());
    }

    protected override void OnStart(string[] args)
    {
        lock (_lifecycleGate)
        {
            _stopping = false;
            var failsafe = FanGuardFailsafeSupervisor.Start(Process.GetCurrentProcess().Id);
            try
            {
                _failsafe = failsafe;
                _cancellation = new CancellationTokenSource();
                _worker = new Thread(() => RunWorker(_cancellation.Token))
                {
                    IsBackground = true,
                    Name = "SurfaceProFanGuard"
                };
                _worker.Start();
            }
            catch
            {
                _worker = null;
                _cancellation?.Dispose();
                _cancellation = null;
                failsafe.SignalShutdown();
                failsafe.Dispose();
                _failsafe = null;
                throw;
            }
        }
    }

    protected override void OnStop()
    {
        StopWorker();
    }

    protected override void OnShutdown()
    {
        StopWorker();
        base.OnShutdown();
    }

    private void RunWorker(CancellationToken cancellationToken)
    {
        try
        {
            new FanGuardRuntime(PulseFailsafe, IsFailsafeAlive).Run(cancellationToken);
        }
        catch (OperationCanceledException) when (_stopping)
        {
        }
        catch (Exception ex)
        {
            GuardLog.Error("Guard worker terminated unexpectedly.", ex);
            if (!_stopping)
            {
                Environment.ExitCode = 1;
                Environment.Exit(1);
            }
        }
    }

    private void StopWorker()
    {
        lock (_lifecycleGate)
        {
            _stopping = true;
            _cancellation?.Cancel();
            WakePipeServer("STOP");
            if (_worker != null && !_worker.Join(TimeSpan.FromSeconds(12)))
            {
                GuardLog.Error("Guard worker did not stop within twelve seconds.", new System.TimeoutException());
            }
            _worker = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _failsafe?.SignalShutdown();
            _failsafe?.Dispose();
            _failsafe = null;
        }
    }

    private long PulseFailsafe()
    {
        var failsafe = _failsafe ?? throw new InvalidOperationException("The independent fan failsafe is unavailable.");
        return failsafe.PulseAndRequireAcknowledge();
    }

    private bool IsFailsafeAlive()
    {
        try
        {
            return _failsafe != null && _failsafe.IsAlive();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static void WakePipeServer(string message)
    {
        try
        {
            using (var client = new NamedPipeClientStream(
                       ".",
                       PipeName,
                       PipeDirection.Out,
                       PipeOptions.None))
            {
                client.Connect(500);
                using (var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    writer.WriteLine(message);
                }
            }
        }
        catch
        {
        }
    }
}

internal sealed class FanGuardRuntime
{
    private const string ExpectedManufacturer = "Microsoft Corporation";
    private const string ExpectedModel = "Surface with Windows 8 Pro";
    private static readonly TimeSpan HeartbeatLease = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FailsafeAcknowledgeLease = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PipeRequestDeadline = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SampleMutexWait = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SampleWarningInterval = TimeSpan.FromSeconds(30);
    private const int ErrorNotFound = 1168;

    private readonly object _hardwareGate = new object();
    private readonly FanGuardJournal _journal = new FanGuardJournal();
    private readonly Func<long> _pulseFailsafe;
    private readonly Func<bool> _isFailsafeAlive;
    private readonly Func<FanGuardStatus?> _readSensorSample;
    private readonly Action<string> _logMeasurement;
    private FanModeStatus _lastVerifiedStatus = new FanModeStatus(0x00, 0x00);
    private bool _maximumActive;
    private string? _measurementConflict;
    private long _lastConflictProbe;
    private long _lastHeartbeatTimestamp;
    private long _lastFailsafeAcknowledgeTimestamp;
    private string? _leaseId;
    private volatile Exception? _watchdogFailure;
    private int _watchdogTickRunning;
    private string? _measurementStage;
    private long _measurementStarted;
    private long _lastBusyWarning;
    private long _lastSlowWarning;

    public FanGuardRuntime(Func<long> pulseFailsafe, Func<bool> isFailsafeAlive)
    {
        _pulseFailsafe = pulseFailsafe ?? throw new ArgumentNullException(nameof(pulseFailsafe));
        _isFailsafeAlive = isFailsafeAlive ?? throw new ArgumentNullException(nameof(isFailsafeAlive));
        _readSensorSample = ReadHardwareSample;
        _logMeasurement = GuardLog.Info;
    }

    public void Run(CancellationToken cancellationToken)
    {
        ValidateTarget();
        try
        {
            RecoverAtStartup();
            var timer = new Timer(WatchdogTick, cancellationToken, 500, 500);
            using (var timerDrain = new ManualResetEvent(false))
            {
                try
                {
                    GuardLog.Info("Guard ready; clean startup performed no PNPCFG access.");
                    ServePipe(cancellationToken);
                }
                finally
                {
                    if (!timer.Dispose(timerDrain) || !timerDrain.WaitOne())
                    {
                        throw new System.TimeoutException(
                            "The heartbeat watchdog callback did not drain during shutdown.");
                    }
                }
            }
        }
        finally
        {
            RestoreAutomaticForShutdown();
        }
    }

    private void RecoverAtStartup()
    {
        lock (_hardwareGate)
        {
            if (_journal.Exists)
            {
                GuardLog.Info("Dirty EC state found; performing one bounded startup AUTO recovery.");
                _lastVerifiedStatus = ExecuteOneShot(controller =>
                {
                    controller.ValidateForRecovery();
                    return controller.ForceAutomaticModeAndReport();
                });
                _journal.Clear();
            }
            else
            {
                _lastVerifiedStatus = new FanModeStatus(0x00, 0x00);
            }
            _maximumActive = false;
            _leaseId = null;
            _lastFailsafeAcknowledgeTimestamp = 0;
            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void ServePipe(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using (var pipe = new NamedPipeServerStream(
                       FanGuardService.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                       PipeOptions.Asynchronous | PipeOptions.WriteThrough, 4096, 4096, CreatePipeSecurity()))
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_watchdogFailure != null)
                        throw new InvalidOperationException("The heartbeat watchdog failed.", _watchdogFailure);

                    try
                    {
                        pipe.WaitForConnection();
                    }
                    catch (IOException ex) when (IsPeerDisconnect(ex))
                    {
                        break;
                    }

                    try
                    {
                        ReplyToRequest(pipe, cancellationToken);
                    }
                    catch (System.TimeoutException ex)
                    {
                        GuardLog.Error("A pipe client exceeded the request deadline.", ex);
                    }
                    catch (InvalidDataException ex)
                    {
                        GuardLog.Error("A pipe client sent an invalid request.", ex);
                    }
                    catch (IOException ex)
                    {
                        GuardLog.Error("The pipe client disconnected.", ex);
                        break;
                    }

                    if (!pipe.IsConnected) break;
                    try
                    {
                        pipe.Disconnect();
                    }
                    catch (IOException)
                    {
                        // This connection cannot be reused. The next listener retains the same ACL.
                        break;
                    }
                }
            }
        }
    }

    private void ReplyToRequest(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var request = ReadRequestWithDeadline(pipe);
        string response;
        if (string.Equals(request, "STOP", StringComparison.OrdinalIgnoreCase) &&
            cancellationToken.IsCancellationRequested)
        {
            response = "OK|STOPPING";
        }
        else
        {
            if (_watchdogFailure != null)
                throw new InvalidOperationException("The heartbeat watchdog failed.", _watchdogFailure);

            try
            {
                response = HandleRequest(request);
            }
            catch (Exception ex)
            {
                GuardLog.Error("Guard request failed: " + request, ex);
                response = "ERR|REQUEST_FAILED|" + Sanitize(ex.Message);
            }
        }

        // The command has already completed. A lost reply is a connection failure only.
        var bytes = Encoding.UTF8.GetBytes(response + "\n");
        pipe.Write(bytes, 0, bytes.Length);
    }

    private static bool IsPeerDisconnect(IOException ex)
    {
        var code = ex.HResult & 0xffff;
        return code == 109 || code == 232 || code == 233; // Broken pipe, no data, pipe not connected.
    }

    private string HandleRequest(string request)
    {
        var fields = request.Split('|');
        var command = fields.Length == 0 ? string.Empty : fields[0].Trim().ToUpperInvariant();
        lock (_hardwareGate)
        {
            switch (command)
            {
                case "MAX":
                    var requestedLease = ParseLease(fields);
                    if (_measurementConflict != null) return _measurementConflict;
                    if (_maximumActive && !string.Equals(_leaseId, requestedLease, StringComparison.Ordinal))
                    {
                        return "ERR|LEASE_MISMATCH|Maximum mode belongs to another live controller.";
                    }
                    if (!_isFailsafeAlive())
                    {
                        throw new InvalidOperationException("The independent fan failsafe is not running.");
                    }
                    RequireFailsafeAcknowledge();

                    if (!_maximumActive)
                    {
                        _journal.MarkDirty();
                        try
                        {
                            var entered = ExecuteOneShot(controller => controller.EnterMaximumOneShot());
                            if (!entered.IsMaximum)
                            {
                                throw new InvalidDataException("Maximum-mode acknowledgement was incomplete.");
                            }
                            var committedAcknowledge = RequireFailsafeAcknowledge();
                            _maximumActive = true;
                            _lastVerifiedStatus = entered;
                            _leaseId = requestedLease;
                            _lastFailsafeAcknowledgeTimestamp = committedAcknowledge;
                            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
                            GuardLog.Info("Persistent firmware maximum enabled by one bounded command.");
                            return FormatStatus(entered, "MAX");
                        }
                        catch
                        {
                            _maximumActive = false;
                            _leaseId = null;
                            _lastFailsafeAcknowledgeTimestamp = 0;
                            try
                            {
                                _lastVerifiedStatus = ExecuteOneShot(
                                    controller => controller.ForceAutomaticModeAndReport());
                                _journal.Clear();
                            }
                            catch
                            {
                                // Keep the durable marker so service recovery retries on restart.
                            }
                            throw;
                        }
                    }

                    _lastFailsafeAcknowledgeTimestamp = RequireFailsafeAcknowledge();
                    _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
                    return FormatStatus(RequireCachedStatus(true), "MAX");

                case "AUTO":
                    AcceptAutomatic(ExecuteOneShot(controller => controller.ForceAutomaticModeAndReport()));
                    GuardLog.Info("Firmware automatic mode restored by request.");
                    return FormatStatus(_lastVerifiedStatus, "AUTO");

                case "STATUS":
                    return ReadAndValidateStatus();

                case "MEASURE":
                    return ReadCurrentMeasurement();

                case "PING":
                    var pingLease = ParseLease(fields);
                    if (_maximumActive && !string.Equals(_leaseId, pingLease, StringComparison.Ordinal))
                    {
                        return "ERR|LEASE_MISMATCH|Heartbeat lease is not current.";
                    }
                    var pingStatus = ReadAndValidateStatus();
                    if (_maximumActive && _measurementConflict == null)
                    {
                        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
                    }
                    return pingStatus;

                default:
                    return "ERR|UNKNOWN_COMMAND|Only STATUS, PING, MAX, AUTO, and MEASURE are accepted.";
            }
        }
    }

    private void WatchdogTick(object? state)
    {
        var stage = Volatile.Read(ref _measurementStage);
        if (stage != null && ElapsedSince(Interlocked.Read(ref _measurementStarted), Stopwatch.GetTimestamp()) >= TimeSpan.FromSeconds(1))
            QueueMeasurementWarning(ref _lastSlowWarning, "MEASURE has been in progress for at least one second; current stage: " + stage);

        if (Interlocked.Exchange(ref _watchdogTickRunning, 1) != 0)
        {
            return;
        }

        try
        {
            var cancellationToken = state is CancellationToken token ? token : CancellationToken.None;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!_isFailsafeAlive())
            {
                FailWatchdog(new InvalidOperationException("The independent fan failsafe terminated."));
                return;
            }
            if (!_journal.Exists && !_maximumActive)
            {
                return;
            }

            try
            {
                lock (_hardwareGate)
                {
                    if (!_journal.Exists && !_maximumActive)
                    {
                        return;
                    }

                    var now = Stopwatch.GetTimestamp();
                    if (_maximumActive && !FanGuardMonotonic.IsFresh(
                            _lastFailsafeAcknowledgeTimestamp,
                            now,
                            FailsafeAcknowledgeLease))
                    {
                        throw new System.TimeoutException(
                            "The independent fan failsafe acknowledgement lease expired.");
                    }
                    if (_maximumActive && ElapsedSince(_lastHeartbeatTimestamp, now) <= HeartbeatLease)
                    {
                        var status = RequireCachedStatus(true);
                        if (status.IsMaximum)
                        {
                            _lastFailsafeAcknowledgeTimestamp = RequireFailsafeAcknowledge();
                            return;
                        }
                        GuardLog.Info("Cached maximum-mode acknowledgement was lost; restoring automatic mode.");
                    }

                    AcceptAutomatic(ExecuteOneShot(controller => controller.ForceAutomaticModeAndReport()));
                    GuardLog.Info("Heartbeat expired; firmware automatic mode restored.");
                }
            }
            catch (Exception ex)
            {
                FailWatchdog(ex);
            }
        }
        finally
        {
            Volatile.Write(ref _watchdogTickRunning, 0);
        }
    }

    private long RequireFailsafeAcknowledge()
    {
        try
        {
            var acknowledgedAt = _pulseFailsafe();
            var observedAt = Stopwatch.GetTimestamp();
            if (!FanGuardMonotonic.IsFresh(acknowledgedAt, observedAt, FailsafeAcknowledgeLease))
            {
                throw new System.TimeoutException("The independent fan failsafe acknowledgement was stale.");
            }
            return acknowledgedAt;
        }
        catch (Exception ex)
        {
            _watchdogFailure = ex;
            throw;
        }
    }

    private void FailWatchdog(Exception failure)
    {
        GuardLog.Error("Heartbeat watchdog root failure.", failure);
        try
        {
            lock (_hardwareGate)
            {
                if (_journal.Exists || _maximumActive)
                {
                    _lastVerifiedStatus = ExecuteOneShot(
                        controller => controller.ForceAutomaticModeAndReport());
                    _journal.Clear();
                    _maximumActive = false;
                    _leaseId = null;
                    _lastFailsafeAcknowledgeTimestamp = 0;
                }
            }
        }
        catch (Exception recoveryFailure)
        {
            failure = new AggregateException(failure, recoveryFailure);
        }
        _watchdogFailure = failure;
        FanGuardService.WakePipeServer("WATCHDOG");
    }

    private void RestoreAutomaticForShutdown()
    {
        lock (_hardwareGate)
        {
            if (!_journal.Exists && !_maximumActive)
            {
                return;
            }
            try
            {
                _lastVerifiedStatus = ExecuteOneShot(
                    controller => controller.ForceAutomaticModeAndReport());
                _journal.Clear();
                _maximumActive = false;
                _leaseId = null;
                _lastFailsafeAcknowledgeTimestamp = 0;
                GuardLog.Info("Guard shutdown restored firmware automatic mode.");
            }
            catch (Exception ex)
            {
                GuardLog.Error("Guard shutdown could not verify automatic mode; dirty marker retained.", ex);
                throw;
            }
        }
    }

    private T ExecuteOneShot<T>(Func<FanController, T> action) where T : class =>
        TryExecuteOneShot(action, measurement: false) ??
        throw new System.TimeoutException("The global ISA/EC mutex could not be acquired.");

    private T? TryExecuteOneShot<T>(Func<FanController, T> action, bool measurement) where T : class
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        using (var mutex = new Mutex(false, @"Global\Access_ISABUS.HTP.Method"))
        {
            var acquired = false;
            try
            {
                if (measurement)
                {
                    Interlocked.Exchange(ref _measurementStarted, Stopwatch.GetTimestamp());
                    Volatile.Write(ref _measurementStage, "waiting for sensor bus");
                }
                try
                {
                    acquired = mutex.WaitOne(measurement ? SampleMutexWait : TimeSpan.FromSeconds(10), false);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                {
                    if (measurement)
                        QueueMeasurementWarning(ref _lastBusyWarning, "Skipped MEASURE: sensor bus remained busy for 200 ms.");
                    return null;
                }

                if (measurement) Volatile.Write(ref _measurementStage, "checking driver identity and state");
                DriverTrust.VerifyRunningPinnedDriver();
                if (measurement) Volatile.Write(ref _measurementStage, "reading device sensors");
                using (var rawIo = WinRingPortIo.Open())
                {
                    var identity = PnpScanner.ReadExpectedAt2E(rawIo);
                    if (!identity.IsIte8519 || identity.Version != 0x08)
                    {
                        throw new InvalidDataException(
                            "The exact ITE 8519 rev08 decoder was not available at fixed port 0x2E.");
                    }
                    using (var session = I2EcFanSession.Open(rawIo, 0x2E))
                    {
                        return action(new FanController(session));
                    }
                }
            }
            finally
            {
                if (measurement) Volatile.Write(ref _measurementStage, null);
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }

    private static void QueueMeasurementWarning(ref long lastWarning, string message)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref lastWarning);
        if (previous != 0 && ElapsedSince(previous, now) < SampleWarningInterval) return;
        if (Interlocked.CompareExchange(ref lastWarning, now, previous) == previous)
            ThreadPool.QueueUserWorkItem(_ => GuardLog.Info(message));
    }

    private string ReadAndValidateStatus()
    {
        if (_measurementConflict != null)
        {
            // With no owned lease, retry a read occasionally so that an independently
            // restored firmware mode can be recognized without issuing any command.
            if (!_maximumActive && ElapsedSince(_lastConflictProbe, Stopwatch.GetTimestamp()) >= TimeSpan.FromSeconds(5))
            {
                _lastConflictProbe = Stopwatch.GetTimestamp();
                return ReadCurrentMeasurement();
            }
            return _measurementConflict;
        }
        var status = RequireCachedStatus(_maximumActive);
        return FormatStatus(status, _maximumActive ? "MAX" : "AUTO");
    }

    private FanGuardStatus? ReadHardwareSample() =>
        TryExecuteOneShot(controller => controller.ReadGuardStatus(), measurement: true);

    private string ReadCurrentMeasurement()
    {
        var ownedBeforeRead = _maximumActive;
        var hadConflict = _measurementConflict != null;
        try { return ReadAndReconcileMeasurement(); }
        finally
        {
            if (ownedBeforeRead && !_maximumActive)
                _logMeasurement("Two hardware reads confirmed firmware AUTO while MAX was owned; released the lease.");
            else if (!hadConflict && _measurementConflict != null)
                _logMeasurement("Hardware mode conflict detected. " + _measurementConflict);
        }
    }

    private string ReadAndReconcileMeasurement()
    {
        var sample = _readSensorSample();
        if (sample == null) return "ERR|SAMPLE_BUSY|Sensor bus busy; no new measurement.";
        if (_maximumActive && sample.IsAutomatic)
        {
            RecordConflict(sample);
            // Reopen the sensor bus and verify both firmware mode registers independently.
            // Keep the dirty marker until the complete confirming read has returned.
            var confirmation = _readSensorSample();
            if (confirmation == null) return _measurementConflict!;
            if (!confirmation.IsAutomatic) return RecordConflict(confirmation, "First hardware read was 00/00.");
            AcceptAutomatic(new FanModeStatus(confirmation.Mode, confirmation.ModeShadow));
            return FormatMeasuredStatus(confirmation, "AUTO");
        }
        if (_maximumActive ? !sample.IsMaximum : !sample.IsAutomatic) return RecordConflict(sample);
        if (_journal.Exists != _maximumActive)
            return RecordConflict(sample, "The durable marker disagrees with guard ownership.");
        _lastVerifiedStatus = new FanModeStatus(sample.Mode, sample.ModeShadow);
        _measurementConflict = null;
        return FormatMeasuredStatus(sample, _maximumActive ? "MAX" : "AUTO");
    }

    private string RecordConflict(FanGuardStatus sample, string? context = null)
    {
        var snapshot = sample.Snapshot;
        var detail = string.Format(CultureInfo.InvariantCulture,
            "Guard expected {0}; measured mode/shadow {1:X2}/{2:X2}, fan command/duty {3:X2}/{4:X2} and {5:X2}/{6:X2}.",
            _maximumActive ? "MAX" : "AUTO", sample.Mode, sample.ModeShadow,
            snapshot.Fan0Command, snapshot.Reading.Fan0DutyRaw, snapshot.Fan1Command, snapshot.Reading.Fan1DutyRaw);
        _lastConflictProbe = Stopwatch.GetTimestamp();
        _measurementConflict = "ERR|MODE_CONFLICT|" + detail + (context == null ? string.Empty : " " + context);
        return _measurementConflict;
    }

    private void AcceptAutomatic(FanModeStatus status)
    {
        if (!status.IsAutomatic) throw new InvalidDataException("Hardware AUTO acknowledgement was incomplete.");
        _lastVerifiedStatus = status;
        _journal.Clear();
        _maximumActive = false;
        _leaseId = null;
        _lastFailsafeAcknowledgeTimestamp = 0;
        _measurementConflict = null;
    }

    private FanModeStatus RequireCachedStatus(bool maximumExpected)
    {
        var status = _lastVerifiedStatus;
        if (maximumExpected)
        {
            if (!status.IsMaximum || !_journal.Exists)
            {
                throw new InvalidOperationException("Cached maximum-mode ownership is incomplete.");
            }
        }
        else if (!status.IsAutomatic || _journal.Exists)
        {
            throw new InvalidOperationException("Cached automatic-mode ownership is incomplete.");
        }
        return status;
    }

    private static string ParseLease(string[] fields)
    {
        if (fields.Length != 2 || !Guid.TryParseExact(fields[1].Trim(), "N", out var lease))
        {
            throw new InvalidDataException("A 32-character lease identifier is required.");
        }
        return lease.ToString("N");
    }

    private static string ReadRequestWithDeadline(NamedPipeServerStream pipe)
    {
        var readTask = ReadBoundedRequestAsync(pipe);
        var completed = Task.WhenAny(readTask, Task.Delay(PipeRequestDeadline))
            .GetAwaiter().GetResult();
        if (ReferenceEquals(completed, readTask))
        {
            return readTask.GetAwaiter().GetResult();
        }

        if (!CancelIoEx(pipe.SafePipeHandle, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "The stalled pipe read could not be cancelled.");
            }
        }

        try
        {
            completed = Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(1)))
                .GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, readTask))
            {
                throw new IOException("The cancelled pipe read did not terminate.");
            }
            readTask.GetAwaiter().GetResult();
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        throw new System.TimeoutException("The pipe request did not arrive within three seconds.");
    }

    private static async Task<string> ReadBoundedRequestAsync(Stream stream)
    {
        var bytes = new byte[256];
        var count = 0;
        var next = new byte[1];
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(next, 0, 1).ConfigureAwait(false);
            if (read == 0 || next[0] == '\n')
            {
                return Encoding.UTF8.GetString(bytes, 0, count).Trim();
            }
            if (next[0] != '\r')
            {
                bytes[count++] = next[0];
            }
        }

        throw new InvalidDataException("The pipe request exceeded 255 bytes.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafePipeHandle pipeHandle, IntPtr overlapped);

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    private static TimeSpan ElapsedSince(long start, long end)
    {
        return FanGuardMonotonic.ElapsedSince(start, end);
    }

    private static string FormatStatus(FanModeStatus status, string state)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "OK|{0}|0|0|0|0|--|--|{1:X2}|{2:X2}",
            state,
            status.Mode,
            status.ModeShadow);
    }

    private static string FormatMeasuredStatus(FanGuardStatus status, string state)
    {
        var reading = status.Snapshot.Reading;
        return string.Format(
            CultureInfo.InvariantCulture,
            "OK|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7:X2}|{8:X2}",
            state,
            reading.Fan0Rpm,
            reading.Fan1Rpm,
            reading.Fan0DutyRaw,
            reading.Fan1DutyRaw,
            reading.Thermal?.TemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture) ?? "--",
            reading.Thermal?.CriticalTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture) ?? "--",
            status.Mode,
            status.ModeShadow);
    }

    private static void ValidateTarget()
    {
        var target = TargetIdentity.Read();
        if (!target.IsExpected(ExpectedManufacturer, ExpectedModel))
        {
            throw new InvalidOperationException("Guard target mismatch: " + target.ToDisplayString());
        }
    }

    private static string Sanitize(string value)
    {
        return value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}

internal static class FanGuardMonotonic
{
    public static bool IsFresh(long timestamp, long observedAt, TimeSpan lease)
    {
        if (timestamp <= 0 || observedAt < timestamp || lease < TimeSpan.Zero)
        {
            return false;
        }
        var elapsedTicks = observedAt - timestamp;
        var allowedTicks = lease.TotalSeconds * Stopwatch.Frequency;
        return elapsedTicks <= allowedTicks;
    }

    public static TimeSpan ElapsedSince(long timestamp, long observedAt)
    {
        if (timestamp <= 0 || observedAt < timestamp)
        {
            return TimeSpan.MaxValue;
        }
        return TimeSpan.FromSeconds((observedAt - timestamp) / (double)Stopwatch.Frequency);
    }
}

internal sealed class FanGuardJournal
{
    private readonly string _path;

    public FanGuardJournal()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(commonData, "SurfaceProFanControl");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "fan-guard.dirty");
    }

    public bool Exists => File.Exists(_path) || File.Exists(_path + ".tmp");

    public void MarkDirty()
    {
        var payload = Encoding.UTF8.GetBytes(
            "target=surface-pro-1-it8519\r\n" +
            "state=manual-maximum\r\n" +
            "utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\r\n");
        using (var stream = new FileStream(
                   _path,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.Read,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(payload, 0, payload.Length);
            stream.Flush(true);
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
        var temporary = _path + ".tmp";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }
    }
}

internal static class GuardLog
{
    private static readonly object Sync = new object();

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message + " | " + exception.GetType().Name + ": " + exception.Message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SurfaceProFanControl");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "fan-guard.log");
            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + level + " " +
                    message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }
}
