using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    public enum VanillaField
    {
        CurrentHP, MaxHP, CurrentSP, MaxSP, CharacterName, X, Y,
        CurrentTargetId, ActionState, Map, AutobattleEnabled, StatusEffects, ClientReady, Loading
    }

    public enum StateValidation { Unavailable, Unverified, Valid, Invalid }

    public abstract class StateValue
    {
        public bool IsAvailable { get; internal set; }
        public StateValidation Validation { get; internal set; }
        public abstract object UntypedValue { get; }
        public DateTimeOffset? LastObservedAtUtc { get; internal set; }
        // A first sample is not evidence of a transition.
        public DateTimeOffset? LastChangedAtUtc { get; internal set; }
        public ulong? Address { get; internal set; }
        public string Error { get; internal set; }
        public string Evidence { get; internal set; }

        public override string ToString()
        {
            if (!IsAvailable) return "Unavailable";
            var statuses = UntypedValue as uint[];
            return statuses != null ? string.Join(", ", statuses) : Convert.ToString(UntypedValue, CultureInfo.InvariantCulture);
        }
    }

    public sealed class StateValue<T> : StateValue
    {
        private readonly T value;
        internal StateValue(T value) { this.value = value; }
        [JsonIgnore]
        public T Value
        {
            get
            {
                if (!IsAvailable) throw new InvalidOperationException("This field is unavailable; inspect IsAvailable before using Value.");
                // Do not let a caller mutate a snapshot's status values.
                return value is uint[] ? (T)(object)((uint[])(object)value).Clone() : value;
            }
        }
        public override object UntypedValue { get { return IsAvailable ? (object)Value : null; } }
    }

    public sealed class VanillaClientState
    {
        public Guid SessionId { get; internal set; }
        public DateTimeOffset SampledAtUtc { get; internal set; }
        public bool IsDemo { get; internal set; }
        public int? ProcessId { get; internal set; }
        public string ProcessName { get; internal set; }
        public string ExecutablePath { get; internal set; }
        public string Fingerprint { get; internal set; }
        public string BuildProfile { get; internal set; }
        public ulong? ModuleBaseAddress { get; internal set; }
        public int? TargetPointerSize { get; internal set; }
        public string ConnectionStatus { get; internal set; }
        public string Error { get; internal set; }
        public DateTimeOffset? LastStateChangeAtUtc { get; internal set; }
        public DateTimeOffset? LastMovementAtUtc { get; internal set; }
        public DateTimeOffset? LastTargetActivityAtUtc { get; internal set; }
        public DateTimeOffset? LastCombatActivityAtUtc { get; internal set; }
        public IReadOnlyDictionary<VanillaField, StateValue> Fields { get; internal set; }
        // Frozen by MemoryStateSource; used to prevent a diagnostics map from inheriting build-profile trust.
        [JsonIgnore]
        internal VanillaMemoryMap SourceMap { get; set; }
        public StateValue<uint> CurrentHP { get { return Get<uint>(VanillaField.CurrentHP); } }
        public StateValue<uint> MaxHP { get { return Get<uint>(VanillaField.MaxHP); } }
        public StateValue<uint> CurrentSP { get { return Get<uint>(VanillaField.CurrentSP); } }
        public StateValue<uint> MaxSP { get { return Get<uint>(VanillaField.MaxSP); } }
        public StateValue<string> CharacterName { get { return Get<string>(VanillaField.CharacterName); } }
        public StateValue<int> X { get { return Get<int>(VanillaField.X); } }
        public StateValue<int> Y { get { return Get<int>(VanillaField.Y); } }
        public StateValue<ulong> CurrentTargetId { get { return Get<ulong>(VanillaField.CurrentTargetId); } }
        public StateValue<uint> ActionState { get { return Get<uint>(VanillaField.ActionState); } }
        public StateValue<string> Map { get { return Get<string>(VanillaField.Map); } }
        public StateValue<bool> AutobattleEnabled { get { return Get<bool>(VanillaField.AutobattleEnabled); } }
        public StateValue<uint[]> StatusEffects { get { return Get<uint[]>(VanillaField.StatusEffects); } }
        public StateValue<bool> ClientReady { get { return Get<bool>(VanillaField.ClientReady); } }
        public StateValue<bool> Loading { get { return Get<bool>(VanillaField.Loading); } }
        private StateValue<T> Get<T>(VanillaField field) { return (StateValue<T>)Fields[field]; }

        internal static VanillaClientState Create(Guid session, DateTimeOffset now, VanillaClientState previous,
            IDictionary<VanillaField, object> values, IDictionary<VanillaField, ulong> addresses,
            VanillaMemoryMap map, string unavailableError = null)
        {
            var fields = new Dictionary<VanillaField, StateValue>();
            foreach (VanillaField field in Enum.GetValues(typeof(VanillaField)))
            {
                object value = null;
                bool available = values != null && values.TryGetValue(field, out value);
                if (!available) value = null;
                StateValue observation;
                switch (field)
                {
                    case VanillaField.CharacterName: case VanillaField.Map:
                        observation = new StateValue<string>((string)value); break;
                    case VanillaField.X: case VanillaField.Y:
                        observation = new StateValue<int>(available ? (int)value : 0); break;
                    case VanillaField.CurrentTargetId:
                        observation = new StateValue<ulong>(available ? (ulong)value : 0); break;
                    case VanillaField.AutobattleEnabled: case VanillaField.ClientReady: case VanillaField.Loading:
                        observation = new StateValue<bool>(available && (bool)value); break;
                    case VanillaField.StatusEffects:
                        observation = new StateValue<uint[]>(available ? (uint[])((uint[])value).Clone() : null); break;
                    default: observation = new StateValue<uint>(available ? (uint)value : 0); break;
                }
                observation.IsAvailable = available;
                observation.Validation = available ? StateValidation.Unverified : StateValidation.Unavailable;
                observation.LastObservedAtUtc = available ? (DateTimeOffset?)now : null;
                observation.Error = available ? null : (unavailableError ?? "No address configured.");
                ulong address;
                if (addresses != null && addresses.TryGetValue(field, out address)) observation.Address = address;
                VanillaFieldMapping mapping;
                if (map != null && map.Fields.TryGetValue(field, out mapping)) observation.Evidence = mapping.Evidence;
                StateValue old;
                if (available && previous != null && previous.SessionId == session && previous.Fields.TryGetValue(field, out old) && old.IsAvailable)
                {
                    bool equal = value is uint[] ? ((uint[])value).SequenceEqual((uint[])old.UntypedValue) : Equals(value, old.UntypedValue);
                    observation.LastChangedAtUtc = equal ? old.LastChangedAtUtc : now;
                }
                fields.Add(field, observation);
            }
            return new VanillaClientState
            {
                SessionId = session, SampledAtUtc = now, Error = unavailableError, SourceMap = map,
                LastStateChangeAtUtc = fields.Values.Select(field => field.LastChangedAtUtc).Max(),
                Fields = new ReadOnlyDictionary<VanillaField, StateValue>(fields)
            };
        }
    }

    public interface IStateSource : IDisposable
    {
        VanillaClientState Poll(DateTimeOffset now);
        bool IsStopped { get; }
        string Status { get; }
    }
}
