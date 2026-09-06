using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _4RTools.Model.Vanilla.Automation
{
    /// <summary>Portable, bounded profile storage, independent of the launch working directory.</summary>
    public sealed class AutomationProfileStore
    {
        private const int MaximumBytes = 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string directory;

        public AutomationProfileStore(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("An application directory is required.");
            directory = Path.Combine(Path.GetFullPath(baseDirectory), "Profiles", "Vanilla");
            Directory.CreateDirectory(directory);
            if (!File.Exists(ProfilePath("Default"))) Save("Default", new VanillaAutomationSettings());
        }

        public string[] Names()
        {
            return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension).Where(IsValidName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public VanillaAutomationSettings Load(string name)
        {
            string path = ProfilePath(name);
            try { return ReadValidated(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is DecoderFallbackException)
            {
                throw new InvalidDataException("Profile '" + name + "' is invalid and has been preserved. Choose another profile or import a valid copy under a new name. " + ex.Message, ex);
            }
        }

        public void Save(string name, VanillaAutomationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string path = ProfilePath(name);
            byte[] content = Serialize(settings);
            if (File.Exists(path))
            {
                // A failed load must never be followed by a quiet overwrite with defaults.
                try { ReadValidated(path); }
                catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is DecoderFallbackException || ex is InvalidDataException)
                {
                    throw new InvalidDataException("The existing profile '" + name + "' is invalid and has been preserved. Save this profile with a different name.", ex);
                }
            }
            WriteAtomically(path, content);
        }

        public void Import(string path, string name)
        {
            // Validate everything before creating or replacing a destination file.
            ProfilePath(name);
            Save(name, ReadValidated(path));
        }

        public void Export(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose an export file.");
            WriteAtomically(Path.GetFullPath(path), Serialize(Load(name)));
        }

        private string ProfilePath(string name)
        {
            if (!IsValidName(name))
                throw new ArgumentException("Use a profile name of 1–64 characters without file path characters, leading or trailing spaces, a trailing period, or a Windows reserved filename.");
            return Path.Combine(directory, name + ".json");
        }

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim()
                || name.EndsWith(".", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name == "." || name == "..") return false;
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CLOCK$") return false;
            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] >= '1' && stem[3] <= '9') return false;
            return true;
        }

        private static VanillaAutomationSettings ReadValidated(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a profile file.");
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > MaximumBytes) throw new InvalidDataException("Profile must contain 1 byte to 1 MiB of JSON.");
                using (var reader = new StreamReader(stream, Utf8, true, 4096)) json = reader.ReadToEnd();
            }
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Unexpected content after the profile.");
                var root = token as JObject;
                if (root == null || root["Version"]?.Type != JTokenType.Integer || root.Value<int>("Version") != 1)
                    throw new ArgumentException("Profile must declare Version 1.");
                ValidateProperties(root);
                return VanillaAutomationSettings.FromJson(root.ToString(Formatting.None));
            }
        }

        private static void ValidateProperties(JToken token)
        {
            var objectToken = token as JObject;
            if (objectToken != null)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in objectToken.Properties())
                {
                    if (!names.Add(property.Name)) throw new ArgumentException("Duplicate profile property: " + property.Name + ".");
                    if (property.Name.StartsWith("$", StringComparison.Ordinal)) throw new ArgumentException("Profile metadata properties are not supported.");
                    ValidateProperties(property.Value);
                }
            }
            else if (token is JArray)
                foreach (var child in token.Children()) ValidateProperties(child);
        }

        private static byte[] Serialize(VanillaAutomationSettings settings)
        {
            byte[] content = Utf8.GetBytes(settings.ToJson());
            if (content.Length > MaximumBytes) throw new ArgumentException("Profile exceeds 1 MiB.");
            return content;
        }

        private static void WriteAtomically(string path, byte[] content)
        {
            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) throw new DirectoryNotFoundException("The profile destination directory does not exist.");
            string temporary = Path.Combine(parent, ".vanilla-profile-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
