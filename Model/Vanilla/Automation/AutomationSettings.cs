using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _4RTools.Model.Vanilla.Automation
{
    public enum TeleportMode { SmartIdle, FixedInterval }
    public enum SequenceStepKind { PressKey, KeyDown, KeyUp, Wait, Click }
    public enum RuleCondition { Timed, HealthBelow, SpBelow, StatusPresent, StatusMissing, NoTarget, NoCombat, SamePosition }

    public sealed class SequenceStep
    {
        public SequenceStepKind Kind { get; set; }
        public int Key { get; set; }
        public int DelayMs { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class TeleportSettings
    {
        public bool Enabled { get; set; }
        public TeleportMode Mode { get; set; } = TeleportMode.SmartIdle;
        public int Key { get; set; }
        public int NoTargetTimeoutMs { get; set; } = 10000;
        public int NoCombatTimeoutMs { get; set; } = 10000;
        public int CooldownMs { get; set; } = 15000;
        public int GraceMs { get; set; } = 3000;
        public int FixedIntervalMs { get; set; } = 30000;
        public bool StuckEnabled { get; set; }
        public int StuckTimeoutMs { get; set; } = 30000;
    }

    public sealed class SpRecoverySettings
    {
        public bool Enabled { get; set; }
        public decimal ThresholdPercent { get; set; } = 30;
        public int CooldownMs { get; set; } = 30000;
        public bool PreferOutOfCombat { get; set; } = true;
        public List<SequenceStep> Sequence { get; set; } = new List<SequenceStep>();
    }

    public sealed class AutomationRuleSettings
    {
        public string Name { get; set; } = "Timed action";
        public bool Enabled { get; set; }
        public RuleCondition Condition { get; set; }
        public int PeriodMs { get; set; } = 30000;
        public decimal ThresholdPercent { get; set; } = 30;
        public uint StatusId { get; set; }
        public int CooldownMs { get; set; } = 30000;
        public bool PreferOutOfCombat { get; set; }
        public List<SequenceStep> Sequence { get; set; } = new List<SequenceStep>();
    }

    /// <summary>Portable user preferences; contains no process identity or resolved addresses.</summary>
    public sealed class VanillaAutomationSettings
    {
        public int Version { get; set; } = 1;
        public bool DryRun { get; set; } = true;
        public int EmergencyKey { get; set; } = 19; // Pause
        public int MaxStateAgeMs { get; set; } = 1500;
        public TeleportSettings Teleport { get; set; } = new TeleportSettings();
        public SpRecoverySettings SpRecovery { get; set; } = new SpRecoverySettings();
        public List<AutomationRuleSettings> Rules { get; set; } = new List<AutomationRuleSettings>();

        public void Validate()
        {
            Require(Version == 1, "Unsupported automation profile version.");
            Require(Teleport != null && SpRecovery != null && Rules != null, "Automation settings sections cannot be null.");
            ValidateKey(EmergencyKey, "Emergency key");
            Range(MaxStateAgeMs, 100, 5000, "State freshness");
            Require(Enum.IsDefined(typeof(TeleportMode), Teleport.Mode), "Unknown teleport mode.");
            if (Teleport.Enabled || Teleport.Key != 0) ValidateKey(Teleport.Key, "Teleport key");
            Require(Teleport.Key == 0 || Teleport.Key != EmergencyKey, "Teleport and emergency keys must differ.");
            Range(Teleport.NoTargetTimeoutMs, 100, 3600000, "No-target timeout");
            Range(Teleport.NoCombatTimeoutMs, 100, 3600000, "No-combat timeout");
            Range(Teleport.CooldownMs, 1000, 3600000, "Teleport cooldown");
            Range(Teleport.GraceMs, 100, 600000, "Teleport grace");
            Range(Teleport.FixedIntervalMs, 1000, 3600000, "Fixed interval");
            Range(Teleport.StuckTimeoutMs, 1000, 3600000, "Stuck timeout");
            Percent(SpRecovery.ThresholdPercent);
            Range(SpRecovery.CooldownMs, 1000, 3600000, "SP cooldown");
            ValidateSequence(SpRecovery.Sequence, SpRecovery.Enabled, EmergencyKey);
            Require(Rules.Count <= 32, "At most 32 additional rules are supported.");
            foreach (var rule in Rules)
            {
                Require(rule != null && !string.IsNullOrWhiteSpace(rule.Name) && rule.Name.Length <= 80, "Each rule needs a name of 1–80 characters.");
                Require(Enum.IsDefined(typeof(RuleCondition), rule.Condition), "Unknown rule condition.");
                Range(rule.PeriodMs, 1000, 3600000, "Rule interval");
                Range(rule.CooldownMs, 1000, 3600000, "Rule cooldown");
                Percent(rule.ThresholdPercent);
                ValidateSequence(rule.Sequence, rule.Enabled, EmergencyKey);
            }
        }

        public string ToJson()
        {
            Validate();
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public VanillaAutomationSettings Clone() { return FromJson(ToJson()); }

        public static VanillaAutomationSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024)
                throw new ArgumentException("Automation profile is empty or exceeds 1 MB.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                Require(!reader.Read() && token.Type == JTokenType.Object, "Automation profile must contain exactly one JSON object.");
                foreach (var item in ((JContainer)token).DescendantsAndSelf().OfType<JObject>())
                    Require(item.Properties().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == item.Properties().Count(),
                        "Automation profile contains property names that differ only by case.");
                var result = token.ToObject<VanillaAutomationSettings>(JsonSerializer.Create(new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MissingMemberHandling = MissingMemberHandling.Error,
                    MaxDepth = 16
                }));
                result.Validate();
                return result;
            }
        }

        internal static void ValidateSequence(IList<SequenceStep> steps, bool required, int emergencyKey)
        {
            Require(steps != null && steps.Count <= 64, "A sequence must contain at most 64 steps.");
            Require(!required || steps.Any(step => step != null && step.Kind != SequenceStepKind.Wait), "An enabled rule needs an input step.");
            var held = new HashSet<int>();
            long duration = 0;
            foreach (var step in steps)
            {
                Require(step != null && Enum.IsDefined(typeof(SequenceStepKind), step.Kind), "Unknown sequence step.");
                Range(step.DelayMs, 0, 60000, "Step delay");
                duration += step.DelayMs;
                if (step.Kind == SequenceStepKind.Wait) continue;
                if (step.Kind == SequenceStepKind.Click)
                {
                    Range(step.X, 0, 32767, "Click X");
                    Range(step.Y, 0, 32767, "Click Y");
                    continue;
                }
                ValidateKey(step.Key, "Sequence key");
                Require(step.Key != emergencyKey, "A sequence cannot press the emergency key.");
                if (step.Kind == SequenceStepKind.KeyDown) Require(held.Add(step.Key), "A key cannot be held down twice.");
                if (step.Kind == SequenceStepKind.KeyUp) Require(held.Remove(step.Key), "Key up requires a preceding key down.");
                if (step.Kind == SequenceStepKind.PressKey) Require(!held.Contains(step.Key), "A held key cannot also be pressed.");
            }
            Require(held.Count == 0, "Every key down requires a matching key up.");
            Require(duration <= 300000, "A sequence cannot wait for more than five minutes in total.");
        }

        private static void ValidateKey(int key, string name)
        {
            // Ordinary single virtual keys only, not modifier bit masks or mouse buttons.
            Require(key >= 8 && key <= 254 && key != 255, name + " must be a keyboard virtual-key code (8–254).");
        }
        private static void Percent(decimal value) { Require(value > 0 && value <= 100, "Threshold must be above 0 and at most 100 percent."); }
        private static void Range(int value, int min, int max, string name) { Require(value >= min && value <= max, name + " must be between " + min + " and " + max + "."); }
        private static void Require(bool valid, string error) { if (!valid) throw new ArgumentException(error); }
    }
}
