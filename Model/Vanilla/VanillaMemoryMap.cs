using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace _4RTools.Model.Vanilla
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum VanillaValueEncoding { UInt32, UInt64, Int32, Utf8, Boolean8, UInt32Array }

    public sealed class VanillaFieldMapping
    {
        // Null/empty Module means absolute Address; otherwise Address is a module offset.
        public string Module { get; set; }
        public string Address { get; set; }
        // For each offset: read a target-width pointer at the current address, then add offset.
        public List<string> PointerOffsets { get; set; } = new List<string>();
        public VanillaValueEncoding Encoding { get; set; }
        public int ByteCount { get; set; }
        // Human-supplied provenance, never automatic proof that a signal is verified.
        public string Evidence { get; set; }

        [JsonIgnore]
        public int ReadSize
        {
            get
            {
                switch (Encoding)
                {
                    case VanillaValueEncoding.UInt64: return 8;
                    case VanillaValueEncoding.Boolean8: return 1;
                    case VanillaValueEncoding.Utf8: case VanillaValueEncoding.UInt32Array: return ByteCount;
                    default: return 4;
                }
            }
        }
    }

    public sealed class VanillaMemoryMap
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProcessName { get; set; }
        public string Evidence { get; set; }
        public Dictionary<VanillaField, VanillaFieldMapping> Fields { get; set; } = new Dictionary<VanillaField, VanillaFieldMapping>();

        private static JsonSerializerSettings Settings()
        {
            return new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                MaxDepth = 16,
                Converters = { new StringEnumConverter() }
            };
        }

        public static VanillaMemoryMap Load(string path)
        {
            if (new FileInfo(path).Length > 65536) throw new ArgumentException("Memory map exceeds 64 KiB.");
            return Parse(File.ReadAllText(path));
        }

        public static VanillaMemoryMap Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) throw new ArgumentException("Memory map must contain 1 to 65536 characters.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Unexpected content after memory map.");
                if (token.Type != JTokenType.Object) throw new ArgumentException("Memory map must be a JSON object.");
                if (((JObject)token).GetValue("SchemaVersion", StringComparison.OrdinalIgnoreCase) == null)
                    throw new ArgumentException("Memory map must declare SchemaVersion 1.");
                var map = token.ToObject<VanillaMemoryMap>(JsonSerializer.Create(Settings()));
                map.Validate();
                return map;
            }
        }

        public string ToJson()
        {
            Validate();
            return JsonConvert.SerializeObject(this, Formatting.Indented, Settings());
        }

        public void Validate()
        {
            if (SchemaVersion != 1) throw new ArgumentException("Unsupported memory map SchemaVersion; expected 1.");
            if (Fields == null || Fields.Count > 13) throw new ArgumentException("Fields must contain at most 13 entries.");
            if (Fields.Count > 0 && string.IsNullOrWhiteSpace(ProcessName)) throw new ArgumentException("ProcessName is required for configured memory fields.");
            if (ProcessName != null && (ProcessName.Length > 260 || ProcessName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0))
                throw new ArgumentException("ProcessName must be an executable name without a path.");
            if (Evidence != null && Evidence.Length > 4096) throw new ArgumentException("Map Evidence exceeds 4096 characters.");
            foreach (var entry in Fields)
            {
                var m = entry.Value;
                if (!Enum.IsDefined(typeof(VanillaField), entry.Key) || m == null) throw new ArgumentException("Unknown or null field mapping.");
                if (m.Module != null && (m.Module.Length > 260 || m.Module.IndexOfAny(new[] { '/', '\\', ':' }) >= 0))
                    throw new ArgumentException(entry.Key + ": Module must be a filename without a path.");
                ulong address = ParseAddress(m.Address);
                if (string.IsNullOrWhiteSpace(m.Module) && address == 0) throw new ArgumentException(entry.Key + ": Absolute address cannot be zero.");
                if (m.PointerOffsets == null || m.PointerOffsets.Count > 8) throw new ArgumentException(entry.Key + ": At most 8 pointer offsets are supported.");
                foreach (string offset in m.PointerOffsets) ParseAddress(offset);
                if (m.Evidence != null && m.Evidence.Length > 4096) throw new ArgumentException(entry.Key + ": Evidence exceeds 4096 characters.");
                if (!ValidEncoding(entry.Key, m.Encoding)) throw new ArgumentException(entry.Key + ": Unsupported encoding " + m.Encoding + ".");
                if ((m.Encoding == VanillaValueEncoding.Utf8 || m.Encoding == VanillaValueEncoding.UInt32Array) && (m.ByteCount < 1 || m.ByteCount > 256))
                    throw new ArgumentException(entry.Key + ": ByteCount must be 1 to 256.");
                if (m.Encoding == VanillaValueEncoding.UInt32Array && m.ByteCount % 4 != 0)
                    throw new ArgumentException(entry.Key + ": Status byte count must be a multiple of 4.");
                if (m.Encoding != VanillaValueEncoding.Utf8 && m.Encoding != VanillaValueEncoding.UInt32Array && m.ByteCount != 0 && m.ByteCount != m.ReadSize)
                    throw new ArgumentException(entry.Key + ": ByteCount conflicts with fixed-width encoding.");
            }
        }

        private static bool ValidEncoding(VanillaField field, VanillaValueEncoding encoding)
        {
            switch (field)
            {
                case VanillaField.CharacterName: case VanillaField.Map: return encoding == VanillaValueEncoding.Utf8;
                case VanillaField.X: case VanillaField.Y: return encoding == VanillaValueEncoding.Int32;
                case VanillaField.CurrentTargetId: return encoding == VanillaValueEncoding.UInt32 || encoding == VanillaValueEncoding.UInt64;
                case VanillaField.AutobattleEnabled: return encoding == VanillaValueEncoding.Boolean8;
                case VanillaField.StatusEffects: return encoding == VanillaValueEncoding.UInt32Array;
                default: return encoding == VanillaValueEncoding.UInt32;
            }
        }

        public static ulong ParseAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Address/offset is required; use decimal or 0x hexadecimal.");
            string value = text.Trim();
            bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            ulong result;
            if (!ulong.TryParse(hex ? value.Substring(2) : value, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out result))
                throw new ArgumentException("Invalid unsigned address/offset: " + text);
            return result;
        }
    }
}
