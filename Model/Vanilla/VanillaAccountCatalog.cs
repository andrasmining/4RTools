using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// UI profile catalog. Vanilla still permits at most two enabled/running clients, but the user
    /// may keep any number of saved account profiles and switch which two are enabled.
    /// Password values are already DPAPI-protected before they reach this store.
    /// </summary>
    internal sealed class VanillaAccountCatalogStore
    {
        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public List<VanillaReconnectAccount> Accounts { get; set; } = new List<VanillaReconnectAccount>();
        }

        private readonly string path;

        internal VanillaAccountCatalogStore()
        {
            path = Path.Combine(VanillaAppData.RootDirectory, "account-profiles.json");
        }

        internal string FilePath { get { return path; } }

        internal List<VanillaReconnectAccount> Load(IEnumerable<VanillaReconnectAccount> supervisorSeed)
        {
            var result = new List<VanillaReconnectAccount>();
            try
            {
                if (File.Exists(path))
                {
                    var file = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(path));
                    if (file != null && file.Version == 1 && file.Accounts != null)
                        result.AddRange(file.Accounts.Where(a => a != null).Select(a => a.Clone()));
                }
            }
            catch (Exception ex)
            {
                VanillaDebugLog.Write("SETTINGS", "Account catalog load failed: " + ex);
            }

            foreach (VanillaReconnectAccount seed in (supervisorSeed ?? Enumerable.Empty<VanillaReconnectAccount>()).Where(a => a != null))
            {
                int existing = result.FindIndex(a => string.Equals(a.Id, seed.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) result[existing] = seed.Clone();
                else result.Add(seed.Clone());
            }

            result = result
                .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last().Clone())
                .ToList();

            if (result.Count == 0)
                result.Add(new VanillaReconnectAccount { Label = "Client 1", Enabled = false });

            NormalizeEnabledLimit(result);
            Save(result);
            return result;
        }

        internal void Save(IEnumerable<VanillaReconnectAccount> accounts)
        {
            var copy = (accounts ?? Enumerable.Empty<VanillaReconnectAccount>())
                .Where(a => a != null)
                .Select(a => a.Clone())
                .ToList();
            if (copy.Count == 0) throw new InvalidOperationException("Keep at least one saved account profile.");
            if (copy.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Saved account profile IDs must be unique.");
            if (copy.Count(a => a.Enabled) > 2)
                throw new InvalidOperationException("Only two Vanilla account profiles may be enabled at once.");

            Directory.CreateDirectory(VanillaAppData.RootDirectory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(new FileModel { Accounts = copy }, Formatting.Indented));
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temp, path, backup);
            }
            else File.Move(temp, path);
        }

        internal static void NormalizeEnabledLimit(IList<VanillaReconnectAccount> accounts)
        {
            if (accounts == null) return;
            int enabled = 0;
            foreach (VanillaReconnectAccount account in accounts)
            {
                if (account == null || !account.Enabled) continue;
                enabled++;
                if (enabled > 2) account.Enabled = false;
            }
        }
    }
}
