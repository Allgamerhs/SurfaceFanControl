using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using SurfaceProFanControl.Core;

namespace SurfaceProI2EcFanTest;

internal static class FanGuardMeasurementSelfTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Lease = "45c586afc94746c9bd7fd6eb413b9b6e";
    private static readonly Type Runtime = typeof(FanGuardRuntime);

    public static int Run()
    {
        // Do not initialize the real service or journal constructor. All marker operations
        // in this test are confined to one temporary file, and all EC reads use FakeItePortIo.
        var path = Path.GetTempFileName();
        var completed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Guard measurement self-test failed: " + message);
            completed++;
        }
        try
        {
            var journal = (FanGuardJournal)FormatterServices.GetUninitializedObject(typeof(FanGuardJournal));
            typeof(FanGuardJournal).GetField("_path", Private)!.SetValue(journal, path);
            var guard = (FanGuardRuntime)FormatterServices.GetUninitializedObject(Runtime);
            void Set(string field, object value) => Runtime.GetField(field, Private)!.SetValue(guard, value);
            object? Get(string field) => Runtime.GetField(field, Private)!.GetValue(guard);
            string Request(string request) =>
                (string)Runtime.GetMethod("HandleRequest", Private)!.Invoke(guard, new object[] { request });
            string Measure(Func<FanGuardStatus?> read)
            {
                var previous = Get("_readSensorSample")!;
                Set("_readSensorSample", read);
                try { return Request("MEASURE"); }
                finally { Set("_readSensorSample", previous); }
            }
            void OwnMaximum()
            {
                journal.MarkDirty();
                Set("_maximumActive", true);
                Set("_lastVerifiedStatus", new FanModeStatus(1, 1));
                Set("_leaseId", Lease);
                Set("_lastHeartbeatTimestamp", 9876L);
                Set("_lastFailsafeAcknowledgeTimestamp", Stopwatch.GetTimestamp());
            }
            Set("_journal", journal);
            Set("_hardwareGate", new object());
            OwnMaximum();

            using var fake = FakeItePortIo.CreateTarget();
            var reads = 0;
            FanGuardStatus ReadAutomatic()
            {
                using var session = I2EcFanSession.Open(fake, 0x2E);
                reads++;
                return new FanController(session).ReadGuardStatus();
            }
            var automatic = ReadAutomatic();
            Set("_readSensorSample", (Func<FanGuardStatus?>)ReadAutomatic);
            Set("_logMeasurement", (Action<string>)(_ => { }));
            var weakTargets = new FanGuardStatus(1, 1, automatic.Snapshot);
            var ownedTargets = new FanGuardStatus(1, 1,
                new FanSnapshot(new EcFanReading(8192, 0xB7, 7900, 0xB7), 0xB7, 0xB7));
            var normalReads = 0;
            Check(Measure(() => { normalReads++; return ownedTargets; }).StartsWith("OK|MAX|") && normalReads == 1,
                "Steady MAX requires only one measurement.");
            var calls = 0;
            Check(Measure(() => ++calls == 1 ? automatic : null).StartsWith("ERR|MODE_CONFLICT|") &&
                calls == 2 && journal.Exists, "An incomplete confirmation relinquished the marker.");
            Check(Request("STATUS").StartsWith("ERR|MODE_CONFLICT|") &&
                Request("PING|" + Lease).StartsWith("ERR|MODE_CONFLICT|") &&
                Request("MAX|" + Lease).StartsWith("ERR|MODE_CONFLICT|") &&
                Equals(Get("_lastHeartbeatTimestamp"), 9876L), "An unconfirmed lease was reported or extended.");
            calls = 0;
            Check(Measure(() => ++calls == 1 ? automatic : ownedTargets).StartsWith("ERR|MODE_CONFLICT|") &&
                journal.Exists, "Disagreeing readings released ownership.");
            Check(Measure(() => weakTargets).Contains("01/01") &&
                Request("STATUS").StartsWith("ERR|MODE_CONFLICT|") && journal.Exists,
                "A mismatched hardware target was reported as verified MAX.");

            // Losing either the confirming transaction or the durable clear must not announce AUTO.
            calls = 0;
            try
            {
                Measure(() => ++calls == 1 ? automatic : throw new IOException("Lost confirmation"));
                throw new InvalidOperationException("Missing confirmation failure.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
            Check(journal.Exists && Request("STATUS").StartsWith("ERR|MODE_CONFLICT|"),
                "Failed confirmation exposed a stale cached mode.");
            using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    Measure(() => automatic);
                    throw new InvalidOperationException("A locked marker was cleared.");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                Check(journal.Exists && Equals(Get("_leaseId"), Lease) &&
                    Request("STATUS").StartsWith("ERR|MODE_CONFLICT|"), "A failed durable clear completed the transition.");
            }
            reads = 0;
            Check(Measure(ReadAutomatic).StartsWith("OK|AUTO|") && reads == 2 && fake.MemoryWriteCount == 0 &&
                !journal.Exists && Get("_leaseId") == null && Equals(Get("_lastFailsafeAcknowledgeTimestamp"), 0L),
                "Two independent hardware AUTO reads did not release MAX without an EC write.");
            Check(Request("STATUS").StartsWith("OK|AUTO|") && Request("PING|" + Lease).StartsWith("OK|AUTO|"),
                "Cached status continued to claim MAX after a verified loss.");
            Check(Measure(() => ownedTargets).StartsWith("ERR|MODE_CONFLICT|") && !journal.Exists &&
                Get("_leaseId") == null && Request("MAX|" + Lease).StartsWith("ERR|MODE_CONFLICT|"),
                "A maximum measurement was adopted without ownership.");
            Set("_lastConflictProbe", Stopwatch.GetTimestamp() - 6L * Stopwatch.Frequency);
            reads = 0;
            Check(Request("STATUS").StartsWith("OK|AUTO|") && reads == 1 && fake.MemoryWriteCount == 0 &&
                Request("STATUS").StartsWith("OK|AUTO|"), "A subsequent genuine AUTO read did not clear unowned conflict.");
            OwnMaximum();
            journal.Clear();
            Check(Measure(() => ownedTargets).Contains("durable marker") &&
                Request("STATUS").StartsWith("ERR|MODE_CONFLICT|"), "A missing marker was reported as verified MAX.");
            return completed;
        }
        finally { File.Delete(path); }
    }

}
