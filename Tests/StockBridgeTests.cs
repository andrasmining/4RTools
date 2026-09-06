using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Model.Vanilla;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    public static class StockBridgeTests
    {
        public static int Run()
        {
            int failed = 0;
            var tests = new Dictionary<string, System.Action>
            {
                { "Stock bridge connects with unverified display data without enabling input", UnverifiedDisplay },
                { "Stock thresholds use coherent validated HP/SP and preserve zero SP", Thresholds },
                { "Stock automatic features require their own verified capabilities", Capabilities },
                { "Stock input failure latches OFF without another send", InputFailure },
                { "Stale stock observations cannot resume input automatically", Stale },
                { "Changing Vanilla session invalidates the captured stock backend", SessionChange },
                { "Vanilla legacy memory writes are rejected before accessing a process", WriteGuard },
                { "Vanilla AHK rejects options that send global input", GlobalOptions },
                { "Stock status actions read one validated array without zero padding", StatusSnapshot },
                { "Worker cancellation interrupts waits and runs cleanup", WorkerCancellation },
                { "Normal stock OFF cancels sends without latching a failure", NormalOff },
                { "Stopped callbacks cannot send into a later ON interval", RestartCancellation },
                { "Stopping at the input boundary cancels without a native retry", DispatchCancellation }
            };
            foreach (var test in tests)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + ex); }
            }
            Console.WriteLine("Stock bridge: {0} passed; {1} failed. All client memory and input were simulated.", tests.Count - failed, failed);
            return failed;
        }

        private static void UnverifiedDisplay()
        {
            using (var fixture = new Fixture(false, true, false))
            {
                string name;
                Assert(fixture.Backend.TryReadCharacterName(out name) && name == "Offline character", "An observed name can be displayed without granting trust.");
                Assert(fixture.Backend.GetEnableError(new Profile("Offline")) != null, "Input remains unavailable.");
                Assert(fixture.Backend.Capabilities.Contains("HP not verified") && fixture.Backend.Capabilities.Contains("name not verified")
                    && fixture.Backend.Capabilities.Contains("statuses unavailable"),
                    "Readable candidate values are distinguished from unavailable fields.");
                Throws(() => fixture.Backend.ReadVital(VanillaField.CurrentHP));
                Throws(() => fixture.Backend.SetEnabled(true));
                Assert(fixture.InputFactories == 0 && fixture.Input.Sends == 0, "No native-input adapter is created for unverified state.");
            }
        }
        private static void Thresholds()
        {
            using (var fixture = new Fixture())
            {
                Assert(fixture.Backend.ReadVital(VanillaField.CurrentHP) == 90, "HP is read from the validated snapshot.");
                Assert(!fixture.Backend.IsBelow(true, 90) && fixture.Backend.IsBelow(true, 91), "HP threshold is strict.");
                Assert(fixture.Backend.IsBelow(false, 30), "Zero current SP is a valid recovery trigger.");
                Assert(fixture.InputFactories == 0, "Reading values never creates an input adapter.");
            }
        }
        private static void Capabilities()
        {
            using (var fixture = new Fixture(true, false))
            {
                var profile = new Profile("Offline");
                Assert(fixture.Backend.GetEnableError(profile) == null, "Manual stock features need no invented ready signal.");
                fixture.Backend.SetEnabled(true); fixture.Backend.Send(new SequenceStep { Key = 118 });
                Assert(fixture.Input.Sends == 1, "Manual input uses its own capability.");
                profile.Autopot.hpKey = Key.F1; profile.Autopot.hpPercent = 30;
                Assert(fixture.Backend.GetEnableError(profile).Contains("readiness"), "Configured Autopot requires readiness.");
                Throws(() => fixture.Backend.IsBelow(true, 30));
                profile.Autopot.hpKey = Key.None;
                profile.AutoRefreshSpammer1.RefreshKey = Key.F2;
                Assert(fixture.Backend.GetEnableError(profile).Contains("readiness"), "Timers require readiness.");
            }
        }
        private static void InputFailure()
        {
            using (var fixture = new Fixture())
            {
                fixture.Input.Fail = true; fixture.Backend.SetEnabled(true);
                Throws(() => fixture.Backend.Send(new SequenceStep { Key = 118 }));
                Throws(() => fixture.Backend.SetEnabled(true));
                Throws(() => fixture.Backend.Send(new SequenceStep { Key = 118 }));
                Assert(fixture.Input.Sends == 1 && fixture.InputFactories == 1 && !fixture.Backend.Enabled && fixture.Backend.LastFailure != null,
                    "A failed input path is latched and is not retried.");
            }
        }
        private static void Stale()
        {
            using (var fixture = new Fixture())
            {
                fixture.Backend.SetEnabled(true); fixture.Fresh = false;
                Throws(() => fixture.Backend.Send(new SequenceStep { Key = 118 }));
                fixture.Fresh = true; Throws(() => fixture.Backend.SetEnabled(true));
                Assert(fixture.InputFactories == 0, "Stale state cannot start native input.");
            }
        }
        private static void SessionChange()
        {
            using (var first = new Fixture())
            using (var second = new Fixture())
            {
                first.Backend.SetEnabled(true); first.Snapshot = second.Snapshot;
                Throws(() => first.Backend.Send(new SequenceStep { Key = 118 }));
                Assert(first.InputFactories == 0, "Another session cannot receive the old backend's input.");
            }
        }
        private static void WriteGuard()
        {
            using (var client = new Client("Vanilla MMO", 0, 0))
            {
                Assert(client.IsVanilla, "Vanilla is recognized without fake stock offsets.");
                Throws(() => client.WriteMemory(123, 456u));
                Throws(() => client.WriteMemory(123, new byte[] { 1 }));
                Throws(() => client.ReadCurrentHp());
            }
        }
        private static void GlobalOptions()
        {
            using (var fixture = new Fixture())
            {
                foreach (int option in new[] { 0, 1, 2 })
                {
                    var profile = new Profile("Offline");
                    if (option == 0) profile.AHK.ahkMode = AHK.SPEED_BOOST;
                    if (option == 1) profile.AHK.noShift = true;
                    if (option == 2) profile.AHK.mouseFlick = true;
                    Assert(fixture.Backend.GetEnableError(profile).Contains("global input"), "Global input options must be rejected.");
                }
            }
        }
        private static void StatusSnapshot()
        {
            using (var fixture = new Fixture())
            {
                var statuses = fixture.Backend.ReadStatuses();
                Assert(statuses.SequenceEqual(new uint[] { 10, 20 }), "No fabricated zero-filled status slots.");
                statuses[0] = 999;
                Assert(fixture.Backend.ReadStatuses()[0] == 10, "A caller cannot mutate a cached status snapshot.");
            }
        }
        private static void WorkerCancellation()
        {
            using (var entered = new ManualResetEventSlim())
            using (var cleaned = new ManualResetEventSlim())
            {
                var worker = new _4RThread(_ =>
                {
                    try { entered.Set(); Thread.Sleep(Timeout.Infinite); }
                    finally { cleaned.Set(); }
                    return 0;
                });
                _4RThread.Start(worker);
                try
                {
                    Assert(entered.Wait(2000), "Worker started.");
                    _4RThread.Stop(worker);
                    Assert(cleaned.Wait(2000), "Cancellation interrupts waiting and runs cleanup instead of suspending the worker.");
                }
                finally { _4RThread.Stop(worker); }
            }
        }

        private static void NormalOff()
        {
            using (var fixture = new Fixture())
            {
                fixture.Backend.SetEnabled(true);
                fixture.Backend.Send(new SequenceStep { Key = 118 });
                fixture.Backend.SetEnabled(false);
                Cancels(() => fixture.Backend.Send(new SequenceStep { Key = 118 }));
                // Cancellation must happen before even inspecting a native window.
                Cancels(() => fixture.Backend.SendWindowMessage(IntPtr.Zero, 0x0100, System.Windows.Forms.Keys.F7, 0));
                Assert(fixture.Backend.LastFailure == null && fixture.Input.Sends == 1, "OFF is expected cancellation, not a failed native operation.");
                fixture.Backend.SetEnabled(true);
                fixture.Backend.Send(new SequenceStep { Key = 118 });
                Assert(fixture.Input.Sends == 2 && fixture.Backend.Enabled, "An explicit new ON interval can use the same healthy connection.");
            }
        }

        private static void RestartCancellation()
        {
            using (var fixture = new Fixture())
            using (var entered = new ManualResetEventSlim())
            using (var resume = new ManualResetEventSlim())
            using (var finished = new ManualResetEventSlim())
            {
                int failures = 0;
                var worker = new _4RThread(_ =>
                {
                    try
                    {
                        entered.Set();
                        WaitThroughInterrupt(resume);
                        fixture.Backend.Send(new SequenceStep { Key = 118 });
                        return 0;
                    }
                    finally { finished.Set(); }
                }, error => { Interlocked.Increment(ref failures); fixture.Backend.Fail(error.Message); return true; });
                fixture.Backend.SetEnabled(true);
                _4RThread.Start(worker);
                try
                {
                    Assert(entered.Wait(2000), "The old callback is in progress.");
                    fixture.Backend.SetEnabled(false);
                    _4RThread.Stop(worker);
                    fixture.Backend.SetEnabled(true);
                    resume.Set();
                    Assert(finished.Wait(2000), "The stopped callback reaches its pending input boundary.");
                    Assert(fixture.Input.Sends == 0 && fixture.Backend.LastFailure == null && fixture.Backend.Enabled && failures == 0,
                        "A delayed old callback is cancelled without sending into or disabling the new ON interval.");
                    fixture.Backend.Send(new SequenceStep { Key = 118 });
                    Assert(fixture.Input.Sends == 1, "Fresh work in the new interval remains usable.");
                }
                finally { _4RThread.Stop(worker); resume.Set(); finished.Wait(2000); }
            }
        }

        private static void DispatchCancellation()
        {
            using (var fixture = new Fixture())
            using (var entered = new ManualResetEventSlim())
            using (var resume = new ManualResetEventSlim())
            using (var finished = new ManualResetEventSlim())
            {
                fixture.Input.BeforePermission = () => { entered.Set(); WaitThroughInterrupt(resume); };
                var worker = new _4RThread(_ =>
                {
                    try { fixture.Backend.Send(new SequenceStep { Key = 118 }); return 0; }
                    finally { finished.Set(); }
                }, error => { fixture.Backend.Fail(error.Message); return true; });
                fixture.Backend.SetEnabled(true);
                _4RThread.Start(worker);
                try
                {
                    Assert(entered.Wait(2000), "The input adapter has been entered before its final permission check.");
                    _4RThread.Stop(worker);
                    resume.Set();
                    Assert(finished.Wait(2000), "Cancellation exits dispatch.");
                    Assert(fixture.Input.Sends == 0 && fixture.Backend.LastFailure == null && fixture.Backend.Enabled,
                        "Stopping after the initial safety check is still expected cancellation before any native send.");
                }
                finally { _4RThread.Stop(worker); resume.Set(); finished.Wait(2000); }
            }
        }

        private static void WaitThroughInterrupt(ManualResetEventSlim gate)
        {
            // Model a callback finishing non-interruptible work before its next input boundary.
            while (!gate.IsSet)
            {
                try { if (!gate.Wait(2000)) throw new TimeoutException("The offline test did not release its callback."); }
                catch (ThreadInterruptedException) { }
            }
        }

        private static void Cancels(System.Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Expected normal cancellation.");
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws(System.Action action) { try { action(); } catch { return; } throw new Exception("Expected the unsafe or unavailable operation to be rejected."); }
        private sealed class Fixture : IDisposable
        {
            private readonly MemoryStateSource source;
            public VanillaClientState Snapshot;
            public bool Fresh = true;
            public int InputFactories;
            public readonly FakeInput Input = new FakeInput();
            public readonly VanillaStockBackend Backend;
            public Fixture(bool verified = true, bool readyVerified = true, bool includeStatuses = true)
            {
                var map = new VanillaMemoryMap { ProcessName = "VanillaTestClient.exe" };
                var memory = new FakeMemory();
                foreach (var field in new[] { VanillaField.CurrentHP, VanillaField.MaxHP, VanillaField.CurrentSP, VanillaField.MaxSP,
                    VanillaField.CharacterName, VanillaField.ClientReady, VanillaField.StatusEffects }
                    .Where(field => includeStatuses || field != VanillaField.StatusEffects))
                {
                    var mapping = new VanillaFieldMapping { Module = map.ProcessName, Address = ((int)field * 256 + 256).ToString(), Evidence = "Synthetic offline bridge test." };
                    byte[] bytes;
                    if (field == VanillaField.CharacterName)
                    {
                        mapping.Encoding = VanillaValueEncoding.Utf8; mapping.ByteCount = 32;
                        bytes = new byte[32]; Array.Copy(Encoding.UTF8.GetBytes("Offline character"), bytes, 17);
                    }
                    else if (field == VanillaField.ClientReady) { mapping.Encoding = VanillaValueEncoding.Boolean8; bytes = new byte[] { 1 }; }
                    else if (field == VanillaField.StatusEffects)
                    { mapping.Encoding = VanillaValueEncoding.UInt32Array; mapping.ByteCount = 8; bytes = BitConverter.GetBytes(10u).Concat(BitConverter.GetBytes(20u)).ToArray(); }
                    else bytes = BitConverter.GetBytes(field == VanillaField.CurrentHP ? 90u : field == VanillaField.CurrentSP ? 0u : 100u);
                    map.Fields[field] = mapping;
                    memory.Put(0x400000 + VanillaMemoryMap.ParseAddress(mapping.Address), bytes);
                }
                var profile = new VanillaBuildProfile { Label = "Offline fixture", Sha256 = new string('a', 64), Machine = 0x14c,
                    ImageSize = 0x10000, Evidence = "Offline only", MemoryMap = map,
                    VerifiedFields = verified ? map.Fields.Keys.Where(field => readyVerified || field != VanillaField.ClientReady).ToList() : new List<VanillaField>() };
                source = new MemoryStateSource(memory, map);
                Snapshot = source.Poll(DateTimeOffset.UtcNow);
                new VanillaStateAdapter(profile, new VanillaExecutableIdentity { Sha256 = profile.Sha256, Machine = profile.Machine, ImageSize = profile.ImageSize }).Observe(Snapshot, TimeSpan.Zero);
                Backend = new VanillaStockBackend(() => Snapshot, () => Fresh, permitted => { InputFactories++; Input.Permitted = permitted; return Input; });
            }
            public void Dispose() { Backend.Dispose(); source.Dispose(); }
        }
        private sealed class FakeInput : IInputSink
        {
            public int Sends;
            public bool Fail;
            public Func<bool> Permitted;
            public System.Action BeforePermission;
            public void Send(SequenceStep step)
            {
                BeforePermission?.Invoke();
                Assert(Permitted(), "The selected input capability must remain valid until dispatch.");
                Sends++; if (Fail) throw new InvalidOperationException("Simulated native rejection");
            }
            public void ReleaseAll() { }
        }
        private sealed class FakeMemory : IReadOnlyProcessMemory
        {
            private readonly Dictionary<ulong, byte> bytes = new Dictionary<ulong, byte>();
            public int ProcessId { get { return 42; } }
            public string ProcessName { get { return "VanillaTestClient"; } }
            public int PointerSize { get { return 4; } }
            public ulong MainModuleBaseAddress { get { return 0x400000; } }
            public bool IsStopped { get; private set; }
            public string LastError { get { return null; } }
            public void EnsureAlive() { if (IsStopped) throw new ObjectDisposedException("offline memory"); }
            public ulong GetModuleBase(string name) { return MainModuleBaseAddress; }
            public byte[] ReadBytes(ulong address, int count) { return Enumerable.Range(0, count).Select(index => bytes[address + (ulong)index]).ToArray(); }
            public void Put(ulong address, byte[] value) { for (int index = 0; index < value.Length; index++) bytes[address + (ulong)index] = value[index]; }
            public void Dispose() { IsStopped = true; }
        }
    }
}
