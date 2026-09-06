using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using _4RTools.Model.Vanilla;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    public static class BuildProfileTests
    {
        public static int Run()
        {
            int failed = 0;
            var tests = new Dictionary<string, System.Action>
            {
                { "Build profiles match exact SHA, architecture and image size", Identity },
                { "Build profile parsing rejects malformed and ambiguous data", Parsing },
                { "Build profiles require portable roots and explicit semantic evidence", ProfileSafety },
                { "Unverified known builds cannot infer readiness or gameplay", Unverified },
                { "Verified fields become optional typed automation signals", Verified },
                { "Unknown action codes remain unavailable", UnknownAction },
                { "Resource and coordinate sanity failures invalidate fields", Sanity },
                { "Readiness never follows from valid HP alone", Readiness },
                { "Build adapters reject arbitrary diagnostics maps", MapProvenance },
                { "Demo and mismatched builds cannot enable automation", Untrusted },
                { "Portable mappings follow a relocated module", Relocation },
                { "Profile adapters freeze definitions for their session", Frozen },
                { "Verified activity timestamps track movement, target and combat transitions", Activity },
                { "Activity continuity resets on map/loading/readiness changes", ActivityReset },
                { "Unknown combat observations do not fabricate idle transitions", UnknownActivity },
                { "Profile catalog rejects duplicate matches and malformed files", Catalog }
            };
            foreach (var test in tests)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + ex); }
            }
            Console.WriteLine("Build profiles: {0} passed; {1} failed. All state was simulated.", tests.Count - failed, failed);
            return failed;
        }

        private static void Identity()
        {
            var profile = Profile(); var identity = IdentityValue();
            Assert(profile.Matches(identity), "Exact identity matches.");
            identity.ImageSize++; Assert(!profile.Matches(identity), "Image size is part of identity.");
            identity = IdentityValue(); identity.Machine = 0x8664; Assert(!profile.Matches(identity), "Architecture is part of identity.");
            identity = IdentityValue(); identity.Sha256 = new string('b', 64); Assert(!profile.Matches(identity), "SHA is part of identity.");
        }
        private static void Parsing()
        {
            var profile = Profile();
            Assert(VanillaBuildProfile.Parse(profile.ToJson()).VerifiedFields.Count == profile.VerifiedFields.Count, "Round trip verified fields.");
            foreach (string value in new[] { "null", "{}", "{\"SchemaVersion\":1,\"SchemaVersion\":1}", profile.ToJson() + " {}",
                profile.ToJson().Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 2"),
                profile.ToJson().Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 1, \"schemaversion\": 1"),
                profile.ToJson().Replace("\"InCombat\": false,", "") }) Throws(() => VanillaBuildProfile.Parse(value));
        }
        private static void ProfileSafety()
        {
            var profile = Profile(); profile.MemoryMap.Fields[VanillaField.CurrentHP].Module = null; Throws(profile.Validate);
            profile = Profile(); profile.MemoryMap.Fields[VanillaField.CurrentHP].Module = "Other.exe"; Throws(profile.Validate);
            profile = Profile(); profile.MemoryMap.Fields[VanillaField.CurrentHP].Address = "0x1000000"; Throws(profile.Validate);
            profile = Profile(); profile.MemoryMap.Fields[VanillaField.CurrentHP].Evidence = ""; Throws(profile.Validate);
            profile = Profile(); profile.VerifiedFields.Add(VanillaField.CurrentHP); Throws(profile.Validate);
            profile = Profile(); profile.ActionStates.Clear(); Throws(profile.Validate);
            profile = Profile(); profile.NoTargetValues.Clear(); Throws(profile.Validate);
        }
        private static void Unverified()
        {
            var profile = Profile(); profile.VerifiedFields.Clear();
            using (var fixture = new Fixture(profile))
            {
                var value = fixture.Observe();
                Assert(value.TrustedBuild && !value.Ready && !value.HealthValidated && !value.TargetValidated, "Fingerprint alone proves no gameplay.");
                Assert(!value.CurrentHP.HasValue && !value.HasTarget.HasValue, "Unknown is nullable, not zero/idle.");
                Assert(fixture.Snapshot.CurrentHP.Validation == StateValidation.Unverified, "Accessible is not validated.");
            }
        }
        private static void Verified()
        {
            using (var fixture = new Fixture(Profile()))
            {
                var value = fixture.Observe();
                Assert(value.Ready && value.HealthValidated && value.CurrentHP == 90 && value.MaxHP == 100, "HP ready observation.");
                Assert(value.SpValidated && value.CurrentSP == 20 && value.MaxSP == 100, "SP observation.");
                Assert(value.TargetValidated && value.HasTarget == false, "Explicit no-target meaning.");
                Assert(value.CombatValidated && value.InCombat == false && value.CastingValidated && value.IsCasting == false, "Explicit idle meaning.");
                Assert(value.PositionValidated && value.X == 10 && value.Y == 20 && value.Map == "test_map", "Position and map.");
                Assert(value.StatusValidated && value.StatusEffects.SequenceEqual(new uint[] { 10, 20 }), "Status values.");
                Assert(fixture.Snapshot.CurrentHP.Validation == StateValidation.Valid, "Diagnostics reflects validation.");
            }
        }
        private static void UnknownAction()
        {
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Put(VanillaField.ActionState, 999u); var value = fixture.Observe();
                Assert(!value.CombatValidated && !value.InCombat.HasValue && !value.IsCasting.HasValue, "No default idle action.");
                Assert(fixture.Snapshot.ActionState.Validation == StateValidation.Invalid, "Unknown action code retained as invalid.");
            }
        }
        private static void Sanity()
        {
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Put(VanillaField.CurrentHP, 101u); fixture.Put(VanillaField.MaxSP, 0u); fixture.Put(VanillaField.X, uint.MaxValue);
                var value = fixture.Observe();
                Assert(!value.HealthValidated && !value.SpValidated && !value.PositionValidated, "Sanity errors clear capabilities.");
                Assert(fixture.Snapshot.CurrentHP.Validation == StateValidation.Invalid && fixture.Snapshot.CurrentHP.Error != null, "Sanity failure explains invalid HP.");
            }
        }
        private static void Readiness()
        {
            var profile = Profile(); profile.VerifiedFields.Remove(VanillaField.ClientReady);
            using (var fixture = new Fixture(profile))
            {
                var value = fixture.Observe(); Assert(value.HealthValidated && !value.Ready, "HP cannot imply client ready.");
            }
        }
        private static void MapProvenance()
        {
            var profile = Profile();
            var adapter = new VanillaStateAdapter(profile, IdentityValue());
            profile.MemoryMap.Fields[VanillaField.CurrentHP].Address = "0x900";
            using (var fixture = new Fixture(profile))
            {
                fixture.Observe(); var value = adapter.Observe(fixture.Snapshot, TimeSpan.Zero);
                Assert(!value.TrustedBuild && !value.Ready && !value.HealthValidated, "A different source map cannot inherit trust.");
            }
        }
        private static void Untrusted()
        {
            var identity = IdentityValue(); identity.Sha256 = new string('b', 64);
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Observe();
                var value = new VanillaStateAdapter(Profile(), identity).Observe(fixture.Snapshot, TimeSpan.Zero);
                Assert(!value.TrustedBuild && !value.HealthValidated, "Wrong executable loses all trust.");
                var unknown = new VanillaStateAdapter(null, IdentityValue()).Observe(fixture.Snapshot, TimeSpan.Zero);
                Assert(!unknown.TrustedBuild && !unknown.Ready, "Missing profile is safe.");
            }
            using (var source = new DemoStateSource())
            {
                var value = new VanillaStateAdapter(Profile(), IdentityValue()).Observe(source.Poll(DateTimeOffset.UtcNow), TimeSpan.Zero);
                Assert(!value.TrustedBuild && !value.Ready, "Demo remains synthetic.");
            }
        }
        private static void Relocation()
        {
            using (var fixture = new Fixture(Profile(), 0x600000))
            {
                var value = fixture.Observe(); Assert(value.HealthValidated && value.CurrentHP == 90, "Relocated module preserves portable definition.");
                Assert(fixture.Snapshot.CurrentHP.Address == 0x600100, "Runtime address follows actual base.");
            }
        }
        private static void Frozen()
        {
            var profile = Profile();
            using (var fixture = new Fixture(profile))
            {
                profile.VerifiedFields.Clear(); profile.Sha256 = new string('b', 64);
                var value = fixture.Observe(); Assert(value.HealthValidated, "Changes to caller profile do not affect active adapter.");
            }
        }
        private static void Catalog()
        {
            string directory = Path.Combine(Path.GetTempPath(), "4RTools-profile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var logs = new List<string>();
            try
            {
                File.WriteAllText(Path.Combine(directory, "invalid.json"), "not json");
                File.WriteAllText(Path.Combine(directory, "known.json"), Profile().ToJson());
                Assert(VanillaBuildProfile.Find(directory, IdentityValue(), logs.Add) != null, "One exact profile resolves despite rejected malformed file.");
                Assert(logs.Any(value => value.StartsWith("Rejected build profile")), "Malformed profile failure is logged.");
                File.WriteAllText(Path.Combine(directory, "duplicate.json"), Profile().ToJson());
                Assert(VanillaBuildProfile.Find(directory, IdentityValue(), logs.Add) == null, "Ambiguous exact profiles fail closed.");
            }
            finally
            {
                // This test owns the newly-created unique directory and these exact files only.
                foreach (string name in new[] { "invalid.json", "known.json", "duplicate.json" }) File.Delete(Path.Combine(directory, name));
                Directory.Delete(directory);
            }
        }
        private static void Activity()
        {
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == null && fixture.Snapshot.LastTargetActivityAtUtc == null
                    && fixture.Snapshot.LastCombatActivityAtUtc == null && fixture.Snapshot.LastStateChangeAtUtc == null, "First idle sample is not a transition.");
                fixture.Put(VanillaField.X, 11); fixture.Put(VanillaField.CurrentTargetId, 100); fixture.Put(VanillaField.ActionState, 8);
                fixture.Observe(); var activeAt = fixture.Snapshot.SampledAtUtc;
                Assert(fixture.Snapshot.LastMovementAtUtc == activeAt && fixture.Snapshot.LastTargetActivityAtUtc == activeAt
                    && fixture.Snapshot.LastCombatActivityAtUtc == activeAt && fixture.Snapshot.LastStateChangeAtUtc == activeAt, "Verified changes update activity timestamps.");
                fixture.Put(VanillaField.CurrentTargetId, 0); fixture.Put(VanillaField.ActionState, 7); fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == activeAt && fixture.Snapshot.LastTargetActivityAtUtc == fixture.Snapshot.SampledAtUtc
                    && fixture.Snapshot.LastCombatActivityAtUtc == fixture.Snapshot.SampledAtUtc, "Loss of activity is a verified transition.");
            }
        }
        private static void ActivityReset()
        {
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Observe(); fixture.Put(VanillaField.X, 11); fixture.Observe(); Assert(fixture.Snapshot.LastMovementAtUtc.HasValue, "Movement exists before reset.");
                fixture.PutText(VanillaField.Map, "second_map"); fixture.Put(VanillaField.X, 12); fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == null, "Map transition resets movement continuity.");
                fixture.Put(VanillaField.X, 13); fixture.Observe(); fixture.Put(VanillaField.ClientReady, 0); fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == null, "Readiness loss resets activity.");
                fixture.Put(VanillaField.ClientReady, 1); fixture.Put(VanillaField.X, 14); fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == null, "Readiness recovery starts a new baseline.");
                fixture.Put(VanillaField.X, 15); fixture.Observe(); fixture.Put(VanillaField.Loading, 1); fixture.Observe();
                Assert(fixture.Snapshot.LastMovementAtUtc == null, "Loading resets activity.");
            }
            var adapter = new VanillaStateAdapter(Profile(), IdentityValue());
            using (var first = new Fixture(Profile()))
            using (var second = new Fixture(Profile()))
            {
                first.Observe(); adapter.Observe(first.Snapshot, TimeSpan.Zero);
                first.Put(VanillaField.X, 11); first.Observe(); adapter.Observe(first.Snapshot, TimeSpan.FromSeconds(1));
                Assert(first.Snapshot.LastMovementAtUtc.HasValue, "Session has movement history.");
                second.Put(VanillaField.X, 30); second.Observe(); adapter.Observe(second.Snapshot, TimeSpan.FromSeconds(2));
                Assert(second.Snapshot.LastMovementAtUtc == null, "New session cannot inherit earlier movement history.");
            }
        }
        private static void UnknownActivity()
        {
            using (var fixture = new Fixture(Profile()))
            {
                fixture.Put(VanillaField.ActionState, 999); fixture.Observe();
                fixture.Put(VanillaField.ActionState, 7); fixture.Observe();
                Assert(fixture.Snapshot.LastCombatActivityAtUtc == null, "Unknown followed by idle is not a combat transition.");
            }
        }

        private static VanillaBuildProfile Profile()
        {
            var profile = new VanillaBuildProfile { Label = "Offline fixture", Sha256 = new string('a', 64), Machine = 0x14c, ImageSize = 0x10000,
                Evidence = "Synthetic offline test data only.", MemoryMap = new VanillaMemoryMap { ProcessName = "VanillaTestClient.exe" },
                NoTargetValues = new List<ulong> { 0 }, ActionStates = new List<VanillaActionMeaning> { new VanillaActionMeaning { Value = 7 },
                    new VanillaActionMeaning { Value = 8, InCombat = true }, new VanillaActionMeaning { Value = 9, IsCasting = true } } };
            foreach (VanillaField field in Enum.GetValues(typeof(VanillaField)))
            {
                var mapping = new VanillaFieldMapping { Module = "VanillaTestClient.exe", Address = (0x100 + (int)field * 0x100).ToString(), Evidence = "Simulated verified field for offline tests." };
                if (field == VanillaField.X || field == VanillaField.Y) mapping.Encoding = VanillaValueEncoding.Int32;
                else if (field == VanillaField.CharacterName || field == VanillaField.Map) { mapping.Encoding = VanillaValueEncoding.Utf8; mapping.ByteCount = 32; }
                else if (field == VanillaField.AutobattleEnabled || field == VanillaField.ClientReady || field == VanillaField.Loading) mapping.Encoding = VanillaValueEncoding.Boolean8;
                else if (field == VanillaField.StatusEffects) { mapping.Encoding = VanillaValueEncoding.UInt32Array; mapping.ByteCount = 8; }
                profile.MemoryMap.Fields.Add(field, mapping); profile.VerifiedFields.Add(field);
            }
            profile.Validate(); return profile;
        }
        private static VanillaExecutableIdentity IdentityValue() { return new VanillaExecutableIdentity { Sha256 = new string('a', 64), Machine = 0x14c, ImageSize = 0x10000 }; }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws(System.Action action) { try { action(); } catch { return; } throw new Exception("Expected invalid profile to be rejected."); }

        private sealed class Fixture : IDisposable
        {
            private readonly VanillaBuildProfile profile;
            private readonly FakeMemory memory;
            private readonly MemoryStateSource source;
            private readonly VanillaStateAdapter adapter;
            public VanillaClientState Snapshot;
            public Fixture(VanillaBuildProfile profile, ulong moduleBase = 0x400000)
            {
                this.profile = VanillaBuildProfile.Parse(profile.ToJson());
                memory = new FakeMemory { MainModuleBaseAddress = moduleBase };
                foreach (var pair in this.profile.MemoryMap.Fields)
                {
                    var bytes = new byte[pair.Value.ReadSize];
                    if (pair.Key == VanillaField.CharacterName || pair.Key == VanillaField.Map)
                        Array.Copy(Encoding.UTF8.GetBytes(pair.Key == VanillaField.Map ? "test_map" : "Offline character"), bytes, pair.Key == VanillaField.Map ? 8 : 17);
                    memory.Put(moduleBase + VanillaMemoryMap.ParseAddress(pair.Value.Address), bytes);
                }
                Put(VanillaField.CurrentHP, 90); Put(VanillaField.MaxHP, 100); Put(VanillaField.CurrentSP, 20); Put(VanillaField.MaxSP, 100);
                Put(VanillaField.X, 10); Put(VanillaField.Y, 20); Put(VanillaField.ActionState, 7); Put(VanillaField.ClientReady, 1);
                memory.Put(moduleBase + VanillaMemoryMap.ParseAddress(this.profile.MemoryMap.Fields[VanillaField.StatusEffects].Address), BitConverter.GetBytes(10u).Concat(BitConverter.GetBytes(20u)).ToArray());
                source = new MemoryStateSource(memory, this.profile.MemoryMap); adapter = new VanillaStateAdapter(profile, IdentityValue());
            }
            public void Put(VanillaField field, uint value)
            {
                var mapping = profile.MemoryMap.Fields[field];
                memory.Put(memory.MainModuleBaseAddress + VanillaMemoryMap.ParseAddress(mapping.Address), BitConverter.GetBytes(value).Take(mapping.ReadSize).ToArray());
            }
            public void PutText(VanillaField field, string value)
            {
                var mapping = profile.MemoryMap.Fields[field];
                var bytes = new byte[mapping.ReadSize];
                var text = Encoding.UTF8.GetBytes(value); Array.Copy(text, bytes, text.Length);
                memory.Put(memory.MainModuleBaseAddress + VanillaMemoryMap.ParseAddress(mapping.Address), bytes);
            }
            private int sample;
            public RuleObservation Observe()
            {
                var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(sample++);
                Snapshot = source.Poll(timestamp); return adapter.Observe(Snapshot, TimeSpan.FromSeconds(sample));
            }
            public void Dispose() { source.Dispose(); }
        }
        private sealed class FakeMemory : IReadOnlyProcessMemory
        {
            private readonly Dictionary<ulong, byte> bytes = new Dictionary<ulong, byte>();
            public int ProcessId { get { return 42; } }
            public string ProcessName { get { return "VanillaTestClient"; } }
            public int PointerSize { get { return 4; } }
            public ulong MainModuleBaseAddress { get; set; }
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
