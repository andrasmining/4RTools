using System;
using System.Collections.Generic;
using System.Linq;
using _4RTools.Model.Vanilla.Automation;

namespace Vanilla.Diagnostics.Tests
{
    public static class AutomationTests
    {
        public static int Run()
        {
            int failed = 0;
            var tests = new Dictionary<string, System.Action>
            {
                { "Automation defaults are OFF and dry run", Defaults },
                { "Smart teleport tracks target and combat independently", IndependentTimers },
                { "Unknown target never means idle", UnknownTarget },
                { "Unknown casting blocks smart teleport", UnknownCasting },
                { "Teleport waits for exact cooldown and grace boundaries", CooldownAndGrace },
                { "Stuck detection resets movement and combat", Stuck },
                { "Stuck detection resets when a target is acquired", StuckTarget },
                { "Fixed interval is explicit and works without target fields", FixedInterval },
                { "Fixed interval respects known combat", FixedCombat },
                { "Dry run never calls the input sink", DryRun },
                { "SP threshold is strict and zero SP is valid", SpThreshold },
                { "Recovery waits until combat ends", RecoveryCombat },
                { "A sequence can finish its own casting action", SequenceCasting },
                { "Sequence scheduling does not replay a delayed backlog", SequenceScheduling },
                { "Sequences do not overlap and cooldown starts at completion", SequenceCooldown },
                { "Recovery and teleport share one scheduler", SharedScheduler },
                { "Disconnect cancels remaining steps and releases held inputs", Disconnect },
                { "Invalid required state switches automation OFF", InvalidState },
                { "Session and fingerprint changes stop automation", IdentityChange },
                { "Map changes cancel sequences and apply grace", MapChange },
                { "Loading pauses sequences and restarts timers", Loading },
                { "Global OFF cancels queued input", GlobalOff },
                { "Test Once uses global readiness gates and runs one sequence", TestOnce },
                { "Input failure stops the engine", InputFailure },
                { "Health, freshness and clock failures stop automation", BaseSafety },
                { "Profile round trip preserves sequence and isolates mutable settings", ProfileRoundTrip },
                { "Malformed profiles and unsafe sequences fail validation", MalformedProfiles },
                { "Timed, HP and status rules use the same scheduler", GenericRules },
                { "Unknown status does not become missing status", UnknownStatus },
                { "Farming must be explicitly enabled", Farming },
                { "Changing settings stops automation", ChangedSettings },
                { "Missing observations restart smart and stuck idle timers", ObservationGap },
                { "Lost observation continuity cancels queued held-key input", SequenceObservationGap },
                { "Observation continuity accepts the exact freshness boundary", GapBoundary }
            };
            foreach (var test in tests)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + ex); }
            }
            Console.WriteLine("Automation: {0} passed; {1} failed. All input was simulated.", tests.Count - failed, failed);
            return failed;
        }

        private static void Defaults()
        {
            var rig = new Rig(new VanillaAutomationSettings());
            rig.Engine.Tick(TimeSpan.Zero, rig.State);
            Assert(!rig.Engine.Enabled && !rig.Engine.FarmingEnabled && rig.Settings.DryRun, "Safe startup defaults.");
            Equal(0, rig.Sink.Sent.Count);
        }

        private static void IndependentTimers()
        {
            var rig = Smart();
            rig.At(0);
            rig.State.HasTarget = true; rig.At(500);
            rig.State.HasTarget = false; rig.At(1000);
            rig.State.InCombat = true; rig.At(1400);
            rig.State.InCombat = false; rig.At(1600);
            rig.At(2599); Equal(0, rig.Sink.Sent.Count);
            rig.At(2600); Equal(1, rig.Sink.Sent.Count);
        }

        private static void UnknownTarget()
        {
            var rig = Smart(); rig.At(0); rig.State.HasTarget = null; rig.At(1000);
            Assert(!rig.Engine.Enabled && !rig.Engine.NoTargetSince.HasValue, "Unknown target stops smart mode.");
            Equal(0, rig.Sink.Sent.Count);
        }
        private static void UnknownCasting()
        {
            var rig = Smart(); rig.State.CastingValidated = false; rig.At(0);
            Assert(!rig.Engine.Enabled, "Unverified casting stops smart mode.");
        }
        private static void CooldownAndGrace()
        {
            var rig = Smart(settings => { settings.Teleport.GraceMs = 3000; settings.Teleport.CooldownMs = 4000; });
            rig.At(0); rig.Through(2999); Equal(0, rig.Sink.Sent.Count);
            rig.At(3000); Equal(1, rig.Sink.Sent.Count);
            rig.At(3001); rig.Through(6999); Equal(1, rig.Sink.Sent.Count);
            rig.At(7000); Equal(2, rig.Sink.Sent.Count);
        }
        private static void Stuck()
        {
            var rig = Smart(settings => { settings.Teleport.NoTargetTimeoutMs = 20000; settings.Teleport.StuckEnabled = true; settings.Teleport.StuckTimeoutMs = 2000; });
            rig.At(0); rig.At(1000);
            rig.State.X++; rig.At(1900); rig.Through(3899); Equal(0, rig.Sink.Sent.Count);
            rig.State.InCombat = true; rig.At(3900);
            rig.State.InCombat = false; rig.At(4000); rig.Through(5999); Equal(0, rig.Sink.Sent.Count);
            rig.At(6000); Equal(1, rig.Sink.Sent.Count);
        }
        private static void FixedInterval()
        {
            var rig = Smart(settings => settings.Teleport.Mode = TeleportMode.FixedInterval);
            rig.State.HasTarget = rig.State.InCombat = rig.State.IsCasting = null;
            rig.State.TargetValidated = rig.State.CombatValidated = rig.State.CastingValidated = false;
            rig.At(0); rig.Through(2999); Equal(0, rig.Sink.Sent.Count);
            rig.At(3000); Equal(1, rig.Sink.Sent.Count);
            rig.At(100000); Equal(1, rig.Sink.Sent.Count); // A real polling gap first applies grace.
            rig.At(100100); Equal(2, rig.Sink.Sent.Count);
            rig.At(100101); Equal(2, rig.Sink.Sent.Count);
        }
        private static void StuckTarget()
        {
            var rig = Smart(settings => { settings.Teleport.NoTargetTimeoutMs = 20000; settings.Teleport.StuckEnabled = true; settings.Teleport.StuckTimeoutMs = 2000; });
            rig.At(0); rig.State.HasTarget = true; rig.At(1999);
            rig.State.HasTarget = false; rig.At(2000); rig.Through(3999); Equal(0, rig.Sink.Sent.Count);
            rig.At(4000); Equal(1, rig.Sink.Sent.Count);
        }
        private static void FixedCombat()
        {
            var rig = Smart(settings => settings.Teleport.Mode = TeleportMode.FixedInterval);
            rig.At(0); rig.State.InCombat = true; rig.At(3000); Equal(0, rig.Sink.Sent.Count);
            rig.State.InCombat = false; rig.At(3100); Equal(1, rig.Sink.Sent.Count);
        }
        private static void DryRun()
        {
            var rig = Recovery(settings => settings.DryRun = true, HeldSequence());
            rig.At(0); rig.At(100); rig.At(101); rig.At(700); rig.Engine.Stop("Test stopped");
            Equal(0, rig.Sink.Sent.Count); Equal(0, rig.Sink.Releases);
            Assert(rig.Logs.Any(line => line.StartsWith("WOULD EXECUTE")) && rig.Logs.Any(line => line.StartsWith("WOULD SEND")), "Dry-run action logs.");
        }
        private static void SpThreshold()
        {
            var rig = Recovery(); rig.State.CurrentSP = 30; rig.At(0); rig.At(100);
            Equal(0, rig.Sink.Sent.Count);
            rig.State.CurrentSP = 29; rig.At(101); Equal(1, rig.Sink.Sent.Count);
            rig.State.CurrentSP = 0; rig.At(1101); Equal(2, rig.Sink.Sent.Count);
        }
        private static void RecoveryCombat()
        {
            var rig = Recovery(); rig.State.InCombat = true; rig.At(0); rig.At(100); Equal(0, rig.Sink.Sent.Count);
            rig.State.InCombat = false; rig.At(200); Equal(1, rig.Sink.Sent.Count);
        }
        private static void SequenceCasting()
        {
            var rig = Recovery(null, new List<SequenceStep> { Key(118, 500), Key(119) });
            rig.At(0); rig.At(100); rig.State.IsCasting = true; rig.At(600);
            Equal(2, rig.Sink.Sent.Count); Assert(!rig.Engine.Busy, "Own casting can complete.");
        }
        private static void SequenceScheduling()
        {
            var rig = Recovery(null, new List<SequenceStep> { Key(118), new SequenceStep { Kind = SequenceStepKind.Wait, DelayMs = 500 }, Key(119), Key(120) });
            rig.At(0); rig.At(100); Equal(1, rig.Sink.Sent.Count);
            rig.At(1000); Equal(1, rig.Sink.Sent.Count);
            rig.At(1499); Equal(1, rig.Sink.Sent.Count);
            rig.At(1500); Equal(2, rig.Sink.Sent.Count);
            rig.At(1501); Equal(3, rig.Sink.Sent.Count);
        }
        private static void SequenceCooldown()
        {
            var rig = Recovery(null, new List<SequenceStep> { Key(118, 3000), Key(119) });
            rig.At(0); rig.At(100); rig.Through(2000); Equal(1, rig.Sink.Sent.Count);
            rig.At(3100); Equal(2, rig.Sink.Sent.Count);
            rig.At(4099); Equal(2, rig.Sink.Sent.Count);
            rig.At(4100); Equal(3, rig.Sink.Sent.Count);
        }
        private static void Disconnect()
        {
            var rig = Recovery(null, HeldSequence()); rig.At(0); rig.At(100);
            rig.Engine.Tick(TimeSpan.FromMilliseconds(200), null);
            Assert(!rig.Engine.Enabled && !rig.Engine.Busy, "Disconnect cancels.");
            Equal(1, rig.Sink.Releases); rig.At(10000); Equal(1, rig.Sink.Sent.Count);
        }
        private static void SharedScheduler()
        {
            var rig = Recovery(settings => { settings.Teleport.Enabled = true; settings.Teleport.Key = 117; },
                new List<SequenceStep> { Key(118, 3000), Key(119) });
            rig.Engine.SetFarmingEnabled(true); rig.At(0); rig.At(100); rig.Through(2000);
            Equal(1, rig.Sink.Sent.Count); Equal(118, rig.Sink.Sent[0].Key);
            rig.At(3100); rig.State.CurrentSP = 100; rig.At(3101);
            Equal(3, rig.Sink.Sent.Count); Equal(117, rig.Sink.Sent[2].Key);
        }
        private static void InvalidState()
        {
            var rig = Recovery(null, HeldSequence()); rig.At(0); rig.At(100);
            rig.State.SpValidated = false; rig.At(200);
            Assert(!rig.Engine.Enabled && !rig.Engine.Busy, "Required SP invalidates sequence.");
            Equal(1, rig.Sink.Releases);
        }
        private static void IdentityChange()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                var rig = Smart(); rig.At(0);
                if (mode == 0) rig.State.SessionId = Guid.NewGuid();
                else if (mode == 1) rig.State.Fingerprint = "different-build";
                else rig.State.ProcessId++;
                rig.At(2000); Assert(!rig.Engine.Enabled, "Changed identity stops."); Equal(0, rig.Sink.Sent.Count);
            }
        }
        private static void MapChange()
        {
            var rig = Recovery(null, HeldSequence()); rig.At(0); rig.At(100);
            rig.State.Map = "second"; rig.At(200);
            Assert(rig.Engine.Enabled && !rig.Engine.Busy, "Map changes pause and cancel."); Equal(1, rig.Sink.Releases);
            rig.At(299); Equal(1, rig.Sink.Sent.Count);
        }
        private static void Loading()
        {
            var rig = Smart(); rig.At(0); rig.State.Loading = true; rig.State.Ready = false; rig.At(2000);
            Assert(rig.Engine.Enabled, "Loading pauses.");
            rig.State.Loading = false; rig.State.Ready = true; rig.At(2100); rig.At(3099); Equal(0, rig.Sink.Sent.Count);
            rig.At(3100); Equal(1, rig.Sink.Sent.Count);
        }
        private static void GlobalOff()
        {
            var rig = Recovery(null, HeldSequence()); rig.At(0); rig.At(100); rig.Engine.SetEnabled(false); rig.At(5000);
            Equal(1, rig.Sink.Sent.Count); Equal(1, rig.Sink.Releases); Assert(!rig.Engine.Busy, "Off removes queue.");
        }
        private static void TestOnce()
        {
            var rig = Recovery(settings => settings.SpRecovery.Enabled = false, new List<SequenceStep> { Key(118, 500), Key(119) });
            rig.At(0); rig.At(100);
            Assert(rig.Engine.TestOnce(TimeSpan.FromMilliseconds(100), rig.State), "Manual test starts.");
            Assert(!rig.Engine.TestOnce(TimeSpan.FromMilliseconds(100), rig.State), "No overlap.");
            rig.At(600); rig.At(10000); Equal(2, rig.Sink.Sent.Count);
            rig.Engine.SetEnabled(false);
            Assert(!rig.Engine.TestOnce(TimeSpan.FromMilliseconds(10000), rig.State), "Off blocks Test Once.");
            rig.Engine.SetEnabled(true); rig.State.TrustedBuild = false;
            Assert(!rig.Engine.TestOnce(TimeSpan.FromMilliseconds(10000), rig.State), "Unknown build blocks Test Once.");
        }
        private static void InputFailure()
        {
            var rig = Recovery(); rig.Sink.Fail = true; rig.At(0); rig.At(100);
            Assert(!rig.Engine.Enabled && !rig.Engine.Busy && rig.Engine.Status.Contains("input failed"), "Input failure stops.");
            Equal(1, rig.Sink.Releases);
        }
        private static void BaseSafety()
        {
            foreach (var mutate in new System.Action<Rig>[] { r => r.State.CurrentHP = 0, r => r.State.MaxHP = 0,
                r => r.State.CurrentHP = 1000, r => r.State.TrustedBuild = false, r => r.State.Ready = false,
                r => r.State.ObservedAt = TimeSpan.FromSeconds(-10), r => r.State.ObservedAt = TimeSpan.FromSeconds(10) })
            {
                var rig = Smart(); mutate(rig); rig.Engine.Tick(TimeSpan.Zero, rig.State);
                Assert(!rig.Engine.Enabled, "Invalid base state stops."); Equal(0, rig.Sink.Sent.Count);
            }
            var clock = Smart(); clock.At(1000); clock.At(999); Assert(!clock.Engine.Enabled, "Clock regression stops.");
        }
        private static void ProfileRoundTrip()
        {
            var rig = Recovery(null, HeldSequence());
            var clone = VanillaAutomationSettings.FromJson(rig.Settings.ToJson());
            Equal(2, clone.SpRecovery.Sequence.Count); Equal(SequenceStepKind.KeyUp, clone.SpRecovery.Sequence[1].Kind);
            rig.Settings.SpRecovery.Sequence[0].Key = 120; rig.At(0); rig.At(100);
            Equal(118, rig.Sink.Sent[0].Key);
            Assert(!clone.ToJson().Contains("ProcessId") && !clone.ToJson().Contains("SessionId"), "No session identity persisted.");
        }
        private static void MalformedProfiles()
        {
            foreach (string value in new[] { "null", "{", "{\"Version\":2}", "{\"Teleport\":null}", "{\"Bogus\":1}", "{\"MaxStateAgeMs\":100000}",
                "{} {}", "{\"DryRun\":true,\"DryRun\":false}", "{\"DryRun\":true,\"dryrun\":false}" })
                Throws(() => VanillaAutomationSettings.FromJson(value));
            var settings = new VanillaAutomationSettings();
            settings.SpRecovery.Sequence = new List<SequenceStep> { new SequenceStep { Kind = SequenceStepKind.KeyDown, Key = 118 } };
            Throws(settings.Validate);
            settings.SpRecovery.Sequence = new List<SequenceStep> { Key(settings.EmergencyKey) }; Throws(settings.Validate);
            settings.SpRecovery.Sequence = new List<SequenceStep> { new SequenceStep { Kind = SequenceStepKind.Click, X = -1 } }; Throws(settings.Validate);
            settings.SpRecovery.Sequence.Clear(); settings.SpRecovery.Enabled = true; Throws(settings.Validate);
        }
        private static void GenericRules()
        {
            foreach (var condition in new[] { RuleCondition.Timed, RuleCondition.HealthBelow, RuleCondition.StatusPresent, RuleCondition.StatusMissing })
            {
                var settings = BaseSettings();
                settings.Rules.Add(new AutomationRuleSettings { Name = "Example", Enabled = true, Condition = condition, PeriodMs = 1000,
                    ThresholdPercent = 100, StatusId = condition == RuleCondition.StatusPresent ? 10u : 20u, Sequence = new List<SequenceStep> { Key(118) } });
                var rig = new Rig(settings); rig.Engine.SetEnabled(true); rig.At(0); rig.At(1000); Equal(1, rig.Sink.Sent.Count);
            }
        }
        private static void UnknownStatus()
        {
            var settings = BaseSettings(); settings.Rules.Add(new AutomationRuleSettings { Enabled = true, Condition = RuleCondition.StatusMissing, Sequence = new List<SequenceStep> { Key(118) } });
            var rig = new Rig(settings); rig.Engine.SetEnabled(true); rig.State.StatusEffects = null; rig.At(0);
            Assert(!rig.Engine.Enabled, "Unknown status stops status rule."); Equal(0, rig.Sink.Sent.Count);
        }
        private static void Farming()
        {
            var rig = Smart(); rig.Engine.SetFarmingEnabled(false); rig.At(0); rig.At(10000); Equal(0, rig.Sink.Sent.Count);
            rig.Engine.SetFarmingEnabled(true); rig.At(10001); rig.At(11000); Equal(0, rig.Sink.Sent.Count);
            rig.At(11001); Equal(1, rig.Sink.Sent.Count);
        }
        private static void ChangedSettings()
        {
            var rig = Recovery(null, HeldSequence()); rig.At(0); rig.At(100); rig.Engine.Configure(rig.Settings);
            Assert(!rig.Engine.Enabled && !rig.Engine.Busy, "Settings changes stop."); Equal(1, rig.Sink.Releases);
        }
        private static void ObservationGap()
        {
            var rig = Smart(); rig.At(0); rig.At(100); rig.At(5000);
            Equal(0, rig.Sink.Sent.Count);
            Assert(rig.Engine.NoTargetSince == TimeSpan.FromMilliseconds(5000) && rig.Engine.NoCombatSince == TimeSpan.FromMilliseconds(5000),
                "Unobserved time does not count as idle.");
            rig.Through(5999); Equal(0, rig.Sink.Sent.Count); rig.At(6000); Equal(1, rig.Sink.Sent.Count);
            var stuck = Smart(settings => { settings.Teleport.NoTargetTimeoutMs = 60000; settings.Teleport.StuckEnabled = true; settings.Teleport.StuckTimeoutMs = 2000; });
            stuck.At(0); stuck.At(1000); stuck.At(10000); stuck.Through(11999); Equal(0, stuck.Sink.Sent.Count);
            stuck.At(12000); Equal(1, stuck.Sink.Sent.Count);
        }
        private static void SequenceObservationGap()
        {
            foreach (bool dryRun in new[] { false, true })
            {
                var rig = Recovery(settings => settings.DryRun = dryRun, HeldSequence());
                rig.At(0); rig.At(100); rig.At(2000);
                Assert(rig.Engine.Enabled && !rig.Engine.Busy, "A fresh sample after a gap starts a new baseline and cancels the old sequence.");
                Equal(dryRun ? 0 : 1, rig.Sink.Releases);
                rig.State.CurrentSP = 100; rig.Through(5000);
                Equal(dryRun ? 0 : 1, rig.Sink.Sent.Count);
                Assert(rig.Logs.Any(value => value.Contains("Observation gap")), "Lost continuity is diagnosed.");
            }
        }
        private static void GapBoundary()
        {
            var exact = Smart(); exact.At(0); exact.At(exact.Settings.MaxStateAgeMs); Equal(1, exact.Sink.Sent.Count);
            var exceeded = Smart(); exceeded.At(0); exceeded.At(exceeded.Settings.MaxStateAgeMs + 1); Equal(0, exceeded.Sink.Sent.Count);
        }

        private static Rig Smart(System.Action<VanillaAutomationSettings> modify = null)
        {
            var settings = BaseSettings(); settings.Teleport.Enabled = true; settings.Teleport.Key = 117;
            if (modify != null) modify(settings);
            var rig = new Rig(settings); rig.Engine.SetEnabled(true); rig.Engine.SetFarmingEnabled(true); return rig;
        }
        private static Rig Recovery(System.Action<VanillaAutomationSettings> modify = null, List<SequenceStep> steps = null)
        {
            var settings = BaseSettings(); settings.SpRecovery.Enabled = true; settings.SpRecovery.CooldownMs = 1000;
            settings.SpRecovery.Sequence = steps ?? new List<SequenceStep> { Key(118) };
            if (modify != null) modify(settings);
            var rig = new Rig(settings); rig.Engine.SetEnabled(true); return rig;
        }
        private static VanillaAutomationSettings BaseSettings()
        {
            return new VanillaAutomationSettings { DryRun = false, Teleport = new TeleportSettings { GraceMs = 100,
                CooldownMs = 1000, NoTargetTimeoutMs = 1000, NoCombatTimeoutMs = 1000, FixedIntervalMs = 3000 } };
        }
        private static SequenceStep Key(int key, int delay = 0) { return new SequenceStep { Key = key, DelayMs = delay }; }
        private static List<SequenceStep> HeldSequence()
        {
            return new List<SequenceStep> { new SequenceStep { Kind = SequenceStepKind.KeyDown, Key = 118, DelayMs = 1000 }, new SequenceStep { Kind = SequenceStepKind.KeyUp, Key = 118 } };
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual) { Assert(Equals(expected, actual), "Expected " + expected + ", got " + actual + "."); }
        private static void Throws(System.Action action) { try { action(); } catch { return; } throw new Exception("Expected validation failure."); }

        private sealed class Rig
        {
            public readonly VanillaAutomationSettings Settings;
            public readonly FakeInput Sink = new FakeInput();
            public readonly List<string> Logs = new List<string>();
            public readonly AutomationEngine Engine;
            private int? lastSample;
            public readonly RuleObservation State = new RuleObservation { ProcessId = 42, SessionId = Guid.NewGuid(), Fingerprint = "offline-test-build",
                TrustedBuild = true, Ready = true, CurrentHP = 90, MaxHP = 100, CurrentSP = 20, MaxSP = 100,
                HealthValidated = true, SpValidated = true, TargetValidated = true, CombatValidated = true, CastingValidated = true,
                PositionValidated = true, StatusValidated = true, HasTarget = false, InCombat = false, IsCasting = false,
                X = 10, Y = 20, Map = "first", StatusEffects = new uint[] { 10 } };
            public Rig(VanillaAutomationSettings settings) { Settings = settings; Engine = new AutomationEngine(settings, Sink, Logs.Add); }
            public void At(int milliseconds)
            {
                var now = TimeSpan.FromMilliseconds(milliseconds); State.ObservedAt = now;
                Engine.Tick(now, State); lastSample = milliseconds;
            }
            public void Through(int milliseconds)
            {
                if (lastSample.HasValue)
                    for (int sample = lastSample.Value + 100; sample < milliseconds; sample += 100) At(sample);
                At(milliseconds);
            }
        }
        private sealed class FakeInput : IInputSink
        {
            public readonly List<SequenceStep> Sent = new List<SequenceStep>();
            public int Releases;
            public bool Fail;
            public void Send(SequenceStep step) { if (Fail) throw new InvalidOperationException("Simulated input rejection"); Sent.Add(step); }
            public void ReleaseAll() { Releases++; }
        }
    }
}
