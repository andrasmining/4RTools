using System;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using _4RTools.Model.Vanilla;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaMemoryScannerTests
    {
        public static int Run()
        {
            int failed = 0;
            failed += Test("Finder query failure discards a partially captured baseline", QueryFailure);
            failed += Test("Finder failed recapture clears old candidates and latches failure", RecaptureFailure);
            failed += Test("Finder partial reads preserve Win32 299 and stop observation", PartialRead);
            failed += Test("Finder failed refinement does not mutate completed candidate values", AtomicRefine);
            failed += Test("Finder candidate limit does not partially advance comparison baseline", AtomicFirstCompare);
            failed += Test("Finder invalid query ranges stop without skipping pages", InvalidRanges);
            failed += Test("Finder unsupported datatypes never become misleading mappings", MappingTypes);
            failed += Test("Finder rejects stale candidates after a completed refinement", StaleCandidate);
            failed += Test("Finder signed hexadecimal input cannot truncate to sixteen bits", SignedHexWidth);
            failed += Test("Finder empty comparison never restarts a broad scan implicitly", EmptyComparison);
            failed += Test("Finder writable scan respects its explicit query boundary", WritableBoundary);
            return failed;
        }

        private static int Test(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); return 1; }
        }

        private static void QueryFailure()
        {
            var memory = new FakeMemory { RegionBytes = 4, FailQuery = 2 };
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                Throws<MemoryObservationException>(() => session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule));
                Check(memory.Reads == 1, "The test must fail after one successful region read.");
                AssertStopped(session, memory, 5);
                JObject report = JObject.Parse(session.ExportReport());
                Check((bool)report["IsStopped"] && (int)report["NativeErrorCode"] == 5 && (int)report["CandidateCount"] == 0,
                    "Failure report must preserve native error and expose no partial candidates.");
                Check(report["LastSampleAtUtc"].Type == JTokenType.Null && report["ExportedAtUtc"] != null,
                    "A failed capture must not export a completed-sample timestamp.");
            }
        }

        private static void RecaptureFailure()
        {
            var memory = new FakeMemory { RegionBytes = 4 };
            memory.Set(0, 10); memory.Set(4, 10);
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.StartExact(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, 10);
                Check(session.Candidates.Count == 2 && session.HasBaseline && session.LastSampleAtUtc.HasValue, "Initial capture failed.");
                memory.FailRead = memory.Reads + 2;
                Throws<MemoryObservationException>(() => session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule));
                AssertStopped(session, memory, 5);
            }
        }

        private static void PartialRead()
        {
            var memory = new FakeMemory { ReturnPartial = true };
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                Throws<MemoryObservationException>(() => session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule));
                AssertStopped(session, memory, 299);
            }
        }

        private static void AtomicRefine()
        {
            var memory = new FakeMemory { RegionBytes = 4 };
            memory.Set(0, 10); memory.Set(4, 10);
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.StartExact(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, 10);
                VanillaMemoryCandidate[] previous = session.Candidates.ToArray();
                memory.Set(0, 20); memory.Set(4, 20); memory.FailRead = memory.Reads + 2;
                Throws<MemoryObservationException>(() => session.Refine(VanillaMemoryScanComparison.Increased, null));
                Check(previous.All(item => item.PreviousValue == 10 && item.CurrentValue == 10),
                    "An incomplete refinement changed a previously completed candidate object.");
                AssertStopped(session, memory, 5);
            }
        }

        private static void AtomicFirstCompare()
        {
            var memory = new FakeMemory(250001 * 4);
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule);
                for (int offset = 0; offset < memory.Bytes.Length; offset += 4) memory.Set(offset, 1);
                Throws<InvalidOperationException>(() => session.FirstCompare(VanillaMemoryScanComparison.Changed, null));
                Check(!session.IsStopped && session.HasBaseline && session.Candidates.Count == 0,
                    "A candidate-count limit should retain only the earlier complete baseline.");
                Array.Clear(memory.Bytes, 0, memory.Bytes.Length); memory.Set(0, 1);
                session.FirstCompare(VanillaMemoryScanComparison.Changed, null);
                Check(session.Candidates.Count == 1 && session.Candidates[0].MainModuleOffset == 0
                    && session.Candidates[0].PreviousValue == 0 && session.Candidates[0].CurrentValue == 1,
                    "A failed comparison partially advanced its baseline.");
            }
        }

        private static void InvalidRanges()
        {
            foreach (int invalid in new[] { 1, 2, 3 })
            {
                var memory = new FakeMemory { InvalidRange = invalid };
                using (var session = new VanillaMemoryDiscoverySession(memory))
                {
                    Throws<MemoryObservationException>(() => session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule));
                    Check(session.IsStopped && memory.Disposed && memory.Queries == 1 && memory.Reads == 0,
                        "Invalid query metadata must close observation before any page skipping or read.");
                }
            }
        }

        private static void MappingTypes()
        {
            foreach (VanillaMemoryScanValueType type in Enum.GetValues(typeof(VanillaMemoryScanValueType)))
            {
                var memory = new FakeMemory(); memory.Set(0, 50);
                using (var session = new VanillaMemoryDiscoverySession(memory))
                {
                    session.StartExact(type, VanillaMemoryScanScope.MainModule, 50);
                    VanillaMemoryCandidate candidate = session.Candidates.Single();
                    if (type == VanillaMemoryScanValueType.UInt32 || type == VanillaMemoryScanValueType.Int32)
                        Check((string)JObject.Parse(session.MappingSnippet(candidate))["Encoding"] == type.ToString(), "Supported mapping changed datatype.");
                    else
                        Throws<InvalidOperationException>(() => session.MappingSnippet(candidate));
                    Check((string)JObject.Parse(session.ExportReport())["ValueType"] == type.ToString(), "Candidate report lost its original datatype.");
                }
            }
        }

        private static void StaleCandidate()
        {
            var memory = new FakeMemory(); memory.Set(0, 10);
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.StartExact(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, 10);
                VanillaMemoryCandidate old = session.Candidates.Single();
                memory.Set(0, 20);
                session.Refine(VanillaMemoryScanComparison.Increased, null);
                Check(old.CurrentValue == 10 && session.Candidates.Single().CurrentValue == 20, "Completed scan values must be immutable across refinements.");
                Throws<ArgumentException>(() => session.MappingSnippet(old));
                session.MappingSnippet(session.Candidates.Single());
            }
        }

        private static void SignedHexWidth()
        {
            Check(VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Int16, "0xFFFF") == -1, "Signed Int16 hexadecimal interpretation changed.");
            Throws<ArgumentException>(() => VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Int16, "0x10000"));
            Throws<ArgumentException>(() => VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Int16, "0xFFFFFFFF"));
        }

        private static void EmptyComparison()
        {
            var memory = new FakeMemory();
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.StartExact(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, 50);
                Check(session.HasComparison && session.Candidates.Count == 0, "An empty exact scan is still a completed comparison.");
                int reads = memory.Reads;
                Throws<InvalidOperationException>(() => session.FirstCompare(VanillaMemoryScanComparison.Changed, null));
                Throws<InvalidOperationException>(() => session.Refine(VanillaMemoryScanComparison.Changed, null));
                Check(memory.Reads == reads, "An empty comparison unexpectedly restarted observation.");
                session.Reset();
                session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule);
                session.FirstCompare(VanillaMemoryScanComparison.Changed, null);
                Check(session.HasComparison && session.Candidates.Count == 0, "An empty changed scan is still a completed comparison.");
            }
        }

        private static void WritableBoundary()
        {
            Check(VanillaMemoryDiscoverySession.BoundedWritableScanEnd(0x7FFFFFFEFFFFUL, 65536) == 0x7FFF0000UL, "64-bit system boundary did not retain the conservative target limit.");
            Check(VanillaMemoryDiscoverySession.BoundedWritableScanEnd(0x6FFFFFFFUL, 65536) == 0x70000000UL, "A lower system maximum was ignored.");
            var memory = new FakeMemory { RegionBytes = 4 };
            using (var session = new VanillaMemoryDiscoverySession(memory))
            {
                session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.AllWritableMemory);
                Check(memory.Queries == 2 && session.BaselineBytes == 8 && session.ScanEnd == memory.WritableScanEnd,
                    "Writable scan crossed its explicit end instead of finishing at its last region.");
            }
        }

        private static void AssertStopped(VanillaMemoryDiscoverySession session, FakeMemory memory, int error)
        {
            Check(session.IsStopped && memory.Disposed && session.NativeErrorCode == error, "Observation failure was not latched with its native error.");
            Check(!session.HasBaseline && session.BaselineBytes == 0 && session.Candidates.Count == 0 && !session.LastSampleAtUtc.HasValue,
                "Stopped observation retained stale or partially completed data.");
            int calls = memory.Queries + memory.Reads;
            string firstError = session.LastError;
            Throws<MemoryObservationException>(() => session.CaptureBaseline(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule));
            Throws<MemoryObservationException>(() => session.StartExact(VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, 0));
            Throws<MemoryObservationException>(() => session.FirstCompare(VanillaMemoryScanComparison.Changed, null));
            Throws<MemoryObservationException>(() => session.Refine(VanillaMemoryScanComparison.Changed, null));
            Throws<MemoryObservationException>(() => session.Reset());
            Check(calls == memory.Queries + memory.Reads && session.LastError == firstError, "Stopped session retried memory access or lost the first error.");
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name + ".");
        }

        private sealed class FakeMemory : IVanillaMemoryScanSource
        {
            public int ProcessId => 123;
            public string ProcessName => "Vanilla MMO";
            public string ExecutablePath => "Vanilla MMO.exe";
            public string ExecutableSha256 => new string('a', 64);
            public ulong MainModuleBaseAddress => 0x400000;
            public uint MainModuleSize => (uint)Bytes.Length;
            public ulong WritableScanStart => MainModuleBaseAddress;
            public ulong WritableScanEnd => MainModuleBaseAddress + MainModuleSize;
            public readonly byte[] Bytes;
            public int RegionBytes, Queries, Reads, FailQuery, FailRead, InvalidRange;
            public bool Disposed, ReturnPartial;

            public FakeMemory(int size = 8) { Bytes = new byte[size]; }
            public void Set(int offset, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, Bytes, offset, 4); }
            public VanillaScanRegion Query(ulong address)
            {
                Check(!Disposed, "Source queried after disposal.");
                Check(address < WritableScanEnd, "Query exceeded the explicit scan boundary.");
                Queries++;
                if (Queries == FailQuery) throw new Win32Exception(5, "VirtualQueryEx: Win32 5 (Access is denied).");
                if (InvalidRange == 1) return new VanillaScanRegion { Address = address, Size = 0 };
                if (InvalidRange == 2) return new VanillaScanRegion { Address = address + 4, Size = 4 };
                if (InvalidRange == 3) return new VanillaScanRegion { Address = address - 4, Size = 4 };
                return new VanillaScanRegion { Address = address, Size = (ulong)(RegionBytes == 0 ? Bytes.Length : RegionBytes), State = 0x1000, Protection = 0x04 };
            }
            public byte[] Read(ulong address, int count)
            {
                Check(!Disposed, "Source read after disposal.");
                Reads++;
                if (Reads == FailRead) throw new Win32Exception(5, "ReadProcessMemory: Win32 5 (Access is denied).");
                byte[] result = new byte[ReturnPartial ? count - 1 : count];
                Buffer.BlockCopy(Bytes, (int)(address - MainModuleBaseAddress), result, 0, result.Length);
                return result;
            }
            public void Dispose() { Disposed = true; }
        }
    }
}
