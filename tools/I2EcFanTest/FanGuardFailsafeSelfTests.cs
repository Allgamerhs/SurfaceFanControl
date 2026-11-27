using System;
using System.Diagnostics;
using System.Threading;

namespace SurfaceProI2EcFanTest;

internal static class FanGuardFailsafeSelfTests
{
    public static int Run()
    {
        var completed = 0;
        NonceBindsAllEventNames();
        completed++;
        ReadyAndPerPulseAcknowledgementsComplete();
        completed++;
        PulseBeforeReadyIsRejected();
        completed++;
        MissingReadyFaultsTheChannel();
        completed++;
        ExitedCompanionFailsReadyImmediately();
        completed++;
        MissingAcknowledgeFaultsTheChannelPermanently();
        completed++;
        StaleAcknowledgeDoesNotSatisfyANewPulse();
        completed++;
        CompanionExitDuringPulseIsDetected();
        completed++;
        MonotonicAcknowledgeLeaseRejectsStaleTicks();
        completed++;
        completed += FanGuardMeasurementSelfTests.Run();
        return completed;
    }

    private static void NonceBindsAllEventNames()
    {
        var nonce = Guid.NewGuid().ToString("N");
        var names = FanGuardFailsafeProtocol.CreateEventNames(1234, nonce);
        var binding = ".1234." + nonce + ".";
        Assert(names.Pulse.Contains(binding), "Pulse event was not bound to the PID and nonce.");
        Assert(names.Ready.Contains(binding), "READY event was not bound to the PID and nonce.");
        Assert(names.Acknowledge.Contains(binding), "ACK event was not bound to the PID and nonce.");
        Assert(names.Pulse != names.Ready && names.Ready != names.Acknowledge && names.Pulse != names.Acknowledge,
            "Handshake event names were not distinct.");
        Assert(!FanGuardFailsafeProtocol.TryNormalizeNonce("not-a-nonce", out _),
            "Malformed failsafe nonce was accepted.");
    }

    private static void ReadyAndPerPulseAcknowledgementsComplete()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            Exception? companionFailure = null;
            var companion = new Thread(() =>
            {
                try
                {
                    ready.Set();
                    for (var index = 0; index < 2; index++)
                    {
                        if (!pulse.WaitOne(2000))
                        {
                            throw new TimeoutException("Test companion did not receive a pulse.");
                        }
                        acknowledge.Set();
                    }
                }
                catch (Exception ex)
                {
                    companionFailure = ex;
                }
            }) { IsBackground = true };
            companion.Start();

            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => true);
            channel.WaitUntilReady(TimeSpan.FromSeconds(2));
            var first = channel.PulseAndRequireAcknowledge(TimeSpan.FromSeconds(2));
            var second = channel.PulseAndRequireAcknowledge(TimeSpan.FromSeconds(2));

            Assert(companion.Join(2000), "Test companion did not finish two pulse acknowledgements.");
            if (companionFailure != null)
            {
                throw new InvalidOperationException("Test companion failed.", companionFailure);
            }
            Assert(first > 0 && second >= first, "Acknowledgement timestamps were not monotonic.");
        }
    }

    private static void PulseBeforeReadyIsRejected()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => true);
            AssertThrows<InvalidOperationException>(
                () => channel.PulseAndRequireAcknowledge(TimeSpan.FromMilliseconds(50)),
                "Pulse was allowed before the READY handshake.");
        }
    }

    private static void MissingReadyFaultsTheChannel()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => true);
            AssertThrows<TimeoutException>(
                () => channel.WaitUntilReady(TimeSpan.FromMilliseconds(40)),
                "Missing READY did not time out.");
            ready.Set();
            AssertThrows<InvalidOperationException>(
                () => channel.WaitUntilReady(TimeSpan.FromMilliseconds(40)),
                "A late READY revived a faulted channel.");
        }
    }

    private static void ExitedCompanionFailsReadyImmediately()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => false);
            AssertThrows<InvalidOperationException>(
                () => channel.WaitUntilReady(TimeSpan.FromSeconds(1)),
                "Exited companion was not detected during READY.");
        }
    }

    private static void MissingAcknowledgeFaultsTheChannelPermanently()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(true, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => true);
            channel.WaitUntilReady(TimeSpan.FromSeconds(1));
            AssertThrows<TimeoutException>(
                () => channel.PulseAndRequireAcknowledge(TimeSpan.FromMilliseconds(40)),
                "Missing per-pulse ACK did not time out.");

            acknowledge.Set();
            AssertThrows<InvalidOperationException>(
                () => channel.PulseAndRequireAcknowledge(TimeSpan.FromMilliseconds(40)),
                "A late ACK was reused by a later pulse.");
        }
    }

    private static void CompanionExitDuringPulseIsDetected()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(true, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(false, EventResetMode.AutoReset))
        {
            var alive = 1;
            var channel = new FanGuardFailsafePulseChannel(
                pulse,
                ready,
                acknowledge,
                () => Volatile.Read(ref alive) == 1);
            channel.WaitUntilReady(TimeSpan.FromSeconds(1));

            var companion = new Thread(() =>
            {
                if (pulse.WaitOne(2000))
                {
                    Volatile.Write(ref alive, 0);
                }
            }) { IsBackground = true };
            companion.Start();

            AssertThrows<InvalidOperationException>(
                () => channel.PulseAndRequireAcknowledge(TimeSpan.FromSeconds(1)),
                "Companion exit during a pulse was not detected.");
            Assert(companion.Join(2000), "Exit-injection companion did not finish.");
        }
    }

    private static void StaleAcknowledgeDoesNotSatisfyANewPulse()
    {
        using (var pulse = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var ready = new EventWaitHandle(true, EventResetMode.AutoReset))
        using (var acknowledge = new EventWaitHandle(true, EventResetMode.AutoReset))
        {
            var channel = new FanGuardFailsafePulseChannel(pulse, ready, acknowledge, () => true);
            channel.WaitUntilReady(TimeSpan.FromSeconds(1));
            AssertThrows<TimeoutException>(
                () => channel.PulseAndRequireAcknowledge(TimeSpan.FromMilliseconds(40)),
                "A stale ACK satisfied a new pulse.");
        }
    }

    private static void MonotonicAcknowledgeLeaseRejectsStaleTicks()
    {
        var start = Stopwatch.Frequency * 10L;
        var lease = TimeSpan.FromSeconds(2);
        Assert(FanGuardMonotonic.IsFresh(start, start + (Stopwatch.Frequency * 2L), lease),
            "ACK was rejected at the lease boundary.");
        Assert(!FanGuardMonotonic.IsFresh(start, start + (Stopwatch.Frequency * 2L) + 1, lease),
            "Stale ACK tick was accepted.");
        Assert(!FanGuardMonotonic.IsFresh(start, start - 1, lease),
            "Backward monotonic observation was accepted.");
        Assert(!FanGuardMonotonic.IsFresh(0, start, lease),
            "Uninitialized ACK timestamp was accepted.");
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("Failsafe self-test failed: " + message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failsafe self-test failed: " + message);
        }
    }
}
