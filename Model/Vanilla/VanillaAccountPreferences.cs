using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaAccountProxyPreferences
    {
        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, VanillaProxyRoute> Proxies { get; set; } = new Dictionary<string, VanillaProxyRoute>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly object Gate = new object();
        private static FileModel cache;
        private static string PathName { get { return Path.Combine(VanillaAppData.RootDirectory, "account-runtime.json"); } }

        internal static VanillaProxyRoute Get(string accountId, VanillaProxyRoute fallback)
        {
            VanillaProxyRoute value;
            return TryGet(accountId, out value) ? value : fallback;
        }

        internal static bool TryGet(string accountId, out VanillaProxyRoute value)
        {
            value = default(VanillaProxyRoute);
            if (string.IsNullOrWhiteSpace(accountId)) return false;
            lock (Gate)
            {
                EnsureLoaded();
                return cache.Proxies.TryGetValue(accountId, out value);
            }
        }

        /// <summary>
        /// Seeds an account that predates per-account proxy settings exactly once. This freezes
        /// the previous shared proxy choice into the account so later account edits/recovery do
        /// not accidentally inherit another account's compatibility fallback.
        /// </summary>
        internal static VanillaProxyRoute Ensure(string accountId, VanillaProxyRoute fallback)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return fallback;
            lock (Gate)
            {
                EnsureLoaded();
                VanillaProxyRoute value;
                if (cache.Proxies.TryGetValue(accountId, out value)) return value;
                cache.Proxies[accountId] = fallback;
                SaveLocked();
            }
            VanillaDebugLog.Write("SETTINGS", "Account proxy initialized from legacy shared setting: accountId=" + accountId + ", proxy=" + fallback + ".");
            return fallback;
        }

        internal static void Set(string accountId, VanillaProxyRoute value)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return;
            lock (Gate)
            {
                EnsureLoaded();
                cache.Proxies[accountId] = value;
                SaveLocked();
            }
            VanillaDebugLog.Write("SETTINGS", "Account proxy saved: accountId=" + accountId + ", proxy=" + value + ".");
        }

        internal static void Remove(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return;
            lock (Gate)
            {
                EnsureLoaded();
                if (!cache.Proxies.Remove(accountId)) return;
                SaveLocked();
            }
            VanillaDebugLog.Write("SETTINGS", "Account proxy removed: accountId=" + accountId + ".");
        }

        private static void EnsureLoaded()
        {
            if (cache != null) return;
            cache = new FileModel();
            try
            {
                if (!File.Exists(PathName)) return;
                var loaded = JsonConvert.DeserializeObject<FileModel>(File.ReadAllText(PathName));
                if (loaded == null || loaded.Version != 1) return;
                if (loaded.Proxies == null) loaded.Proxies = new Dictionary<string, VanillaProxyRoute>(StringComparer.OrdinalIgnoreCase);
                cache = loaded;
            }
            catch (Exception ex)
            {
                VanillaDebugLog.Write("SETTINGS", "Account proxy preferences load failed: " + ex.Message);
            }
        }

        private static void SaveLocked()
        {
            Directory.CreateDirectory(VanillaAppData.RootDirectory);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(cache, Formatting.Indented));
            if (File.Exists(PathName))
            {
                string backup = PathName + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temp, PathName, backup);
            }
            else File.Move(temp, PathName);
        }
    }
}
