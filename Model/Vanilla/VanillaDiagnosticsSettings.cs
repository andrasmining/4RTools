using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _4RTools.Model.Vanilla
{
    // Settings only: this object is deliberately not an automation Action.
    public sealed class VanillaDiagnosticsSettings
    {
        public int PollIntervalMilliseconds { get; set; } = 500;
        public string MemoryMapJson { get; set; } = "{\"SchemaVersion\":1,\"ProcessName\":\"Vanilla MMO\",\"Fields\":{}}";

        public void Validate()
        {
            if (PollIntervalMilliseconds < 250 || PollIntervalMilliseconds > 10000)
                throw new ArgumentException("Diagnostics polling must be between 250 and 10000 ms.");
            VanillaMemoryMap.Parse(MemoryMapJson).Validate();
        }

        public static VanillaDiagnosticsSettings FromToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new VanillaDiagnosticsSettings();
            var settings = token.Type == JTokenType.String
                ? JsonConvert.DeserializeObject<VanillaDiagnosticsSettings>(token.Value<string>())
                : token.ToObject<VanillaDiagnosticsSettings>();
            if (settings == null) throw new ArgumentException("Vanilla diagnostics settings cannot be null.");
            settings.Validate();
            return settings;
        }
    }
}
