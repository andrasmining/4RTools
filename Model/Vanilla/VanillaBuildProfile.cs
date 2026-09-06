using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using _4RTools.Model.Vanilla.Automation;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaActionMeaning
    {
        [JsonProperty(Required = Required.Always)]
        public uint Value { get; set; }
        [JsonProperty(Required = Required.Always)]
        public bool InCombat { get; set; }
        [JsonProperty(Required = Required.Always)]
        public bool IsCasting { get; set; }
    }

    /// <summary>Audited build definitions, stored separately from portable user preferences.</summary>
    public sealed class VanillaBuildProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string Label { get; set; }
        public string Sha256 { get; set; }
        public ushort Machine { get; set; }
        public uint ImageSize { get; set; }
        public string Evidence { get; set; }
        public VanillaMemoryMap MemoryMap { get; set; } = new VanillaMemoryMap();
        public List<VanillaField> VerifiedFields { get; set; } = new List<VanillaField>();
        public List<VanillaActionMeaning> ActionStates { get; set; } = new List<VanillaActionMeaning>();
        public List<ulong> NoTargetValues { get; set; } = new List<ulong>();
        public uint MaximumVital { get; set; } = 1000000000;
        public int MaximumCoordinate { get; set; } = 65535;

        public bool Matches(VanillaExecutableIdentity identity)
        {
            return identity != null && identity.Machine == Machine && identity.ImageSize == ImageSize
                && string.Equals(identity.Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
        }

        public void Validate()
        {
            Require(SchemaVersion == 1, "Unsupported build profile schema version.");
            Require(!string.IsNullOrWhiteSpace(Label) && Label.Length <= 160, "Build profile needs a label of 1–160 characters.");
            Require(Sha256 != null && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit), "Build profile requires a SHA-256 fingerprint.");
            Require(Machine == 0x14c || Machine == 0x8664, "Build profile must describe an x86 or x64 executable.");
            Require(ImageSize >= 4096, "Build profile image size is invalid.");
            Require(!string.IsNullOrWhiteSpace(Evidence) && Evidence.Length <= 4096, "Build evidence is required and must fit 4096 characters.");
            Require(MaximumVital > 0 && MaximumCoordinate > 0 && MaximumCoordinate <= 1000000, "Invalid state sanity limits.");
            Require(MemoryMap != null && VerifiedFields != null && ActionStates != null && NoTargetValues != null, "Build profile sections cannot be null.");
            MemoryMap.Validate();
            Require(!string.IsNullOrWhiteSpace(MemoryMap.ProcessName), "Build profile requires the expected executable name.");
            Require(VerifiedFields.Count == VerifiedFields.Distinct().Count(), "Verified field list contains duplicates.");
            Require(ActionStates.Count <= 256 && NoTargetValues.Count <= 16, "Build profile semantic mapping is too large.");
            Require(ActionStates.All(item => item != null) && ActionStates.Select(item => item.Value).Distinct().Count() == ActionStates.Count,
                "Action states contain null or duplicate entries.");
            Require(NoTargetValues.Distinct().Count() == NoTargetValues.Count, "No-target values contain duplicates.");
            foreach (var entry in MemoryMap.Fields)
            {
                var mapping = entry.Value;
                Require(string.Equals(mapping.Module, MemoryMap.ProcessName, StringComparison.OrdinalIgnoreCase),
                    entry.Key + ": Portable builds require a root relative to the fingerprinted main executable.");
                ulong root = VanillaMemoryMap.ParseAddress(mapping.Address);
                int rootSize = mapping.PointerOffsets.Count > 0 ? (Machine == 0x14c ? 4 : 8) : mapping.ReadSize;
                Require(root < ImageSize && (ulong)rootSize <= ImageSize - root, entry.Key + ": Root extends outside the fingerprinted image.");
            }
            foreach (VanillaField field in VerifiedFields)
            {
                VanillaFieldMapping mapping;
                Require(Enum.IsDefined(typeof(VanillaField), field) && MemoryMap.Fields.TryGetValue(field, out mapping), "Verified field has no mapping: " + field);
                mapping = MemoryMap.Fields[field];
                Require(!string.IsNullOrWhiteSpace(mapping.Evidence), "Verified field requires recorded evidence: " + field);
            }
            Require(!VerifiedFields.Contains(VanillaField.ActionState) || ActionStates.Count > 0, "Verified action state requires explicit combat/casting meanings.");
            Require(!VerifiedFields.Contains(VanillaField.CurrentTargetId) || NoTargetValues.Count > 0, "Verified target state requires explicit no-target values.");
        }

        public string ToJson()
        {
            Validate();
            return JsonConvert.SerializeObject(this, Formatting.Indented, JsonSettings());
        }

        public static VanillaBuildProfile Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 131072) throw new ArgumentException("Build profile must contain 1–131072 characters.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 20, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Unexpected content after build profile.");
                Require(token.Type == JTokenType.Object && ((JObject)token).GetValue("SchemaVersion", StringComparison.OrdinalIgnoreCase) != null,
                    "Build profile must declare SchemaVersion.");
                foreach (var item in ((JContainer)token).DescendantsAndSelf().OfType<JObject>())
                    Require(item.Properties().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == item.Properties().Count(),
                        "Build profile contains property names that differ only by case.");
                var profile = token.ToObject<VanillaBuildProfile>(JsonSerializer.Create(JsonSettings()));
                profile.Validate();
                return profile;
            }
        }

        public static VanillaBuildProfile Find(string directory, VanillaExecutableIdentity identity, Action<string> log)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (!Directory.Exists(directory)) { log("Vanilla build directory is absent; automation requires a verified build profile."); return null; }
            try
            {
                var paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
                if (paths.Length > 128) { log("Too many Vanilla build profiles; profile resolution stopped."); return null; }
                VanillaBuildProfile matched = null;
                foreach (string path in paths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    VanillaBuildProfile candidate;
                    try
                    {
                        if (new FileInfo(path).Length > 131072) throw new ArgumentException("Profile exceeds 128 KiB.");
                        candidate = Parse(File.ReadAllText(path));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException)
                    { log("Rejected build profile " + Path.GetFileName(path) + ": " + ex.Message); continue; }
                    if (!candidate.Matches(identity)) continue;
                    if (matched != null) { log("Multiple profiles match the Vanilla fingerprint; automation remains unavailable."); return null; }
                    matched = candidate;
                }
                log(matched == null ? "Unknown Vanilla fingerprint; automation remains unavailable."
                    : "Matched Vanilla build: " + matched.Label + "; verified fields: " + matched.VerifiedFields.Count);
                return matched;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { log("Build profile resolution failed: " + ex.Message); return null; }
        }

        private static JsonSerializerSettings JsonSettings()
        {
            return new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error,
                MaxDepth = 20, Converters = { new StringEnumConverter() } };
        }
        private static void Require(bool value, string error) { if (!value) throw new ArgumentException(error); }
    }

    /// <summary>Converts observed bytes into automation signals only for proven build semantics.</summary>
    public sealed class VanillaStateAdapter
    {
        private readonly VanillaBuildProfile profile;
        private readonly string fingerprint;
        private readonly string sourceMapJson;
        private readonly bool trusted;
        private Guid activitySession;
        private string activityMap;
        private int? previousX, previousY;
        private bool? previousTarget, previousCombat;
        private DateTimeOffset? lastMovement, lastTargetActivity, lastCombatActivity;
        public VanillaStateAdapter(VanillaBuildProfile profile, VanillaExecutableIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            fingerprint = identity.Sha256;
            this.profile = profile == null ? null : VanillaBuildProfile.Parse(profile.ToJson());
            trusted = this.profile != null && this.profile.Matches(identity);
            sourceMapJson = this.profile == null ? null : this.profile.MemoryMap.ToJson();
        }

        public RuleObservation Observe(VanillaClientState state, TimeSpan observedAt)
        {
            var result = new RuleObservation { ObservedAt = observedAt, Fingerprint = fingerprint };
            if (state == null) { ResetActivity(); return result; }
            state.LastMovementAtUtc = state.LastTargetActivityAtUtc = state.LastCombatActivityAtUtc = null;
            result.ProcessId = state.ProcessId ?? 0;
            result.SessionId = state.SessionId;
            result.TrustedBuild = trusted && !state.IsDemo && state.TargetPointerSize == (profile.Machine == 0x14c ? 4 : 8)
                && string.Equals(NormalizeName(state.ProcessName), NormalizeName(profile.MemoryMap.ProcessName), StringComparison.OrdinalIgnoreCase)
                && state.SourceMap != null && string.Equals(sourceMapJson, state.SourceMap.ToJson(), StringComparison.Ordinal);
            foreach (var field in state.Fields.Values)
            {
                field.Validation = field.IsAvailable ? StateValidation.Unverified : StateValidation.Unavailable;
                if (field.IsAvailable) field.Error = null;
            }
            if (!result.TrustedBuild || state.Error != null) { ResetActivity(); return result; }
            foreach (VanillaField field in profile.VerifiedFields)
                if (state.Fields[field].IsAvailable) state.Fields[field].Validation = StateValidation.Valid;

            ValidateVitals(state, VanillaField.CurrentHP, VanillaField.MaxHP);
            ValidateVitals(state, VanillaField.CurrentSP, VanillaField.MaxSP);
            ValidatePosition(state, VanillaField.X);
            ValidatePosition(state, VanillaField.Y);
            ValidateText(state, VanillaField.CharacterName);
            ValidateText(state, VanillaField.Map);
            result.HealthValidated = Valid(state, VanillaField.CurrentHP) && Valid(state, VanillaField.MaxHP);
            result.SpValidated = Valid(state, VanillaField.CurrentSP) && Valid(state, VanillaField.MaxSP);
            if (result.HealthValidated) { result.CurrentHP = state.CurrentHP.Value; result.MaxHP = state.MaxHP.Value; }
            if (result.SpValidated) { result.CurrentSP = state.CurrentSP.Value; result.MaxSP = state.MaxSP.Value; }
            result.TargetValidated = Valid(state, VanillaField.CurrentTargetId);
            if (result.TargetValidated) result.HasTarget = !profile.NoTargetValues.Contains(state.CurrentTargetId.Value);
            if (Valid(state, VanillaField.ActionState))
            {
                var meaning = profile.ActionStates.SingleOrDefault(action => action.Value == state.ActionState.Value);
                if (meaning == null) Invalid(state, VanillaField.ActionState, "Action code has no verified combat/casting meaning.");
                else
                {
                    result.CombatValidated = result.CastingValidated = true;
                    result.InCombat = meaning.InCombat;
                    result.IsCasting = meaning.IsCasting;
                }
            }
            result.PositionValidated = Valid(state, VanillaField.X) && Valid(state, VanillaField.Y);
            if (result.PositionValidated) { result.X = state.X.Value; result.Y = state.Y.Value; }
            if (Valid(state, VanillaField.Map)) result.Map = state.Map.Value;
            result.StatusValidated = Valid(state, VanillaField.StatusEffects);
            if (result.StatusValidated) result.StatusEffects = state.StatusEffects.Value;
            result.Ready = Valid(state, VanillaField.ClientReady) && state.ClientReady.Value;
            result.Loading = Valid(state, VanillaField.Loading) && state.Loading.Value;
            UpdateActivity(state, result);
            return result;
        }

        private void UpdateActivity(VanillaClientState state, RuleObservation observation)
        {
            if (!observation.Ready || observation.Loading) { ResetActivity(); return; }
            if (activitySession != state.SessionId || !string.Equals(activityMap, observation.Map, StringComparison.Ordinal)) ResetActivity();
            activitySession = state.SessionId;
            activityMap = observation.Map;
            if (observation.PositionValidated && previousX.HasValue && previousY.HasValue
                && (previousX != observation.X || previousY != observation.Y)) lastMovement = state.SampledAtUtc;
            previousX = observation.PositionValidated ? observation.X : null;
            previousY = observation.PositionValidated ? observation.Y : null;
            if (observation.TargetValidated)
            {
                if (observation.HasTarget == true || (previousTarget.HasValue && previousTarget != observation.HasTarget)) lastTargetActivity = state.SampledAtUtc;
                previousTarget = observation.HasTarget;
            }
            else previousTarget = null;
            if (observation.CombatValidated && observation.CastingValidated)
            {
                bool combat = observation.InCombat == true || observation.IsCasting == true;
                if (combat || (previousCombat.HasValue && previousCombat != combat)) lastCombatActivity = state.SampledAtUtc;
                previousCombat = combat;
            }
            else previousCombat = null;
            state.LastMovementAtUtc = lastMovement;
            state.LastTargetActivityAtUtc = lastTargetActivity;
            state.LastCombatActivityAtUtc = lastCombatActivity;
        }

        private void ResetActivity()
        {
            activitySession = Guid.Empty;
            activityMap = null;
            previousX = previousY = null;
            previousTarget = previousCombat = null;
            lastMovement = lastTargetActivity = lastCombatActivity = null;
        }

        private void ValidateVitals(VanillaClientState state, VanillaField current, VanillaField maximum)
        {
            var a = state.Fields[current] as StateValue<uint>;
            var b = state.Fields[maximum] as StateValue<uint>;
            if (!a.IsAvailable || !b.IsAvailable) return;
            if (b.Value == 0 || b.Value > profile.MaximumVital || a.Value > b.Value)
            {
                Invalid(state, current, "Resource values must satisfy 0 <= current <= maximum within the build sanity limit.");
                Invalid(state, maximum, "Resource maximum is zero, exceeds the build sanity limit, or is below current.");
            }
        }
        private void ValidatePosition(VanillaClientState state, VanillaField field)
        {
            var value = (StateValue<int>)state.Fields[field];
            if (value.IsAvailable && (value.Value < 0 || value.Value > profile.MaximumCoordinate))
                Invalid(state, field, "Coordinate is outside the build sanity limit.");
        }
        private static void ValidateText(VanillaClientState state, VanillaField field)
        {
            var value = (StateValue<string>)state.Fields[field];
            if (value.IsAvailable && (string.IsNullOrWhiteSpace(value.Value) || value.Value.Any(char.IsControl)))
                Invalid(state, field, "Text is empty or contains control characters.");
        }
        private static bool Valid(VanillaClientState state, VanillaField field) { return state.Fields[field].Validation == StateValidation.Valid; }
        private static void Invalid(VanillaClientState state, VanillaField field, string reason) { state.Fields[field].Validation = StateValidation.Invalid; state.Fields[field].Error = reason; }
        private static string NormalizeName(string name) { return name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name; }
    }
}
