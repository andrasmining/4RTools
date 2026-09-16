using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Persistent character roster. Retains the historical filename and Accounts/Label JSON keys
    /// so upgrades preserve IDs, descriptions, encrypted passwords and per-row proxy preferences.
    /// One username may have several character rows; the enabled-client limit remains two.
    /// </summary>
    internal sealed class VanillaAccountCatalogStore
    {
        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public List<VanillaReconnectAccount> Accounts { get; set; } = new List<VanillaReconnectAccount>();
        }
        private readonly string directory, path;
        internal VanillaAccountCatalogStore() : this(VanillaAppData.RootDirectory) { }
        internal VanillaAccountCatalogStore(string directory)
        {
            this.directory = Path.GetFullPath(directory);
            path = Path.Combine(this.directory, "account-profiles.json");
        }
        internal string FilePath { get { return path; } }

        private FileModel ReadExisting()
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Character catalog is too large; preserved unchanged.");
            var file = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(path));
            if (file == null || file.Version != 1 || file.Accounts == null)
                throw new InvalidDataException("Character catalog is invalid or from a newer version; preserved unchanged.");
            return file;
        }

        internal List<VanillaReconnectAccount> Load(IEnumerable<VanillaReconnectAccount> supervisorSeed)
        {
            var result = File.Exists(path) ? ReadExisting().Accounts.Select(a => a?.Clone()).ToList()
                : new List<VanillaReconnectAccount>();
            foreach (var seed in (File.Exists(path) ? Enumerable.Empty<VanillaReconnectAccount>()
                : supervisorSeed ?? Enumerable.Empty<VanillaReconnectAccount>()).Where(a => a != null))
            {
                // The complete roster owns saved data; the two-row runtime subset can be stale.
                if (!result.Any(a => a != null && string.Equals(a.Id, seed.Id, StringComparison.OrdinalIgnoreCase)))
                    result.Add(seed.Clone());
            }
            if (result.Count == 0) result.Add(new VanillaReconnectAccount { Label = "Client 1", Enabled = false });
            if (result.Any(a => a != null && !string.IsNullOrWhiteSpace(a.CharacterName)))
                result.RemoveAll(VanillaCharacterRoster.IsEmptyDefault);
            NormalizeEnabledLimit(result);
            VanillaCharacterRoster.Validate(result);
            Save(result);
            return result;
        }

        internal void Save(IEnumerable<VanillaReconnectAccount> accounts)
        {
            var copy = (accounts ?? Enumerable.Empty<VanillaReconnectAccount>()).Select(a => a?.Clone()).ToList();
            VanillaCharacterRoster.Validate(copy);
            if (File.Exists(path)) ReadExisting(); // Never overwrite an unreadable/future catalog with a partial fallback.
            Directory.CreateDirectory(directory);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonConvert.SerializeObject(new FileModel { Accounts = copy }, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static void NormalizeEnabledLimit(IList<VanillaReconnectAccount> accounts)
        {
            if (accounts == null) return;
            int enabled = 0;
            foreach (var account in accounts)
                if (account != null && account.Enabled && ++enabled > 2) account.Enabled = false;
        }
    }
}
