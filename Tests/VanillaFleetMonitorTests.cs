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
            failed += Test("Fleet retains denied process metadata and stops subsequent access", DeniedMetadata);
            failed += Test("Fleet names the failed window metadata operation", DeniedWindowMetadata);
            failed += Test("Fleet invalidates stale values while preserving the healthy client", HealthySibling);
            failed += Test("Fleet clears a failed PID only after it disappears from enumeration", FailureLifetime);
            failed += Test("Fleet never retries a failed reader startup", FailedReaderStartup);
            failed += Test("Fleet retains a reader failure without checking process metadata again", StoppedReader);
            failed += Test("Fleet enumeration failure closes readers and permanently stops enumeration", FailedEnumeration);
            return failed;
        }

        private static void DeniedMetadata()
        {
            var process = new FakeProcess(11) { ExitError = new Win32Exception(5) };
            int creates = 0;
            using (var monitor = Monitor(() => new[] { process }, pid => { creates++; return new FakeReader(pid); }))
            {
                VanillaFleetClientInfo first = monitor.Poll().Single();
                Check(first.ProcessId == 11 && first.Error.StartsWith("Process.HasExited failed for PID 11: Win32 5 ("),
                    "An enumerated PID must retain its exact denied metadata operation and native error.");
                Check(first.Error.Contains("Observer PID 700, 32-bit") && first.Error.Contains("elevated no, integrity medium"),
                    "Failure must include the supplied observer context without querying a live process.");
                Check(!first.Ready && !first.CurrentHP.HasValue && !first.CurrentSP.HasValue,
                    "Failure cannot imply valid gameplay fields.");
                for (int i = 0; i < 3; i++) Check(monitor.Poll().Single().Error == first.Error, "The original failure remains visible.");
                Check(creates == 0 && process.ExitReads == 1 && process.WindowReads == 0,
                    "Stop at the denied operation; timer polls cannot repeat metadata or create a memory reader.");
                Check(process.Disposals == 4 && monitor.PollCount == 4, "Release every enumeration wrapper and preserve smoke poll accounting.");
            }
        }

        private static void DeniedWindowMetadata()
        {
            var process = new FakeProcess(11) { WindowError = new Win32Exception(5) };
            using (var monitor = Monitor(() => new[] { process }, pid => { throw new Exception("Reader must not open."); }))
            {
                string failure = monitor.Poll().Single().Error;
                Check(failure.StartsWith("Process.MainWindowHandle failed for PID 11: Win32 5 ("),
                    "The failing property must not be confused with the preceding successful HasExited query.");
                monitor.Poll();
                Check(process.ExitReads == 1 && process.WindowReads == 1, "Do not retry either property after window metadata denial.");
            }
        }

        private static void HealthySibling()
        {
            var affected = new FakeProcess(11);
            var healthy = new FakeProcess(12);
            var affectedReader = new FakeReader(11);
            var healthyReader = new FakeReader(12);
            using (var monitor = Monitor(() => new[] { affected, healthy }, pid => pid == 11 ? affectedReader : healthyReader))
            {
                Check(monitor.Poll().All(info => info.CurrentHP == 50), "Start with two successful fake observations.");
                affected.ExitError = new InvalidOperationException("Test process metadata unavailable");
                VanillaFleetClientInfo[] failure = monitor.Poll().ToArray();
                Check(failure[0].Error.Contains("Process.HasExited failed for PID 11") && !failure[0].CurrentHP.HasValue,
                    "Metadata failure replaces stale HP and retains the precise PID.");
                Check(affectedReader.Disposals == 1 && affectedReader.Polls == 1, "Dispose the affected reader before another memory poll.");
                monitor.Poll();
                Check(healthyReader.Polls == 3 && healthyReader.Disposals == 0 && failure[1].CurrentHP == 50,
                    "An unrelated healthy client must continue observation.");
                Check(affected.ExitReads == 2, "The failed client's metadata must stop while its sibling continues.");
            }
            Check(healthyReader.Disposals == 1, "Monitor disposal releases the healthy reader once.");
        }

        private static void FailureLifetime()
        {
            var original = new FakeProcess(11) { ExitError = new Win32Exception(5) };
            FakeProcess[] processes = { original };
            int creates = 0;
            using (var monitor = Monitor(() => processes, pid => { creates++; return new FakeReader(pid); }))
            {
                string failure = monitor.Poll().Single().Error;
                var samePid = new FakeProcess(11) { Window = IntPtr.Zero };
                processes = new[] { samePid };
                Check(monitor.Poll().Single().Error == failure && samePid.ExitReads == 0 && samePid.WindowReads == 0,
                    "A new enumeration wrapper or hidden window cannot reset the PID failure.");
                processes = new FakeProcess[0];
                Check(monitor.Poll().Count == 0, "An absent PID removes its retained failure.");
                processes = new[] { new FakeProcess(11) };
                Check(monitor.Poll().Single().Error == null && creates == 1,
                    "Only observed disappearance permits a newly enumerated PID to start a new observation.");
            }
        }

        private static void FailedReaderStartup()
        {
            var process = new FakeProcess(11);
            int creates = 0;
            using (var monitor = Monitor(() => new[] { process }, pid =>
            {
                creates++;
                throw new Win32Exception(5, "OpenProcess(read/limited-query, 0x1010) failed for PID 11: Win32 5 (test denial).");
            }))
            {
                string failure = monitor.Poll().Single().Error;
                Check(failure.Contains("OpenProcess(read/limited-query, 0x1010)"), "Preserve the original reader failure unchanged.");
                Check(monitor.Poll().Single().Error == failure && creates == 1 && process.ExitReads == 1 && process.WindowReads == 1,
                    "A failed memory open must also stop future metadata probes for that PID.");
            }
        }

        private static void StoppedReader()
        {
            var process = new FakeProcess(11);
            var reader = new FakeReader(11) { StopOnPoll = true };
            using (var monitor = Monitor(() => new[] { process }, pid => reader))
            {
                Check(monitor.Poll().Single().Error == "Test memory read denied.", "Expose the first failed reader poll.");
                monitor.Poll();
                Check(process.ExitReads == 1 && process.WindowReads == 1 && reader.MemoryAttempts == 1,
                    "A stopped reader's cached failure must require no further metadata or memory access.");
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
                Check(firstReader.Polls == 1 && secondReader.Polls == 1 && partial.ExitReads == 0,
                    "A failed enumeration cannot poll old memory or inspect its incomplete metadata result.");
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
                return new VanillaFleetClientInfo { ProcessId = processId, CurrentHP = IsStopped ? (uint?)null : 50,
                    Error = IsStopped ? "Test memory read denied." : null };
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
