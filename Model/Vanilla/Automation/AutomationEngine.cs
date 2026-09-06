using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace _4RTools.Model.Vanilla.Automation
{
    /// <summary>Single-threaded scheduler; call Tick from the application's UI timer.</summary>
    public sealed class AutomationEngine
    {
        private readonly IInputSink input;
        private readonly Action<string> log;
        private VanillaAutomationSettings settings;
        private readonly Dictionary<string, TimeSpan> lastRuns = new Dictionary<string, TimeSpan>();
        private TimeSpan? lastTick, activeSince, graceUntil;
        private Guid session;
        private int process;
        private string fingerprint, map;
        private int? x, y;
        private List<SequenceStep> sequence;
        private int nextStep;
        private TimeSpan nextStepAt;
        private string activeRule;
        private bool inputSent;

        public bool Enabled { get; private set; }
        public bool FarmingEnabled { get; private set; }
        public bool Busy { get { return sequence != null; } }
        public string Status { get; private set; } = "Automation OFF";
        public TimeSpan? LastTeleportAt { get; private set; }
        public TimeSpan? NoTargetSince { get; private set; }
        public TimeSpan? NoCombatSince { get; private set; }
        public TimeSpan? PositionUnchangedSince { get; private set; }

        public AutomationEngine(VanillaAutomationSettings settings, IInputSink input, Action<string> log)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            this.input = input;
            this.log = log ?? (_ => { });
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Clone();
        }

        public void Configure(VanillaAutomationSettings value)
        {
            var validated = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
            Stop("Settings changed; automation OFF");
            settings = validated;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled == Enabled) return;
            if (!enabled) { Stop("Automation OFF"); return; }
            ClearHistory();
            Enabled = true;
            SetStatus(settings.DryRun ? "Automation ON (dry run)" : "Automation ON");
        }

        public void SetFarmingEnabled(bool enabled)
        {
            if (enabled == FarmingEnabled) return;
            FarmingEnabled = enabled;
            ResetIdle();
            activeSince = lastTick;
            if (!enabled && activeRule == "teleport") Cancel("Farming stopped");
            WriteLog(enabled ? "Farming enabled" : "Farming disabled");
        }

        public void Stop(string reason)
        {
            Cancel(reason);
            Enabled = false;
            ClearHistory();
            SetStatus(reason ?? "Automation OFF");
        }

        public void Tick(TimeSpan now, RuleObservation observation)
        {
            if (!Prepare(now, observation)) return;
            string invalid = RequiredStateError(observation);
            if (invalid != null) { Stop(invalid); return; }
            UpdateIdle(now, observation);
            if (Busy)
            {
                if (!ActiveRuleMayContinue(observation)) { Cancel("Sequence cancelled: combat started"); return; }
                Advance(now);
                return;
            }
            if (graceUntil.HasValue && now < graceUntil.Value)
            { SetStatus("Waiting for loading/teleport grace"); return; }

            var sp = settings.SpRecovery;
            if (sp.Enabled && Below(observation.CurrentSP, observation.MaxSP, sp.ThresholdPercent)
                && (!sp.PreferOutOfCombat || OutOfCombat(observation)) && Cooldown("sp", now, sp.CooldownMs))
            { Start("sp", "SP RECOVERY", sp.Sequence, now); return; }

            var teleport = settings.Teleport;
            if (teleport.Enabled && FarmingEnabled && Cooldown("teleport", now, teleport.CooldownMs)
                && CanTeleport(now, observation))
            {
                LastTeleportAt = now;
                graceUntil = now + TimeSpan.FromMilliseconds(teleport.GraceMs);
                ResetIdle();
                Start("teleport", "TELEPORT", new List<SequenceStep> { new SequenceStep { Key = teleport.Key } }, now);
                return;
            }

            for (int index = 0; index < settings.Rules.Count; index++)
            {
                var rule = settings.Rules[index];
                string id = "rule:" + index.ToString(CultureInfo.InvariantCulture);
                if (rule.Enabled && (!rule.PreferOutOfCombat || OutOfCombat(observation))
                    && Cooldown(id, now, rule.CooldownMs) && Matches(rule, id, now, observation))
                { Start(id, rule.Name, rule.Sequence, now); return; }
            }
            SetStatus(settings.DryRun ? "Watching validated state (dry run)" : "Watching validated state");
        }

        /// <summary>Runs the configured SP sequence once, with the same safety checks as automatic execution.</summary>
        public bool TestOnce(TimeSpan now, RuleObservation observation)
        {
            if (Busy || !Enabled) { WriteLog("Test Once rejected: enable automation and wait for the current sequence."); return false; }
            if (!Prepare(now, observation)) return false;
            string invalid = RequiredStateError(observation) ?? SpStateError(observation, settings.SpRecovery.PreferOutOfCombat);
            if (invalid != null) { Stop(invalid); return false; }
            if ((graceUntil.HasValue && now < graceUntil.Value)
                || (settings.SpRecovery.PreferOutOfCombat && !OutOfCombat(observation)))
            { WriteLog("Test Once rejected: grace period or combat is active."); return false; }
            try { VanillaAutomationSettings.ValidateSequence(settings.SpRecovery.Sequence, true, settings.EmergencyKey); }
            catch (ArgumentException ex) { WriteLog("Test Once rejected: " + ex.Message); return false; }
            Start("manual-sp", "SP RECOVERY TEST", settings.SpRecovery.Sequence, now);
            if (!Enabled) return false;
            lastRuns["sp"] = now;
            return true;
        }

        private bool Prepare(TimeSpan now, RuleObservation observation)
        {
            if (!Enabled) return false;
            if (now < TimeSpan.Zero || (lastTick.HasValue && now < lastTick.Value))
            { Stop("Automation stopped: monotonic clock moved backwards"); return false; }
            bool observationGap = lastTick.HasValue && now - lastTick.Value > TimeSpan.FromMilliseconds(settings.MaxStateAgeMs);
            lastTick = now;
            string invalid = BaseStateError(now, observation);
            if (invalid != null) { Stop(invalid); return false; }
            if (session != Guid.Empty && (session != observation.SessionId || process != observation.ProcessId
                || !string.Equals(fingerprint, observation.Fingerprint, StringComparison.Ordinal)))
            { Stop("Automation stopped: selected client/session/build changed"); return false; }

            bool first = session == Guid.Empty;
            bool mapChanged = !first && !string.Equals(map, observation.Map, StringComparison.Ordinal);
            session = observation.SessionId;
            process = observation.ProcessId;
            fingerprint = observation.Fingerprint;
            map = observation.Map;
            if (first || mapChanged || observationGap)
            {
                Cancel(observationGap ? "Observation continuity was lost" : mapChanged ? "Map changed" : "Client selected");
                ResetIdle();
                // Fixed interval keeps its explicit schedule; state-dependent timers require a new baseline.
                if (first || mapChanged) activeSince = now;
                graceUntil = now + TimeSpan.FromMilliseconds(settings.Teleport.GraceMs);
                if (mapChanged) WriteLog("Map changed; timers reset");
                if (observationGap) WriteLog("Observation gap exceeded state freshness; queued input cancelled and idle timers reset");
            }
            if (observation.Loading)
            {
                Cancel("Client is loading");
                ResetIdle();
                activeSince = now;
                graceUntil = now + TimeSpan.FromMilliseconds(settings.Teleport.GraceMs);
                SetStatus("Paused: client is loading");
                return false;
            }
            return true;
        }

        private string BaseStateError(TimeSpan now, RuleObservation state)
        {
            if (state == null || state.SessionId == Guid.Empty || state.ProcessId <= 0)
                return "Automation stopped: client disconnected";
            if (!state.TrustedBuild || string.IsNullOrWhiteSpace(state.Fingerprint))
                return "Automation stopped: client build is not verified";
            if (state.ObservedAt > now || now - state.ObservedAt > TimeSpan.FromMilliseconds(settings.MaxStateAgeMs))
                return "Automation stopped: state is stale";
            if (!state.Ready && !state.Loading) return "Automation stopped: client readiness is not verified";
            if (!state.HealthValidated || !ValidResource(state.CurrentHP, state.MaxHP))
                return "Automation stopped: HP state is not validated";
            if (state.CurrentHP == 0) return "Automation stopped: character is dead";
            return null;
        }

        private string RequiredStateError(RuleObservation state)
        {
            if (settings.Teleport.Enabled && FarmingEnabled && settings.Teleport.Mode == TeleportMode.SmartIdle)
            {
                if (!ValidTarget(state)) return "Automation stopped: target state is not validated";
                if (!ValidCombat(state)) return "Automation stopped: combat/casting state is not validated";
                if (settings.Teleport.StuckEnabled && !ValidPosition(state)) return "Automation stopped: position is not validated";
            }
            if (settings.SpRecovery.Enabled || activeRule == "manual-sp")
            {
                string error = SpStateError(state, settings.SpRecovery.PreferOutOfCombat);
                if (error != null) return error;
            }
            foreach (var rule in settings.Rules.Where(rule => rule.Enabled))
            {
                if ((rule.PreferOutOfCombat || rule.Condition == RuleCondition.NoCombat) && !ValidCombat(state))
                    return "Automation stopped: rule requires validated combat/casting state";
                if (rule.Condition == RuleCondition.SpBelow && (!state.SpValidated || !ValidResource(state.CurrentSP, state.MaxSP)))
                    return "Automation stopped: rule requires validated SP";
                if (rule.Condition == RuleCondition.NoTarget && !ValidTarget(state))
                    return "Automation stopped: rule requires validated target state";
                if (rule.Condition == RuleCondition.SamePosition && (!ValidPosition(state) || !ValidCombat(state) || !ValidTarget(state)))
                    return "Automation stopped: stationary rule requires validated position, target and combat";
                if ((rule.Condition == RuleCondition.StatusPresent || rule.Condition == RuleCondition.StatusMissing)
                    && (!state.StatusValidated || state.StatusEffects == null))
                    return "Automation stopped: rule requires validated statuses";
            }
            return null;
        }

        private static string SpStateError(RuleObservation state, bool requireCombat)
        {
            if (!state.SpValidated || !ValidResource(state.CurrentSP, state.MaxSP)) return "Automation stopped: SP state is not validated";
            if (requireCombat && !ValidCombat(state)) return "Automation stopped: combat/casting state is not validated";
            return null;
        }

        private void UpdateIdle(TimeSpan now, RuleObservation state)
        {
            NoTargetSince = ValidTarget(state) && state.HasTarget == false ? (TimeSpan?)(NoTargetSince ?? now) : null;
            NoCombatSince = ValidCombat(state) && OutOfCombat(state) ? (TimeSpan?)(NoCombatSince ?? now) : null;
            if (!ValidPosition(state) || !ValidTarget(state) || !ValidCombat(state)
                || state.HasTarget != false || !OutOfCombat(state)) PositionUnchangedSince = null;
            else if (x != state.X || y != state.Y || !PositionUnchangedSince.HasValue) PositionUnchangedSince = now;
            x = state.X;
            y = state.Y;
        }

        private bool CanTeleport(TimeSpan now, RuleObservation state)
        {
            // Fixed interval is explicit and may operate without target/combat fields, but respects known activity.
            if (state.HasTarget == true || state.InCombat == true || state.IsCasting == true) return false;
            var teleport = settings.Teleport;
            if (teleport.Mode == TeleportMode.FixedInterval)
                return Elapsed(now, LastRunOrStart("teleport"), teleport.FixedIntervalMs);
            if (!OutOfCombat(state) || state.HasTarget != false) return false;
            bool idle = Elapsed(now, NoTargetSince, teleport.NoTargetTimeoutMs)
                && Elapsed(now, NoCombatSince, teleport.NoCombatTimeoutMs);
            bool stuck = teleport.StuckEnabled && Elapsed(now, PositionUnchangedSince, teleport.StuckTimeoutMs)
                && Elapsed(now, NoCombatSince, teleport.NoCombatTimeoutMs);
            return idle || stuck;
        }

        private bool Matches(AutomationRuleSettings rule, string id, TimeSpan now, RuleObservation state)
        {
            switch (rule.Condition)
            {
                case RuleCondition.Timed: return Elapsed(now, LastRunOrStart(id), rule.PeriodMs);
                case RuleCondition.HealthBelow: return Below(state.CurrentHP, state.MaxHP, rule.ThresholdPercent);
                case RuleCondition.SpBelow: return Below(state.CurrentSP, state.MaxSP, rule.ThresholdPercent);
                case RuleCondition.StatusPresent: return state.StatusEffects.Contains(rule.StatusId);
                case RuleCondition.StatusMissing: return !state.StatusEffects.Contains(rule.StatusId);
                case RuleCondition.NoTarget: return Elapsed(now, NoTargetSince, rule.PeriodMs);
                case RuleCondition.NoCombat: return Elapsed(now, NoCombatSince, rule.PeriodMs);
                case RuleCondition.SamePosition: return Elapsed(now, PositionUnchangedSince, rule.PeriodMs);
                default: return false;
            }
        }

        private bool ActiveRuleMayContinue(RuleObservation state)
        {
            if (activeRule == "sp" || activeRule == "manual-sp")
                return !settings.SpRecovery.PreferOutOfCombat || state.InCombat == false;
            if (activeRule != null && activeRule.StartsWith("rule:", StringComparison.Ordinal))
            {
                var rule = settings.Rules[int.Parse(activeRule.Substring(5), CultureInfo.InvariantCulture)];
                // A sequence may itself start casting; only external combat interrupts its remaining steps.
                return !rule.PreferOutOfCombat || state.InCombat == false;
            }
            return true;
        }

        private void Start(string id, string description, List<SequenceStep> steps, TimeSpan now)
        {
            if (Busy) throw new InvalidOperationException("An action sequence is already active.");
            sequence = steps;
            nextStep = 0;
            nextStepAt = now;
            activeRule = id;
            lastRuns[id] = now;
            WriteLog((settings.DryRun ? "WOULD EXECUTE " : "Triggered ") + description);
            SetStatus((settings.DryRun ? "Dry run: " : "Executing: ") + description);
            Advance(now);
        }

        private void Advance(TimeSpan now)
        {
            if (!Busy || now < nextStepAt) return;
            if (nextStep >= sequence.Count) { Complete(now); return; }
            var step = sequence[nextStep++];
            try
            {
                if (step.Kind != SequenceStepKind.Wait)
                {
                    if (!settings.DryRun)
                    {
                        inputSent = true;
                        input.Send(new SequenceStep { Kind = step.Kind, Key = step.Key, DelayMs = step.DelayMs, X = step.X, Y = step.Y });
                    }
                    WriteLog((settings.DryRun ? "WOULD SEND " : "Sent ") + Describe(step));
                }
                // Relative to actual execution, never the missed deadline. One step per tick prevents bursts.
                nextStepAt = now + TimeSpan.FromMilliseconds(step.DelayMs);
                if (nextStep >= sequence.Count && step.DelayMs == 0) Complete(now);
            }
            catch (Exception ex) { Stop("Automation stopped: input failed: " + ex.Message); }
        }

        private void Complete(TimeSpan now)
        {
            string completed = activeRule;
            sequence = null;
            activeRule = null;
            inputSent = false; // Validated sequences end with all held keys released.
            if (completed != "teleport") lastRuns[completed == "manual-sp" ? "sp" : completed] = now;
            WriteLog("Sequence completed: " + completed);
        }

        private void Cancel(string reason)
        {
            if (Busy) WriteLog("Sequence cancelled: " + reason);
            sequence = null;
            activeRule = null;
            if (!inputSent) return;
            inputSent = false;
            try { input.ReleaseAll(); }
            catch (Exception ex) { WriteLog("Held-input release failed: " + ex.Message); }
        }

        private void ClearHistory()
        {
            lastRuns.Clear();
            lastTick = activeSince = graceUntil = LastTeleportAt = null;
            session = Guid.Empty;
            process = 0;
            fingerprint = map = null;
            ResetIdle();
        }
        private void ResetIdle() { NoTargetSince = NoCombatSince = PositionUnchangedSince = null; x = y = null; }
        private TimeSpan? LastRunOrStart(string id)
        {
            TimeSpan value;
            if (!lastRuns.TryGetValue(id, out value)) return activeSince;
            return activeSince.HasValue && activeSince.Value > value ? activeSince : value;
        }
        private bool Cooldown(string id, TimeSpan now, int interval) { TimeSpan value; return !lastRuns.TryGetValue(id, out value) || Elapsed(now, value, interval); }
        private static bool Elapsed(TimeSpan now, TimeSpan? since, int ms) { return since.HasValue && now - since.Value >= TimeSpan.FromMilliseconds(ms); }
        private static bool ValidResource(uint? current, uint? maximum) { return current.HasValue && maximum.HasValue && maximum > 0 && current <= maximum; }
        private static bool Below(uint? current, uint? maximum, decimal threshold) { return ValidResource(current, maximum) && current.Value * 100m < maximum.Value * threshold; }
        private static bool ValidTarget(RuleObservation state) { return state.TargetValidated && state.HasTarget.HasValue; }
        private static bool ValidCombat(RuleObservation state) { return state.CombatValidated && state.CastingValidated && state.InCombat.HasValue && state.IsCasting.HasValue; }
        private static bool OutOfCombat(RuleObservation state) { return ValidCombat(state) && state.InCombat == false && state.IsCasting == false; }
        private static bool ValidPosition(RuleObservation state) { return state.PositionValidated && state.X.HasValue && state.Y.HasValue && state.X >= 0 && state.Y >= 0; }
        private static string Describe(SequenceStep step) { return step.Kind == SequenceStepKind.Click ? "Click (" + step.X + ", " + step.Y + ")" : step.Kind + " key " + step.Key; }
        private void SetStatus(string value) { if (Status == value) return; Status = value; WriteLog(value); }
        private void WriteLog(string value) { log(value); }
    }
}
