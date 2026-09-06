using System;

namespace _4RTools.Model.Vanilla.Automation
{
    /// <summary>
    /// An adapter supplies one coherent snapshot in the engine's monotonic clock domain.
    /// Validated flags represent proven semantics, not merely a successful memory read.
    /// Ready includes a proven in-game/loading signal. Combat must include hit/recovery activity.
    /// </summary>
    public sealed class RuleObservation
    {
        public TimeSpan ObservedAt { get; set; }
        public Guid SessionId { get; set; }
        public int ProcessId { get; set; }
        public string Fingerprint { get; set; }
        public bool TrustedBuild { get; set; }
        public bool Ready { get; set; }
        public bool Loading { get; set; }
        public uint? CurrentHP { get; set; }
        public uint? MaxHP { get; set; }
        public uint? CurrentSP { get; set; }
        public uint? MaxSP { get; set; }
        public bool HealthValidated { get; set; }
        public bool SpValidated { get; set; }
        public bool TargetValidated { get; set; }
        public bool CombatValidated { get; set; }
        public bool CastingValidated { get; set; }
        public bool PositionValidated { get; set; }
        public bool StatusValidated { get; set; }
        public bool? HasTarget { get; set; }
        public bool? InCombat { get; set; }
        public bool? IsCasting { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public string Map { get; set; }
        public uint[] StatusEffects { get; set; }
    }

    /// <summary>Ordinary input bound to the selected process/window. Failures must throw.</summary>
    public interface IInputSink
    {
        void Send(SequenceStep step);
        // Release only keys/buttons held by this sink, verifying the original window identity.
        void ReleaseAll();
    }
}
