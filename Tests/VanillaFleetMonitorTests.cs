using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using _4RTools.Model.Vanilla;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaFleetMonitorTests
    {
        public static int Run()
        {
            int failed = 0;
            failed += Test("Fleet uses the PID snapshot without protected process metadata", PidSnapshotAvoidsProtectedMetadata);
            failed += Test("Fleet preserves a healthy sibling when one reader stops", HealthySibling);
            failed += Test("Fleet clears a failed PID only after it disappears from enumeration", FailureLifetime);
            failed += Test("Fleet never retries a failed reader startup", FailedReaderStartup);
            failed += Test("Fleet retains a stopped reader without process metadata probes", StoppedReader);
            failed += Test("Fleet enumeration failure closes readers and permanently stops enumeration", FailedEnumeration);
            return failed;
        }

        private static void PidSnapshotAvoidsProtectedMetadata()
        {
            var process = new FakeProcess(11)
            {
                ExitError = new Win32Exception(5),
                WindowError = new Win32Exception(5)
            };
            int creates = 0;
            using (var monitor = Monitor(() => new[] { process }, pid => { creates++; return new FakeReader(pid); }))
            {
                for (int i = 0; i < 4; i++)
                {
                    VanillaFleetClientInfo info = monitor.Poll().Single();
                    Check(info.ProcessId == 11 && info.Error == null && info.CurrentHP == 50,
                        "An enumerated Vanilla PID must proceed directly to the VM reader.");
                }
                Check(creates == 1, "The reader is created once and reused.");
                Check(process.ExitReads == 0 && process.WindowReads == 0,
                    "Fleet polling must not touch HasExited or MainWindowHandle before VM_READ observation.");
                Check(process.Disposals == 4 && monitor.PollCount == 4,
                    "Every enumeration wrapper is released while the PID reader remains reusable.");
            }
        }

        private static void HealthySibling()
        {
            var affected = new FakeProcess(11);
            var healthy = new FakeProcess(12);
            var affectedReader = new FakeReader(11) { StopOnPoll = true };
            var healthyReader = new FakeReader(12);
            using (var monitor = Monitor(() => new[] { affected, healthy }, pid => pid == 11 ? affectedReader : healthyReader))
            {
                VanillaFleetClientInfo[] first = monitor.Poll().ToArray();
                Check(first[0].Error == "Test memory read denied." && !first[0].CurrentHP.HasValue,
                    "The affected reader exposes its memory failure without stale values.");
                Check(first[1].Error == null && first[1].CurrentHP == 50,
                    "An unrelated healthy client remains readable.");

                VanillaFleetClientInfo[] second = monitor.Poll().ToArray();
                Check(second[0].Error == "Test memory read denied." && affectedReader.MemoryAttempts == 1,
                    "A stopped reader may return its cached failure but must not attempt memory again.");
                Check(second[1].CurrentHP == 50 && healthyReader.MemoryAttempts == 2,
                    "Healthy sibling observation continues normally.");
                Check(affected.ExitReads == 0 && affected.WindowReads == 0 && healthy.ExitReads == 0 && healthy.WindowReads == 0,
                    "Neither client requires process-query metadata.");
            }
            Check(affectedReader.Disposals == 1 && healthyReader.Disposals == 1,
                "Monitor disposal releases both readers exactly once.");
        }

        private static void FailureLifetime()
        {
            FakeProcess[] processes = { new FakeProcess(11) };
            int creates = 0;
            using (var monitor = Monitor(() => processes, pid =>
            {
                creates++;
                if (creates == 1) throw new Win32Exception(5, "OpenProcess(read, 0x0010) failed for PID 11: Win32 5 (test denial).");
                return new FakeReader(pid);
            }))
            {
                string failure = monitor.Poll().Single().Error;
                processes = new[] { new FakeProcess(11) };
                Check(monitor.Poll().Single().Error == failure && creates == 1,
                    "The same still-enumerated PID retains its original failed reader without retrying it.");
                processes = new FakeProcess[0];
                Check(monitor.Poll().Count == 0, "An absent PID removes its retained failure.");
                processes = new[] { new FakeProcess(11) };
                Check(monitor.Poll().Single().Error == null && creates == 2,
                    "Observed disappearance permits the newly enumerated PID to start a fresh reader.");
            }
        }

        private static void FailedReaderStartup()
        {
            var process = new FakeProcess(11);
            int creates = 0;
            using (var monitor = Monitor(() => new[] { process }, pid =>
            {
                creates++;
                throw new Win32Exception(5, "OpenProcess(read, 0x0010) failed for PID 11: Win32 5 (test denial).");
            }))
            {
                string failure = monitor.Poll().Single().Error;
                Check(failure.Contains("OpenProcess(read, 0x0010)"), "Preserve the original reader failure unchanged.");
                Check(monitor.Poll().Single().Error == failure && creates == 1,
                    "A failed VM_READ open is retained for that PID until enumeration shows it disappeared.");
                Check(process.ExitReads == 0 && process.WindowReads == 0,
                    "Reader failure handling must not fall back to process-query metadata.");
            }
        }

        private static void StoppedReader()
        {
            var process = new FakeProcess(11);
            var reader = new FakeReader(11) { StopOnPoll = true };
            using (var monitor = Monitor(() => new[] { process }, pid => reader))
            {
                Check(monitor.Poll().Single().Error == "Test memory read denied.", "Expose the first failed reader poll.");
                Check(monitor.Poll().Single().Error == "Test memory read denied.", "Retain the stopped reader result.");
                Check(reader.MemoryAttempts == 1,
                    "A stopped reader cannot attempt memory again on later dashboard timer polls.");
                Check(process.ExitReads == 0 && process.WindowReads == 0,
                    "Stopped readers do not trigger process-query metadata probes.");
            }
        }

        private static void FailedEnumeration()
        {
            var first = new FakeProcess(11);
            var second = new FakeProcess(12);
            var partial = new FakeProcess(13);
            var firstReader = new FakeReader(11);
            var secondReader = new FakeReader(12);
            int enumerations = 0;
            using (var monitor = Monitor(() => ++enumerations == 1
                ? new[] { first, second } : FailAfter(partial), pid => pid == 11 ? firstReader : secondReader))
            {
                Check(monitor.Poll().Count == 2, "Start with two observed fake clients.");
                MemoryObservationException failure = null;
                try { monitor.Poll(); }
                catch (MemoryObservationException ex) { failure = ex; }
                Check(failure != null && failure.NativeErrorCode == 5
                    && failure.Message.StartsWith("Process.GetProcessesByName(Vanilla MMO) failed: Win32 5 (")
                    && failure.Message.Contains("Observer PID 700, 32-bit"),
                    "Keep the enumeration operation, native failure and observer identity; do not return an empty fleet.");
                Check(firstReader.Disposals == 1 && secondReader.Disposals == 1 && partial.Disposals == 1,
                    "Close both existing observations and release the partially enumerated wrapper.");
                Check(firstReader.Polls == 1 && secondReader.Polls == 1 && partial.ExitReads == 0 && partial.WindowReads == 0,
                    "A failed enumeration cannot poll old memory or inspect incomplete process metadata.");
                try { monitor.Poll(); throw new Exception("A stopped monitor returned a misleading fleet."); }
                catch (MemoryObservationException ex) { Check(ReferenceEquals(ex, failure), "Retain the initial failure unchanged."); }
                Check(enumerations == 2, "Later timer polls must never repeat the failed enumeration.");
            }
            Check(firstReader.Disposals == 1 && secondReader.Disposals == 1,
                "Monitor disposal cannot dispose the released observations again.");
        }

        private static IEnumerable<VanillaFleetMonitor.IProcessMetadata> FailAfter(FakeProcess process)
        {
            yield return process;
            throw new Win32Exception(5);
        }

        private static VanillaFleetMonitor Monitor(Func<IEnumerable<VanillaFleetMonitor.IProcessMetadata>> processes,
            Func<int, VanillaFleetMonitor.IClientReader> reader)
        {
            var context = new ProcessObservationContext(700, 4, null, false, 0x2000);
            return new VanillaFleetMonitor(AppDomain.CurrentDomain.BaseDirectory, processes, reader, () => context);
        }

        private sealed class FakeProcess : VanillaFleetMonitor.IProcessMetadata
        {
            public FakeProcess(int processId) { ProcessId = processId; }
            public int ProcessId { get; }
            public Exception ExitError { get; set; }
            public Exception WindowError { get; set; }
            public IntPtr Window { get; set; } = new IntPtr(1);
            public int ExitReads { get; private set; }
            public int WindowReads { get; private set; }
            public int Disposals { get; private set; }
            public bool HasExited
            {
                get { ExitReads++; if (ExitError != null) throw ExitError; return false; }
            }
            public IntPtr MainWindowHandle
            {
                get { WindowReads++; if (WindowError != null) throw WindowError; return Window; }
            }
            public void Dispose() { Disposals++; }
        }

        private sealed class FakeReader : VanillaFleetMonitor.IClientReader
        {
            private readonly int processId;
            public FakeReader(int processId) { this.processId = processId; }
            public bool IsStopped { get; private set; }
            public bool StopOnPoll { get; set; }
            public int Polls { get; private set; }
            public int MemoryAttempts { get; private set; }
            public int Disposals { get; private set; }
            public VanillaFleetClientInfo Poll(TimeSpan now)
            {
                Polls++;
                if (!IsStopped) MemoryAttempts++;
                if (StopOnPoll) IsStopped = true;
                return new VanillaFleetClientInfo
                {
                    ProcessId = processId,
                    CurrentHP = IsStopped ? (uint?)null : 50,
                    Error = IsStopped ? "Test memory read denied." : null
                };
            }
            public void Dispose() { Disposals++; IsStopped = true; }
        }

        private static int Test(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); return 1; }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
